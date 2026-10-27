using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Turns the scene into a foggy night: moonlight, dark ambient, dense distance fog,
/// no skybox and a spooky post-process grade (bloom for the lamps, vignette, grain, cold tint).
/// Runs in Start so it wins over FogSetup (which sets daytime fog in Awake).
/// </summary>
public class NightAtmosphere : MonoBehaviour
{
    public static NightAtmosphere Instance { get; private set; }

    [Header("Moon")]
    [SerializeField] private Light moonLight;
    [SerializeField] private Color moonColor = new Color(0.5f, 0.58f, 0.85f);
    [SerializeField, Min(0f)] private float moonIntensity = 0.35f;
    [SerializeField] private Vector3 moonRotation = new Vector3(35f, -40f, 0f);

    [Header("Ambient")]
    [SerializeField] private Color skyAmbient = new Color(0.1f, 0.11f, 0.16f);
    [SerializeField] private Color equatorAmbient = new Color(0.06f, 0.065f, 0.09f);
    [SerializeField] private Color groundAmbient = new Color(0.02f, 0.02f, 0.025f);

    [Header("Distance Fog")]
    [SerializeField] private Color fogColor = new Color(0.06f, 0.07f, 0.095f);
    [SerializeField, Range(0.001f, 0.2f)] private float fogDensity = 0.04f;
    [Tooltip("Slowly 'breathes' the fog density so the night feels alive.")]
    [SerializeField, Range(0f, 0.5f)] private float densityBreathing = 0.15f;

    [Header("Post Processing")]
    [SerializeField] private bool spookyGrade = true;

    [Header("Shared Materials (used by lamps and fog)")]
    public Material lanternMaterial;
    public Material glowMaterial;
    public Material fogMaterial;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        ApplyLighting();
        ApplyCamera(Camera.main);

        if (spookyGrade)
            CreatePostProcessVolume();
    }

    private void Update()
    {
        if (densityBreathing <= 0f) return;
        float breath = Mathf.PerlinNoise(Time.time * 0.05f, 0.37f) * 2f - 1f;
        RenderSettings.fogDensity = fogDensity * (1f + breath * densityBreathing);
    }

    private void ApplyLighting()
    {
        if (moonLight == null)
        {
            foreach (Light light in FindObjectsByType<Light>())
            {
                if (light.type == LightType.Directional)
                {
                    moonLight = light;
                    break;
                }
            }
        }

        if (moonLight != null)
        {
            moonLight.color = moonColor;
            moonLight.intensity = moonIntensity;
            moonLight.shadows = LightShadows.Soft;
            moonLight.transform.rotation = Quaternion.Euler(moonRotation);
        }

        //RenderSettings.skybox = null; //added a dedicated skybox
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = skyAmbient;
        RenderSettings.ambientEquatorColor = equatorAmbient;
        RenderSettings.ambientGroundColor = groundAmbient;
        RenderSettings.reflectionIntensity = 0.2f;

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = fogColor;
        RenderSettings.fogDensity = fogDensity;

        DynamicGI.UpdateEnvironment();
    }

    public void ApplyCamera(Camera cam)
    {
        if (cam == null) return;

        cam.clearFlags = CameraClearFlags.Skybox;

        UniversalAdditionalCameraData data =
            cam.GetUniversalAdditionalCameraData();

        if (data != null)
        {
            data.renderPostProcessing = spookyGrade;
            data.requiresDepthTexture = true;
        }
    }

    private void CreatePostProcessVolume()
    {
        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();

        Bloom bloom = profile.Add<Bloom>(true);
        bloom.threshold.Override(0.9f);
        bloom.intensity.Override(1.4f);
        bloom.scatter.Override(0.75f);
        bloom.tint.Override(new Color(1f, 0.85f, 0.7f));

        Vignette vignette = profile.Add<Vignette>(true);
        vignette.intensity.Override(0.38f);
        vignette.smoothness.Override(0.5f);
        vignette.color.Override(Color.black);

        ColorAdjustments color = profile.Add<ColorAdjustments>(true);
        color.postExposure.Override(0.5f);
        color.contrast.Override(18f);
        color.saturation.Override(-35f);
        color.colorFilter.Override(new Color(0.82f, 0.88f, 1f));

        FilmGrain grain = profile.Add<FilmGrain>(true);
        grain.type.Override(FilmGrainLookup.Medium3);
        grain.intensity.Override(0.35f);
        grain.response.Override(0.6f);

        Tonemapping tonemapping = profile.Add<Tonemapping>(true);
        tonemapping.mode.Override(TonemappingMode.ACES);

        GameObject go = new GameObject("NightPostProcess");
        go.transform.SetParent(transform, false);
        Volume volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 10f;
        volume.sharedProfile = profile;
    }
}
