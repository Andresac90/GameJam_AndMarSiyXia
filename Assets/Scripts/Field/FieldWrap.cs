
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Wraps the player to the opposite side of the field with a screen fade
/// and sound effect.
/// Put this on the Player root with the CharacterController.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class FieldWrap : MonoBehaviour
{
    public FieldBounds field;

    [Header("Teleport Fade")]
    [Min(0.01f)] public float fadeInDuration = 0.10f;
    [Min(0f)] public float blackDuration = 0.04f;
    [Min(0.01f)] public float fadeOutDuration = 0.20f;

    [Range(0f, 1f)]
    public float fadeOpacity = 0.95f;

    [Header("Teleport Sound")]
    [SerializeField] private AudioClip teleportSound;
    [SerializeField, Range(0f, 1f)] private float teleportSoundVolume = 1f;

    CharacterController controller;
    Image fadeOverlay;
    AudioSource audioSource;
    bool wrapping;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        audioSource = GetComponent<AudioSource>();

        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;

        if (field == null)
            field = FindAnyObjectByType<FieldBounds>();

        CreateFadeOverlay();
    }

    void LateUpdate()
    {
        if (field == null || wrapping)
            return;

        if (field.IsOutside(transform.position))
        {
            Vector3 destination = field.GetOppositePoint(transform.position);
            StartCoroutine(WrapWithFade(destination));
        }
    }

    void CreateFadeOverlay()
    {
        GameObject canvasObject = new GameObject("Teleport Fade Canvas");

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 1000;

        canvasObject.AddComponent<CanvasScaler>();

        GameObject imageObject = new GameObject("Teleport Fade");
        imageObject.transform.SetParent(canvasObject.transform, false);

        fadeOverlay = imageObject.AddComponent<Image>();
        fadeOverlay.color = new Color(0f, 0f, 0f, 0f);
        fadeOverlay.raycastTarget = false;

        RectTransform rect = fadeOverlay.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    IEnumerator WrapWithFade(Vector3 destination)
    {
        wrapping = true;

        if (teleportSound != null)
            audioSource.PlayOneShot(teleportSound, teleportSoundVolume);

        controller.enabled = false;

        yield return Fade(0f, fadeOpacity, fadeInDuration);

        transform.position = destination;

        if (blackDuration > 0f)
            yield return new WaitForSecondsRealtime(blackDuration);

        controller.enabled = true;

        yield return Fade(fadeOpacity, 0f, fadeOutDuration);

        wrapping = false;
    }

    IEnumerator Fade(float startAlpha, float endAlpha, float duration)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;

            float t = Mathf.Clamp01(elapsed / duration);
            t = Mathf.SmoothStep(0f, 1f, t);

            Color color = fadeOverlay.color;
            color.a = Mathf.Lerp(startAlpha, endAlpha, t);
            fadeOverlay.color = color;

            yield return null;
        }

        Color finalColor = fadeOverlay.color;
        finalColor.a = endAlpha;
        fadeOverlay.color = finalColor;
    }
}
