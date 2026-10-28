using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class RemyStandToSitSetup
{
    private const string ModelPath = "Assets/New folder/Remy@Stand To Sit.fbx";
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string ControllerPath = "Assets/New folder/Student2_RemyStandToSit.controller";
    private static readonly string DoneKey = "ReplaceStudent2WithRemy_v1_" + Application.dataPath;

    static RemyStandToSitSetup()
    {
        EditorApplication.delayCall += ReplaceStudent2Once;
    }

    private static void ReplaceStudent2Once()
    {
        if (EditorPrefs.GetBool(DoneKey, false) || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        ReplaceStudent2();
    }

    [MenuItem("Tools/Classroom/Replace Student2 Completely with Remy")]
    public static void ReplaceStudent2()
    {
        AssetDatabase.Refresh();
        AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);

        var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError("Unity could not import Remy@Stand To Sit.fbx. Student2 was left unchanged.");
            return;
        }

        var clips = importer.clipAnimations;
        if (clips.Length == 0) clips = importer.defaultClipAnimations;
        bool importSettingsChanged = !importer.importAnimation ||
            importer.animationType != ModelImporterAnimationType.Human ||
            importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = true;
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        foreach (var clip in clips)
        {
            if (clip.loopTime || !clip.lockRootPositionXZ || !clip.lockRootRotation)
                importSettingsChanged = true;
            clip.loopTime = false;
            clip.lockRootPositionXZ = true;
            clip.lockRootRotation = true;
        }
        if (clips.Length > 0) importer.clipAnimations = clips;
        if (importSettingsChanged) importer.SaveAndReimport();

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var importedAssets = AssetDatabase.LoadAllAssetsAtPath(ModelPath);
        var avatar = importedAssets.OfType<Avatar>().FirstOrDefault(item => item.isValid && item.isHuman);
        var animation = importedAssets.OfType<AnimationClip>()
            .FirstOrDefault(clip => !clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase));
        if (model == null || avatar == null || animation == null)
        {
            Debug.LogError("Remy FBX needs a valid Humanoid rig and a sit animation clip. Student2 was left unchanged.");
            return;
        }

        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var student = GameObject.Find("Student2");
        if (student == null)
        {
            Debug.LogError("Could not find Student2 in SampleScene. No scene objects were changed.");
            return;
        }

        var originalTransform = student.transform;
        var parent = originalTransform.parent;
        var localPosition = originalTransform.localPosition;
        var localRotation = originalTransform.localRotation;
        var localScale = originalTransform.localScale;
        var siblingIndex = originalTransform.GetSiblingIndex();
        var wasActive = student.activeSelf;
        var oldBounds = GetRendererBounds(student);

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var stateMachine = controller.layers[0].stateMachine;
        foreach (var existing in stateMachine.states)
            stateMachine.RemoveState(existing.state);
        var sitState = stateMachine.AddState("Remy Stand To Sit", new Vector3(250f, 80f, 0f));
        sitState.motion = animation;
        stateMachine.defaultState = sitState;

        GameObject replacement;
        try
        {
            replacement = PrefabUtility.InstantiatePrefab(model, parent) as GameObject;
        }
        catch (Exception exception)
        {
            Debug.LogError($"Could not create the Remy replacement. Student2 was left unchanged. {exception.Message}");
            return;
        }
        if (replacement == null)
        {
            Debug.LogError("Could not create the Remy replacement. Student2 was left unchanged.");
            return;
        }

        Undo.RegisterCreatedObjectUndo(replacement, "Replace Student2 with Remy");
        replacement.name = "Student2";
        replacement.transform.SetParent(parent, false);
        replacement.transform.localPosition = localPosition;
        replacement.transform.localRotation = localRotation;
        replacement.transform.localScale = localScale;
        replacement.transform.SetSiblingIndex(siblingIndex);

        var animator = replacement.GetComponent<Animator>();
        if (animator == null) animator = Undo.AddComponent<Animator>(replacement);
        animator.avatar = avatar;
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        var newBounds = GetRendererBounds(replacement);
        if (oldBounds.HasValue && newBounds.HasValue && newBounds.Value.size.y > 0.001f)
        {
            float uniformFit = oldBounds.Value.size.y / newBounds.Value.size.y;
            if (!float.IsNaN(uniformFit) && !float.IsInfinity(uniformFit) &&
                uniformFit > 0.01f && uniformFit < 100f)
                replacement.transform.localScale = localScale * uniformFit;
        }

        replacement.SetActive(wasActive);
        Undo.DestroyObjectImmediate(student);

        EditorUtility.SetDirty(animator);
        EditorUtility.SetDirty(controller);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        EditorPrefs.SetBool(DoneKey, true);
        Debug.Log("Replaced Student2 with the complete Remy FBX character and its stand-to-sit animation. Student2's scene position and orientation were preserved.");
    }

    private static Bounds? GetRendererBounds(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return null;
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }
}
