using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>
/// Trick or Threat game loop. Win by collecting candy; lose when the Warden catches you.
///
///   Outcome        | Candy | Timer     | Warden                          | Beacon
///   Spare human    | +1    | Resets    | Re-freezes where he stands      | No
///   Kill human     | 0     | Set to 0  | Wakes and chases                | No
///   Kill monster   | 0     | Unchanged | Unchanged                       | Next house lights up
///   Spare monster  | -1    | Unchanged | Unchanged, unless you had 0 candy (then wakes) | No
///
/// The timer counts down per house while the player can move; at 0 the Warden wakes.
/// Listens to the existing conversation/spawner events; put it on any object in the play scene.
/// </summary>
public class GameLoop : MonoBehaviour
{
    [Header("Rules")]
    [SerializeField, Min(1)] private int candyToWin = 5;
    [SerializeField, Min(5f)] private float houseTime = 90f;
    [Tooltip("Light the first house's lamps so new players can find it.")]
    [SerializeField] private bool firstHouseLit = true;
    [Tooltip("Stop the clock while the dialogue is open (the AI can take a while to answer).")]
    [SerializeField] private bool pauseTimerDuringDialogue = true;

    [Header("Warden")]
    [SerializeField] private Warden warden;
    [SerializeField, Min(5f)] private float wardenSpawnDistance = 25f;
    [Tooltip("Being this close to the current house's door counts as being at the house (safe from the Warden).")]
    [SerializeField, Min(1f)] private float sanctuaryRadius = 9f;

    [Header("References (auto-found if empty)")]
    [SerializeField] private NPCConversationTest conversation;
    [SerializeField] private HouseSpawner spawner;
    [SerializeField] private FirstPersonController_TEST player;
    [SerializeField] private FieldBounds field;

    [Header("Scenes")]
    [SerializeField] private string mainMenuScene = "Scene_MainMenu";

    [Header("Events")]
    [Tooltip("Called with the new candy count, e.g. to fill the candy bucket.")]
    public UnityEvent<int> onCandyChanged;
    public UnityEvent onWin;
    public UnityEvent onLose;

    public int Candy { get; private set; }
    public float TimeLeft { get; private set; }

    private bool timerExpired;
    private bool beaconPending;
    private bool ended;
    private bool won;
    private string message;
    private float messageUntil;
    private float flashUntil;
    private Transform currentDoor;
    private bool inEncounter;
    private bool sanctuaryWarned;

    private void Awake()
    {
        if (conversation == null) conversation = FindAnyObjectByType<NPCConversationTest>();
        if (spawner == null) spawner = FindAnyObjectByType<HouseSpawner>();
        if (player == null) player = FindAnyObjectByType<FirstPersonController_TEST>();
        if (field == null) field = FindAnyObjectByType<FieldBounds>();

        TimeLeft = houseTime;
        beaconPending = firstHouseLit;

        // Subscribe before HouseSpawner.Start spawns the first house.
        if (spawner != null) spawner.HouseSpawned += OnHouseSpawned;
    }

    private void OnEnable()
    {
        if (conversation == null) return;
        conversation.CandyChosen += OnSpared;
        conversation.KillChosen += OnKilled;
        conversation.NextEncounterRequested += OnLeftHouse;
    }

    private void OnDisable()
    {
        if (conversation != null)
        {
            conversation.CandyChosen -= OnSpared;
            conversation.KillChosen -= OnKilled;
            conversation.NextEncounterRequested -= OnLeftHouse;
        }
        if (spawner != null) spawner.HouseSpawned -= OnHouseSpawned;
    }

    private void Start()
    {
        if (warden == null) warden = SpawnWarden();
        if (warden != null)
        {
            warden.Freeze();
            warden.CaughtPlayer += OnCaught;
        }

        onCandyChanged?.Invoke(Candy);
        Show($"Trick or treat. Bring home {candyToWin} candy... and don't wake the Warden.", 6f);
    }

    private void Update()
    {
        if (ended) return;

        UpdateSanctuary();

        if (timerExpired) return;

        bool inDialogue = player != null && !player.ControlsEnabled;
        if (pauseTimerDuringDialogue && inDialogue) return;

        TimeLeft -= Time.deltaTime;
        if (TimeLeft <= 0f)
            ExpireTimer("Time's up. The Warden wakes.");
    }

    // ------------------------------------------------------------------ rules

    private void OnSpared()
    {
        if (ended) return;

        if (!conversation.CurrentNPCIsMonster)
        {
            SetCandy(Candy + 1);
            TimeLeft = houseTime;
            timerExpired = false;
            if (warden != null) warden.Freeze();

            if (Candy >= candyToWin)
            {
                StartCoroutine(WinRoutine());
                return;
            }

            Show("They were human. +1 candy. The Warden freezes where he stands.");
        }
        else if (Candy <= 0)
        {
            if (warden != null) warden.Wake();
            Show("You had nothing to steal... the Warden stirs.");
        }
        else
        {
            SetCandy(Candy - 1);
            Show("The monster stole a candy!");
        }
    }

    private void OnKilled()
    {
        if (ended) return;

        if (conversation.CurrentNPCIsMonster)
        {
            beaconPending = true;
            Show("It was a monster. The next house lights up.");
        }
        else
        {
            ExpireTimer("That was a person. The Warden wakes and comes for you.");
        }
    }

    private void ExpireTimer(string text)
    {
        TimeLeft = 0f;
        timerExpired = true;
        if (warden != null) warden.Wake();
        Show(text);
    }

    private void SetCandy(int value)
    {
        Candy = Mathf.Max(0, value);
        onCandyChanged?.Invoke(Candy);
    }

    private void OnHouseSpawned(GameObject house)
    {
        HouseLampposts lamps = house.GetComponent<HouseLampposts>();
        if (lamps != null) lamps.Lit = beaconPending;
        beaconPending = false;

        inEncounter = false;
        HouseDoor door = house.GetComponentInChildren<HouseDoor>(true);
        currentDoor = door != null ? door.transform : house.transform;
        if (door != null) door.Knocked += () => inEncounter = true;
    }

    private void OnLeftHouse() => inEncounter = false;

    // The house is a sanctuary: from arriving at the door until the player leaves
    // (Next Encounter), the Warden only lurks at a distance and watches.
    private void UpdateSanctuary()
    {
        if (warden == null || player == null) return;

        bool nearDoor = false;
        if (currentDoor != null)
        {
            Vector3 offset = player.transform.position - currentDoor.position;
            offset.y = 0f;
            nearDoor = offset.magnitude <= sanctuaryRadius;
        }

        bool safe = inEncounter || nearDoor;
        warden.HoldBack = safe;

        if (safe && warden.IsAwake && !sanctuaryWarned)
        {
            sanctuaryWarned = true;
            Show("The Warden stops at the edge of the light... it's waiting for you to leave.");
        }
        else if (!safe)
        {
            sanctuaryWarned = false;
        }
    }

    // ------------------------------------------------------------ win / lose

    private IEnumerator WinRoutine()
    {
        ended = true;
        won = true;
        Show($"{candyToWin} candy. That's enough for tonight.");
        yield return new WaitForSeconds(2f);
        EndGame();
        onWin?.Invoke();
    }

    private void OnCaught()
    {
        if (ended) return;
        StartCoroutine(CaughtRoutine());
    }

    private IEnumerator CaughtRoutine()
    {
        ended = true;
        won = false;

        if (player != null) player.SetControlsEnabled(false);
        Camera cam = Camera.main;
        if (cam != null && warden != null) warden.JumpScare(cam.transform);
        flashUntil = Time.time + 0.6f;

        yield return new WaitForSeconds(1.6f);
        EndGame();
        onLose?.Invoke();
    }

    private void EndGame()
    {
        if (conversation != null) conversation.WaitForNextKnock();
        if (warden != null && won) warden.Freeze();
        if (player != null) player.SetControlsEnabled(false);
    }

    private Warden SpawnWarden()
    {
        Vector3 origin = player != null ? player.transform.position : transform.position;
        Vector3 position = origin + Vector3.forward * wardenSpawnDistance;

        if (field != null)
        {
            for (int i = 0; i < 30; i++)
            {
                Vector3 candidate = field.GetRandomPoint(0.8f);
                Vector3 flat = candidate - origin; flat.y = 0f;
                if (flat.magnitude >= wardenSpawnDistance) { position = candidate; break; }
            }
        }

        position.y = field != null ? field.Center.y : 0f;
        GameObject go = new GameObject("Warden");
        go.transform.position = position;
        Warden spawned = go.AddComponent<Warden>();
        spawned.Init(player != null ? player.transform : null);
        return spawned;
    }

    // -------------------------------------------------------------------- HUD

    private void Show(string text, float seconds = 4f)
    {
        message = text;
        messageUntil = Time.time + seconds;
    }

    private void OnGUI()
    {
        float scale = Screen.height / 1080f;

        if (Time.time < flashUntil)
        {
            GUI.color = new Color(0.6f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        if (ended && EndScreenReady())
        {
            DrawEndScreen(scale);
            return;
        }

        // Candy counter.
        GUIStyle candyStyle = Style(Mathf.RoundToInt(34 * scale), TextAnchor.UpperLeft, new Color(1f, 0.6f, 0.15f));
        Shadowed(new Rect(30 * scale, 24 * scale, 600 * scale, 60 * scale), $"CANDY  {Candy} / {candyToWin}", candyStyle);

        // Timer or Warden warning.
        bool wardenAwake = warden != null && warden.IsAwake;
        if (timerExpired || wardenAwake)
        {
            float blink = Mathf.PingPong(Time.time * 2f, 1f);
            GUIStyle warn = Style(Mathf.RoundToInt(36 * scale), TextAnchor.UpperCenter, Color.Lerp(new Color(0.5f, 0f, 0f), Color.red, blink));
            Shadowed(new Rect(0, 24 * scale, Screen.width, 60 * scale), "THE WARDEN IS AWAKE", warn);
        }
        else
        {
            int seconds = Mathf.CeilToInt(TimeLeft);
            Color color = TimeLeft > 30f ? Color.white : TimeLeft > 10f ? new Color(1f, 0.6f, 0.2f)
                : Color.Lerp(new Color(0.6f, 0f, 0f), Color.red, Mathf.PingPong(Time.time * 3f, 1f));
            GUIStyle timer = Style(Mathf.RoundToInt(40 * scale), TextAnchor.UpperCenter, color);
            Shadowed(new Rect(0, 20 * scale, Screen.width, 60 * scale), $"{seconds / 60}:{seconds % 60:00}", timer);
        }

        // Event message.
        if (!string.IsNullOrEmpty(message) && Time.time < messageUntil)
        {
            float alpha = Mathf.Clamp01(messageUntil - Time.time);
            GUIStyle msg = Style(Mathf.RoundToInt(30 * scale), TextAnchor.MiddleCenter, new Color(1f, 0.92f, 0.85f, alpha));
            msg.wordWrap = true;
            Shadowed(new Rect(Screen.width * 0.15f, Screen.height * 0.72f, Screen.width * 0.7f, 120 * scale), message, msg);
        }
    }

    private bool EndScreenReady() => won ? Time.time > messageUntil - 2f : Time.time > flashUntil + 0.9f;

    private void DrawEndScreen(float scale)
    {
        GUI.color = new Color(0f, 0f, 0f, 0.8f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        string title = won ? "YOU SURVIVED THE NIGHT" : "THE WARDEN CAUGHT YOU";
        string body = won
            ? $"You brought home {Candy} candy. Happy Halloween."
            : $"You had {Candy} of {candyToWin} candy.";

        GUIStyle titleStyle = Style(Mathf.RoundToInt(64 * scale), TextAnchor.MiddleCenter, won ? new Color(1f, 0.6f, 0.15f) : Color.red);
        GUIStyle bodyStyle = Style(Mathf.RoundToInt(30 * scale), TextAnchor.MiddleCenter, Color.white);
        Shadowed(new Rect(0, Screen.height * 0.3f, Screen.width, 100 * scale), title, titleStyle);
        Shadowed(new Rect(0, Screen.height * 0.3f + 100 * scale, Screen.width, 60 * scale), body, bodyStyle);

        GUIStyle button = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(28 * scale) };
        float w = 320 * scale, h = 64 * scale, y = Screen.height * 0.58f;

        if (GUI.Button(new Rect(Screen.width * 0.5f - w - 20 * scale, y, w, h), "Try Again", button))
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);

        if (GUI.Button(new Rect(Screen.width * 0.5f + 20 * scale, y, w, h), "Main Menu", button))
            SceneManager.LoadScene(mainMenuScene);
    }

    private static GUIStyle Style(int size, TextAnchor anchor, Color color)
    {
        GUIStyle style = new GUIStyle(GUI.skin.label) { fontSize = size, alignment = anchor, fontStyle = FontStyle.Bold };
        style.normal.textColor = color;
        return style;
    }

    private static void Shadowed(Rect rect, string text, GUIStyle style)
    {
        Color color = style.normal.textColor;
        style.normal.textColor = new Color(0f, 0f, 0f, color.a * 0.8f);
        GUI.Label(new Rect(rect.x + 2, rect.y + 2, rect.width, rect.height), text, style);
        style.normal.textColor = color;
        GUI.Label(rect, text, style);
    }
}
