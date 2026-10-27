
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Generates NPC replies.
/// - Ollama: a local Ollama server (editor / desktop testing).
/// - WebLLM: the model runs inside the player's browser with WebGPU (WebGL builds,
///   see Assets/Plugins/WebGL/WebLLMBridge.jslib). Nothing to install; the model
///   downloads once (~0.9 GB) and the browser caches it.
/// Auto picks WebLLM in WebGL builds and Ollama everywhere else.
/// </summary>
public class NPCDialogueAI : MonoBehaviour
{
    /// <summary>
    /// Auto: WebLLM (small model, in the browser) for WebGL builds, the bundled llama-server
    /// (bigger model, no install) for Windows/macOS builds, Ollama in the editor.
    /// </summary>
    public enum Backend { Auto, Ollama, WebLLM, LocalServer }

    [SerializeField] private Backend backend = Backend.Auto;

    [Header("Ollama")]
    [SerializeField] private string endpoint =
        "http://localhost:11434/api/generate";

    [SerializeField] private string model = "llama3.2:3b";
    [SerializeField, Range(0f, 1.5f)] private float temperature = 0.8f;
    [SerializeField, Min(16)] private int maxTokens = 120;
    [SerializeField, Min(10)] private int timeoutSeconds = 120;

#pragma warning disable 0414 // read only by the WebGL build
    [Header("WebLLM (WebGL builds)")]
    [Tooltip("WebLLM model id. Llama-3.2-3B-Instruct-q4f16_1-MLC is smarter but a ~2.3 GB download.")]
    [SerializeField] private string webLLMModel = "Llama-3.2-1B-Instruct-q4f16_1-MLC";
    [Tooltip("Used on GPUs without 16-bit float support.")]
    [SerializeField] private string webLLMFallbackModel = "Llama-3.2-1B-Instruct-q4f32_1-MLC";
    [SerializeField] private bool showLoadingStatus = true;
#pragma warning restore 0414

    [Header("Repetition control")]
    [Tooltip("Ollama repeat_penalty (1 = off).")]
    [SerializeField, Range(1f, 2f)] private float repeatPenalty = 1.18f;
#pragma warning disable 0414 // read only by the WebGL build
    [Tooltip("WebLLM frequency/presence penalty (0 = off).")]
    [SerializeField, Range(0f, 2f)] private float webLLMPenalty = 0.5f;
#pragma warning restore 0414

    private const string FallbackReply = "I didn't catch that. Could you ask me again?";

    /// <summary>One chat turn: role is "system", "user" (the kid) or "assistant" (the NPC).</summary>
    [Serializable]
    public class ChatMessage
    {
        public string role;
        public string content;

        public ChatMessage() { }

        public ChatMessage(string role, string content)
        {
            this.role = role;
            this.content = content;
        }
    }

    [Serializable]
    private class ChatMessageList
    {
        public ChatMessage[] messages;
    }

    [Serializable]
    private class OllamaChatRequest
    {
        public string model;
        public ChatMessage[] messages;
        public bool stream;
        public OllamaChatOptions options;
    }

    [Serializable]
    private class OllamaChatOptions
    {
        public float temperature;
        public int num_predict;
        public float repeat_penalty;
        public float top_p;
    }

    [Serializable]
    private class LocalChatRequest
    {
        public ChatMessage[] messages;
        public int max_tokens;
        public float temperature;
        public float top_p;
        public float repeat_penalty;
        public bool stream;
    }

    [Serializable]
    private class LocalChatChoice
    {
        public ChatMessage message;
    }

    [Serializable]
    private class LocalChatResponse
    {
        public LocalChatChoice[] choices;
    }

    [Serializable]
    private class OllamaChatResponse
    {
        public ChatMessage message;
        public string error;
    }

    [Serializable]
    private class OllamaRequest
    {
        public string model;
        public string prompt;
        public bool stream;
        public OllamaOptions options;
    }

    [Serializable]
    private class OllamaOptions
    {
        public float temperature;
        public int num_predict;
    }

    [Serializable]
    private class OllamaResponse
    {
        public string response;
        public bool done;
        public string error;
    }

    [Serializable]
    private class WebLLMProgress
    {
        public float progress;
        public string text;
    }

    [Serializable]
    private class WebLLMReply
    {
        public int id;
        public string text;
        public string error;
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void WebLLM_Init(string modelId, string fallbackModelId, string objectName);
    [DllImport("__Internal")] private static extern void WebLLM_Generate(int requestId, string prompt, int maxTokens, float temperature);
    [DllImport("__Internal")] private static extern void WebLLM_GenerateChat(int requestId, string messagesJson, int maxTokens, float temperature, float penalty);
#endif

    private enum WebLLMState { NotStarted, Loading, Ready, Failed }

    private WebLLMState webLLMState = WebLLMState.NotStarted;
    private float webLLMProgress;
    private string webLLMStatus = "";
    private int nextRequestId;
    private readonly Dictionary<int, string> webLLMReplies = new Dictionary<int, string>();

    private bool UseLocalServer =>
        backend == Backend.LocalServer ||
        (backend == Backend.Auto && !Application.isEditor &&
         Application.platform != RuntimePlatform.WebGLPlayer && LocalLLMServer.IsAvailable);

    private bool UseWebLLM =>
        backend == Backend.WebLLM ||
        (backend == Backend.Auto && Application.platform == RuntimePlatform.WebGLPlayer);

    /// <summary>True while the browser model is still downloading.</summary>
    public bool IsLoading => UseWebLLM && webLLMState == WebLLMState.Loading;

    private void Start()
    {
        // Start the (large) model download right away, so it is ready by the first door.
        if (UseWebLLM) StartWebLLM();
        else if (UseLocalServer) StartCoroutine(LocalLLMServer.EnsureStarted());
    }

    /// <summary>
    /// Chat-style generation: a system message plus the real back-and-forth, so the model
    /// knows what it already said (fewer repeats than one big prompt).
    /// </summary>
    public IEnumerator GenerateChat(IList<ChatMessage> messages, Action<string> onComplete)
    {
        ChatMessage[] array = new ChatMessage[messages.Count];
        messages.CopyTo(array, 0);

        if (UseWebLLM)
            yield return ChatWithWebLLM(array, onComplete);
        else if (UseLocalServer)
            yield return ChatWithLocalServer(array, onComplete);
        else
            yield return ChatWithOllama(array, onComplete);
    }

    public IEnumerator GenerateReply(
        string prompt,
        Action<string> onComplete)
    {
        if (UseWebLLM)
            yield return GenerateWithWebLLM(prompt, onComplete);
        else if (UseLocalServer)
            yield return ChatWithLocalServer(new[] { new ChatMessage("user", prompt) }, onComplete);
        else
            yield return GenerateWithOllama(prompt, onComplete);
    }

    // ---------------------------------------------------------------- WebLLM

    private void StartWebLLM()
    {
        if (webLLMState != WebLLMState.NotStarted) return;

#if UNITY_WEBGL && !UNITY_EDITOR
        webLLMState = WebLLMState.Loading;
        webLLMStatus = "Starting...";
        WebLLM_Init(webLLMModel, webLLMFallbackModel, gameObject.name);
#else
        webLLMState = WebLLMState.Failed;
        webLLMStatus = "WebLLM only runs in WebGL builds. Set Backend to Auto or Ollama in the editor.";
        Debug.LogWarning("NPCDialogueAI: " + webLLMStatus);
#endif
    }

    private IEnumerator ChatWithWebLLM(ChatMessage[] messages, Action<string> onComplete)
    {
        string json = JsonUtility.ToJson(new ChatMessageList { messages = messages });
        yield return RunWebLLM(id =>
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            WebLLM_GenerateChat(id, json, maxTokens, temperature, webLLMPenalty);
#endif
        }, onComplete);
    }

    private IEnumerator GenerateWithWebLLM(string prompt, Action<string> onComplete)
    {
        yield return RunWebLLM(id =>
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            WebLLM_Generate(id, prompt, maxTokens, temperature);
#endif
        }, onComplete);
    }

    private IEnumerator RunWebLLM(Action<int> send, Action<string> onComplete)
    {
        StartWebLLM();

        // First visit may arrive before the download finishes: wait for it.
        while (webLLMState == WebLLMState.Loading)
            yield return null;

        if (webLLMState != WebLLMState.Ready)
        {
            onComplete?.Invoke(FallbackReply);
            yield break;
        }

        int id = ++nextRequestId;
        webLLMReplies.Remove(id);
        send(id);

        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (!webLLMReplies.ContainsKey(id) && Time.realtimeSinceStartup < deadline)
            yield return null;

        string text = webLLMReplies.TryGetValue(id, out string reply) ? reply : null;
        webLLMReplies.Remove(id);
        onComplete?.Invoke(string.IsNullOrWhiteSpace(text) ? FallbackReply : text.Trim());
    }

    // Called from WebLLMBridge.jslib through SendMessage.
    private void OnWebLLMProgress(string json)
    {
        WebLLMProgress report = JsonUtility.FromJson<WebLLMProgress>(json);
        if (report == null) return;
        webLLMProgress = report.progress;
        webLLMStatus = report.text;
    }

    private void OnWebLLMReady(string modelId)
    {
        webLLMState = WebLLMState.Ready;
        webLLMProgress = 1f;
        webLLMStatus = "";
        Debug.Log("NPCDialogueAI: WebLLM ready with " + modelId);
    }

    private void OnWebLLMError(string message)
    {
        webLLMState = WebLLMState.Failed;
        webLLMStatus = message;
        Debug.LogError("NPCDialogueAI: WebLLM failed: " + message);
    }

    private void OnWebLLMReply(string json)
    {
        WebLLMReply reply = JsonUtility.FromJson<WebLLMReply>(json);
        if (reply != null) webLLMReplies[reply.id] = reply.text;
    }

    private void OnWebLLMReplyError(string json)
    {
        WebLLMReply reply = JsonUtility.FromJson<WebLLMReply>(json);
        if (reply == null) return;
        Debug.LogError("NPCDialogueAI: WebLLM reply failed: " + reply.error);
        webLLMReplies[reply.id] = null;
    }

    // ---------------------------------------------------------- Local server

    private IEnumerator ChatWithLocalServer(ChatMessage[] messages, Action<string> onComplete)
    {
        // First door may arrive while the model is still loading: wait for it.
        yield return LocalLLMServer.EnsureStarted();

        if (LocalLLMServer.State != LocalLLMServer.Status.Ready)
        {
            onComplete?.Invoke(FallbackReply);
            yield break;
        }

        LocalChatRequest requestData = new LocalChatRequest
        {
            messages = messages,
            max_tokens = maxTokens,
            temperature = temperature,
            top_p = 0.9f,
            repeat_penalty = repeatPenalty,
            stream = false
        };

        byte[] body = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(requestData));

        using (UnityWebRequest request = new UnityWebRequest(LocalLLMServer.BaseUrl + "/v1/chat/completions", "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = timeoutSeconds;

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Local AI request failed: " + request.error);
                onComplete?.Invoke(FallbackReply);
                yield break;
            }

            LocalChatResponse result = null;
            try
            {
                result = JsonUtility.FromJson<LocalChatResponse>(request.downloadHandler.text);
            }
            catch (Exception exception)
            {
                Debug.LogError("Could not parse local AI response: " + exception.Message);
            }

            string text = result?.choices != null && result.choices.Length > 0 && result.choices[0].message != null
                ? result.choices[0].message.content
                : null;

            onComplete?.Invoke(string.IsNullOrWhiteSpace(text) ? FallbackReply : text.Trim());
        }
    }

    private void OnGUI()
    {
        if (showLoadingStatus && UseLocalServer)
        {
            DrawLocalServerStatus();
            return;
        }

        if (!showLoadingStatus || !UseWebLLM) return;
        if (webLLMState != WebLLMState.Loading && webLLMState != WebLLMState.Failed) return;

        float scale = Screen.height / 1080f;
        string text = webLLMState == WebLLMState.Loading
            ? $"Waking the neighbours... {Mathf.RoundToInt(webLLMProgress * 100f)}%  (first visit downloads the AI, ~1 GB)"
            : "The neighbours won't talk: " + webLLMStatus;

        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(20 * scale),
            alignment = TextAnchor.LowerLeft,
            wordWrap = true
        };
        style.normal.textColor = webLLMState == WebLLMState.Failed ? new Color(1f, 0.45f, 0.4f) : new Color(1f, 0.85f, 0.6f);

        Rect rect = new Rect(30 * scale, Screen.height - 90 * scale, Screen.width * 0.6f, 70 * scale);
        GUI.Label(rect, text, style);

        if (webLLMState == WebLLMState.Loading)
        {
            Rect bar = new Rect(30 * scale, Screen.height - 22 * scale, Screen.width * 0.3f, 6 * scale);
            GUI.color = new Color(1f, 1f, 1f, 0.25f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.6f, 0.15f, 0.9f);
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(webLLMProgress), bar.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }

    private void DrawLocalServerStatus()
    {
        LocalLLMServer.Status state = LocalLLMServer.State;
        if (state != LocalLLMServer.Status.Starting && state != LocalLLMServer.Status.Failed) return;

        float scale = Screen.height / 1080f;
        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(20 * scale),
            alignment = TextAnchor.LowerLeft,
            wordWrap = true
        };
        style.normal.textColor = state == LocalLLMServer.Status.Failed ? new Color(1f, 0.45f, 0.4f) : new Color(1f, 0.85f, 0.6f);

        string text = state == LocalLLMServer.Status.Starting
            ? "Waking the neighbours..."
            : "The neighbours won't talk: " + LocalLLMServer.Error;
        GUI.Label(new Rect(30 * scale, Screen.height - 90 * scale, Screen.width * 0.6f, 70 * scale), text, style);
    }

    // ---------------------------------------------------------------- Ollama

    private IEnumerator ChatWithOllama(ChatMessage[] messages, Action<string> onComplete)
    {
        OllamaChatRequest requestData = new OllamaChatRequest
        {
            model = model,
            messages = messages,
            stream = false,
            options = new OllamaChatOptions
            {
                temperature = temperature,
                num_predict = maxTokens,
                repeat_penalty = repeatPenalty,
                top_p = 0.9f
            }
        };

        byte[] body = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(requestData));
        string chatEndpoint = endpoint.Replace("/api/generate", "/api/chat");

        using (UnityWebRequest request = new UnityWebRequest(chatEndpoint, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = timeoutSeconds;

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Ollama request failed: " + request.error);
                onComplete?.Invoke(FallbackReply);
                yield break;
            }

            OllamaChatResponse result = null;
            try
            {
                result = JsonUtility.FromJson<OllamaChatResponse>(request.downloadHandler.text);
            }
            catch (Exception exception)
            {
                Debug.LogError("Could not parse Ollama response: " + exception.Message);
            }

            string text = result != null && result.message != null ? result.message.content : null;
            if (string.IsNullOrWhiteSpace(text))
            {
                Debug.LogError("Invalid Ollama response: " + request.downloadHandler.text);
                onComplete?.Invoke(FallbackReply);
                yield break;
            }

            onComplete?.Invoke(text.Trim());
        }
    }

    private IEnumerator GenerateWithOllama(string prompt, Action<string> onComplete)
    {
        OllamaRequest requestData = new OllamaRequest
        {
            model = model,
            prompt = prompt,
            stream = false,
            options = new OllamaOptions
            {
                temperature = temperature,
                num_predict = maxTokens
            }
        };

        string json = JsonUtility.ToJson(requestData);
        byte[] body = System.Text.Encoding.UTF8.GetBytes(json);

        using (UnityWebRequest request =
               new UnityWebRequest(endpoint, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader(
                "Content-Type", "application/json");
            request.timeout = timeoutSeconds;

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError(
                    "Ollama request failed: " + request.error);

                onComplete?.Invoke(FallbackReply);
                yield break;
            }

            OllamaResponse result = null;

            try
            {
                result = JsonUtility.FromJson<OllamaResponse>(
                    request.downloadHandler.text);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "Could not parse Ollama response: " +
                    exception.Message);
            }

            if (result == null ||
                !string.IsNullOrWhiteSpace(result.error) ||
                string.IsNullOrWhiteSpace(result.response))
            {
                Debug.LogError(
                    "Invalid Ollama response: " +
                    request.downloadHandler.text);

                onComplete?.Invoke(
                    "I'm not sure what to say to that.");
                yield break;
            }

            onComplete?.Invoke(result.response.Trim());
        }
    }
}
