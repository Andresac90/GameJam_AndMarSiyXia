using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Menu: Trick or Threat > Merge Player Into PlayerKid.
/// Makes PlayerKid.prefab the single player: controller, gun, interactor, hands Animator,
/// normal scale. Cleans the hand clips and swaps NPC_Test over to PlayerKid.
/// </summary>
public static class PlayerKidMergeTool
{
    private const string KidPath = "Assets/Prefabs/PlayerKid.prefab";
    private const string OldPlayerPath = "Assets/Prefabs/Player.prefab";
    private const string ControllerPath = "Assets/Animations/PlayerKid.controller";
    private const string NpcScenePath = "Assets/Scenes/NPC_Test.unity";
    private const string AnimTestScenePath = "Assets/Scenes/PlayerAnimTest.unity";

    private static readonly string[] ClipPaths =
    {
        "Assets/Animations/Walk.anim",
        "Assets/Animations/GettingCandy.anim"
    };

    [MenuItem("Trick or Threat/Merge Player Into PlayerKid")]
    public static void Merge()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        Run();
        EditorSceneManager.OpenScene(NpcScenePath);
    }

    /// <summary>Batch mode entry: -executeMethod PlayerKidMergeTool.Run</summary>
    public static void Run()
    {
        CleanClips();
        MergePrefab();
        SwapPlayerInScene(NpcScenePath);
        RemoveSceneAnimators(AnimTestScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("Trick or Threat: PlayerKid is now the player (NPC_Test updated).");
    }

    // The clips were recorded with "everything": they also keyed the player root position
    // and the whole camera, which would freeze movement and mouse look. Keep only the hands.
    private static void CleanClips()
    {
        foreach (string path in ClipPaths)
        {
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null) continue;

            int removed = 0;
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (!IsBodyPath(binding.path)) continue;
                AnimationUtility.SetEditorCurve(clip, binding, null);
                removed++;
            }

            foreach (EditorCurveBinding binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                if (!IsBodyPath(binding.path)) continue;
                AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
                removed++;
            }

            EditorUtility.SetDirty(clip);
            Debug.Log($"Trick or Threat: removed {removed} root/camera curves from {path}");
        }

        AssetDatabase.SaveAssets();
    }

    private static bool IsBodyPath(string path) => path == "" || path == "Main Camera";

    private static void MergePrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(KidPath);

        try
        {
            Camera cam = root.GetComponentInChildren<Camera>(true);
            Transform camTransform = cam.transform;

            // Root * camera scale was ~1 already, so the hands keep their world size.
            root.transform.localScale = Vector3.one;
            camTransform.localScale = Vector3.one;
            camTransform.localRotation = Quaternion.identity;

            CharacterController controller = root.GetComponent<CharacterController>();
            float eye = controller != null ? controller.center.y + controller.height * 0.5f - 0.2f : 0.8f;
            camTransform.localPosition = new Vector3(0f, eye, 0f);

            cam.nearClipPlane = 0.03f;
            cam.gameObject.tag = "MainCamera";

            FirstPersonController_TEST fps = GetOrAdd<FirstPersonController_TEST>(root);
            SerializedObject fpsSO = new SerializedObject(fps);
            fpsSO.FindProperty("cameraTransform").objectReferenceValue = camTransform;
            fpsSO.FindProperty("mouseSensitivity").floatValue = 0.1f;
            fpsSO.ApplyModifiedPropertiesWithoutUndo();

            Animator animator = GetOrAdd<Animator>(root);
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            GetOrAdd<PlayerHandsAnimation>(root);

            Transform rightHand = camTransform.Find("RightHand");
            Transform pistol = rightHand != null ? FindDeep(rightHand, "Pistol") : null;

            // First-person hands should not throw shadows on the world.
            foreach (Renderer renderer in camTransform.GetComponentsInChildren<Renderer>(true))
                renderer.shadowCastingMode = ShadowCastingMode.Off;

            AudioSource gunAudio = GetOrAdd<AudioSource>(cam.gameObject);
            gunAudio.playOnAwake = false;
            gunAudio.spatialBlend = 0f;

            PlayerInteractor interactor = GetOrAdd<PlayerInteractor>(cam.gameObject);
            interactor.player = fps;

            PlayerGun gun = GetOrAdd<PlayerGun>(cam.gameObject);
            SerializedObject gunSO = new SerializedObject(gun);
            gunSO.FindProperty("aimCamera").objectReferenceValue = cam;
            gunSO.FindProperty("player").objectReferenceValue = fps;
            gunSO.FindProperty("viewModel").objectReferenceValue = rightHand;
            gunSO.FindProperty("muzzle").objectReferenceValue = pistol;
            gunSO.FindProperty("audioSource").objectReferenceValue = gunAudio;
            gunSO.FindProperty("damage").floatValue = 100f;
            gunSO.FindProperty("hitMask").intValue = ~(1 << 2);
            gunSO.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, KidPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void SwapPlayerInScene(string scenePath)
    {
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        GameObject kidPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(KidPath);

        Vector3 position = new Vector3(1.7f, 1.05f, -1.7f);
        Quaternion rotation = Quaternion.identity;

        // Remove every old Player.prefab instance, remembering where it stood.
        foreach (FirstPersonController_TEST fps in Object.FindObjectsByType<FirstPersonController_TEST>(FindObjectsInactive.Include))
        {
            GameObject instanceRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(fps.gameObject);
            if (instanceRoot == null) continue;

            string source = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instanceRoot);
            if (source != OldPlayerPath) continue;

            position = instanceRoot.transform.position;
            rotation = instanceRoot.transform.rotation;
            Object.DestroyImmediate(instanceRoot);
        }

        FirstPersonController_TEST player = Object.FindAnyObjectByType<FirstPersonController_TEST>();
        if (player == null)
        {
            GameObject kid = (GameObject)PrefabUtility.InstantiatePrefab(kidPrefab, scene);
            kid.name = "PlayerKid";
            kid.transform.SetPositionAndRotation(position, rotation);
            player = kid.GetComponent<FirstPersonController_TEST>();
        }

        DoorEncounter encounter = Object.FindAnyObjectByType<DoorEncounter>();
        if (encounter != null)
        {
            SerializedObject encounterSO = new SerializedObject(encounter);
            encounterSO.FindProperty("player").objectReferenceValue = player;
            encounterSO.FindProperty("playerHands").objectReferenceValue = player.GetComponent<PlayerHandsAnimation>();
            encounterSO.ApplyModifiedPropertiesWithoutUndo();
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    // PlayerAnimTest added an Animator on its PlayerKid instance; the prefab has one now.
    private static void RemoveSceneAnimators(string scenePath)
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
            return;

        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        bool changed = false;

        foreach (Animator animator in Object.FindObjectsByType<Animator>(FindObjectsInactive.Include))
        {
            if (!PrefabUtility.IsAddedComponentOverride(animator)) continue;

            GameObject instanceRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(animator.gameObject);
            if (instanceRoot == null || PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instanceRoot) != KidPath)
                continue;

            Object.DestroyImmediate(animator);
            changed = true;
        }

        if (changed)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T component = go.GetComponent<T>();
        return component != null ? component : go.AddComponent<T>();
    }

    private static Transform FindDeep(Transform parent, string name)
    {
        foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
            if (child.name == name)
                return child;
        return null;
    }
}
