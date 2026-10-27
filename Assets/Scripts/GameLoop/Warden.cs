using System;
using UnityEngine;

/// <summary>
/// The Warden: a tall figure that stands frozen like a statue in the field.
/// GameLoop wakes it (timer ran out, a human was killed, or a monster had nothing to steal)
/// and it walks, then runs, straight at the player, gliding through houses.
/// GameLoop freezes it again when the player spares a human.
/// While frozen it only turns to face the player when nobody is looking.
/// The body is a placeholder built from primitives; set a model to replace it.
/// </summary>
public class Warden : MonoBehaviour
{
    public enum State { Frozen, Awake, Caught }

    [Header("Chase")]
    [SerializeField, Min(0.1f)] private float walkSpeed = 2.6f;
    [Tooltip("Top speed after chasing for 'rampTime' seconds. The player walks at about 4.")]
    [SerializeField, Min(0.1f)] private float runSpeed = 4.4f;
    [SerializeField, Min(0.1f)] private float rampTime = 25f;
    [SerializeField, Min(0.3f)] private float catchDistance = 1.3f;

    [Header("Look")]
    [Tooltip("Optional model to use instead of the placeholder body.")]
    [SerializeField] private GameObject modelPrefab;
    [SerializeField] private Color eyeColor = new Color(1f, 0.08f, 0.04f);

    public event Action CaughtPlayer;
    public State CurrentState { get; private set; } = State.Frozen;
    public bool IsAwake => CurrentState == State.Awake;

    private Transform target;
    private float awakeTime;
    private float groundY;
    private float stepTimer;
    private Light eyeLight;
    private Renderer[] eyes;
    private Renderer bodyRenderer;
    private AudioSource voice;
    private AudioSource feet;
    private MaterialPropertyBlock block;

    public void Init(Transform player)
    {
        target = player;
        groundY = transform.position.y;
    }

    private void Awake()
    {
        block = new MaterialPropertyBlock();
        BuildBody();

        voice = gameObject.AddComponent<AudioSource>();
        voice.spatialBlend = 1f;
        voice.loop = true;
        voice.clip = CreateDrone();
        voice.minDistance = 3f;
        voice.maxDistance = 40f;
        voice.rolloffMode = AudioRolloffMode.Linear;
        voice.playOnAwake = false;

        feet = gameObject.AddComponent<AudioSource>();
        feet.spatialBlend = 1f;
        feet.minDistance = 2f;
        feet.maxDistance = 30f;
        feet.rolloffMode = AudioRolloffMode.Linear;
        feet.playOnAwake = false;

        SetEyes(0.35f);
    }

    public void Wake()
    {
        if (CurrentState == State.Caught) return;
        if (CurrentState != State.Awake)
        {
            awakeTime = 0f;
            feet.pitch = 0.45f;
            feet.PlayOneShot(ProceduralAudio.Burst, 1f);
            voice.Play();
        }
        CurrentState = State.Awake;
        SetEyes(1f);
    }

    public void Freeze()
    {
        if (CurrentState == State.Caught) return;
        CurrentState = State.Frozen;
        voice.Stop();
        SetEyes(0.35f);
    }

    /// <summary>Puts the Warden right in front of the camera for the catch scare.</summary>
    public void JumpScare(Transform cameraTransform)
    {
        CurrentState = State.Caught;
        Vector3 forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
        // Eyes level with the player's, face filling the screen.
        transform.position = cameraTransform.position + forward * 1.05f + Vector3.down * 3.3f;
        transform.rotation = Quaternion.LookRotation(-forward);
        SetEyes(0.25f);
        voice.Stop();
        feet.pitch = 0.6f;
        feet.PlayOneShot(ProceduralAudio.Burst, 1f);
        feet.PlayOneShot(ProceduralAudio.Gunshot, 0.6f);
    }

    private void Update()
    {
        if (target == null) return;

        Vector3 toPlayer = target.position - transform.position;
        toPlayer.y = 0f;
        float distance = toPlayer.magnitude;

        if (CurrentState == State.Frozen)
        {
            // Statue rule: it only moves its gaze when you're not looking at it.
            if (bodyRenderer != null && !bodyRenderer.isVisible && distance > 0.1f)
                transform.rotation = Quaternion.LookRotation(toPlayer);
            return;
        }

        if (CurrentState != State.Awake) return;

        awakeTime += Time.deltaTime;
        float speed = Mathf.Lerp(walkSpeed, runSpeed, Mathf.Clamp01(awakeTime / rampTime));

        if (distance <= catchDistance)
        {
            CurrentState = State.Caught;
            voice.Stop();
            CaughtPlayer?.Invoke();
            return;
        }

        Vector3 step = toPlayer / Mathf.Max(distance, 0.001f) * speed * Time.deltaTime;
        transform.position = new Vector3(transform.position.x + step.x, groundY, transform.position.z + step.z);
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(toPlayer), Time.deltaTime * 6f);

        // Heavy footsteps, faster as it speeds up.
        stepTimer -= Time.deltaTime;
        if (stepTimer <= 0f)
        {
            stepTimer = Mathf.Lerp(0.75f, 0.38f, (speed - walkSpeed) / Mathf.Max(0.01f, runSpeed - walkSpeed));
            feet.pitch = UnityEngine.Random.Range(0.35f, 0.45f);
            feet.PlayOneShot(ProceduralAudio.Knock, 0.9f);
        }

        voice.pitch = Mathf.Lerp(0.8f, 1.2f, Mathf.Clamp01(1f - distance / 30f));
    }

    private void SetEyes(float level)
    {
        if (eyeLight != null) eyeLight.intensity = 1.5f * level;
        if (eyes == null) return;

        foreach (Renderer eye in eyes)
        {
            eye.GetPropertyBlock(block);
            block.SetColor("_EmissionColor", eyeColor * 8f * level);
            eye.SetPropertyBlock(block);
        }
    }

    private void BuildBody()
    {
        if (modelPrefab != null)
        {
            GameObject model = Instantiate(modelPrefab, transform);
            bodyRenderer = model.GetComponentInChildren<Renderer>();
            eyes = new Renderer[0];
            return;
        }

        Material skin = GoreFX.CreateLitMaterial(new Color(0.03f, 0.03f, 0.035f), 0.15f);
        Material eyeMat = GoreFX.CreateLitMaterial(Color.black, 0.9f);
        eyeMat.EnableKeyword("_EMISSION");
        eyeMat.SetColor("_EmissionColor", eyeColor * 8f);

        GameObject body = Part(PrimitiveType.Capsule, skin, new Vector3(0f, 1.55f, 0f), new Vector3(0.6f, 1.5f, 0.42f), Vector3.zero);
        bodyRenderer = body.GetComponent<Renderer>();
        Part(PrimitiveType.Sphere, skin, new Vector3(0f, 3.25f, 0.06f), new Vector3(0.42f, 0.55f, 0.45f), new Vector3(12f, 0f, 0f));
        Part(PrimitiveType.Cube, skin, new Vector3(-0.48f, 1.85f, 0.05f), new Vector3(0.11f, 1.9f, 0.11f), new Vector3(0f, 0f, -6f));
        Part(PrimitiveType.Cube, skin, new Vector3(0.48f, 1.85f, 0.05f), new Vector3(0.11f, 1.9f, 0.11f), new Vector3(0f, 0f, 6f));
        Part(PrimitiveType.Cube, skin, new Vector3(-0.56f, 0.82f, 0.12f), new Vector3(0.05f, 0.35f, 0.05f), new Vector3(15f, 0f, -10f)); // long fingers
        Part(PrimitiveType.Cube, skin, new Vector3(0.56f, 0.82f, 0.12f), new Vector3(0.05f, 0.35f, 0.05f), new Vector3(15f, 0f, 10f));

        eyes = new[]
        {
            Part(PrimitiveType.Sphere, eyeMat, new Vector3(-0.1f, 3.3f, 0.25f), Vector3.one * 0.07f, Vector3.zero).GetComponent<Renderer>(),
            Part(PrimitiveType.Sphere, eyeMat, new Vector3(0.1f, 3.3f, 0.25f), Vector3.one * 0.07f, Vector3.zero).GetComponent<Renderer>()
        };

        GameObject lightObject = new GameObject("EyeGlow");
        lightObject.transform.SetParent(transform, false);
        lightObject.transform.localPosition = new Vector3(0f, 3.3f, 0.6f);
        eyeLight = lightObject.AddComponent<Light>();
        eyeLight.type = LightType.Point;
        eyeLight.color = eyeColor;
        eyeLight.range = 5f;
        eyeLight.shadows = LightShadows.None;
    }

    private GameObject Part(PrimitiveType type, Material material, Vector3 position, Vector3 scale, Vector3 euler)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.transform.SetParent(transform, false);
        part.transform.localPosition = position;
        part.transform.localRotation = Quaternion.Euler(euler);
        part.transform.localScale = scale;
        part.GetComponent<Renderer>().sharedMaterial = material;
        part.layer = 2; // never blocks bullets, interaction or blood
        Destroy(part.GetComponent<Collider>()); // it glides through everything
        return part;
    }

    /// <summary>Low, uneven drone that loops while the Warden hunts.</summary>
    private static AudioClip CreateDrone()
    {
        const int rate = 44100;
        float[] data = new float[rate * 2];
        System.Random rng = new System.Random(5);
        float lowpass = 0f;

        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)rate;
            float noise = (float)rng.NextDouble() * 2f - 1f;
            lowpass += (noise - lowpass) * 0.02f;
            // 2 s loop: frequencies chosen to complete whole cycles so it loops cleanly.
            float tone = Mathf.Sin(2f * Mathf.PI * 41f * t) * 0.5f + Mathf.Sin(2f * Mathf.PI * 61.5f * t) * 0.3f;
            float breath = 0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 1f * t);
            data[i] = (tone + lowpass * 3f) * breath * 0.5f;
        }

        AudioClip clip = AudioClip.Create("WardenDrone", data.Length, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
