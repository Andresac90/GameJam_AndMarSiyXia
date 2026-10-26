using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Menu: Trick or Threat > Setup NPC Test Scene.
/// Wires the open scene: player (scale, camera, interactor, gun, pistol), a knockable
/// door in the HouseTest gap, NPC placement, DoorEncounter references and GoreFX.
/// Safe to run more than once. Everything is undoable; save the scene afterwards.
/// </summary>
public static class TrickOrThreatSceneSetup
{
    private const string PistolPrefabPath = "Assets/Prefabs/Pistol.prefab";
    private const string DoorMaterialPath = "Assets/Materials/Door.mat";
    private const string GoreFolder = "Assets/Materials/Gore";
    private const string DecalMaterialPath = GoreFolder + "/BloodDecalTemplate.mat";

    /// <summary>Batch mode entry: -executeMethod TrickOrThreatSceneSetup.SetupNPCTestSceneAndSave</summary>
    public static void SetupNPCTestSceneAndSave()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/NPC_Test.unity", OpenSceneMode.Single);
        Setup();
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Trick or Threat/Setup NPC Test Scene")]
    public static void Setup()
    {
        FirstPersonController_TEST player = Object.FindAnyObjectByType<FirstPersonController_TEST>();
        if (player == null)
        {
            EditorUtility.DisplayDialog("Trick or Threat",
                "No FirstPersonController_TEST in the open scene. Drag Assets/Prefabs/Player.prefab in first.", "OK");
            return;
        }

        Undo.SetCurrentGroupName("Trick or Threat Setup");
        int undoGroup = Undo.GetCurrentGroup();

        HouseDoor door = SetupDoor(out Vector3 doorFront, out Vector3 doorCenter);
        NPCHealth npc = SetupNPC(doorFront, doorCenter);
        Camera cam = SetupPlayer(player, doorFront, doorCenter);
        SetupGore(npc);
        SetupEncounter(door, player, npc);

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);

        Selection.activeGameObject = cam != null ? cam.gameObject : player.gameObject;
        Debug.Log("Trick or Threat: scene wired. Save the scene (Ctrl/Cmd+S) and press Play. " +
                  "If the pistol points backwards, rotate PlayerCamera/Pistol 180° on Y.");
    }

    // ------------------------------------------------------------ Door

    private static HouseDoor SetupDoor(out Vector3 doorFront, out Vector3 doorCenter)
    {
        GameObject house = GameObject.Find("HouseTest");

        // Door gap: between Cube (3) (left) and Cube (2) (right), above Cube (1), below Cube (top).
        Bounds? left = RendererBounds(house, "Cube (3)");
        Bounds? right = RendererBounds(house, "Cube (2)");
        Bounds? bottom = RendererBounds(house, "Cube (1)");
        Bounds? top = RendererBounds(house, "Cube");

        float minX = left?.max.x ?? 0.95f;
        float maxX = right?.min.x ?? 2.45f;
        float minY = Mathf.Max(0f, bottom?.max.y ?? 0.35f);
        float maxY = top?.min.y ?? 2.45f;
        float z = right?.center.z ?? 3.28f;
        float frontZ = right?.min.z ?? 2.78f;

        float width = maxX - minX;
        float height = maxY - minY;

        doorCenter = new Vector3((minX + maxX) * 0.5f, minY, z);
        doorFront = new Vector3(doorCenter.x, 0f, frontZ);

        HouseDoor existing = Object.FindAnyObjectByType<HouseDoor>();
        if (existing != null)
            return existing;

        GameObject hinge = new GameObject("Door_Hinge");
        Undo.RegisterCreatedObjectUndo(hinge, "Create door");
        if (house != null)
            Undo.SetTransformParent(hinge.transform, house.transform, "Parent door");
        hinge.transform.SetPositionAndRotation(new Vector3(minX, minY, z), Quaternion.identity);

        GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        panel.name = "Door_Panel";
        Undo.RegisterCreatedObjectUndo(panel, "Create door panel");
        panel.transform.SetParent(hinge.transform, false);
        panel.transform.localPosition = new Vector3(width * 0.5f, height * 0.5f, 0f);
        panel.transform.localScale = new Vector3(width - 0.02f, height - 0.02f, 0.1f);
        panel.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial(
            DoorMaterialPath, () => GoreFX.CreateLitMaterial(new Color(0.22f, 0.12f, 0.07f), 0.25f));

        GameObject knob = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        knob.name = "Door_Knob";
        Undo.RegisterCreatedObjectUndo(knob, "Create door knob");
        Object.DestroyImmediate(knob.GetComponent<Collider>());
        knob.transform.SetParent(hinge.transform, false);
        knob.transform.localPosition = new Vector3(width - 0.15f, height * 0.45f, -0.07f);
        knob.transform.localScale = Vector3.one * 0.07f;

        AudioSource audio = Undo.AddComponent<AudioSource>(hinge);
        audio.playOnAwake = false;
        audio.spatialBlend = 1f;

        return Undo.AddComponent<HouseDoor>(hinge);
    }

    // ------------------------------------------------------------- NPC

    private static NPCHealth SetupNPC(Vector3 doorFront, Vector3 doorCenter)
    {
        NPCHealth npc = Object.FindAnyObjectByType<NPCHealth>(FindObjectsInactive.Include);
        if (npc == null)
        {
            GameObject npcObject = GameObject.Find("Test_NPC");
            if (npcObject == null)
                return null;
            npc = Undo.AddComponent<NPCHealth>(npcObject);
        }

        // Stand on the porch, just in front of the door, facing outward.
        float groundY = GroundHeight(new Vector3(doorFront.x, 0f, doorFront.z - 0.6f));
        float halfHeight = 1f;
        CapsuleCollider capsule = npc.GetComponent<CapsuleCollider>();
        if (capsule != null)
            halfHeight = capsule.height * 0.5f * npc.transform.lossyScale.y;

        Undo.RecordObject(npc.transform, "Place NPC");
        npc.transform.SetPositionAndRotation(
            new Vector3(doorCenter.x, groundY + halfHeight, doorFront.z - 0.6f),
            Quaternion.Euler(0f, 180f, 0f));

        return npc;
    }

    // ---------------------------------------------------------- Player

    private static Camera SetupPlayer(FirstPersonController_TEST player, Vector3 doorFront, Vector3 doorCenter)
    {
        Transform root = player.transform;
        CharacterController controller = player.GetComponent<CharacterController>();
        Camera cam = player.GetComponentInChildren<Camera>(true);

        // The prefab is scaled ~2.3x, which puts the eyes ~5 m up. Normalize the scene instance.
        // (Root * camera scale was already 1, so the arm rig keeps its world size.)
        float halfHeight = controller != null ? controller.height * 0.5f : 1f;
        Vector3 spawn = new Vector3(doorFront.x, 0f, doorFront.z - 4.5f);
        spawn.y = GroundHeight(spawn) + halfHeight - (controller != null ? controller.center.y : 0f) + 0.05f;

        Vector3 look = doorCenter - spawn;
        look.y = 0f;

        Undo.RecordObject(root, "Fix player transform");
        root.localScale = Vector3.one;
        root.SetPositionAndRotation(spawn, Quaternion.LookRotation(look.sqrMagnitude > 0.01f ? look : Vector3.forward));
        PrefabUtility.RecordPrefabInstancePropertyModifications(root);

        if (cam == null)
        {
            Debug.LogWarning("Trick or Threat: player has no camera child.");
            return null;
        }

        Undo.RecordObject(cam.transform, "Fix player camera");
        cam.transform.localScale = Vector3.one;
        cam.transform.localRotation = Quaternion.identity;
        cam.transform.localPosition = new Vector3(0f,
            (controller != null ? controller.center.y : 0f) + halfHeight - 0.2f, 0f);
        PrefabUtility.RecordPrefabInstancePropertyModifications(cam.transform);

        Undo.RecordObject(cam, "Camera near clip");
        cam.nearClipPlane = 0.03f;
        cam.tag = "MainCamera";
        PrefabUtility.RecordPrefabInstancePropertyModifications(cam);

        SerializedObject controllerSO = new SerializedObject(player);
        controllerSO.FindProperty("cameraTransform").objectReferenceValue = cam.transform;
        SerializedProperty sensitivity = controllerSO.FindProperty("mouseSensitivity");
        if (sensitivity.floatValue > 0.5f)
            sensitivity.floatValue = 0.1f; // Input System mouse delta is in pixels.
        controllerSO.ApplyModifiedProperties();

        PlayerInteractor interactor = cam.GetComponent<PlayerInteractor>();
        if (interactor == null)
            interactor = Undo.AddComponent<PlayerInteractor>(cam.gameObject);
        Undo.RecordObject(interactor, "Wire interactor");
        interactor.player = player;

        AudioSource gunAudio = cam.GetComponent<AudioSource>();
        if (gunAudio == null)
        {
            gunAudio = Undo.AddComponent<AudioSource>(cam.gameObject);
            gunAudio.playOnAwake = false;
            gunAudio.spatialBlend = 0f;
        }

        Transform pistol = cam.transform.Find("Pistol");
        Transform muzzle = pistol != null ? pistol.Find("Muzzle") : null;
        if (pistol == null)
            pistol = CreatePistol(cam.transform, out muzzle);

        PlayerGun gun = cam.GetComponent<PlayerGun>();
        if (gun == null)
            gun = Undo.AddComponent<PlayerGun>(cam.gameObject);

        SerializedObject gunSO = new SerializedObject(gun);
        gunSO.FindProperty("aimCamera").objectReferenceValue = cam;
        gunSO.FindProperty("player").objectReferenceValue = player;
        gunSO.FindProperty("viewModel").objectReferenceValue = pistol;
        gunSO.FindProperty("muzzle").objectReferenceValue = muzzle;
        gunSO.FindProperty("audioSource").objectReferenceValue = gunAudio;
        gunSO.FindProperty("damage").floatValue = 100f;
        gunSO.FindProperty("hitMask").intValue = ~(1 << 2);
        gunSO.ApplyModifiedProperties();

        return cam;
    }

    private static Transform CreatePistol(Transform cam, out Transform muzzle)
    {
        muzzle = null;
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PistolPrefabPath);
        if (prefab == null)
        {
            Debug.LogWarning("Trick or Threat: " + PistolPrefabPath + " not found, gun has no model.");
            return null;
        }

        GameObject pistol = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        Undo.RegisterCreatedObjectUndo(pistol, "Create pistol");
        pistol.name = "Pistol";
        pistol.transform.SetParent(cam, false);
        pistol.transform.localPosition = Vector3.zero;
        pistol.transform.localRotation = Quaternion.identity;
        pistol.transform.localScale = Vector3.one;

        foreach (Collider collider in pistol.GetComponentsInChildren<Collider>())
            Object.DestroyImmediate(collider);

        FitViewModel(pistol.transform, cam, out Vector3 muzzleLocal);

        GameObject muzzleObject = new GameObject("Muzzle");
        Undo.RegisterCreatedObjectUndo(muzzleObject, "Create muzzle");
        muzzleObject.transform.SetParent(pistol.transform, false);
        muzzleObject.transform.position = cam.TransformPoint(muzzleLocal);
        muzzle = muzzleObject.transform;

        return pistol.transform;
    }

    /// <summary>
    /// Orients and scales the pistol to sit bottom-right of the view.
    /// Longest axis = barrel, the half that spans the full length = slide (top),
    /// the short lower part = grip (rear).
    /// </summary>
    private static void FitViewModel(Transform pistol, Transform cam, out Vector3 muzzleLocal)
    {
        List<Vector3> points = new List<Vector3>();
        Matrix4x4 toCam = cam.worldToLocalMatrix;

        foreach (MeshFilter filter in pistol.GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null) continue;
            Matrix4x4 matrix = toCam * filter.transform.localToWorldMatrix;
            // Edit mode can read imported meshes even with Read/Write off; fall back to bounds if not.
            Vector3[] vertices = filter.sharedMesh.vertices;

            if (vertices != null && vertices.Length > 0)
            {
                foreach (Vector3 v in vertices)
                    points.Add(matrix.MultiplyPoint3x4(v));
            }
            else
            {
                Bounds b = filter.sharedMesh.bounds;
                points.Add(matrix.MultiplyPoint3x4(b.min));
                points.Add(matrix.MultiplyPoint3x4(b.max));
            }
        }

        if (points.Count == 0)
        {
            pistol.localPosition = new Vector3(0.18f, -0.17f, 0.42f);
            muzzleLocal = pistol.localPosition + Vector3.forward * 0.15f;
            return;
        }

        Bounds bounds = new Bounds(points[0], Vector3.zero);
        foreach (Vector3 p in points) bounds.Encapsulate(p);

        Vector3 size = bounds.size;
        int[] axes = { 0, 1, 2 };
        System.Array.Sort(axes, (a, b) => size[b].CompareTo(size[a]));
        int lengthAxis = axes[0];
        int heightAxis = axes[1];

        // Which side of the height axis is the slide? The slide spans the full length.
        float midHeight = bounds.center[heightAxis];
        float highMin = float.MaxValue, highMax = float.MinValue, lowMin = float.MaxValue, lowMax = float.MinValue;
        float lowSum = 0f;
        int lowCount = 0;
        foreach (Vector3 p in points)
        {
            if (p[heightAxis] >= midHeight)
            {
                highMin = Mathf.Min(highMin, p[lengthAxis]);
                highMax = Mathf.Max(highMax, p[lengthAxis]);
            }
            else
            {
                lowMin = Mathf.Min(lowMin, p[lengthAxis]);
                lowMax = Mathf.Max(lowMax, p[lengthAxis]);
                lowSum += p[lengthAxis];
                lowCount++;
            }
        }

        bool slideIsHigh = (highMax - highMin) >= (lowMax - lowMin);

        // The grip hangs under the rear, so the barrel points away from it.
        float gripCenter;
        if (slideIsHigh)
        {
            gripCenter = lowCount > 0 ? lowSum / lowCount : bounds.center[lengthAxis];
        }
        else
        {
            float sum = 0f;
            int count = 0;
            foreach (Vector3 p in points)
            {
                if (p[heightAxis] >= midHeight)
                {
                    sum += p[lengthAxis];
                    count++;
                }
            }
            gripCenter = count > 0 ? sum / count : bounds.center[lengthAxis];
        }

        Vector3 up = Vector3.zero;
        up[heightAxis] = slideIsHigh ? 1f : -1f;
        Vector3 forward = Vector3.zero;
        forward[lengthAxis] = gripCenter <= bounds.center[lengthAxis] ? 1f : -1f;

        Quaternion rotation = Quaternion.Inverse(Quaternion.LookRotation(forward, up));
        float scale = 0.24f / Mathf.Max(0.0001f, size[lengthAxis]);

        Bounds fitted = new Bounds(rotation * (points[0] * scale), Vector3.zero);
        foreach (Vector3 p in points) fitted.Encapsulate(rotation * (p * scale));

        Vector3 target = new Vector3(0.17f, -0.16f, 0.4f);
        Vector3 offset = target - fitted.center;

        pistol.localRotation = rotation;
        pistol.localScale = Vector3.one * scale;
        pistol.localPosition = offset;

        muzzleLocal = new Vector3(fitted.center.x, fitted.max.y - fitted.size.y * 0.25f, fitted.max.z) + offset;
    }

    // ------------------------------------------------------------ Gore

    private static void SetupGore(NPCHealth npc)
    {
        // GoreFX must not live on the NPC (it gets disabled between encounters).
        if (npc != null)
        {
            foreach (GoreFX onNpc in npc.GetComponentsInChildren<GoreFX>(true))
                Undo.DestroyObjectImmediate(onNpc);
        }

        GoreFX[] all = Object.FindObjectsByType<GoreFX>(FindObjectsInactive.Include);
        GoreFX gore = all.Length > 0 ? all[0] : null;
        for (int i = 1; i < all.Length; i++)
            Undo.DestroyObjectImmediate(all[i]);

        if (gore == null)
        {
            GameObject go = new GameObject("GoreFX");
            Undo.RegisterCreatedObjectUndo(go, "Create GoreFX");
            gore = Undo.AddComponent<GoreFX>(go);
        }

        if (!AssetDatabase.IsValidFolder(GoreFolder))
            AssetDatabase.CreateFolder("Assets/Materials", "Gore");

        Material template = GetOrCreateMaterial(DecalMaterialPath,
            () => GoreFX.CreateCutoutMaterial(new Color(0.42f, 0.01f, 0.02f)));

        SerializedObject goreSO = new SerializedObject(gore);
        goreSO.FindProperty("decalTemplate").objectReferenceValue = template;
        goreSO.ApplyModifiedProperties();
    }

    // ------------------------------------------------------- Encounter

    private static void SetupEncounter(HouseDoor door, FirstPersonController_TEST player, NPCHealth npc)
    {
        NPCConversationTest conversation = Object.FindAnyObjectByType<NPCConversationTest>();

        if (conversation != null)
        {
            SerializedObject conversationSO = new SerializedObject(conversation);
            conversationSO.FindProperty("startEncounterOnPlay").boolValue = false;
            conversationSO.ApplyModifiedProperties();
        }

        DoorEncounter encounter = Object.FindAnyObjectByType<DoorEncounter>();
        if (encounter == null)
        {
            GameObject go = new GameObject("scene manager");
            Undo.RegisterCreatedObjectUndo(go, "Create DoorEncounter");
            encounter = Undo.AddComponent<DoorEncounter>(go);
        }

        SerializedObject encounterSO = new SerializedObject(encounter);
        encounterSO.FindProperty("door").objectReferenceValue = door;
        encounterSO.FindProperty("conversation").objectReferenceValue = conversation;
        encounterSO.FindProperty("player").objectReferenceValue = player;
        if (npc != null)
        {
            encounterSO.FindProperty("npcRoot").objectReferenceValue = npc.gameObject;
            encounterSO.FindProperty("npcHealth").objectReferenceValue = npc;
            encounterSO.FindProperty("npcStandPoint").objectReferenceValue = null;
        }
        encounterSO.ApplyModifiedProperties();
    }

    // --------------------------------------------------------- Helpers

    private static Bounds? RendererBounds(GameObject root, string childName)
    {
        if (root == null) return null;
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name != childName) continue;
            Renderer renderer = child.GetComponent<Renderer>();
            if (renderer != null) return renderer.bounds;
        }
        return null;
    }

    private static float GroundHeight(Vector3 position)
    {
        Physics.SyncTransforms();
        if (Physics.Raycast(new Vector3(position.x, 50f, position.z), Vector3.down, out RaycastHit hit, 100f,
                ~(1 << 2), QueryTriggerInteraction.Ignore))
        {
            // Ignore hits on characters; only accept static-ish ground.
            if (hit.collider.GetComponentInParent<CharacterController>() == null &&
                hit.collider.GetComponentInParent<NPCHealth>() == null)
                return hit.point.y;
        }
        return 0f;
    }

    private static Material GetOrCreateMaterial(string path, System.Func<Material> create)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;

        material = create();
        AssetDatabase.CreateAsset(material, path);
        AssetDatabase.SaveAssets();
        return material;
    }
}
