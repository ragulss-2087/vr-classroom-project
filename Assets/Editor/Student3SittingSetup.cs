using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class Student3SittingSetup
{
    private const string ModelPath = "Assets/New folder/CharacterReplacements/Ch22_nonPBR@Sitting.fbx";
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string ControllerPath = "Assets/New folder/Student3_Sitting.controller";
    private const string AvatarPath = "Assets/New folder/Student3_SittingAvatar.asset";
    private static readonly string DoneKey = "ReplaceStudent3WithSitting_v1_" + Application.dataPath;

    [MenuItem("Tools/Classroom/Replace Student3 Completely with Sitting")]
    public static void ReplaceStudent3()
    {
        AssetDatabase.Refresh();
        AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);

        var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError("Unity could not import the Sitting FBX. Student3 was left unchanged.");
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
            Debug.LogError("The Sitting FBX needs a character model and animation clip. Student3 was left unchanged.");
            return;
        }
        if (avatar == null)
        {
            avatar = AvatarBuilder.BuildGenericAvatar(model, string.Empty);
            if (avatar == null || !avatar.isValid)
            {
                if (avatar != null) UnityEngine.Object.DestroyImmediate(avatar);
                Debug.LogError("Unity could not build an Avatar from the Student3 FBX skeleton. Student3 was left unchanged.");
                return;
            }
            avatar.name = "Student3_SittingAvatar";
            if (AssetDatabase.LoadAssetAtPath<Avatar>(AvatarPath) != null)
                AssetDatabase.DeleteAsset(AvatarPath);
            AssetDatabase.CreateAsset(avatar, AvatarPath);
        }

        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var student = GameObject.Find("Student3");
        if (student == null)
        {
            Debug.LogError("Could not find Student3 in SampleScene. No scene objects were changed.");
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
        var sittingState = stateMachine.AddState("Sitting", new Vector3(250f, 80f, 0f));
        sittingState.motion = animation;
        stateMachine.defaultState = sittingState;

        GameObject replacement;
        try
        {
            replacement = PrefabUtility.InstantiatePrefab(model, parent) as GameObject;
        }
        catch (Exception exception)
        {
            Debug.LogError($"Could not create the Sitting replacement. Student3 was left unchanged. {exception.Message}");
            return;
        }
        if (replacement == null)
        {
            Debug.LogError("Could not create the Sitting replacement. Student3 was left unchanged.");
            return;
        }

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Replace Student3 with Sitting character");
        Undo.RegisterCreatedObjectUndo(replacement, "Create Sitting replacement");
        replacement.name = "Student3";
        replacement.transform.SetParent(parent, false);
        replacement.transform.localPosition = localPosition;
        replacement.transform.localRotation = localRotation;
        replacement.transform.localScale = localScale;

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

        replacement.transform.SetSiblingIndex(siblingIndex);
        replacement.SetActive(wasActive);
        Undo.DestroyObjectImmediate(student);
        Undo.CollapseUndoOperations(undoGroup);

        EditorUtility.SetDirty(animator);
        EditorUtility.SetDirty(controller);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        EditorPrefs.SetBool(DoneKey, true);
        Debug.Log("Replaced Student3 with the complete Sitting FBX character. Student3's position and orientation were preserved.");
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
