using UnityEngine;

/// <summary>
/// Put on the doorbell (needs a collider). Pressing E rings the bell and counts as
/// knocking on the linked HouseDoor, which starts the encounter.
/// </summary>
public class DoorbellButton : MonoBehaviour, IInteractable
{
    [SerializeField] private HouseDoor door;
    [Tooltip("AudioSource with the bell sound. Leave empty to use one on this object.")]
    [SerializeField] private AudioSource bell;

    public string Prompt => "[E] Ring the doorbell - Trick or Threat?";
    public bool CanInteract => door != null && door.CanInteract;

    private void Awake()
    {
        if (bell == null) bell = GetComponent<AudioSource>();
        if (door == null) door = GetComponentInParent<HouseDoor>();
    }

    public void Interact()
    {
        if (CanInteract) door.RingBell(bell);
    }
}
