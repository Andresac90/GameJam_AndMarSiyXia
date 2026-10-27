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

    [Header("Animated door (optional)")]
    [Tooltip("If set, the door opens through this Animator bool instead of swinging this transform.")]
    public Animator doorAnimator;
    public string openParameter = "isOpen_Obj_1";

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

    /// <summary>Doorbell: plays the bell, waits for it, then counts as a knock.</summary>
    public void RingBell(AudioSource bell)
    {
        if (CanInteract) StartCoroutine(RingRoutine(bell));
    }

    IEnumerator RingRoutine(AudioSource bell)
    {
        knocking = true;
        float wait = 1f;
        if (bell != null && bell.clip != null)
        {
            bell.Play();
            wait = Mathf.Clamp(bell.clip.length, 0.5f, 2.5f);
        }
        yield return new WaitForSeconds(wait);
        knocking = false;
        Knocked?.Invoke();
    }

    public void Open() => SetOpen(true);
    public void Close() => SetOpen(false);

    void SetOpen(bool open)
    {
        IsOpen = open;
        if (doorAnimator != null)
        {
            doorAnimator.enabled = true;
            doorAnimator.SetBool(openParameter, open);
        }
    }

    void Update()
    {
        if (doorAnimator != null) return;

        float target = IsOpen ? openAngle : 0f;
        currentAngle = Mathf.Lerp(currentAngle, target, Time.deltaTime * swingSpeed);
        transform.localRotation = closedRotation * Quaternion.Euler(0f, currentAngle, 0f);
    }
}
