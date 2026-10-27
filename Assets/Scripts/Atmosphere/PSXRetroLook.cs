using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// PS1 look for URP. Put one in a scene:
/// - Renders at low resolution with nearest-neighbour upscaling (pixelated); UI stays sharp.
/// - Drives the PSX/URP Lit globals: vertex snapping, affine textures, colour banding, dithering.
/// - Converts opaque URP Lit / Simple Lit materials in the scene to PSX copies at runtime,
///   including things spawned later (houses, lamps, gibs). Asset files are never modified.
/// </summary>
public class PSXRetroLook : MonoBehaviour
{
    [Header("Screen")]
    [Tooltip("Fraction of screen resolution to render at. 0.35 at 1080p is about 378p.")]
    [SerializeField, Range(0.1f, 1f)] private float renderScale = 0.4f;

    [Header("Geometry")]
    [Tooltip("Vertical vertex grid. Lower = more wobble. 0 turns snapping off.")]
    [SerializeField, Range(0f, 480f)] private float vertexResolution = 160f;
    [Tooltip("0 = perspective-correct textures, 1 = full PS1 affine warping.")]
    [SerializeField, Range(0f, 1f)] private float affineWarp = 1f;

    [Header("Colour")]
    [Tooltip("Levels per colour channel. 32 matches the PS1's 15-bit colour. 0 turns banding off.")]
    [SerializeField, Range(0f, 256f)] private float colorDepth = 32f;
    [SerializeField, Range(0f, 1f)] private float dithering = 1f;
    [Tooltip("Steps in light falloff. 0 = smooth.")]
    [SerializeField, Range(0f, 32f)] private float lightSteps = 8f;

    [Header("Materials")]
    [Tooltip("Material using PSX/URP Lit (Assets/Shaders/PSX_Lit). Keeps the shader in builds.")]
    [SerializeField] private Material psxTemplate;
    [SerializeField] private bool convertSceneMaterials = true;
    [SerializeField, Min(0.1f)] private float rescanInterval = 1f;

    private static readonly int VertexResolutionId = Shader.PropertyToID("_PSXVertexResolution");
    private static readonly int AffineId = Shader.PropertyToID("_PSXAffine");
    private static readonly int ColorDepthId = Shader.PropertyToID("_PSXColorDepth");
    private static readonly int DitherId = Shader.PropertyToID("_PSXDither");
    private static readonly int LightStepsId = Shader.PropertyToID("_PSXLightSteps");

    private static readonly HashSet<string> ConvertibleShaders = new HashSet<string>
    {
        "Universal Render Pipeline/Lit",
        "Universal Render Pipeline/Simple Lit",
        "Universal Render Pipeline/Baked Lit",
        "Standard"
    };

    private readonly Dictionary<Material, Material> converted = new Dictionary<Material, Material>();
    private readonly HashSet<Renderer> seen = new HashSet<Renderer>();

    private Shader psxShader;
    private UniversalRenderPipelineAsset pipeline;
    private float originalRenderScale;
    private UpscalingFilterSelection originalFilter;
    private float nextScan;

    private void OnEnable()
    {
        psxShader = psxTemplate != null ? psxTemplate.shader : Shader.Find("PSX/URP Lit");
        if (psxShader == null)
            Debug.LogError("PSXRetroLook: shader 'PSX/URP Lit' not found.", this);

        pipeline = UniversalRenderPipeline.asset;
        if (pipeline != null)
        {
            originalRenderScale = pipeline.renderScale;
            originalFilter = pipeline.upscalingFilter;
        }

        ApplySettings();
        nextScan = 0f;
    }

    private void OnDisable()
    {
        // The pipeline asset is shared (and saved in the editor): always put it back.
        if (pipeline != null)
        {
            pipeline.renderScale = originalRenderScale;
            pipeline.upscalingFilter = originalFilter;
        }

        Shader.SetGlobalFloat(VertexResolutionId, 0f);
        Shader.SetGlobalFloat(AffineId, 0f);
        Shader.SetGlobalFloat(ColorDepthId, 0f);
        Shader.SetGlobalFloat(LightStepsId, 0f);
    }

    private void Update()
    {
        ApplySettings();

        if (convertSceneMaterials && psxShader != null && Time.unscaledTime >= nextScan)
        {
            nextScan = Time.unscaledTime + rescanInterval;
            ConvertScene();
        }
    }

    private void ApplySettings()
    {
        if (pipeline != null)
        {
            pipeline.renderScale = renderScale;
            pipeline.upscalingFilter = UpscalingFilterSelection.Point;
        }

        Shader.SetGlobalFloat(VertexResolutionId, vertexResolution);
        Shader.SetGlobalFloat(AffineId, affineWarp);
        Shader.SetGlobalFloat(ColorDepthId, colorDepth);
        Shader.SetGlobalFloat(DitherId, dithering);
        Shader.SetGlobalFloat(LightStepsId, lightSteps);
    }

    /// <summary>Converts every renderer under a root right away (optional; the periodic scan also finds them).</summary>
    public void ConvertNow(GameObject root)
    {
        if (psxShader == null || root == null) return;
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            seen.Add(renderer);
            Convert(renderer);
        }
    }

    private void ConvertScene()
    {
        if (seen.Count > 8000)
            seen.RemoveWhere(r => r == null);

        foreach (Renderer renderer in FindObjectsByType<Renderer>(FindObjectsInactive.Include))
        {
            if (seen.Add(renderer))
                Convert(renderer);
        }
    }

    private void Convert(Renderer renderer)
    {
        if (renderer is ParticleSystemRenderer || renderer is TrailRenderer ||
            renderer is LineRenderer || renderer is SpriteRenderer)
            return;

        Material[] materials = renderer.sharedMaterials;
        bool changed = false;

        for (int i = 0; i < materials.Length; i++)
        {
            Material psx = GetPSXMaterial(materials[i]);
            if (psx == null) continue;
            materials[i] = psx;
            changed = true;
        }

        if (changed)
            renderer.sharedMaterials = materials;
    }

    private Material GetPSXMaterial(Material source)
    {
        if (source == null || source.shader == psxShader)
            return null;

        if (converted.TryGetValue(source, out Material cached))
            return cached;

        Material result = null;
        if (IsConvertible(source))
            result = CreatePSXCopy(source);

        converted[source] = result;
        return result;
    }

    private static bool IsConvertible(Material source)
    {
        if (!ConvertibleShaders.Contains(source.shader.name))
            return false;

        // Leave transparent things (glass, fades) on their original shaders.
        if (source.HasProperty("_Surface") && source.GetFloat("_Surface") > 0.5f)
            return false;

        return source.renderQueue < 2900;
    }

    private Material CreatePSXCopy(Material source)
    {
        Material psx = psxTemplate != null ? new Material(psxTemplate) : new Material(psxShader);
        psx.name = source.name + " (PSX)";

        string texProperty = source.HasProperty("_BaseMap") ? "_BaseMap" : source.HasProperty("_MainTex") ? "_MainTex" : null;
        if (texProperty != null)
        {
            psx.SetTexture("_BaseMap", source.GetTexture(texProperty));
            psx.SetTextureScale("_BaseMap", source.GetTextureScale(texProperty));
            psx.SetTextureOffset("_BaseMap", source.GetTextureOffset(texProperty));
        }

        if (source.HasProperty("_BaseColor")) psx.SetColor("_BaseColor", source.GetColor("_BaseColor"));
        else if (source.HasProperty("_Color")) psx.SetColor("_BaseColor", source.GetColor("_Color"));

        if (source.IsKeywordEnabled("_EMISSION") && source.HasProperty("_EmissionColor"))
        {
            psx.EnableKeyword("_EMISSION");
            psx.SetFloat("_EmissionEnabled", 1f);
            psx.SetColor("_EmissionColor", source.GetColor("_EmissionColor"));
            if (source.HasProperty("_EmissionMap") && source.GetTexture("_EmissionMap") != null)
                psx.SetTexture("_EmissionMap", source.GetTexture("_EmissionMap"));
        }

        bool alphaClip = source.IsKeywordEnabled("_ALPHATEST_ON") ||
                         (source.HasProperty("_AlphaClip") && source.GetFloat("_AlphaClip") > 0.5f);
        if (alphaClip)
        {
            psx.EnableKeyword("_ALPHATEST_ON");
            psx.SetFloat("_AlphaClip", 1f);
            if (source.HasProperty("_Cutoff")) psx.SetFloat("_Cutoff", source.GetFloat("_Cutoff"));
            psx.renderQueue = 2450;
        }

        if (source.HasProperty("_Cull"))
            psx.SetFloat("_Cull", source.GetFloat("_Cull"));

        return psx;
    }
}
