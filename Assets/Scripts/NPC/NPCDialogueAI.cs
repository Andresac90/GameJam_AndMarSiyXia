
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class NPCDialogueAI : MonoBehaviour
{
    [Header("Ollama")]
    [SerializeField] private string endpoint =
        "http://localhost:11434/api/generate";

    [SerializeField] private string model = "llama3.2:3b";
    [SerializeField, Range(0f, 1.5f)] private float temperature = 0.8f;
    [SerializeField, Min(16)] private int maxTokens = 120;
    [SerializeField, Min(10)] private int timeoutSeconds = 120;

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

    public IEnumerator GenerateReply(
        string prompt,
        Action<string> onComplete)
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

                onComplete?.Invoke(
                    "I didn't catch that. Could you ask me again?");
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
