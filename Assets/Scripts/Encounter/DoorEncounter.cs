
using System.Collections;
using UnityEngine;

/// <summary>
/// Knock -> door opens -> NPC appears -> dialogue.
/// Candy -> result. Kill -> UI hides, player must shoot the NPC -> gore -> result.
/// Next Encounter -> door closes, NPC resets, waits for the next knock.
/// </summary>
public class DoorEncounter : MonoBehaviour
{
    [Header("References (auto-found if empty)")]
    [SerializeField] private HouseDoor door;
    [SerializeField] private NPCConversationTest conversation;
    [SerializeField] private FirstPersonController_TEST player;
    [SerializeField] private GameObject npcRoot;
    [SerializeField] private NPCHealth npcHealth;
    [SerializeField] private Transform npcStandPoint;
    [SerializeField] private PlayerHandsAnimation playerHands;

    [Header("Timing")]
    [SerializeField, Min(0f)] private float doorOpenDelay = 0.25f;
    [SerializeField, Min(0f)] private float doorCloseDelay = 0.4f;
    [SerializeField, Min(0f)] private float killRevealDelay = 3.5f;
    [SerializeField] private bool hideNPCUntilKnock = true;

    private bool encounterActive;
    private bool waitingForNext;
    private bool killMode;
    private Coroutine routine;

    private void Awake()
    {
        ResolveReferences();

        if (hideNPCUntilKnock && npcRoot != null)
            npcRoot.SetActive(false);
    }

    private void ResolveReferences()
    {
        if (door == null) door = GetComponentInChildren<HouseDoor>();
        if (door == null) door = FindAnyObjectByType<HouseDoor>();
        if (conversation == null) conversation = FindAnyObjectByType<NPCConversationTest>();
        if (player == null) player = FindAnyObjectByType<FirstPersonController_TEST>();
        if (playerHands == null && player != null) playerHands = player.GetComponent<PlayerHandsAnimation>();
        if (npcHealth == null && npcRoot != null) npcHealth = npcRoot.GetComponentInChildren<NPCHealth>(true);
        if (npcRoot == null && npcHealth != null) npcRoot = npcHealth.gameObject;

        if (door == null) Debug.LogWarning("DoorEncounter: no HouseDoor found. Run 'Trick or Threat > Setup NPC Test Scene'.", this);
        if (conversation == null) Debug.LogWarning("DoorEncounter: no NPCConversationTest found.", this);
    }

    private void OnEnable()
    {
        if (door != null)
            door.Knocked += OnDoorKnocked;

        if (conversation != null)
        {
            conversation.KillChosen += OnKillChosen;
            conversation.CandyChosen += OnCandyChosen;
            conversation.NextEncounterRequested += OnNextEncounterRequested;
        }

        if (npcHealth != null)
            npcHealth.Died += OnNPCDied;
    }

    private void OnDisable()
    {
        if (door != null)
            door.Knocked -= OnDoorKnocked;

        if (conversation != null)
        {
            conversation.KillChosen -= OnKillChosen;
            conversation.CandyChosen -= OnCandyChosen;
            conversation.NextEncounterRequested -= OnNextEncounterRequested;
        }

        if (npcHealth != null)
            npcHealth.Died -= OnNPCDied;
    }

    private void OnDoorKnocked()
    {
        if (encounterActive || waitingForNext || conversation == null)
            return;

        if (routine != null)
            StopCoroutine(routine);

        routine = StartCoroutine(BeginDoorEncounter());
    }

    private IEnumerator BeginDoorEncounter()
    {
        encounterActive = true;

        if (player != null)
            player.SetControlsEnabled(false);

        if (door != null)
        {
            door.acceptsKnocks = false;
            door.Open();
        }

        yield return new WaitForSeconds(doorOpenDelay);

        if (npcRoot != null)
        {
            if (npcStandPoint != null && npcStandPoint != npcRoot.transform)
            {
                npcRoot.transform.SetPositionAndRotation(
                    npcStandPoint.position,
                    npcStandPoint.rotation
                );
            }

            npcRoot.SetActive(true);
        }

        conversation.BeginEncounter();
        routine = null;
    }

    private void OnKillChosen()
    {
        encounterActive = false;
        killMode = true;

        // Give the player their body (and the gun) back.
        if (player != null)
            player.SetControlsEnabled(true);
    }

    private void OnNPCDied()
    {
        killMode = false;
        waitingForNext = true;
        encounterActive = false;

        if (routine != null)
            StopCoroutine(routine);

        routine = StartCoroutine(RevealAfterKill());
    }

    private IEnumerator RevealAfterKill()
    {
        // Let the player take in the mess before the UI comes back.
        yield return new WaitForSeconds(killRevealDelay);

        if (player != null)
            player.SetControlsEnabled(false);

        if (conversation != null)
            conversation.ShowKillResult();

        routine = null;
    }

    private void OnCandyChosen()
    {
        waitingForNext = true;
        encounterActive = false;

        // With a GameLoop the candy count (and the hands) follow the game rules
        // (monsters steal instead of giving); without one, every spare is a candy.
        if (playerHands != null && FindAnyObjectByType<GameLoop>() == null)
            playerHands.PlayCandyPose();

        if (player != null)
            player.SetControlsEnabled(false);
    }

    private void OnNextEncounterRequested()
    {
        if (routine != null)
            StopCoroutine(routine);

        routine = StartCoroutine(ResetForNextEncounter());
    }

    private IEnumerator ResetForNextEncounter()
    {
        waitingForNext = false;
        encounterActive = false;
        killMode = false;

        if (playerHands != null)
            playerHands.PlayWalk();

        if (conversation != null)
            conversation.WaitForNextKnock();

        if (npcRoot != null)
        {
            if (npcHealth != null)
                npcHealth.ResetHealth();

            npcRoot.SetActive(false);
        }

        if (door != null)
        {
            door.Close();

            yield return new WaitForSeconds(doorCloseDelay);

            door.acceptsKnocks = true;
        }

        if (player != null)
            player.SetControlsEnabled(true);

        routine = null;
    }

    public void ResetEncounterDoor()
    {
        if (routine != null)
            StopCoroutine(routine);

        routine = null;
        waitingForNext = false;
        encounterActive = false;
        killMode = false;

        if (npcRoot != null)
            npcRoot.SetActive(false);

        if (door != null)
        {
            door.Close();
            door.acceptsKnocks = true;
        }

        if (player != null)
            player.SetControlsEnabled(true);
    }

    private void OnGUI()
    {
        if (!killMode)
            return;

        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 26,
            fontStyle = FontStyle.Bold
        };

        GUI.color = new Color(0.85f, 0.05f, 0.05f);
        GUI.Label(new Rect(0, Screen.height * 0.12f, Screen.width, 40), "DO IT.  [Left Click]", style);
        GUI.color = Color.white;
    }
}
