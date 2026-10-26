using UnityEngine;

/// <summary>Sets up spooky fog at runtime. Add to any object in the scene.</summary>
public class FogSetup : MonoBehaviour
{
    public Color fogColor = new Color(0.55f, 0.58f, 0.62f);
    [Range(0.001f, 0.2f)] public float density = 0.04f;

    void Awake()
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = fogColor;
        RenderSettings.fogDensity = density;
        if (Camera.main) Camera.main.backgroundColor = fogColor;
    }
}
