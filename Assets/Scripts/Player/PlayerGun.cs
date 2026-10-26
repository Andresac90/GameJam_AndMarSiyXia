
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Put on the player camera. Left click fires a hitscan bullet from the screen center.
/// The pistol model (child named "Pistol" by default) kicks back on every shot.
/// </summary>
public class PlayerGun : MonoBehaviour
{
    private const int IgnoreRaycastLayer = 2;

    [Header("References")]
    [SerializeField] private Camera aimCamera;
    [SerializeField] private FirstPersonController_TEST player;
    [SerializeField] private Transform viewModel;
    [SerializeField] private Transform muzzle;
    [SerializeField] private Light muzzleFlash;
    [SerializeField] private AudioSource audioSource;
    [Tooltip("Leave empty to use a generated gunshot.")]
    [SerializeField] private AudioClip gunshotClip;

    [Header("Weapon")]
    [SerializeField] private float range = 60f;
    [SerializeField] private float damage = 100f;
    [SerializeField] private float fireCooldown = 0.35f;
    [SerializeField] private float recoilDegrees = 2f;
    [SerializeField] private LayerMask hitMask = ~0;
    [SerializeField] private bool showCrosshair = true;

    [Header("View Model Recoil")]
    [SerializeField] private float recoilKickBack = 0.06f;
    [SerializeField] private float recoilKickUp = 14f;
    [SerializeField] private float recoilRecovery = 12f;

    private float nextFireTime;
    private float recoil;
    private Vector3 viewModelBasePosition;
    private Quaternion viewModelBaseRotation;
    private Vector3 lastWrittenPosition;
    private Quaternion lastWrittenRotation;
    private bool hasWrittenViewModel;
    private Coroutine flashRoutine;

    private void Awake()
    {
        if (aimCamera == null)
            aimCamera = GetComponent<Camera>();

        if (aimCamera == null)
            aimCamera = GetComponentInChildren<Camera>();

        if (player == null)
            player = GetComponentInParent<FirstPersonController_TEST>();

        if (viewModel == null)
            viewModel = transform.Find("Pistol");

        if (viewModel != null)
        {
            // Models exported from Blender can carry their own camera/lights,
            // which would render over the first-person view.
            foreach (Camera stray in viewModel.GetComponentsInChildren<Camera>(true))
                stray.enabled = false;
            foreach (AudioListener stray in viewModel.GetComponentsInChildren<AudioListener>(true))
                stray.enabled = false;

            viewModelBasePosition = viewModel.localPosition;
            viewModelBaseRotation = viewModel.localRotation;
        }

        if (muzzle == null)
            muzzle = viewModel != null ? viewModel : transform;

        if (muzzleFlash == null)
            muzzleFlash = CreateMuzzleLight();

        muzzleFlash.enabled = false;

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
        }

        if (gunshotClip == null)
            gunshotClip = ProceduralAudio.Gunshot;
    }

    private void Update()
    {
        if (player != null && !player.ControlsEnabled)
            return;

        if (Mouse.current != null &&
            Mouse.current.leftButton.wasPressedThisFrame &&
            Time.time >= nextFireTime)
        {
            Fire();
        }
    }

    public void Fire()
    {
        if (aimCamera == null || Time.time < nextFireTime)
            return;

        nextFireTime = Time.time + fireCooldown;
        recoil = 1f;

        if (audioSource != null && gunshotClip != null)
        {
            audioSource.pitch = Random.Range(0.93f, 1.07f);
            audioSource.PlayOneShot(gunshotClip);
        }

        if (flashRoutine != null)
            StopCoroutine(flashRoutine);

        flashRoutine = StartCoroutine(FlashRoutine());

        if (player != null)
            player.AddPitch(recoilDegrees);

        Ray ray = new Ray(
            aimCamera.transform.position,
            aimCamera.transform.forward
        );

        // Gibs live on Ignore Raycast, so they never block bullets.
        int mask = hitMask & ~(1 << IgnoreRaycastLayer);

        if (!Physics.Raycast(ray, out RaycastHit hit, range, mask, QueryTriggerInteraction.Ignore))
            return;

        NPCHealth health = hit.collider.GetComponentInParent<NPCHealth>();

        if (health != null)
            health.TakeDamage(damage, hit, ray.direction);
        else
            GoreFX.Instance.BulletImpact(hit.point, hit.normal);
    }

    // LateUpdate so recoil is added on top of whatever an Animator wrote this frame.
    private void LateUpdate()
    {
        recoil = Mathf.Lerp(recoil, 0f, Time.deltaTime * recoilRecovery);

        if (viewModel == null)
            return;

        // If something (e.g. the hands Animator) moved the view model since our last
        // write, that pose is the new base. Otherwise keep the stored base so recoil
        // never accumulates.
        if (!hasWrittenViewModel ||
            viewModel.localPosition != lastWrittenPosition ||
            viewModel.localRotation != lastWrittenRotation)
        {
            viewModelBasePosition = viewModel.localPosition;
            viewModelBaseRotation = viewModel.localRotation;
        }

        viewModel.localPosition = viewModelBasePosition;
        viewModel.localRotation = viewModelBaseRotation;

        // Kick up around the gun itself, not the hand mesh pivot (which can be far away).
        Vector3 pivot = viewModel.parent != null
            ? viewModel.parent.InverseTransformPoint(muzzle.position)
            : muzzle.position;
        Quaternion kick = Quaternion.Euler(-recoil * recoilKickUp, 0f, 0f);

        viewModel.localRotation = kick * viewModelBaseRotation;
        viewModel.localPosition = pivot + kick * (viewModelBasePosition - pivot) +
                                  new Vector3(0f, recoil * 0.01f, -recoil * recoilKickBack);

        lastWrittenPosition = viewModel.localPosition;
        lastWrittenRotation = viewModel.localRotation;
        hasWrittenViewModel = true;
    }

    private Light CreateMuzzleLight()
    {
        GameObject flash = new GameObject("MuzzleFlash");
        flash.transform.SetParent(muzzle, false);

        Light light = flash.AddComponent<Light>();
        light.type = LightType.Point;
        light.range = 6f;
        light.intensity = 5f;
        light.color = new Color(1f, 0.75f, 0.4f);
        light.shadows = LightShadows.None;
        return light;
    }

    private IEnumerator FlashRoutine()
    {
        muzzleFlash.enabled = true;

        yield return new WaitForSeconds(0.05f);

        muzzleFlash.enabled = false;
        flashRoutine = null;
    }

    private void OnGUI()
    {
        if (!showCrosshair || (player != null && !player.ControlsEnabled))
            return;

        float x = Screen.width * 0.5f;
        float y = Screen.height * 0.5f;

        GUI.color = new Color(1f, 1f, 1f, 0.85f);
        GUI.DrawTexture(new Rect(x - 7f, y - 1f, 14f, 2f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(x - 1f, y - 7f, 2f, 14f), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }
}
