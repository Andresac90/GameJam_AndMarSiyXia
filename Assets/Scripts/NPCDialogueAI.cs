
using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class NPCDialogueAI : MonoBehaviour
{
    [Header("Ollama")]
    [SerializeField] private string endpoint =
        "http://localhost:11434/api/generate";

    [SerializeField] private string model = "llama3.2:3b";

    [SerializeField] private int maxResponseTokens = 100;

    [Header("NPC Personality")]
    [TextArea(5, 12)]
    [SerializeField] private string personality =
        "You are the same adult man living in the same house " +
        "on Halloween night. A child in a costume visits your door. " +
        "You have a distinct personality, emotions, secrets, and " +
        "a strange relationship with the repeating night. " +
        "Stay in character and speak naturally. " +
        "Keep replies to one to four short sentences. " +
        "React to the child's previous choices when relevant. " +
        "Do not decide the player's actions. " +
        "Do not reveal every mystery immediately.";

    [Serializable]
    private class GenerateRequest
    {
        public string model;
        public string prompt;
        public bool stream;
        public GenerateOptions options;
    }

    [Serializable]
    private class GenerateOptions
    {
        public float temperature;
        public int num_predict;
    }

    [Serializable]
    private class GenerateResponse
    {
        public string response;
        public bool done;
        public string error;
    }

    public void GenerateReply(
        string context,
        Action<string> onSuccess,
        Action<string> onError)
    {
        StartCoroutine(
            SendRequest(context, onSuccess, onError)
        );
    }

    private IEnumerator SendRequest(
        string context,
        Action<string> onSuccess,
        Action<string> onError)
    {
        GenerateRequest body = new GenerateRequest
        {
            model = model,
            prompt =
                "SYSTEM: You are the NPC in the game Trick or Threat.\n" +
                personality +
                "\n\nGAME CONTEXT:\n" + context +
                "\n\nRespond only with the NPC's spoken dialogue. " +
                "Do not include a character name or stage directions.",
            stream = false,
            options = new GenerateOptions
            {
                temperature = 0.8f,
                num_predict = maxResponseTokens
            }
        };

        string json = JsonUtility.ToJson(body);
        byte[] bytes = Encoding.UTF8.GetBytes(json);

        using (UnityWebRequest request = new UnityWebRequest(
            endpoint, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = 120;

            request.SetRequestHeader(
                "Content-Type", "application/json"
            );

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                string details = request.downloadHandler != null
                    ? request.downloadHandler.text
                    : "";

                Debug.LogError(
                    "Ollama request failed: " +
                    request.error + "\n" + details
                );

                onError?.Invoke(
                    "The neighbor is silent. Check that Ollama is running."
                );

                yield break;
            }

            GenerateResponse response = null;

            try
            {
                response = JsonUtility.FromJson<GenerateResponse>(
                    request.downloadHandler.text
                );
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            if (response == null ||
                string.IsNullOrWhiteSpace(response.response))
            {
                onError?.Invoke(
                    "The neighbor couldn't find the words."
                );
            }
            else
            {
                onSuccess?.Invoke(response.response.Trim());
            }
        }
    }
}
