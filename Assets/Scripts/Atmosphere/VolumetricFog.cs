using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 3D volumetric-looking fog made of soft, lit particle volumes that live around the player.
/// - The fog is lit by every light (lamps, muzzle flash) so it glows around them.
/// - The player physically pushes it: fog parts, swirls and trails in their wake.
/// - Gunshots and gore blasts punch holes through it (see Disturb / DisturbLine).
/// - Follows the player through FieldWrap teleports without popping.
/// </summary>
public class VolumetricFog : MonoBehaviour
{
    private const int IgnoreRaycastLayer = 2;

    [System.Serializable]
    public class Layer
    {
        public string name = "Fog";
        [Min(1)] public int maxParticles = 400;
        [Tooltip("Height above the ground (min, max) in meters.")]
        public Vector2 heightRange = new Vector2(0f, 1.5f);
        public Vector2 sizeRange = new Vector2(3f, 7f);
        public Vector2 alphaRange = new Vector2(0.15f, 0.3f);
        public Vector2 lifetimeRange = new Vector2(16f, 26f);
        public Color color = new Color(0.6f, 0.64f, 0.72f);
        [Range(0f, 1f)] public float reactivity = 1f;

        [System.NonSerialized] public ParticleSystem system;
        [System.NonSerialized] public ParticleSystem.Particle[] buffer;
    }

    private struct Blast
    {
        public Vector3 position;
        public Vector3 direction;
        public float radius;
        public float strength;
    }

    private static VolumetricFog active;

    [Header("Setup")]
    [Tooltip("Leave empty to follow the player automatically.")]
    [SerializeField] private Transform target;
    [Tooltip("Soft transparent particle material (Assets/Materials/Night/FogParticle).")]
    [SerializeField] private Material fogMaterial;
    [SerializeField, Min(10f)] private float areaSize = 70f;
    [SerializeField] private Vector3 wind = new Vector3(0.25f, 0f, 0.1f);
    [Tooltip("Faint self-glow so the fog reads as moonlit mist even far from any light.")]
    [SerializeField] private Color moonGlow = new Color(0.07f, 0.075f, 0.1f);

    [Header("Player Interaction")]
    [SerializeField, Min(0.5f)] private float pushRadius = 3.5f;
    [SerializeField, Min(0f)] private float pushStrength = 2.5f;
    [Tooltip("How much fog gets dragged along behind the player.")]
    [SerializeField, Min(0f)] private float wakeStrength = 0.8f;
    [SerializeField, Min(0f)] private float swirlStrength = 1.2f;
    [Tooltip("How quickly disturbed fog calms down again.")]
    [SerializeField, Min(0f)] private float settleSpeed = 0.35f;
    [SerializeField, Min(0.5f)] private float maxFogSpeed = 6f;

    [Header("Layers")]
    [SerializeField] private Layer[] layers =
    {
        new Layer
        {
            name = "GroundMist", maxParticles = 450,
            heightRange = new Vector2(0f, 1.3f), sizeRange = new Vector2(3f, 7f),
            alphaRange = new Vector2(0.24f, 0.4f), lifetimeRange = new Vector2(16f, 26f),
            color = new Color(0.75f, 0.8f, 0.9f), reactivity = 1f
        },
        new Layer
        {
            name = "FogBanks", maxParticles = 350,
            heightRange = new Vector2(1f, 7f), sizeRange = new Vector2(7f, 14f),
            alphaRange = new Vector2(0.05f, 0.1f), lifetimeRange = new Vector2(20f, 32f),
            color = new Color(0.7f, 0.75f, 0.85f), reactivity = 0.5f
        }
    };

    private readonly List<Blast> blasts = new List<Blast>();
    private Vector3 lastTargetPosition;
    private Vector3 targetVelocity;
    private bool hasLastPosition;
    private float groundY;

    /// <summary>Pushes fog away from a point (explosions, gore).</summary>
    public static void Disturb(Vector3 position, Vector3 direction, float radius, float strength)
    {
        if (active == null) return;

        active.blasts.Add(new Blast
        {
            position = position,
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero,
            radius = radius,
            strength = strength
        });
    }

    /// <summary>Punches a tunnel through the fog along a line (bullets).</summary>
    public static void DisturbLine(Vector3 from, Vector3 to, float radius, float strength)
    {
        if (active == null) return;

        Vector3 direction = to - from;
        float length = direction.magnitude;
        int steps = Mathf.Clamp(Mathf.CeilToInt(length / (radius * 1.2f)), 1, 24);

        for (int i = 0; i <= steps; i++)
            Disturb(Vector3.Lerp(from, to, i / (float)steps), direction, radius, strength);
    }

    private void OnEnable() => active = this;

    private void OnDisable()
    {
        if (active == this) active = null;
    }

    private void Start()
    {
        if (fogMaterial == null && NightAtmosphere.Instance != null)
            fogMaterial = NightAtmosphere.Instance.fogMaterial;

        ResolveTarget();
        UpdateGround();

        Texture2D puff = CreatePuffTexture();
        foreach (Layer layer in layers)
            BuildLayer(layer, puff);
    }

    private void ResolveTarget()
    {
        if (target != null) return;

        FirstPersonController_TEST player = FindAnyObjectByType<FirstPersonController_TEST>();
        if (player != null) target = player.transform;
        else if (Camera.main != null) target = Camera.main.transform;
    }

    private void BuildLayer(Layer layer, Texture2D puff)
    {
        GameObject go = new GameObject("Fog_" + layer.name);
        go.transform.SetParent(transform, false);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.prewarm = true;
        main.duration = layer.lifetimeRange.y;
        main.startLifetime = new ParticleSystem.MinMaxCurve(layer.lifetimeRange.x, layer.lifetimeRange.y);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(layer.sizeRange.x, layer.sizeRange.y);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        Color low = layer.color; low.a = layer.alphaRange.x;
        Color high = layer.color; high.a = layer.alphaRange.y;
        main.startColor = new ParticleSystem.MinMaxGradient(low, high);
        main.maxParticles = layer.maxParticles;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = false;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = layer.maxParticles / Mathf.Max(1f, (layer.lifetimeRange.x + layer.lifetimeRange.y) * 0.5f);

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        float height = Mathf.Max(0.1f, layer.heightRange.y - layer.heightRange.x);
        shape.scale = new Vector3(areaSize, height, areaSize);
        shape.position = new Vector3(0f, (layer.heightRange.x + layer.heightRange.y) * 0.5f, 0f);

        // Fade in and out so particles never pop.
        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = fade;

        ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.85f, 1f, 1.15f));

        ParticleSystem.RotationOverLifetimeModule rotation = ps.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);

        ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = wind.x;
        velocity.y = wind.y;
        velocity.z = wind.z;

        ParticleSystem.NoiseModule noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.25f;
        noise.frequency = 0.08f;
        noise.scrollSpeed = 0.03f;
        noise.quality = ParticleSystemNoiseQuality.Low;
        noise.damping = true;

        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortMode = ParticleSystemSortMode.Distance;
        renderer.maxParticleSize = 4f;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sharedMaterial = CreateLayerMaterial(puff);

        layer.system = ps;
        layer.buffer = new ParticleSystem.Particle[layer.maxParticles];

        go.transform.position = EmitterPosition();
        ps.Play();
    }

    private Material CreateLayerMaterial(Texture2D puff)
    {
        Material mat = fogMaterial != null ? new Material(fogMaterial) : CreateFallbackMaterial();
        mat.SetTexture("_BaseMap", puff);
        mat.SetTexture("_MainTex", puff);
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", moonGlow);
        return mat;
    }

    private static Material CreateFallbackMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Simple Lit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");

        Material mat = new Material(shader);
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_Cull", 0f);
        mat.SetVector("_SoftParticleFadeParams", new Vector4(0f, 1f / 1.5f, 0f, 0f));
        mat.SetVector("_CameraFadeParams", new Vector4(0.3f, 1f / 2.2f, 0f, 0f));
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.EnableKeyword("_SOFTPARTICLES_ON");
        mat.EnableKeyword("_FADING_ON");
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.renderQueue = (int)RenderQueue.Transparent;
        return mat;
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            ResolveTarget();
            if (target == null) return;
        }

        float dt = Time.deltaTime;
        Vector3 position = target.position;
        Vector3 teleport = Vector3.zero;

        if (hasLastPosition)
        {
            Vector3 delta = position - lastTargetPosition;

            // A big jump in one frame = FieldWrap teleport: carry the fog along.
            if (delta.magnitude > 5f)
            {
                teleport = delta;
                targetVelocity = Vector3.zero;
            }
            else if (dt > 0f)
            {
                targetVelocity = Vector3.Lerp(targetVelocity, delta / dt, 0.5f);
            }
        }

        lastTargetPosition = position;
        hasLastPosition = true;

        UpdateGround();
        Vector3 emitter = EmitterPosition();
        Vector3 chest = new Vector3(position.x, groundY + 1f, position.z);
        float speed = new Vector2(targetVelocity.x, targetVelocity.z).magnitude;
        float settle = Mathf.Clamp01(settleSpeed * dt);

        foreach (Layer layer in layers)
        {
            if (layer.system == null) continue;

            layer.system.transform.position = emitter;

            int count = layer.system.GetParticles(layer.buffer);
            for (int i = 0; i < count; i++)
            {
                ParticleSystem.Particle particle = layer.buffer[i];
                Vector3 p = particle.position + teleport;
                Vector3 v = particle.velocity * (1f - settle);

                // Player body pushes the fog aside and drags a wake behind it.
                Vector3 offset = p - chest;
                offset.y *= 0.5f;
                float distance = offset.magnitude;
                if (distance < pushRadius && distance > 0.001f)
                {
                    float falloff = 1f - distance / pushRadius;
                    falloff *= falloff;
                    Vector3 away = offset / distance;
                    Vector3 swirl = Vector3.Cross(Vector3.up, away) * swirlStrength * speed;
                    Vector3 accel = away * pushStrength * (0.25f + speed) + targetVelocity * wakeStrength + swirl;
                    v += accel * falloff * layer.reactivity * dt;
                }

                // Gunshots / gore blasts.
                foreach (Blast blast in blasts)
                {
                    Vector3 fromBlast = p - blast.position;
                    float d = fromBlast.magnitude;
                    if (d >= blast.radius) continue;

                    float falloff = 1f - d / blast.radius;
                    Vector3 away = d > 0.001f ? fromBlast / d : Random.onUnitSphere;
                    v += (away + blast.direction * 0.6f) * blast.strength * falloff * layer.reactivity;
                }

                v = Vector3.ClampMagnitude(v, maxFogSpeed);

                // Keep fog above the ground.
                float minY = groundY + 0.15f;
                if (p.y < minY)
                {
                    p.y = minY;
                    if (v.y < 0f) v.y = 0f;
                }

                particle.position = p;
                particle.velocity = v;
                layer.buffer[i] = particle;
            }

            layer.system.SetParticles(layer.buffer, count);
        }

        blasts.Clear();
    }

    private void UpdateGround()
    {
        if (target == null) return;

        if (Physics.Raycast(target.position + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 10f,
                ~(1 << IgnoreRaycastLayer), QueryTriggerInteraction.Ignore) &&
            hit.collider.GetComponentInParent<CharacterController>() == null)
        {
            groundY = hit.point.y;
        }
        else
        {
            CharacterController controller = target.GetComponent<CharacterController>();
            groundY = controller != null
                ? target.position.y + controller.center.y - controller.height * 0.5f
                : target.position.y - 1f;
        }
    }

    private Vector3 EmitterPosition()
    {
        Vector3 p = target != null ? target.position : transform.position;
        return new Vector3(p.x, groundY, p.z);
    }

    /// <summary>Soft cloudy puff: layered noise with a round falloff.</summary>
    private static Texture2D CreatePuffTexture()
    {
        const int size = 128;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "FogPuff",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)(size - 1);
                float v = y / (float)(size - 1);
                float r = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;

                float noise = 0f, amplitude = 0.5f, frequency = 3f;
                for (int o = 0; o < 4; o++)
                {
                    noise += Mathf.PerlinNoise(u * frequency + 13.7f, v * frequency + 7.3f) * amplitude;
                    amplitude *= 0.5f;
                    frequency *= 2f;
                }

                float falloff = Mathf.Clamp01(1f - r);
                falloff = falloff * falloff * (3f - 2f * falloff);
                float alpha = falloff * Mathf.Clamp01(0.35f + noise * 0.9f);

                byte shade = (byte)(Mathf.Lerp(0.85f, 1f, noise) * 255f);
                pixels[y * size + x] = new Color32(shade, shade, shade, (byte)(alpha * 255f));
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }
}
