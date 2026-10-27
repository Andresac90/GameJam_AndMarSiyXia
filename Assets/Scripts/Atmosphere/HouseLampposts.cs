using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Builds old street lamps around a house: two flanking the porch and a zig-zag trail
/// leading out from the front door, so the player can spot the house through the fog
/// and follow the lights to it. Lamps are children of the house and die with it.
/// The house front is its local -Z side (where the door and NPC are).
/// </summary>
public class HouseLampposts : MonoBehaviour
{
    private const int IgnoreRaycastLayer = 2;

    [Header("Layout (house local space, front = -Z)")]
    [Tooltip("Optional: lay the lamps out around this point (e.g. the front door) instead of the house root. Same axes: front = -Z.")]
    [SerializeField] private Transform lampAnchor;
    [Tooltip("Lamps flanking the porch.")]
    [SerializeField] private Vector3[] porchLamps =
    {
        new Vector3(-2.6f, 0f, -2.2f),
        new Vector3(2.6f, 0f, -2.2f)
    };
    [Tooltip("Zig-zag trail leading out from the door, stopping at the field edge.")]
    [SerializeField, Min(0)] private int trailLamps = 4;
    [SerializeField, Min(1f)] private float trailSpacing = 5f;
    [SerializeField, Min(0f)] private float trailZigZag = 1.6f;

    [Header("Look")]
    [SerializeField, Min(1f)] private float poleHeight = 3.4f;
    [SerializeField] private Color lightColor = new Color(1f, 0.72f, 0.42f);
    [SerializeField, Min(0f)] private float lightIntensity = 3f;
    [SerializeField, Min(1f)] private float lightRange = 11f;
    [SerializeField, Min(0.1f)] private float glowSize = 3f;

    [Header("Materials (optional, taken from NightAtmosphere if empty)")]
    [SerializeField] private Material lanternMaterial;
    [SerializeField] private Material glowMaterial;
    [SerializeField] private Material ironMaterial;

    private static Texture2D glowTexture;

    private Transform Frame => lampAnchor != null ? lampAnchor : transform;

    private void Start()
    {
        NightAtmosphere night = NightAtmosphere.Instance;
        if (lanternMaterial == null && night != null) lanternMaterial = night.lanternMaterial;
        if (glowMaterial == null && night != null) glowMaterial = night.glowMaterial;
        if (lanternMaterial == null) lanternMaterial = CreateLanternFallback();
        else lanternMaterial = new Material(lanternMaterial);
        lanternMaterial.EnableKeyword("_EMISSION");
        if (ironMaterial == null) ironMaterial = GoreFX.CreateLitMaterial(new Color(0.06f, 0.06f, 0.065f), 0.35f);

        FieldBounds field = FindAnyObjectByType<FieldBounds>();

        int index = 0;
        foreach (Vector3 local in porchLamps)
            TryBuildLamp(Frame.TransformPoint(local), field, index++);

        for (int i = 0; i < trailLamps; i++)
        {
            float side = i % 2 == 0 ? -trailZigZag : trailZigZag;
            Vector3 local = new Vector3(side, 0f, -2.2f - trailSpacing * (i + 1));

            // Lamps outside the field would be unreachable (FieldWrap teleports the player).
            if (!TryBuildLamp(Frame.TransformPoint(local), field, index++))
                break;
        }
    }

    private bool TryBuildLamp(Vector3 world, FieldBounds field, int index)
    {
        if (field != null && field.IsOutside(world))
            return false;

        world.y = GroundHeight(world);
        BuildLamp(world, index);
        return true;
    }

    private void BuildLamp(Vector3 groundPoint, int index)
    {
        GameObject lamp = new GameObject("Lamppost_" + index);
        lamp.transform.SetParent(transform, true);
        lamp.transform.position = groundPoint;

        // Face the arm toward the walking path (house local X = 0 line).
        Vector3 local = Frame.InverseTransformPoint(groundPoint);
        float side = local.x >= 0f ? -1f : 1f;
        lamp.transform.rotation = Frame.rotation * Quaternion.Euler(0f, side > 0 ? 90f : -90f, 0f);
        lamp.transform.Rotate(0f, Random.Range(-8f, 8f), Random.Range(-2.5f, 2.5f)); // old, slightly crooked

        AddPart(lamp.transform, PrimitiveType.Cylinder, ironMaterial,
            new Vector3(0f, 0.15f, 0f), new Vector3(0.32f, 0.15f, 0.32f), keepCollider: false);
        AddPart(lamp.transform, PrimitiveType.Cylinder, ironMaterial,
            new Vector3(0f, poleHeight * 0.5f, 0f), new Vector3(0.12f, poleHeight * 0.5f, 0.12f), keepCollider: true);
        AddPart(lamp.transform, PrimitiveType.Cube, ironMaterial,
            new Vector3(0f, poleHeight - 0.05f, 0.35f), new Vector3(0.07f, 0.07f, 0.75f), keepCollider: false);

        Vector3 lanternPos = new Vector3(0f, poleHeight - 0.3f, 0.7f);
        AddPart(lamp.transform, PrimitiveType.Cube, ironMaterial,
            lanternPos + Vector3.up * 0.25f, new Vector3(0.38f, 0.06f, 0.38f), keepCollider: false);
        GameObject lantern = AddPart(lamp.transform, PrimitiveType.Cube, lanternMaterial,
            lanternPos, new Vector3(0.28f, 0.4f, 0.28f), keepCollider: false);

        GameObject lightObject = new GameObject("LampLight");
        lightObject.transform.SetParent(lamp.transform, false);
        lightObject.transform.localPosition = lanternPos + Vector3.down * 0.15f;

        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = lightColor;
        light.intensity = lightIntensity;
        light.range = lightRange;
        light.shadows = LightShadows.None;

        ParticleSystem glow = CreateGlow(lamp.transform, lanternPos);

        LampFlicker flicker = lamp.AddComponent<LampFlicker>();
        flicker.Init(light, lantern.GetComponent<Renderer>(), glow, Random.value < 0.25f);
    }

    private static GameObject AddPart(Transform parent, PrimitiveType type, Material material,
        Vector3 localPosition, Vector3 localScale, bool keepCollider)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = localScale;
        part.GetComponent<Renderer>().sharedMaterial = material;

        if (!keepCollider)
            Destroy(part.GetComponent<Collider>());

        return part;
    }

    /// <summary>Camera-facing glow so the lamp reads as a beacon through the fog.</summary>
    private ParticleSystem CreateGlow(Transform parent, Vector3 localPosition)
    {
        GameObject go = new GameObject("LampGlow");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.duration = 1f;
        main.startLifetime = Mathf.Infinity;
        main.startSpeed = 0f;
        main.startSize = glowSize;
        main.startColor = new Color(lightColor.r, lightColor.g, lightColor.b, 0.7f);
        main.maxParticles = 1;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.playOnAwake = false;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;

        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.maxParticleSize = 0.35f;

        Material mat = glowMaterial != null ? new Material(glowMaterial) : CreateGlowFallback();
        // Fade the glow out as the camera walks up to the lamp, so it never floods the screen.
        mat.EnableKeyword("_FADING_ON");
        mat.SetFloat("_CameraFadingEnabled", 1f);
        mat.SetVector("_CameraFadeParams", new Vector4(1f, 1f / 3f, 0f, 0f));
        Texture2D texture = GetGlowTexture();
        mat.SetTexture("_BaseMap", texture);
        mat.SetTexture("_MainTex", texture);
        renderer.sharedMaterial = mat;

        ps.Play();
        return ps;
    }

    private float GroundHeight(Vector3 position)
    {
        if (Physics.Raycast(position + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 100f,
                ~(1 << IgnoreRaycastLayer), QueryTriggerInteraction.Ignore) &&
            !hit.collider.transform.IsChildOf(transform) &&
            hit.collider.GetComponentInParent<CharacterController>() == null)
        {
            return hit.point.y;
        }

        return transform.position.y;
    }

    private static Texture2D GetGlowTexture()
    {
        if (glowTexture != null) return glowTexture;

        const int size = 64;
        glowTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "LampGlow",
            wrapMode = TextureWrapMode.Clamp
        };

        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float r = Vector2.Distance(new Vector2(x, y), new Vector2(size - 1, size - 1) * 0.5f) / (size * 0.5f);
                float a = Mathf.Clamp01(1f - r);
                a = Mathf.Pow(a, 2.2f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }

        glowTexture.SetPixels32(pixels);
        glowTexture.Apply(false, true);
        return glowTexture;
    }

    private static Material CreateLanternFallback()
    {
        Material mat = GoreFX.CreateLitMaterial(new Color(1f, 0.8f, 0.55f), 0.4f);
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", new Color(3.2f, 1.7f, 0.6f));
        return mat;
    }

    private static Material CreateGlowFallback()
    {
        Material mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 2f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.One);
        mat.SetFloat("_ZWrite", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent;
        return mat;
    }
}

/// <summary>Old-lamp flicker: gentle wobble, and some lamps sputter and die for a moment.</summary>
public class LampFlicker : MonoBehaviour
{
    private Light lampLight;
    private Renderer lantern;
    private ParticleSystemRenderer glow;
    private bool dying;
    private float baseIntensity;
    private float seed;
    private float outUntil;
    private MaterialPropertyBlock block;
    private Color baseEmission;

    public void Init(Light light, Renderer lanternRenderer, ParticleSystem glowSystem, bool unstable)
    {
        lampLight = light;
        lantern = lanternRenderer;
        glow = glowSystem != null ? glowSystem.GetComponent<ParticleSystemRenderer>() : null;
        dying = unstable;
        baseIntensity = light.intensity;
        seed = Random.value * 100f;
        block = new MaterialPropertyBlock();
        baseEmission = lantern != null && lantern.sharedMaterial.HasProperty("_EmissionColor")
            ? lantern.sharedMaterial.GetColor("_EmissionColor")
            : Color.white;
    }

    private void Update()
    {
        if (lampLight == null) return;

        float t = Time.time;
        float level = 0.85f + Mathf.PerlinNoise(seed, t * 3f) * 0.3f;

        if (dying)
        {
            if (t < outUntil) level = 0.05f;
            else if (Random.value < 0.004f) outUntil = t + Random.Range(0.05f, 0.6f);
            else level *= 0.6f + Mathf.PerlinNoise(seed + 5f, t * 12f) * 0.6f;
        }

        lampLight.intensity = baseIntensity * level;

        if (lantern != null)
        {
            lantern.GetPropertyBlock(block);
            block.SetColor("_EmissionColor", baseEmission * level);
            lantern.SetPropertyBlock(block);
        }

        if (glow != null)
            glow.enabled = level > 0.2f;
    }
}
