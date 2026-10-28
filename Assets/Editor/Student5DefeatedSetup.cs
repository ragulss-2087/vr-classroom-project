using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class Student5DefeatedSetup
{
    private const string ModelPath = "Assets/New folder/CharacterReplacements/Ch01_nonPBR@Defeated.fbx";
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string ControllerPath = "Assets/New folder/Student5_Defeated.controller";
    private const string AvatarPath = "Assets/New folder/Student5_DefeatedAvatar.asset";
    private static readonly string DoneKey = "ReplaceStudent5WithDefeated_v1_" + Application.dataPath;

    static Student5DefeatedSetup()
    {
        EditorApplication.delayCall += ReplaceStudent5Once;
    }

    private static void ReplaceStudent5Once()
    {
        if (EditorPrefs.GetBool(DoneKey, false) || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        ReplaceStudent5();
    }

    [MenuItem("Tools/Classroom/Replace Student5 Completely with Defeated")]
    public static void ReplaceStudent5()
    {
        AssetDatabase.Refresh();
        AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);

        var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError("Unity could not import Ch01_nonPBR@Defeated.fbx. Student5 was left unchanged.");
            return;
        }

        var clips = importer.clipAnimations;
        if (clips.Length == 0) clips = importer.defaultClipAnimations;
        bool settingsChanged = !importer.importAnimation ||
            importer.animationType != ModelImporterAnimationType.Generic;
        importer.importAnimation = true;
        importer.animationType = ModelImporterAnimationType.Generic;
        foreach (var clip in clips)
        {
            if (clip.loopTime || !clip.lockRootPositionXZ || !clip.lockRootRotation)
                settingsChanged = true;
            clip.loopTime = false;
            clip.lockRootPositionXZ = true;
            clip.lockRootRotation = true;
        }
        if (clips.Length > 0) importer.clipAnimations = clips;
        if (settingsChanged) importer.SaveAndReimport();

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var importedAssets = AssetDatabase.LoadAllAssetsAtPath(ModelPath);
        var avatar = importedAssets.OfType<Avatar>().FirstOrDefault(item => item.isValid);
        if (avatar == null) avatar = AssetDatabase.LoadAssetAtPath<Avatar>(AvatarPath);
        var animation = importedAssets.OfType<AnimationClip>()
            .FirstOrDefault(clip => !clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase));
        if (model == null || animation == null)
        {
            Debug.LogError("The Defeated FBX needs a character model and an animation clip. Student5 was left unchanged.");
            return;
        }
        if (avatar == null)
        {
            avatar = AvatarBuilder.BuildGenericAvatar(model, string.Empty);
            if (avatar == null || !avatar.isValid)
            {
                if (avatar != null) UnityEngine.Object.DestroyImmediate(avatar);
                Debug.LogError("Unity could not build an Avatar from the Student5 FBX skeleton. Student5 was left unchanged.");
                return;
            }
            avatar.name = "Student5_DefeatedAvatar";
            if (AssetDatabase.LoadAssetAtPath<Avatar>(AvatarPath) != null)
                AssetDatabase.DeleteAsset(AvatarPath);
            AssetDatabase.CreateAsset(avatar, AvatarPath);
        }

        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var student = GameObject.Find("Student5");
        if (student == null)
        {
            Debug.LogError("Could not find Student5 in SampleScene. No scene objects were changed.");
            return;
        }
        if (PrefabUtility.GetCorrespondingObjectFromSource(student) == model)
        {
            Debug.Log("Student5 already uses the Defeated FBX character.");
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
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var stateMachine = controller.layers[0].stateMachine;
        foreach (var existing in stateMachine.states)
            stateMachine.RemoveState(existing.state);
        var defeatedState = stateMachine.AddState("Defeated", new Vector3(250f, 80f, 0f));
        defeatedState.motion = animation;
        stateMachine.defaultState = defeatedState;

        GameObject replacement;
        try
        {
            replacement = PrefabUtility.InstantiatePrefab(model, parent) as GameObject;
        }
        catch (Exception exception)
        {
            Debug.LogError($"Could not create the Defeated replacement. Student5 was left unchanged. {exception.Message}");
            return;
        }
        if (replacement == null)
        {
            Debug.LogError("Could not create the Defeated replacement. Student5 was left unchanged.");
            return;
        }

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Replace Student5 with Defeated character");
        Undo.RegisterCreatedObjectUndo(replacement, "Create Defeated replacement");
        replacement.name = "Student5";
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
        Undo.CollapseUndoOperations(undoGroup);

        EditorUtility.SetDirty(animator);
        EditorUtility.SetDirty(controller);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        EditorPrefs.SetBool(DoneKey, true);
        Debug.Log("Replaced Student5 with the complete Defeated FBX character. Student5's position and orientation were preserved.");
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
