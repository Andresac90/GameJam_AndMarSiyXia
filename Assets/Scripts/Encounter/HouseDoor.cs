using System;
using System.Collections;
using UnityEngine;

/// <summary>Anything the player can look at and press E on.</summary>
public interface IInteractable
{
    string Prompt { get; }
    bool CanInteract { get; }
    void Interact();
}

/// <summary>
/// Put this on the door's hinge (pivot) object; the door mesh + collider is a child.
/// Pressing E knocks, then raises Knocked so the encounter can open the door.
/// </summary>
public class HouseDoor : MonoBehaviour, IInteractable
{
    [Tooltip("Negative opens inward when the door panel extends along +X from the hinge.")]
    public float openAngle = -100f;
    public float swingSpeed = 4f;
    public int knockCount = 3;
    public float knockInterval = 0.3f;
    public AudioClip knockClip;

    public event Action Knocked;

    public bool IsOpen { get; private set; }
    public bool acceptsKnocks = true;

    public string Prompt => "[E] Knock - Trick or Threat?";
    public bool CanInteract => acceptsKnocks && !IsOpen && !knocking;

    Quaternion closedRotation;
    float currentAngle;
    bool knocking;
    AudioSource audioSource;

    void Awake()
    {
        closedRotation = transform.localRotation;
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f;
        }
    }

    public void Interact()
    {
        if (CanInteract) StartCoroutine(KnockRoutine());
    }

    IEnumerator KnockRoutine()
    {
        knocking = true;
        for (int i = 0; i < knockCount; i++)
        {
            audioSource.pitch = UnityEngine.Random.Range(0.9f, 1.1f);
            audioSource.PlayOneShot(knockClip != null ? knockClip : ProceduralAudio.Knock);
            yield return new WaitForSeconds(knockInterval);
        }
        knocking = false;
        Knocked?.Invoke();
    }

    public void Open() => IsOpen = true;
    public void Close() => IsOpen = false;

    void Update()
    {
        float target = IsOpen ? openAngle : 0f;
        currentAngle = Mathf.Lerp(currentAngle, target, Time.deltaTime * swingSpeed);
        transform.localRotation = closedRotation * Quaternion.Euler(0f, currentAngle, 0f);
    }
}
