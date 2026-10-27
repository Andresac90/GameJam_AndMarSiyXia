using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class NPCConversationTest : MonoBehaviour
{
    public event Action KillChosen;
    public event Action NextEncounterRequested;
    public event Action CandyChosen;

    [Header("Encounter Integration")]
    [SerializeField] private bool startEncounterOnPlay = true;

    public bool EncounterFinished => decisionMade;

    private enum NPCIdentity
    {
        Human,
        Monster
    }

    private enum NPCPersona
    {
        Friendly,
        Arrogant,
        Defensive,
        Charming,
        Nervous,
        MatterOfFact
    }

    private enum DeceptionStyle
    {
        Omission,
        DirectLie,
        MisleadingTruth,
        Deflection,
        EmotionalManipulation
    }

    [Header("AI")]
    [SerializeField] private NPCDialogueAI dialogueAI;

    [Header("UI References")]
    [SerializeField] private GameObject dialoguePanel;
    [SerializeField] private TMP_Text dialogueText;
    [SerializeField] private TMP_InputField playerInput;
    [SerializeField] private Button sendButton;
    [SerializeField] private Button candyButton;
    [SerializeField] private Button killButton;
    [SerializeField] private Button nextEncounterButton;
    [SerializeField] private TMP_Text statusText;

    [Header("NPC Appearance")]
    [SerializeField] private Renderer npcRenderer;
    [SerializeField] private Material[] personaMaterials;

    [Header("Conversation")]
    [SerializeField, Min(1)] private int maxQuestions = 4;
    [SerializeField] private bool keepIdentityAcrossEncounters = true;
    [SerializeField, Min(1)] private int maxHistoryEntries = 12;

    [Header("Typewriter")]
    [SerializeField, Min(1)] private float charactersPerSecond = 35f;
    [SerializeField, Min(0f)] private float punctuationPause = 0.12f;

    [Header("Test Results")]
    [SerializeField] private int timesSpared;
    [SerializeField] private int timesKilled;
    [SerializeField] private int candiesReceived;

    private NPCIdentity trueIdentity;
    private NPCPersona currentPersona;
    private DeceptionStyle deceptionStyle;

    private bool identityInitialized;
    private bool waitingForAI;
    private bool typing;
    private bool decisionMade;

    private int encounterNumber;
    private int questionsAsked;

    private string currentNPCName;
    private string currentMotive;
    private string currentSecret;
    private string currentTruth;
    private string currentCoverStory;
    private string currentClue;
    private string currentHouseDetail;

    // Kept separately from the visible dialogue.
    // The AI still receives this history for context.
    private readonly List<string> conversationHistory =
        new List<string>();

    private Coroutine typingCoroutine;

    private static readonly string[] NPCNames =
    {
        "Evelyn",
        "Thomas",
        "Mara",
        "Daniel",
        "Helen",
        "Arthur"
    };

    private void Start()
    {
        if (dialogueAI == null)
            dialogueAI = GetComponent<NPCDialogueAI>();

        if (dialoguePanel != null)
            dialoguePanel.SetActive(true);

        if (sendButton != null)
            sendButton.onClick.AddListener(SendQuestion);

        if (playerInput != null)
        {
            playerInput.onSubmit.AddListener(
                delegate { SendQuestion(); });
        }

        if (candyButton != null)
            candyButton.onClick.AddListener(ChooseCandy);

        if (killButton != null)
            killButton.onClick.AddListener(ChooseKill);

        if (nextEncounterButton != null)
        {
            nextEncounterButton.onClick.AddListener(
                StartNextEncounter);
        }

        if (dialogueAI == null)
        {
            SetDialogue("ERROR: NPCDialogueAI is missing.");
            SetStatus("AI component missing");
            SetControls(false);
            return;
        }

        if (keepIdentityAcrossEncounters)
        {
            trueIdentity = UnityEngine.Random.value < 0.5f
                ? NPCIdentity.Human
                : NPCIdentity.Monster;

            identityInitialized = true;
        }

        if (startEncounterOnPlay)
            BeginEncounter();
        else
        {
            if (dialoguePanel != null)
                dialoguePanel.SetActive(false);
            SetControls(false);
            SetDecisionButtons(false);
            SetStatus("Waiting for the player to knock.");
        }
    }

    /// <summary>Starts a dialogue encounter. Called by DoorEncounter after a knock.</summary>
    public void BeginEncounter()
    {
        if (dialogueAI == null)
        {
            SetDialogue("ERROR: NPCDialogueAI is missing.");
            SetStatus("AI component missing");
            return;
        }

        if (dialoguePanel != null)
            dialoguePanel.SetActive(true);

        StartEncounter();
    }

    /// <summary>
    /// Lets a spawned house hand over its NPC's renderer, so persona materials
    /// still work when the NPC is part of a prefab instead of a scene object.
    /// </summary>
    public void SetNPCRenderer(Renderer renderer)
    {
        npcRenderer = renderer;
    }

    /// <summary>Shows the reveal after the player actually shot the NPC.</summary>
    public void ShowKillResult()
    {
        if (dialoguePanel != null)
            dialoguePanel.SetActive(true);

        FinishDecision(
            "You put " + currentNPCName + " down on their own porch. " +
            "What's left of them is dripping off the door frame.");
    }

    private void StartEncounter()
    {
        encounterNumber++;
        questionsAsked = 0;
        waitingForAI = false;
        typing = false;
        decisionMade = false;

        conversationHistory.Clear();

        // Clear all visible text from the previous encounter.
        SetDialogue("");

        if (!keepIdentityAcrossEncounters || !identityInitialized)
        {
            trueIdentity = UnityEngine.Random.value < 0.5f
                ? NPCIdentity.Human
                : NPCIdentity.Monster;

            identityInitialized = true;
        }

        currentPersona = (NPCPersona)UnityEngine.Random.Range(
            0, Enum.GetValues(typeof(NPCPersona)).Length);

        deceptionStyle = (DeceptionStyle)UnityEngine.Random.Range(
            0, Enum.GetValues(typeof(DeceptionStyle)).Length);

        CreateCase();
        ApplyAppearance();

        if (playerInput != null)
            playerInput.text = "";

        if (nextEncounterButton != null)
            nextEncounterButton.gameObject.SetActive(false);

        SetDecisionButtons(false);
        SetControls(false);

        if (dialoguePanel != null)
            dialoguePanel.SetActive(true);

        SetDialogue(
            "You approach a house emerging from the fog.\n" +
            "The door opens halfway.\n\n");

        SetStatus(
            "Encounter " + encounterNumber +
            " | Questions: 0/" + maxQuestions);

        RequestNPCResponse(
            "Begin the encounter. Say something appropriate " +
            "as the person answering the door. Do not explain " +
            "your hidden identity or reveal your secret.");
    }

    private void CreateCase()
    {
        currentNPCName = NPCNames[
            UnityEngine.Random.Range(0, NPCNames.Length)];

        string[] houseDetails =
        {
            "a cracked blue door and a broken porch light",
            "a white house with a missing number beside the door",
            "a narrow house with muddy footprints on the steps",
            "a house with a wind chime moving despite the still air"
        };

        currentHouseDetail = houseDetails[
            UnityEngine.Random.Range(0, houseDetails.Length)];

        if (trueIdentity == NPCIdentity.Human)
            CreateHumanCase();
        else
            CreateMonsterCase();
    }

    private void CreateHumanCase()
    {
        currentMotive =
            "You want to protect your privacy and avoid being judged. " +
            "You are not trying to kill the visitor.";

        currentSecret =
            "Earlier tonight, you went outside even though you told " +
            "your neighbours you had stayed home all evening.";

        currentTruth =
            "You are a real resident of this house. Earlier tonight, " +
            "you went outside to meet someone and returned alone. " +
            "You do not want to explain who you met.";

        currentCoverStory =
            "You claim that you stayed home all evening. If asked " +
            "directly, you initially deny going outside. If the player " +
            "presents a specific contradiction, admit a limited part " +
            "of the truth but conceal whom you met.";

        currentClue =
            "Your account of staying home conflicts with the muddy " +
            "shoes near the door. Nervousness alone proves nothing.";
    }

    private void CreateMonsterCase()
    {
        currentMotive =
            "You are impersonating a resident. You want to learn " +
            "whether someone else is inside and whether the visitor " +
            "knows the real resident. Do not openly confess this.";

        currentSecret =
            "You are not the real resident. You entered the house " +
            "after the real resident disappeared. You know the basic " +
            "layout but do not know the resident's private memories.";

        currentTruth =
            "You are a monster pretending to be the resident. " +
            "You know a few obvious facts about the house, but you " +
            "do not know personal details that were never visible.";

        currentCoverStory =
            "You claim to be the resident. Give plausible, simple " +
            "answers about the house. When asked about private memories, " +
            "invent a vague answer, reinterpret the question, or ask " +
            "what the visitor already knows. Vary your tactics.";

        currentClue =
            "You may be vague about a personal memory or make a small " +
            "inconsistency when asked for a specific detail. You can " +
            "still sound warm, funny, patient, or convincing.";
    }

    private void ApplyAppearance()
    {
        if (npcRenderer == null ||
            personaMaterials == null ||
            personaMaterials.Length == 0)
        {
            return;
        }

        int index = (int)currentPersona % personaMaterials.Length;

        if (personaMaterials[index] != null)
            npcRenderer.material = personaMaterials[index];
    }

    private void SendQuestion()
    {
        if (waitingForAI || typing || decisionMade)
            return;

        if (questionsAsked >= maxQuestions)
        {
            SetStatus("You have used all your questions.");
            return;
        }

        if (playerInput == null)
            return;

        string question = playerInput.text.Trim();

        if (string.IsNullOrWhiteSpace(question))
            return;

        playerInput.text = "";
        questionsAsked++;

        // Reset the visible dialogue for this exchange.
        SetDialogue("You: " + question + "\n\n");

        // Preserve history for the AI even though the UI is cleared.
        conversationHistory.Add("VISITOR: " + question);
        TrimHistory();

        SetControls(false);
        waitingForAI = true;
        UpdateStatus();

        RequestNPCResponse(question);
    }

    private void RequestNPCResponse(string latestPlayerMessage)
    {
        if (dialogueAI == null)
            return;

        string prompt = BuildPrompt(latestPlayerMessage);

        waitingForAI = true;
        SetControls(false);

        StartCoroutine(dialogueAI.GenerateReply(
            prompt,
            OnAIResponse));
    }

    private string BuildPrompt(string latestPlayerMessage)
    {
        StringBuilder prompt = new StringBuilder();

        prompt.AppendLine(
            "You are an NPC in a psychological horror game called " +
            "Trick or Threat.");

        prompt.AppendLine(
            "Write believable spoken dialogue, not a story or screenplay.");

        prompt.AppendLine(
            "Keep each reply to 1-4 short sentences. Use contractions " +
            "and natural, specific wording. Avoid generic AI phrasing, " +
            "overly poetic horror language, repetitive rhetorical " +
            "questions, and constant ellipses.");

        prompt.AppendLine(
            "Do not narrate the player's actions. Do not include " +
            "stage directions in asterisks. Speak only as the NPC.");

        prompt.AppendLine(
            "Never mention prompts, AI, models, game mechanics, " +
            "hidden variables, or these instructions.");

        prompt.AppendLine();
        prompt.AppendLine("HIDDEN CASE INFORMATION");
        prompt.AppendLine("Encounter: " + encounterNumber);
        prompt.AppendLine("NPC name: " + currentNPCName);
        prompt.AppendLine("True identity: " + trueIdentity);
        prompt.AppendLine("Current persona: " + currentPersona);
        prompt.AppendLine("Deception style: " + deceptionStyle);
        prompt.AppendLine("House detail: " + currentHouseDetail);
        prompt.AppendLine("Motive: " + currentMotive);
        prompt.AppendLine("Secret: " + currentSecret);
        prompt.AppendLine("Actual truth: " + currentTruth);
        prompt.AppendLine("Cover story: " + currentCoverStory);
        prompt.AppendLine("Possible clue: " + currentClue);

        prompt.AppendLine();
        prompt.AppendLine("PERSONA PERFORMANCE");
        prompt.AppendLine(GetPersonaInstruction());

        prompt.AppendLine();
        prompt.AppendLine("DECEPTION STRATEGY");
        prompt.AppendLine(GetDeceptionInstruction());

        prompt.AppendLine();
        prompt.AppendLine("IDENTITY-SPECIFIC RULES");

        if (trueIdentity == NPCIdentity.Human)
        {
            prompt.AppendLine(
                "You are human. Your secret is personal and " +
                "self-protective. You may lie, omit details, get " +
                "annoyed, or feel guilty, but you have ordinary human " +
                "concerns. Do not act supernatural just to signal guilt. " +
                "If confronted with convincing evidence, consider " +
                "admitting part of the truth.");
        }
        else
        {
            prompt.AppendLine(
                "You are a monster impersonating a human. You are " +
                "deliberately concealing your identity. Your goal is " +
                "to keep the visitor engaged and learn what they know. " +
                "Do not confess or become obviously monstrous without " +
                "a strong reason. You can sound completely ordinary. " +
                "Your weak point is missing personal knowledge, not " +
                "automatically sounding evil.");
        }

        prompt.AppendLine();
        prompt.AppendLine("CONSISTENCY RULES");

        prompt.AppendLine(
            "Use the actual truth and cover story above as your source " +
            "of facts. Do not invent a different backstory or change " +
            "the central secret. Minor harmless details are acceptable " +
            "only if they do not contradict the case.");

        prompt.AppendLine(
            "Answer the question that was actually asked. Do not " +
            "automatically evade every question. Change your response " +
            "according to the question and conversation.");

        prompt.AppendLine(
            "Do not reveal your true identity, motive, secret, or " +
            "possible clue directly. The player must infer them.");

        prompt.AppendLine(
            "The player has asked " + questionsAsked + " of " +
            maxQuestions + " available questions.");

        // Full retained history is supplied to the AI here.
        prompt.AppendLine();
        prompt.AppendLine("CONVERSATION HISTORY");

        foreach (string entry in conversationHistory)
            prompt.AppendLine(entry);

        prompt.AppendLine();
        prompt.AppendLine("LATEST PLAYER MESSAGE");
        prompt.AppendLine(latestPlayerMessage);

        prompt.AppendLine();
        prompt.AppendLine(
            "Respond in character. Output only the words spoken " +
            "by the NPC.");

        return prompt.ToString();
    }

    private string GetPersonaInstruction()
    {
        switch (currentPersona)
        {
            case NPCPersona.Friendly:
                return "Be welcoming and conversational. Use small " +
                       "social gestures naturally. Do not agree with " +
                       "everything or sound artificially cheerful.";

            case NPCPersona.Arrogant:
                return "Act as though the visitor is wasting your time. " +
                       "Be dismissive and confident, but vary your " +
                       "wording. Do not insult the player in every reply.";

            case NPCPersona.Defensive:
                return "Be sensitive to accusations. Ask why the visitor " +
                       "wants to know, but answer straightforward " +
                       "questions when appropriate.";

            case NPCPersona.Charming:
                return "Be socially skilled and persuasive. Use humour, " +
                       "reassurance, or compliments naturally. Do not " +
                       "turn every response into manipulation.";

            case NPCPersona.Nervous:
                return "Sound worried about the situation. Use occasional " +
                       "self-corrections or uncertainty, not constant " +
                       "stammering. Nervousness does not determine guilt.";

            default:
                return "Be plain-spoken and practical. Give concise " +
                       "answers without unnecessary emotional colour.";
        }
    }

    private string GetDeceptionInstruction()
    {
        switch (deceptionStyle)
        {
            case DeceptionStyle.Omission:
                return "Prefer leaving out a relevant detail rather " +
                       "than stating an outright falsehood. Answer " +
                       "part of the question truthfully.";

            case DeceptionStyle.DirectLie:
                return "When the secret is threatened, you may deny " +
                       "a relevant fact. Keep the lie consistent with " +
                       "your cover story. Do not deny everything.";

            case DeceptionStyle.MisleadingTruth:
                return "Use technically true statements that could " +
                       "give the visitor the wrong impression. Avoid " +
                       "obvious word games.";

            case DeceptionStyle.Deflection:
                return "Sometimes redirect the conversation or ask " +
                       "why the visitor wants to know. Still answer " +
                       "some questions directly.";

            default:
                return "Sometimes appeal to sympathy, politeness, or " +
                       "guilt to discourage further questions. Keep it " +
                       "subtle and do not overuse emotional pressure.";
        }
    }

    private void OnAIResponse(string response)
    {
        waitingForAI = false;

        conversationHistory.Add("NPC: " + response);
        TrimHistory();

        if (typingCoroutine != null)
            StopCoroutine(typingCoroutine);

        typingCoroutine = StartCoroutine(TypeDialogue(response));
    }

    private IEnumerator TypeDialogue(string response)
    {
        typing = true;
        SetControls(false);

        if (dialogueText != null)
        {
            // The player's current question remains visible above.
            dialogueText.text += currentNPCName + ": ";

            foreach (char character in response)
            {
                dialogueText.text += character;

                if (character == '.' || character == '!' ||
                    character == '?' || character == ',')
                {
                    yield return new WaitForSeconds(
                        punctuationPause);
                }

                yield return new WaitForSeconds(
                    1f / charactersPerSecond);
            }

            dialogueText.text += "\n";
        }

        typing = false;
        typingCoroutine = null;

        if (decisionMade)
            yield break;

        if (questionsAsked >= maxQuestions)
        {
            SetControls(false);
            SetDecisionButtons(true);

            SetStatus(
                "No questions remaining. Decide: Candy or Kill.");
        }
        else
        {
            SetControls(true);
            SetDecisionButtons(false);
            UpdateStatus();

            if (playerInput != null)
                playerInput.ActivateInputField();
        }
    }

    private void ChooseCandy()
    {
        if (waitingForAI || typing || decisionMade ||
            questionsAsked < maxQuestions)
        {
            return;
        }

        decisionMade = true;
        CandyChosen?.Invoke();
        timesSpared++;
        candiesReceived++;

        FinishDecision(
            "You give " + currentNPCName +
            " the candy and step away from the door.");
    }

    private void ChooseKill()
    {
        if (waitingForAI || typing || decisionMade ||
            questionsAsked < maxQuestions)
        {
            return;
        }

        decisionMade = true;
        timesKilled++;

        if (KillChosen == null)
        {
            FinishDecision(
                "You attack " + currentNPCName +
                " before the door can close.");
            return;
        }

        // Hand control to the player: they have to pull the trigger themselves.
        SetControls(false);
        SetDecisionButtons(false);

        if (dialoguePanel != null)
            dialoguePanel.SetActive(false);

        KillChosen.Invoke();
    }


    private void FinishDecision(string result)
    {
        SetControls(false);
        SetDecisionButtons(false);

        string identityResult =
            trueIdentity == NPCIdentity.Human
                ? "HUMAN"
                : "MONSTER";

        SetDialogue(
            result + "\n\n" +
            "TEST REVEAL: " + identityResult
        );

        SetStatus("Encounter complete.");

        if (nextEncounterButton != null)
            nextEncounterButton.gameObject.SetActive(true);
    }


    /// <summary>Hides the result panel until the player knocks on the next door.</summary>
    public void WaitForNextKnock()
    {
        if (dialoguePanel != null)
            dialoguePanel.SetActive(false);
        if (nextEncounterButton != null)
            nextEncounterButton.gameObject.SetActive(false);
        SetControls(false);
        SetDecisionButtons(false);
    }

    private void StartNextEncounter()
    {
        if (NextEncounterRequested != null)
            NextEncounterRequested.Invoke();
        else
            BeginEncounter();
    }

    private void SetControls(bool enabled)
    {
        bool canAsk = enabled &&
                      !waitingForAI &&
                      !typing &&
                      !decisionMade &&
                      questionsAsked < maxQuestions;

        if (playerInput != null)
            playerInput.interactable = canAsk;

        if (sendButton != null)
            sendButton.interactable = canAsk;
    }

    private void SetDecisionButtons(bool enabled)
    {
        bool canDecide = enabled &&
                         !waitingForAI &&
                         !typing &&
                         !decisionMade &&
                         questionsAsked >= maxQuestions;

        if (candyButton != null)
            candyButton.interactable = canDecide;

        if (killButton != null)
            killButton.interactable = canDecide;
    }

    private void UpdateStatus()
    {
        SetStatus(
            "Encounter " + encounterNumber +
            " | Persona: " + currentPersona +
            " | Questions: " + questionsAsked +
            "/" + maxQuestions);
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
    }

    private void SetDialogue(string message)
    {
        if (dialogueText != null)
            dialogueText.text = message;
    }

    private void AddDialogue(string message)
    {
        if (dialogueText != null)
            dialogueText.text += message;
    }

    private void TrimHistory()
    {
        while (conversationHistory.Count > maxHistoryEntries)
            conversationHistory.RemoveAt(0);
    }
}