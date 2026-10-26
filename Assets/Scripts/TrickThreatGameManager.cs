
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TrickThreatGameManager : MonoBehaviour
{
    public static bool DialogueOpen { get; private set; }

    [Header("Scene References")]
    [SerializeField] private Transform player;
    [SerializeField] private NPCDialogueAI npcAI;
    [SerializeField] private Transform npc;
    [SerializeField] private Renderer npcRenderer;

    [Header("UI References")]
    [SerializeField] private GameObject dialoguePanel;
    [SerializeField] private TMP_Text dialogueText;
    [SerializeField] private TMP_InputField playerInput;
    [SerializeField] private Button sendButton;
    [SerializeField] private Button candyButton;
    [SerializeField] private Button threatButton;
    [SerializeField] private TMP_Text interactionPrompt;
    [SerializeField] private TMP_Text statusText;

    [Header("Gameplay Settings")]
    [SerializeField] private float interactionDistance = 3f;
    [SerializeField] private float nextEncounterDelay = 2f;

    private Vector3 playerStartPosition;
    private Quaternion playerStartRotation;

    private int encounterNumber = 1;
    private int candyCollected;
    private int timesThreatened;

    private bool encounterActive;
    private bool waitingForAI;
    private bool resetting;

    private readonly List<string> history = new List<string>();

    private readonly Color[] outfitColors =
    {
        new Color(0.20f, 0.35f, 0.65f),
        new Color(0.45f, 0.20f, 0.20f),
        new Color(0.20f, 0.40f, 0.25f),
        new Color(0.35f, 0.25f, 0.45f),
        new Color(0.50f, 0.40f, 0.20f)
    };

    private void Start()
    {
        if (player == null || npc == null || npcAI == null)
        {
            Debug.LogError(
                "GameManager: assign Player, NPC and NPC AI references."
            );
            enabled = false;
            return;
        }

        playerStartPosition = player.position;
        playerStartRotation = player.rotation;

        dialoguePanel.SetActive(false);
        interactionPrompt.gameObject.SetActive(false);

        sendButton.onClick.AddListener(SendPlayerDialogue);
        candyButton.onClick.AddListener(AcceptCandy);
        threatButton.onClick.AddListener(ThreatenNPC);

        DialogueOpen = false;
        UpdateStatus();
    }

    private void Update()
    {
        if (resetting || player == null || npc == null)
            return;

        if (encounterActive)
        {
            interactionPrompt.gameObject.SetActive(false);
            return;
        }

        float distance = Vector3.Distance(
            player.position, npc.position
        );

        bool nearby = distance <= interactionDistance;

        interactionPrompt.gameObject.SetActive(nearby);

        if (nearby && Input.GetKeyDown(KeyCode.E))
            StartEncounter();
    }

    private void StartEncounter()
    {
        encounterActive = true;
        waitingForAI = true;

        dialoguePanel.SetActive(true);
        DialogueOpen = true;

        SetButtons(false);
        playerInput.interactable = false;

        dialogueText.text = "The neighbor is opening the door...";

        RequestReply(
            "The child has arrived at your door for visit " +
            encounterNumber + ". Greet the child naturally. " +
            "React to the recent conversation and previous choices " +
            "if relevant. Do not narrate the child's actions."
        );
    }

    private void SendPlayerDialogue()
    {
        if (!encounterActive || waitingForAI || resetting)
            return;

        string message = playerInput.text.Trim();

        if (string.IsNullOrWhiteSpace(message))
            return;

        playerInput.text = "";

        history.Add("Child: " + message);

        SetButtons(false);
        playerInput.interactable = false;

        dialogueText.text = "The neighbor is thinking...";
        waitingForAI = true;

        RequestReply(
            "The child says: \"" + message + "\". " +
            "Respond directly in character. Do not choose an action " +
            "for the child."
        );
    }

    private string BuildContext(string currentSituation)
    {
        StringBuilder context = new StringBuilder();

        context.AppendLine("Game: Trick or Threat");
        context.AppendLine("Current visit: " + encounterNumber);
        context.AppendLine("Candy accepted: " + candyCollected);
        context.AppendLine("Times threatened: " + timesThreatened);
        context.AppendLine(
            "The same man and house recur in a strange Halloween loop."
        );

        context.AppendLine();
        context.AppendLine("Recent conversation:");

        int first = Mathf.Max(0, history.Count - 8);

        for (int i = first; i < history.Count; i++)
            context.AppendLine(history[i]);

        context.AppendLine();
        context.AppendLine("Current situation:");
        context.AppendLine(currentSituation);

        return context.ToString();
    }

    private void RequestReply(string situation)
    {
        string context = BuildContext(situation);

        npcAI.GenerateReply(
            context,
            OnReplyReceived,
            OnReplyFailed
        );
    }

    private void OnReplyReceived(string reply)
    {
        if (!this || !gameObject.activeInHierarchy)
            return;

        waitingForAI = false;

        history.Add("Neighbor: " + reply);
        dialogueText.text = reply;

        playerInput.interactable = true;
        SetButtons(true);
        UpdateStatus();

        playerInput.ActivateInputField();
    }

    private void OnReplyFailed(string error)
    {
        if (!this || !gameObject.activeInHierarchy)
            return;

        waitingForAI = false;
        dialogueText.text = error;

        playerInput.interactable = true;
        SetButtons(true);
    }

    private void SetButtons(bool enabledState)
    {
        sendButton.interactable = enabledState;
        candyButton.interactable = enabledState;
        threatButton.interactable = enabledState;
    }

    private void AcceptCandy()
    {
        if (!encounterActive || waitingForAI || resetting)
            return;

        candyCollected++;

        history.Add("Child: Accepted candy.");
        FinishEncounter(
            "You take the candy. The neighbor watches you leave."
        );
    }

    private void ThreatenNPC()
    {
        if (!encounterActive || waitingForAI || resetting)
            return;

        timesThreatened++;

        history.Add("Child: Chose to threaten the neighbor.");

        FinishEncounter(
            "The encounter ends after your threat. " +
            "The consequences may follow you into the next loop."
        );
    }

    private void FinishEncounter(string outcome)
    {
        resetting = true;
        SetButtons(false);
        playerInput.interactable = false;

        dialogueText.text = outcome;
        UpdateStatus();

        StartCoroutine(ResetEncounter());
    }

    private IEnumerator ResetEncounter()
    {
        yield return new WaitForSeconds(nextEncounterDelay);

        encounterNumber++;

        // Prototype outfit variation: change the NPC's color.
        if (npcRenderer != null && outfitColors.Length > 0)
        {
            Color color =
                outfitColors[(encounterNumber - 1) % outfitColors.Length];

            npcRenderer.material.color = color;
        }

        // Reset the player to the original approach position.
        CharacterController controller =
            player.GetComponent<CharacterController>();

        if (controller != null)
            controller.enabled = false;

        player.SetPositionAndRotation(
            playerStartPosition, playerStartRotation
        );

        if (controller != null)
            controller.enabled = true;

        dialoguePanel.SetActive(false);
        DialogueOpen = false;

        encounterActive = false;
        waitingForAI = false;
        resetting = false;

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (statusText == null)
            return;

        statusText.text =
            "Visit: " + encounterNumber +
            "    Candy: " + candyCollected +
            "    Threats: " + timesThreatened;
    }

    private void OnDestroy()
    {
        DialogueOpen = false;
    }
}
