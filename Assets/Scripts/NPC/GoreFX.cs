using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Runtime-generated 3D gore. No textures/models/audio needed:
/// - 3D mesh blood droplets with physics that paint splats where they land
/// - Rigidbody gibs (flesh, guts, bone, clothes, brain) that bleed trails
/// - A pumping stump fountain, a growing blood pool and blood on the screen
/// Keep ONE in the scene on an object that is never disabled (it is auto-created if missing).
/// </summary>
public class GoreFX : MonoBehaviour
{
    private const int IgnoreRaycastLayer = 2;

    private static GoreFX instance;

    public static GoreFX Instance
    {
        get
        {
            if (instance == null || !instance.isActiveAndEnabled)
                instance = FindAnyObjectByType<GoreFX>();

            if (instance == null)
                instance = new GameObject("GoreFX").AddComponent<GoreFX>();

            return instance;
        }
    }

    [Header("Amount")]
    [SerializeField, Range(0.25f, 4f)] private float goreMultiplier = 1.5f;
    [SerializeField, Min(1)] private int hitDroplets = 70;
    [SerializeField, Min(1)] private int deathDroplets = 450;
    [SerializeField, Min(0)] private int gibCount = 30;
    [SerializeField, Range(0f, 1f)] private float decalChance = 0.45f;

    [Header("Limits")]
    [SerializeField, Min(10)] private int maxDecals = 450;
    [SerializeField, Min(10)] private int maxGibs = 160;
    [SerializeField, Min(1f)] private float gibLifetime = 90f;

    [Header("Colors")]
    [SerializeField] private Color bloodColor = new Color(0.42f, 0.01f, 0.02f, 1f);
    [SerializeField] private Color fleshColor = new Color(0.58f, 0.14f, 0.14f, 1f);
    [SerializeField] private Color boneColor = new Color(0.86f, 0.82f, 0.7f, 1f);
    [SerializeField] private Color brainColor = new Color(0.82f, 0.48f, 0.52f, 1f);

    [Header("Screen Blood")]
    [SerializeField, Min(0f)] private float screenSplatterRange = 4.5f;
    [SerializeField, Min(1)] private int maxScreenSplats = 25;

    [Header("Optional Assets")]
    [Tooltip("URP Lit material with Alpha Clipping on. The setup tool creates one so the shader variant is included in builds.")]
    [SerializeField] private Material decalTemplate;
    [SerializeField] private AudioClip splatClip;
    [SerializeField] private AudioClip burstClip;

    public float Multiplier => goreMultiplier;

    private struct ScreenSplat
    {
        public Texture2D texture;
        public Rect rect;
        public float born;
        public float life;
        public float drip;
    }

    private readonly Queue<GameObject> decals = new Queue<GameObject>();
    private readonly Queue<GameObject> gibs = new Queue<GameObject>();
    private readonly List<ScreenSplat> screenSplats = new List<ScreenSplat>();

    private Mesh sphereMesh, cubeMesh, quadMesh, cylinderMesh;
    private Material dropletMat, fleshMat, gutsMat, boneMat, brainMat, debrisMat, holeMat;
    private Material[] splatMats, dripMats;
    private Texture2D[] screenTextures;

    private bool resourcesReady;
    private int decalCounter;
    private float lastSplatSoundTime;

    private void Awake()
    {
        if (instance == null)
            instance = this;

        EnsureResources();
    }

    // ------------------------------------------------------------------ API

    /// <summary>Bullet hitting a non-living surface: dust + bullet hole.</summary>
    public void BulletImpact(Vector3 point, Vector3 normal)
    {
        EnsureResources();

        SpawnDroplets(point + normal * 0.02f, normal, 12, 1f, 3f, 40f,
            0.01f, 0.03f, material: debrisMat, makeDecals: false, lifetime: 0.8f);

        GameObject hole = CreateDecal(holeMat, point, normal, Random.Range(0.05f, 0.08f), false);
        Track(decals, hole, maxDecals);
    }

    /// <summary>Kept for older callers: same as BulletImpact.</summary>
    public void SpawnImpact(Vector3 point, Vector3 normal) => BulletImpact(point, normal);

    /// <summary>Non-lethal (or lethal) bullet wound on a body.</summary>
    public void BulletWound(RaycastHit hit, Vector3 shotDirection, Transform body, bool headshot)
    {
        EnsureResources();

        Vector3 dir = shotDirection.normalized;
        float m = goreMultiplier;

        // Entry spray, back toward the shooter.
        SpawnDroplets(hit.point + hit.normal * 0.03f, Vector3.Lerp(hit.normal, -dir, 0.5f),
            Mathf.RoundToInt(hitDroplets * 0.4f * m), 1.5f, 4.5f, 35f, 0.02f, 0.06f);

        // Exit wound: blast out of the back of the body and paint whatever is behind it.
        Vector3 exit = hit.point + dir * 0.4f;
        if (hit.collider != null &&
            hit.collider.Raycast(new Ray(hit.point + dir * 3f, -dir), out RaycastHit exitHit, 3f))
        {
            exit = exitHit.point;
        }

        SpawnDroplets(exit + dir * 0.05f, dir, Mathf.RoundToInt(hitDroplets * m),
            4f, 10f, 18f, 0.02f, 0.08f);

        // Wound keeps bleeding for a moment.
        SpawnDroplets(hit.point + hit.normal * 0.02f, hit.normal + Vector3.down * 0.5f,
            Mathf.RoundToInt(40 * m), 0.3f, 1.5f, 20f, 0.015f, 0.04f,
            emitDuration: 2.5f, parent: body);

        GameObject wound = CreateDecal(splatMats[Random.Range(0, splatMats.Length)],
            hit.point, hit.normal, Random.Range(0.18f, 0.3f), false);
        wound.transform.SetParent(body, true);
        Track(decals, wound, maxDecals);

        SpawnGibs(exit, dir, Mathf.RoundToInt(3 * m), headshot, null, Vector3.one * 0.05f, 0.6f, true);

        if (headshot)
        {
            SpawnDroplets(hit.point, Vector3.up + dir, Mathf.RoundToInt(90 * m),
                2f, 6f, 50f, 0.015f, 0.05f);
        }

        PlaySplat(hit.point, 0.8f);
        SplatterScreenNear(hit.point, 0.5f);
    }

    /// <summary>Blows the body apart. Call after hiding the original body.</summary>
    public void Dismember(Bounds body, Vector3 hitPoint, Vector3 shotDirection, bool headshot, Material clothes)
    {
        EnsureResources();

        Vector3 dir = shotDirection.sqrMagnitude > 0.0001f ? shotDirection.normalized : Vector3.forward;
        float m = goreMultiplier;
        Vector3 center = body.center;

        // Huge omni-directional burst.
        SpawnDroplets(center, Vector3.up, Mathf.RoundToInt(deathDroplets * m),
            2.5f, 11f, 180f, 0.02f, 0.11f);

        // Directional blast following the bullet.
        SpawnDroplets(hitPoint, dir + Vector3.up * 0.3f, Mathf.RoundToInt(deathDroplets * 0.5f * m),
            6f, 14f, 25f, 0.02f, 0.09f);

        if (headshot)
        {
            Vector3 head = new Vector3(center.x, body.max.y - 0.25f, center.z);
            SpawnDroplets(head, Vector3.up, Mathf.RoundToInt(200 * m), 3f, 9f, 70f, 0.015f, 0.06f);
            SpawnGibs(head, dir + Vector3.up, Mathf.RoundToInt(10 * m), true, null,
                Vector3.one * 0.12f, 1.2f, false);
        }

        SpawnGibs(center, dir, Mathf.RoundToInt(gibCount * m), headshot, clothes,
            body.extents * 0.6f, 1f, false);

        SpawnStump(body, clothes);
        SpawnBloodPool(new Vector3(center.x, body.min.y, center.z));

        AudioSource.PlayClipAtPoint(burstClip != null ? burstClip : ProceduralAudio.Burst, center, 1f);
        SplatterScreenNear(center, 1.5f);
    }

    public void SplatterScreen(int count)
    {
        EnsureResources();

        for (int i = 0; i < count; i++)
        {
            if (screenSplats.Count >= maxScreenSplats)
                screenSplats.RemoveAt(0);

            float size = Random.Range(0.15f, 0.45f) * Screen.height;
            screenSplats.Add(new ScreenSplat
            {
                texture = screenTextures[Random.Range(0, screenTextures.Length)],
                rect = new Rect(
                    Random.Range(-0.1f, 0.9f) * Screen.width,
                    Random.Range(-0.15f, 0.75f) * Screen.height,
                    size, size),
                born = Time.time,
                life = Random.Range(3f, 6f),
                drip = Random.Range(8f, 35f)
            });
        }
    }

    public void SplatterScreenNear(Vector3 position, float intensity)
    {
        Camera cam = Camera.main;
        if (cam == null || screenSplatterRange <= 0f)
            return;

        float distance = Vector3.Distance(cam.transform.position, position);
        if (distance > screenSplatterRange)
            return;

        float closeness = 1f - distance / screenSplatterRange;
        SplatterScreen(Mathf.CeilToInt(intensity * closeness * 8f * goreMultiplier));
    }

    public void PlaySplat(Vector3 position, float volume)
    {
        if (Time.time - lastSplatSoundTime < 0.06f)
            return;

        lastSplatSoundTime = Time.time;
        AudioSource.PlayClipAtPoint(splatClip != null ? splatClip : ProceduralAudio.Splat, position, volume);
    }

    /// <summary>Paints a blood splat on a surface. Splats on walls drip downward.</summary>
    public void SpawnDecal(Vector3 point, Vector3 normal, float size, Transform surface = null)
    {
        EnsureResources();

        if (normal.sqrMagnitude < 0.0001f)
            normal = Vector3.up;
        normal.Normalize();

        bool wall = Mathf.Abs(normal.y) < 0.6f;
        bool drip = wall && Random.value < 0.75f;
        Material mat = drip
            ? dripMats[Random.Range(0, dripMats.Length)]
            : splatMats[Random.Range(0, splatMats.Length)];

        GameObject decal = CreateDecal(mat, point, normal, size, drip);

        Transform anchor = FindDecalAnchor(surface);
        if (anchor != null)
            decal.transform.SetParent(anchor, true);

        Track(decals, decal, maxDecals);
    }

    /// <summary>Blood droplets that bleed from a moving object (gibs, stump).</summary>
    public void AttachBleeding(Transform target, float duration)
    {
        ParticleSystem ps = SpawnDroplets(target.position, Vector3.up, Mathf.RoundToInt(70 * goreMultiplier),
            0f, 0.6f, 60f, 0.012f, 0.035f, emitDuration: duration, parent: target, decalSizeScale: 0.8f);

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverDistance = 20f;
    }

    public ParticleSystem SpawnDroplets(
        Vector3 position, Vector3 direction, int count,
        float minSpeed, float maxSpeed, float spreadAngle,
        float minSize, float maxSize,
        float emitDuration = 0f, Transform parent = null,
        float decalSizeScale = 1f, Material material = null,
        bool makeDecals = true, float lifetime = 3.5f)
    {
        EnsureResources();
        count = Mathf.Clamp(count, 1, 3000);

        GameObject go = new GameObject("BloodSpray");
        go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(
            direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.up));
        if (parent != null)
            go.transform.SetParent(parent, true);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.duration = Mathf.Max(0.05f, emitDuration);
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.45f, lifetime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(minSpeed, maxSpeed);
        main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
        main.startColor = Color.white;
        main.gravityModifier = 1.3f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = count + 16;
        main.stopAction = ParticleSystemStopAction.Destroy;

        ParticleSystem.EmissionModule emission = ps.emission;
        if (emitDuration > 0f)
        {
            emission.rateOverTime = count / emitDuration;
        }
        else
        {
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        }

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        if (spreadAngle >= 90f)
        {
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.15f;
        }
        else
        {
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = spreadAngle;
            shape.radius = 0.02f;
        }

        ParticleSystem.CollisionModule collision = ps.collision;
        collision.enabled = true;
        collision.type = ParticleSystemCollisionType.World;
        collision.mode = ParticleSystemCollisionMode.Collision3D;
        collision.collidesWith = ~(1 << IgnoreRaycastLayer);
        collision.bounce = 0f;
        collision.lifetimeLoss = 1f;
        collision.sendCollisionMessages = makeDecals;

        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = sphereMesh;
        renderer.sharedMaterial = material != null ? material : dropletMat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;

        if (makeDecals)
        {
            go.AddComponent<BloodDropletCollision>().Init(
                this, decalChance, minSize * 4f * decalSizeScale, maxSize * 6f * decalSizeScale);
        }

        ps.Play();
        return ps;
    }

    // -------------------------------------------------------------- Pieces

    private void SpawnGibs(Vector3 center, Vector3 dir, int count, bool headshot,
        Material clothes, Vector3 spread, float speedScale, bool small)
    {
        for (int i = 0; i < count; i++)
        {
            float roll = Random.value;
            Material mat;
            Mesh mesh;
            Vector3 scale;

            if (headshot && roll < 0.2f)
            {
                mat = brainMat;
                mesh = sphereMesh;
                scale = RandomVector(0.06f, 0.14f);
            }
            else if (roll < 0.55f)
            {
                mat = Random.value < 0.5f ? fleshMat : gutsMat;
                mesh = Random.value < 0.5f ? sphereMesh : cubeMesh;
                scale = RandomVector(0.06f, 0.22f);
            }
            else if (roll < 0.75f && clothes != null)
            {
                mat = clothes;
                mesh = cubeMesh;
                scale = new Vector3(Random.Range(0.12f, 0.3f), Random.Range(0.1f, 0.28f), Random.Range(0.025f, 0.05f));
            }
            else
            {
                mat = boneMat;
                mesh = cubeMesh;
                scale = new Vector3(0.035f, 0.035f, Random.Range(0.18f, 0.42f));
            }

            if (small)
                scale *= 0.5f;

            GameObject gib = new GameObject("Gib");
            gib.layer = IgnoreRaycastLayer;
            gib.transform.SetPositionAndRotation(
                center + Vector3.Scale(Random.insideUnitSphere, spread),
                Random.rotation);
            gib.transform.localScale = scale;

            gib.AddComponent<MeshFilter>().sharedMesh = mesh;
            gib.AddComponent<MeshRenderer>().sharedMaterial = mat;

            if (mesh == sphereMesh)
                gib.AddComponent<SphereCollider>();
            else
                gib.AddComponent<BoxCollider>();

            Rigidbody rb = gib.AddComponent<Rigidbody>();
            rb.mass = Mathf.Clamp(scale.x * scale.y * scale.z * 1000f, 0.05f, 4f);
            rb.linearDamping = 0.1f;
            rb.angularDamping = 0.3f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            Vector3 velocity = (Random.onUnitSphere * 0.8f + dir * 0.9f + Vector3.up * 0.9f).normalized;
            rb.linearVelocity = velocity * Random.Range(3f, 10f) * speedScale;
            rb.angularVelocity = Random.insideUnitSphere * 20f;

            gib.AddComponent<Gib>().Init(this);

            if (!small && Random.value < 0.75f)
                AttachBleeding(gib.transform, Random.Range(1.5f, 3.5f));

            Track(gibs, gib, maxGibs);
            Destroy(gib, gibLifetime * Random.Range(0.9f, 1.1f));
        }
    }

    private void SpawnStump(Bounds body, Material clothes)
    {
        float width = Mathf.Max(0.3f, Mathf.Min(body.size.x, body.size.z));
        float height = body.size.y * 0.38f;

        GameObject root = new GameObject("Stump");
        root.layer = IgnoreRaycastLayer;
        root.transform.position = new Vector3(body.center.x, body.min.y, body.center.z);

        AddVisual(root.transform, cylinderMesh, clothes != null ? clothes : fleshMat,
            new Vector3(0f, height * 0.5f, 0f), new Vector3(width * 0.9f, height * 0.5f, width * 0.9f));
        AddVisual(root.transform, cylinderMesh, gutsMat,
            new Vector3(0f, height, 0f), new Vector3(width * 0.85f, 0.03f, width * 0.85f));
        AddVisual(root.transform, cubeMesh, boneMat,
            new Vector3(0f, height + 0.14f, -width * 0.12f), new Vector3(0.05f, 0.3f, 0.05f));

        for (int i = 0; i < 5; i++)
        {
            Vector2 offset = Random.insideUnitCircle * width * 0.35f;
            AddVisual(root.transform, sphereMesh, Random.value < 0.5f ? fleshMat : gutsMat,
                new Vector3(offset.x, height + 0.02f, offset.y), RandomVector(0.07f, 0.14f));
        }

        CapsuleCollider capsule = root.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, height * 0.5f, 0f);
        capsule.height = height;
        capsule.radius = width * 0.45f;

        root.AddComponent<BloodStump>().Init(this, new Vector3(0f, height + 0.05f, 0f));
        Track(gibs, root, maxGibs);
    }

    private void SpawnBloodPool(Vector3 feet)
    {
        Vector3 point = feet;
        Vector3 normal = Vector3.up;

        if (Physics.Raycast(feet + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 3f,
                ~(1 << IgnoreRaycastLayer), QueryTriggerInteraction.Ignore))
        {
            point = hit.point;
            normal = hit.normal;
        }

        for (int i = 0; i < 2; i++)
        {
            GameObject pool = CreateDecal(splatMats[Random.Range(0, splatMats.Length)],
                point + Random.insideUnitSphere * 0.15f, normal, 0.2f, false);
            pool.AddComponent<GrowingDecal>().Init(
                Random.Range(2f, 3f) * Mathf.Sqrt(goreMultiplier), Random.Range(7f, 11f));
            Track(decals, pool, maxDecals);
        }
    }

    // ------------------------------------------------------------ Helpers

    private GameObject CreateDecal(Material mat, Vector3 point, Vector3 normal, float size, bool drip)
    {
        Quaternion rotation;
        if (Mathf.Abs(normal.y) < 0.6f)
        {
            // Keep "down" in the texture pointing down the wall so drips run down.
            rotation = Quaternion.LookRotation(-normal, Vector3.up);
        }
        else
        {
            Vector3 tangent = Vector3.ProjectOnPlane(Random.onUnitSphere, normal);
            if (tangent.sqrMagnitude < 0.0001f)
                tangent = Vector3.forward;
            rotation = Quaternion.LookRotation(-normal, tangent);
        }

        GameObject decal = new GameObject("BloodDecal");
        decal.transform.SetPositionAndRotation(
            point + normal * (0.004f + (decalCounter++ % 64) * 0.0003f), rotation);

        float aspect = Random.Range(0.8f, 1.25f);
        decal.transform.localScale = new Vector3(size * aspect, size * (drip ? 1.4f : 1f), 1f);

        decal.AddComponent<MeshFilter>().sharedMesh = quadMesh;
        MeshRenderer renderer = decal.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;

        return decal;
    }

    private static Transform FindDecalAnchor(Transform surface)
    {
        if (surface == null)
            return null;

        // Only anchor to unscaled movers so the splat is not stretched.
        HouseDoor door = surface.GetComponentInParent<HouseDoor>();
        if (door != null)
            return door.transform;

        NPCHealth npc = surface.GetComponentInParent<NPCHealth>();
        if (npc != null)
            return npc.transform;

        return null;
    }

    private static void AddVisual(Transform parent, Mesh mesh, Material mat, Vector3 localPosition, Vector3 localScale)
    {
        GameObject go = new GameObject("StumpPart");
        go.layer = IgnoreRaycastLayer;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = Quaternion.Euler(Random.Range(-8f, 8f), Random.Range(0f, 360f), Random.Range(-8f, 8f));
        go.transform.localScale = localScale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
    }

    private static void Track(Queue<GameObject> queue, GameObject go, int max)
    {
        queue.Enqueue(go);
        while (queue.Count > max)
        {
            GameObject oldest = queue.Dequeue();
            if (oldest != null)
                Destroy(oldest);
        }
    }

    private static Vector3 RandomVector(float min, float max)
    {
        return new Vector3(Random.Range(min, max), Random.Range(min, max), Random.Range(min, max));
    }

    private void EnsureResources()
    {
        if (resourcesReady)
            return;
        resourcesReady = true;

        sphereMesh = PrimitiveMesh(PrimitiveType.Sphere);
        cubeMesh = PrimitiveMesh(PrimitiveType.Cube);
        quadMesh = PrimitiveMesh(PrimitiveType.Quad);
        cylinderMesh = PrimitiveMesh(PrimitiveType.Cylinder);

        dropletMat = CreateLitMaterial(bloodColor, 0.9f);
        fleshMat = CreateLitMaterial(fleshColor, 0.7f);
        gutsMat = CreateLitMaterial(Color.Lerp(fleshColor, bloodColor, 0.6f), 0.85f);
        boneMat = CreateLitMaterial(boneColor, 0.3f);
        brainMat = CreateLitMaterial(brainColor, 0.6f);
        debrisMat = CreateLitMaterial(new Color(0.35f, 0.33f, 0.3f), 0.1f);

        splatMats = new Material[4];
        dripMats = new Material[2];
        screenTextures = new Texture2D[splatMats.Length + dripMats.Length];

        for (int i = 0; i < splatMats.Length; i++)
        {
            Texture2D texture = BloodTextures.Create(i, false);
            screenTextures[i] = texture;
            splatMats[i] = CreateDecalMaterial(texture, bloodColor);
        }

        for (int i = 0; i < dripMats.Length; i++)
        {
            Texture2D texture = BloodTextures.Create(i, true);
            screenTextures[splatMats.Length + i] = texture;
            dripMats[i] = CreateDecalMaterial(texture, bloodColor);
        }

        holeMat = CreateDecalMaterial(screenTextures[0], new Color(0.05f, 0.04f, 0.04f));
    }

    private Material CreateDecalMaterial(Texture2D texture, Color color)
    {
        Material mat = decalTemplate != null ? new Material(decalTemplate) : CreateCutoutMaterial(color);
        mat.SetColor("_BaseColor", color);
        mat.SetTexture("_BaseMap", texture);
        mat.SetTexture("_MainTex", texture);
        return mat;
    }

    private static Mesh PrimitiveMesh(PrimitiveType type)
    {
        GameObject temp = GameObject.CreatePrimitive(type);
        Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;
        Destroy(temp);
        return mesh;
    }

    public static Material CreateLitMaterial(Color color, float smoothness)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");

        Material mat = new Material(shader);
        mat.SetColor("_BaseColor", color);
        mat.SetColor("_Color", color);
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_Glossiness", smoothness);
        mat.enableInstancing = true;
        return mat;
    }

    public static Material CreateCutoutMaterial(Color color)
    {
        Material mat = CreateLitMaterial(color, 0.85f);
        mat.SetFloat("_AlphaClip", 1f);
        mat.SetFloat("_Cutoff", 0.5f);
        mat.EnableKeyword("_ALPHATEST_ON");
        mat.SetOverrideTag("RenderType", "TransparentCutout");
        mat.renderQueue = (int)RenderQueue.AlphaTest;
        return mat;
    }

    private void OnGUI()
    {
        if (screenSplats.Count == 0 || Event.current.type != EventType.Repaint)
            return;

        float now = Time.time;
        Color tint = new Color(Mathf.Min(1f, bloodColor.r * 1.5f), bloodColor.g, bloodColor.b, 1f);

        for (int i = screenSplats.Count - 1; i >= 0; i--)
        {
            ScreenSplat splat = screenSplats[i];
            float age = now - splat.born;

            if (age > splat.life)
            {
                screenSplats.RemoveAt(i);
                continue;
            }

            float fadeStart = splat.life - 1.5f;
            tint.a = age < fadeStart ? 0.92f : Mathf.Lerp(0.92f, 0f, (age - fadeStart) / 1.5f);

            Rect rect = splat.rect;
            rect.y += splat.drip * age;
            rect.height += splat.drip * age * 0.6f;

            GUI.color = tint;
            GUI.DrawTexture(rect, splat.texture, ScaleMode.StretchToFill, true);
        }

        GUI.color = Color.white;
    }
}

/// <summary>Turns droplet collisions into splats (and screen blood if they hit the player).</summary>
public class BloodDropletCollision : MonoBehaviour
{
    private readonly List<ParticleCollisionEvent> events = new List<ParticleCollisionEvent>();
    private ParticleSystem ps;
    private GoreFX fx;
    private float chance;
    private float minSize;
    private float maxSize;

    public void Init(GoreFX owner, float decalChance, float minDecalSize, float maxDecalSize)
    {
        fx = owner;
        chance = decalChance;
        minSize = minDecalSize;
        maxSize = maxDecalSize;
        ps = GetComponent<ParticleSystem>();
    }

    private void OnParticleCollision(GameObject other)
    {
        if (ps == null || fx == null)
            return;

        int count = ps.GetCollisionEvents(other, events);

        if (other.GetComponentInParent<FirstPersonController_TEST>() != null)
        {
            fx.SplatterScreen(Mathf.Min(count, 3));
            return;
        }

        for (int i = 0; i < count; i++)
        {
            if (Random.value > chance)
                continue;

            fx.SpawnDecal(events[i].intersection, events[i].normal,
                Random.Range(minSize, maxSize), other.transform);
        }
    }
}

/// <summary>A flying chunk of body. Leaves splats where it lands.</summary>
public class Gib : MonoBehaviour
{
    private GoreFX fx;
    private int splatsLeft = 4;
    private float lastSplat;

    public void Init(GoreFX owner) => fx = owner;

    private void OnCollisionEnter(Collision collision)
    {
        if (fx == null || splatsLeft <= 0 || Time.time - lastSplat < 0.15f)
            return;

        if (collision.relativeVelocity.magnitude < 1.5f || collision.contactCount == 0)
            return;

        if (collision.collider.GetComponentInParent<FirstPersonController_TEST>() != null)
        {
            fx.SplatterScreen(2);
            return;
        }

        if (collision.collider.gameObject.layer == gameObject.layer)
            return;

        ContactPoint contact = collision.GetContact(0);
        fx.SpawnDecal(contact.point, contact.normal, Random.Range(0.25f, 0.6f), collision.collider.transform);
        fx.PlaySplat(contact.point, 0.35f);

        splatsLeft--;
        lastSplat = Time.time;
    }
}

/// <summary>What's left standing. Pumps blood with a fading heartbeat, then topples.</summary>
public class BloodStump : MonoBehaviour
{
    private GoreFX fx;
    private Vector3 top;

    public void Init(GoreFX owner, Vector3 localTop)
    {
        fx = owner;
        top = localTop;
    }

    private IEnumerator Start()
    {
        const int beats = 14;

        for (int i = 0; i < beats && fx != null; i++)
        {
            float strength = 1f - i / (float)beats;
            Vector3 origin = transform.TransformPoint(top);
            Vector3 up = transform.up + Random.insideUnitSphere * 0.15f;

            fx.SpawnDroplets(origin, up, Mathf.RoundToInt(35f * strength * fx.Multiplier) + 5,
                1f + 2f * strength, 1.5f + 6f * strength, 15f, 0.02f, 0.06f);

            if (i % 3 == 0)
                fx.PlaySplat(origin, 0.4f * strength);

            if (i == 4)
                Topple();

            yield return new WaitForSeconds(0.45f);
        }
    }

    private void Topple()
    {
        Rigidbody rb = gameObject.AddComponent<Rigidbody>();
        rb.mass = 25f;
        Vector2 push = Random.insideUnitCircle.normalized;
        rb.AddForceAtPosition(new Vector3(push.x, 0f, push.y) * 30f,
            transform.TransformPoint(top), ForceMode.Impulse);
    }
}

/// <summary>Scales a pool decal up over time (ease out).</summary>
public class GrowingDecal : MonoBehaviour
{
    private float startSize;
    private float targetSize;
    private float duration;
    private float t;

    public void Init(float target, float seconds)
    {
        startSize = transform.localScale.x;
        targetSize = target;
        duration = seconds;
    }

    private void Update()
    {
        t += Time.deltaTime / Mathf.Max(0.01f, duration);
        float eased = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
        float size = Mathf.Lerp(startSize, targetSize, eased);
        transform.localScale = new Vector3(size, size, 1f);

        if (t >= 1f)
            Destroy(this);
    }
}

/// <summary>Procedural blood splat / drip textures (greyscale + alpha, tinted by the material).</summary>
public static class BloodTextures
{
    public static Texture2D Create(int seed, bool drips)
    {
        const int size = 128;
        System.Random rng = new System.Random(seed * 7919 + (drips ? 104729 : 0));
        float Rand(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        Vector2 center = drips ? new Vector2(0.5f, 0.68f) : new Vector2(0.5f, 0.5f);
        float baseRadius = drips ? Rand(0.16f, 0.2f) : Rand(0.2f, 0.26f);

        const int harmonics = 7;
        float[] amplitude = new float[harmonics];
        float[] phase = new float[harmonics];
        int[] frequency = new int[harmonics];
        for (int k = 0; k < harmonics; k++)
        {
            frequency[k] = k + 2 + rng.Next(0, 5);
            amplitude[k] = Rand(0.03f, 0.14f) / (1f + k * 0.3f);
            phase[k] = Rand(0f, Mathf.PI * 2f);
        }

        int spikeCount = rng.Next(4, 10);
        float[] spikeAngle = new float[spikeCount];
        float[] spikeLength = new float[spikeCount];
        float[] spikeWidth = new float[spikeCount];
        for (int s = 0; s < spikeCount; s++)
        {
            spikeAngle[s] = Rand(-180f, 180f);
            spikeLength[s] = Rand(0.05f, 0.2f);
            spikeWidth[s] = Rand(3f, 9f);
        }

        int dotCount = rng.Next(8, 22);
        Vector3[] dots = new Vector3[dotCount];
        for (int d = 0; d < dotCount; d++)
        {
            float angle = Rand(0f, Mathf.PI * 2f);
            float distance = baseRadius * Rand(1.1f, 2.1f);
            Vector2 p = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
            dots[d] = new Vector3(Mathf.Clamp(p.x, 0.05f, 0.95f), Mathf.Clamp(p.y, 0.05f, 0.95f), Rand(0.008f, 0.035f));
        }

        int dripCount = drips ? rng.Next(3, 7) : 0;
        float[] dripX = new float[dripCount];
        float[] dripWidth = new float[dripCount];
        float[] dripEnd = new float[dripCount];
        for (int j = 0; j < dripCount; j++)
        {
            dripX[j] = center.x + Rand(-0.8f, 0.8f) * baseRadius;
            dripWidth[j] = Rand(0.012f, 0.03f);
            dripEnd[j] = Rand(0.05f, 0.4f);
        }

        Color32[] pixels = new Color32[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2((x + 0.5f) / size, (y + 0.5f) / size);
                Vector2 delta = p - center;
                float r = delta.magnitude;
                float angle = Mathf.Atan2(delta.y, delta.x);

                float radius = baseRadius;
                for (int k = 0; k < harmonics; k++)
                    radius += baseRadius * amplitude[k] * Mathf.Sin(frequency[k] * angle + phase[k]);

                float angleDeg = angle * Mathf.Rad2Deg;
                for (int s = 0; s < spikeCount; s++)
                {
                    float da = Mathf.Abs(Mathf.DeltaAngle(angleDeg, spikeAngle[s]));
                    if (da < spikeWidth[s])
                        radius = Mathf.Max(radius, baseRadius + spikeLength[s] * (1f - da / spikeWidth[s]));
                }

                bool inside = r < radius;
                float edge = r / Mathf.Max(radius, 0.001f);

                for (int d = 0; !inside && d < dotCount; d++)
                {
                    if (Vector2.Distance(p, dots[d]) < dots[d].z)
                    {
                        inside = true;
                        edge = 0.5f;
                    }
                }

                for (int j = 0; !inside && j < dripCount; j++)
                {
                    if (p.y < center.y && p.y > dripEnd[j])
                    {
                        float t = (center.y - p.y) / (center.y - dripEnd[j]);
                        if (Mathf.Abs(p.x - dripX[j]) < dripWidth[j] * (1f - 0.5f * t))
                            inside = true;
                    }

                    if (Vector2.Distance(p, new Vector2(dripX[j], dripEnd[j])) < dripWidth[j] * 1.3f)
                        inside = true;
                }

                float shade = inside ? Mathf.Clamp01(0.7f + 0.3f * edge + Rand(-0.06f, 0.06f)) : 0f;
                byte value = (byte)(shade * 255f);
                pixels[y * size + x] = new Color32(value, value, value, (byte)(inside ? 255 : 0));
            }
        }

        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = drips ? "BloodDrip" : "BloodSplat",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }
}
