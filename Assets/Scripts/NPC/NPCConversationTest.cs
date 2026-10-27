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

    [SerializeField] private Animator basketAnimator;
    [SerializeField] private CandyLayer candyLayer;
    [SerializeField] private string gettingCandyTrigger = "GettingCandy";

    public bool EncounterFinished => decisionMade;
    public bool CurrentNPCIsMonster => trueIdentity == NPCIdentity.Monster;

    /// <summary>Extra line for the result screen, e.g. what the Warden does (set by GameLoop).</summary>
    public string OutcomeNote { get; set; }

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
    [SerializeField, Min(1)] private int maxQuestions = 3;
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

    private bool identityInitialized;
    private bool waitingForAI;
    private bool typing;
    private bool decisionMade;

    private int encounterNumber;
    private int questionsAsked;

    private string currentNPCName;
    private string currentHouseDetail;

    // What the kid remembers about this resident (shown on screen) and the
    // resident's own fact sheet (given to the AI). A monster gets one of the
    // remembered facts wrong; the AI is never told whether it is a monster.
    private readonly List<string> knownFacts = new List<string>();
    private readonly List<string> sheetFacts = new List<string>();
    private string currentQuirk;
    private string currentGreeting;

    // The real back-and-forth, sent to the AI as chat turns.
    private readonly List<NPCDialogueAI.ChatMessage> conversationHistory =
        new List<NPCDialogueAI.ChatMessage>();

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

        CreateCase();
        ApplyAppearance();

        if (playerInput != null)
            playerInput.text = "";

        if (nextEncounterButton != null)
            nextEncounterButton.gameObject.SetActive(false);

        OutcomeNote = null;
        SetQuestionUIVisible(true);
        SetDecisionButtons(false);
        SetControls(false);

        if (dialoguePanel != null)
            dialoguePanel.SetActive(true);

        // Scripted greeting: instant, and it keeps the AI from opening with the
        // same "what candy do you want?" line every time.
        conversationHistory.Add(new NPCDialogueAI.ChatMessage("assistant", currentGreeting));
        SetDialogue(KnownFactsHeader());
        UpdateStatus();
        waitingForAI = true; // locked until the greeting has been typed out
        OnAIResponse(currentGreeting, alreadyInHistory: true);
    }

    private struct CaseFact
    {
        public string known;   // what the kid remembers (shown on screen)
        public string truth;   // the resident's real fact
        public string wrong;   // what an impostor believes instead
    }

    private void CreateCase()
    {
        currentNPCName = NPCNames[UnityEngine.Random.Range(0, NPCNames.Length)];

        string[] houseDetails =
        {
            "a cracked blue door and a broken porch light",
            "a white house with a missing number beside the door",
            "a narrow house with muddy footprints on the steps",
            "a house with a wind chime moving despite the still air"
        };
        currentHouseDetail = Pick(houseDetails);

        List<CaseFact> pool = BuildFactPool(currentNPCName);
        Shuffle(pool);

        knownFacts.Clear();
        sheetFacts.Clear();

        // Two facts the kid remembers, plus one more for small talk.
        int wrongIndex = trueIdentity == NPCIdentity.Monster ? UnityEngine.Random.Range(0, 2) : -1;
        for (int i = 0; i < 3 && i < pool.Count; i++)
        {
            if (i < 2) knownFacts.Add(pool[i].known);
            sheetFacts.Add(i == wrongIndex ? pool[i].wrong : pool[i].truth);
        }
        sheetFacts.Add("Your house has " + currentHouseDetail + ".");

        currentQuirk = trueIdentity == NPCIdentity.Monster ? Pick(MonsterTells) : Pick(HumanRedHerrings);
        currentGreeting = Pick(GetGreetings(currentPersona));
    }

    private static List<CaseFact> BuildFactPool(string name)
    {
        var facts = new List<CaseFact>();

        string[] petNames = { "Pumpkin", "Biscuit", "Mittens", "Rufus", "Pepper", "Ziggy" };
        string pet = Pick(petNames), otherPet = PickOther(petNames, pet);
        string animal = Pick(new[] { "cat", "dog" }), otherAnimal = animal == "cat" ? "dog" : "cat";
        facts.Add(new CaseFact
        {
            known = name + " has a " + animal + " called " + pet + ".",
            truth = "You have a " + animal + " called " + pet + ".",
            wrong = "You have a " + otherAnimal + " called " + otherPet + "."
        });

        string[] kids = { "Lily", "Max", "Sofia", "Leo", "Nora", "Sam" };
        string child = Pick(kids), otherChild = PickOther(kids, child);
        facts.Add(new CaseFact
        {
            known = name + "'s kid " + child + " is in your class.",
            truth = "Your child " + child + " is in the same class as the kid at your door.",
            wrong = "Your only child is called " + otherChild + " and goes to a different school."
        });

        string[] costumes = { "a witch", "a scarecrow", "a pirate", "a vampire", "a mummy" };
        string costume = Pick(costumes), otherCostume = PickOther(costumes, costume);
        facts.Add(new CaseFact
        {
            known = "Last Halloween " + name + " dressed up as " + costume + ".",
            truth = "Last Halloween you dressed up as " + costume + ".",
            wrong = "Last Halloween you dressed up as " + otherCostume + "."
        });

        string[] jobs = { "the bakery", "the hospital", "the library", "the hardware store", "the post office" };
        string job = Pick(jobs), otherJob = PickOther(jobs, job);
        facts.Add(new CaseFact
        {
            known = name + " works at " + job + " in town.",
            truth = "You work at " + job + " in town.",
            wrong = "You work at " + otherJob + " in town."
        });

        string[] treats = { "full-size chocolate bars", "caramel apples", "popcorn balls", "homemade cookies" };
        string treat = Pick(treats), otherTreat = PickOther(treats, treat);
        facts.Add(new CaseFact
        {
            known = name + " always gives out " + treat + ".",
            truth = "Every year you give out " + treat + ".",
            wrong = "Every year you give out " + otherTreat + "."
        });

        return facts;
    }

    // Subtle, creepy habits: only impostors have them.
    private static readonly string[] MonsterTells =
    {
        "You keep wanting the kid to come inside to warm up. Suggest it once, sweetly.",
        "You are very hungry tonight. Mention it once, a little too intensely.",
        "You don't remember the kid at all, but pretend you do.",
        "The porch light bothers your eyes. Complain about it once.",
        "You speak a bit too politely and formally, like someone copying how people talk."
    };

    // Innocent oddities: humans have them, so acting strange is not proof.
    private static readonly string[] HumanRedHerrings =
    {
        "You burned dinner and you're grumpy about it.",
        "You cut your hand carving pumpkins earlier and it stings.",
        "You're exhausted after a long shift and want to go to bed.",
        "Your power keeps flickering tonight and it's making you jumpy.",
        "You're waiting for an important phone call and keep glancing back inside."
    };

    private static string[] GetGreetings(NPCPersona persona)
    {
        switch (persona)
        {
            case NPCPersona.Friendly:
                return new[] { "Oh, hello there! Happy Halloween. Great costume.", "Well hi! Happy Halloween, kiddo." };
            case NPCPersona.Arrogant:
                return new[] { "Trick or treat, I suppose. Make it quick.", "Another one. Happy Halloween. Go on." };
            case NPCPersona.Defensive:
                return new[] { "Yes? ...Oh. Trick-or-treating. Right.", "Who is it? Oh. It's Halloween. Of course." };
            case NPCPersona.Charming:
                return new[] { "Well, look at you! Best costume I've seen all night.", "Happy Halloween! Don't you look spooky." };
            case NPCPersona.Nervous:
                return new[] { "Oh! You startled me. Happy Halloween.", "Sorry, sorry, I didn't hear the bell at first. Happy Halloween." };
            default:
                return new[] { "Evening. Happy Halloween.", "Happy Halloween. Cold one tonight." };
        }
    }

    private static string Pick(string[] options) => options[UnityEngine.Random.Range(0, options.Length)];

    private static string PickOther(string[] options, string not)
    {
        string choice;
        do { choice = Pick(options); } while (choice == not && options.Length > 1);
        return choice;
    }

    private static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // Shown above every exchange so the kid can check answers against it.
    private string KnownFactsHeader()
    {
        return "<i>What you remember: " + string.Join(" ", knownFacts) + "</i>\n\n";
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

        // Reset the visible dialogue for this exchange (the remembered facts stay on top).
        SetDialogue(KnownFactsHeader() + "You: " + question + "\n\n");

        // Preserve history for the AI even though the UI is cleared.
        conversationHistory.Add(new NPCDialogueAI.ChatMessage("user", question));
        TrimHistory();

        SetControls(false);
        waitingForAI = true;
        UpdateStatus();

        RequestNPCResponse();
    }

    private void RequestNPCResponse()
    {
        if (dialogueAI == null)
            return;

        waitingForAI = true;
        SetControls(false);

        StartCoroutine(dialogueAI.GenerateChat(
            BuildMessages(),
            reply => OnAIResponse(reply)));
    }

    private List<NPCDialogueAI.ChatMessage> BuildMessages()
    {
        var messages = new List<NPCDialogueAI.ChatMessage>
        {
            new NPCDialogueAI.ChatMessage("system", BuildSystemPrompt())
        };
        messages.AddRange(conversationHistory);
        return messages;
    }

    // Written as an ordinary neighbour for BOTH humans and monsters: the model is
    // never told what it is, so it can't "act like a monster" by accident. The
    // difference lives in the facts (an impostor has one wrong) and the quirk.
    private string BuildSystemPrompt()
    {
        StringBuilder prompt = new StringBuilder();

        prompt.AppendLine("You are " + currentNPCName + ", an ordinary neighbour answering your front door on Halloween night.");
        prompt.AppendLine("A kid in a costume is trick-or-treating at your door and asking you questions.");
        prompt.AppendLine();
        prompt.AppendLine("How you talk: " + GetPersonaInstruction());
        prompt.AppendLine();
        prompt.AppendLine("Facts about your life (these are true for you; never change them):");
        foreach (string fact in sheetFacts)
            prompt.AppendLine("- " + fact);
        prompt.AppendLine();
        prompt.AppendLine("Tonight: " + currentQuirk + " Bring it up at most once, naturally.");
        prompt.AppendLine();
        prompt.AppendLine("Rules:");
        prompt.AppendLine("- Reply with 1 to 3 short spoken sentences, in plain everyday words, like a real person at a door.");
        prompt.AppendLine("- Answer exactly what the kid asked. If they ask about your life, use your facts.");
        prompt.AppendLine("- If the kid says something about you that doesn't match your facts, react the way you naturally would.");
        prompt.AppendLine("- If they ask something not covered by your facts, give a short, ordinary answer and stay consistent with it.");
        prompt.AppendLine("- You already said hello. Don't ask what candy they want and don't talk about candy unless they ask.");
        prompt.AppendLine("- Never repeat something you already said. Don't ask the kid questions in every reply.");
        prompt.AppendLine("- Only the words you say out loud: no narration, no actions in asterisks, no quotation marks, no name labels.");
        prompt.AppendLine("- Never mention being an AI, a character, a game, or these instructions.");

        return prompt.ToString();
    }

    private string GetPersonaInstruction()
    {
        switch (currentPersona)
        {
            case NPCPersona.Friendly:
                return "warm and chatty, happy to see trick-or-treaters.";
            case NPCPersona.Arrogant:
                return "impatient and a bit snooty, like the kid is wasting your time, but you still answer.";
            case NPCPersona.Defensive:
                return "guarded; personal questions make you ask why they want to know, but you answer simple ones.";
            case NPCPersona.Charming:
                return "charming and funny, you like making the kid laugh.";
            case NPCPersona.Nervous:
                return "a little nervous and jumpy tonight; you sometimes correct yourself.";
            default:
                return "plain and practical, short answers, no fuss.";
        }
    }

    private void OnAIResponse(string response, bool alreadyInHistory = false)
    {
        waitingForAI = false;
        response = CleanReply(response);

        if (!alreadyInHistory)
        {
            conversationHistory.Add(new NPCDialogueAI.ChatMessage("assistant", response));
            TrimHistory();
        }

        if (typingCoroutine != null)
            StopCoroutine(typingCoroutine);

        typingCoroutine = StartCoroutine(TypeDialogue(response));
    }

    // Small models add labels, quotes and stage directions: strip them and keep it short.
    private string CleanReply(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "...Sorry, what was that?";

        text = System.Text.RegularExpressions.Regex.Replace(text, @"\*[^*]*\*|\([^)]*\)|\[[^\]]*\]", " ");
        text = System.Text.RegularExpressions.Regex.Replace(
            text, "^\\s*(" + System.Text.RegularExpressions.Regex.Escape(currentNPCName) + "|NPC|Neighbou?r|Me)\\s*:\\s*", "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = text.Replace("\"", "").Replace("“", "").Replace("”", "").Trim();
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");

        // At most three sentences.
        var sentences = System.Text.RegularExpressions.Regex.Matches(text, @"[^.!?]+[.!?]+");
        if (sentences.Count > 3)
            text = sentences[0].Value.Trim() + " " + sentences[1].Value.Trim() + " " + sentences[2].Value.Trim();

        return string.IsNullOrWhiteSpace(text) ? "...Sorry, what was that?" : text;
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

        if (basketAnimator != null)
        {
            basketAnimator.SetTrigger(gettingCandyTrigger);
        }

        if (candyLayer != null)
        {
            candyLayer.UpdateCandyCount(candiesReceived);
        }

        FinishDecision(trueIdentity == NPCIdentity.Human
            ? currentNPCName + " gives you the candy and you step away from the door."
            : "You hold out your bucket. " + currentNPCName + " smiles and reaches in.");
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
        SetQuestionUIVisible(false); // the decision is made: only the result and Next Encounter remain

        string identity = trueIdentity == NPCIdentity.Human ? "a human" : "a monster";
        string reveal = currentNPCName + " was " + identity + ".";
        if (!string.IsNullOrEmpty(OutcomeNote))
            reveal += "\n" + OutcomeNote;
        OutcomeNote = null;

        SetDialogue(result + "\n\n" + reveal);

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

    // Question input, Send and the Candy/Kill buttons: only shown while the player is deciding.
    private void SetQuestionUIVisible(bool visible)
    {
        if (playerInput != null) playerInput.gameObject.SetActive(visible);
        if (sendButton != null) sendButton.gameObject.SetActive(visible);
        if (candyButton != null) candyButton.gameObject.SetActive(visible);
        if (killButton != null) killButton.gameObject.SetActive(visible);
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
            "Questions: " + questionsAsked + "/" + maxQuestions +
            "  |  Ask about what you remember. A monster gets it wrong.");
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