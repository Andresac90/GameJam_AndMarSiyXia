using System.Collections;
using UnityEngine;

/// <summary>
/// Put this on the root of the FieldHouse prefab (next to House). The root contains the house,
/// its door, the NPC and the DoorEncounter, so they all live and die together.
/// When the player finishes the encounter (Next Encounter button), the whole prefab is removed
/// and the HouseSpawner creates a fresh house + NPC at a random spot in the field.
/// </summary>
[RequireComponent(typeof(House))]
public class FieldHouseLink : MonoBehaviour
{
    [Tooltip("Seconds to wait after 'Next Encounter' so the door can close and the player gets control back.")]
    public float despawnDelay = 0.8f;

    [Tooltip("Optional. Leave empty to auto-find the NPC's renderer (used for persona materials).")]
    public Renderer npcRenderer;

    House house;
    NPCConversationTest conversation;
    FirstPersonController_TEST player;
    bool finishing;

    void Awake()
    {
        house = GetComponent<House>();
    }

    void Start()
    {
        conversation = FindAnyObjectByType<NPCConversationTest>();
        player = FindAnyObjectByType<FirstPersonController_TEST>();

        if (conversation == null)
        {
            Debug.LogError("FieldHouseLink: no NPCConversationTest in the scene.", this);
            return;
        }

        // The conversation lives in the scene, the NPC lives in this prefab: hand over its renderer.
        if (npcRenderer == null)
        {
            NPCHealth npc = GetComponentInChildren<NPCHealth>(true);
            if (npc != null) npcRenderer = npc.GetComponentInChildren<Renderer>(true);
        }
        if (npcRenderer != null) conversation.SetNPCRenderer(npcRenderer);

        conversation.NextEncounterRequested += OnNextEncounterRequested;
    }

    void OnNextEncounterRequested()
    {
        // No spawner = this house is placed by hand (e.g. the NPC_Test scene): leave it alone.
        if (house.spawner == null || finishing) return;

        finishing = true;
        StartCoroutine(DespawnRoutine());
    }

    IEnumerator DespawnRoutine()
    {
        // DoorEncounter closes the door and re-enables the player during this time.
        yield return new WaitForSeconds(despawnDelay);

        if (player != null) player.SetControlsEnabled(true);

        house.Finish();   // destroys house + NPC + encounter, spawns a new set elsewhere
    }

    void OnDestroy()
    {
        if (conversation != null)
            conversation.NextEncounterRequested -= OnNextEncounterRequested;
    }
}
