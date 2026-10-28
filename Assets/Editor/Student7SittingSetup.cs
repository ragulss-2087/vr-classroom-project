using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class Student7SittingSetup
{
    private const string ModelPath = "Assets/New folder/CharacterReplacements/Ch22_nonPBR@Sitting.fbx";
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string ControllerPath = "Assets/New folder/Student7_Sitting.controller";
    private const string AvatarPath = "Assets/New folder/Student7_SittingAvatar.asset";

    [MenuItem("Tools/Classroom/Replace Student7 Completely with Sitting")]
    public static void ReplaceStudent7()
    {
        try
        {
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("Sitting FBX could not be imported.");
            var clips = importer.clipAnimations;
            if (clips.Length == 0) clips = importer.defaultClipAnimations;
            importer.importAnimation = true;
            importer.animationType = ModelImporterAnimationType.Generic;
            foreach (var clipSettings in clips)
            {
                clipSettings.lockRootPositionXZ = true;
                clipSettings.lockRootRotation = true;
            }
            if (clips.Length > 0) importer.clipAnimations = clips;
            importer.SaveAndReimport();

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var assets = AssetDatabase.LoadAllAssetsAtPath(ModelPath);
            var avatar = assets.OfType<Avatar>().FirstOrDefault(a => a.isValid);
            var clip = assets.OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase));
            if (model == null || clip == null) throw new InvalidOperationException("Sitting FBX model or animation clip could not be loaded.");
            if (avatar == null)
            {
                avatar = AvatarBuilder.BuildGenericAvatar(model, string.Empty);
                if (avatar == null || !avatar.isValid) throw new InvalidOperationException("Could not build a valid avatar from the Sitting FBX skeleton.");
                avatar.name = "Student7_SittingAvatar";
                var previous = AssetDatabase.LoadAssetAtPath<Avatar>(AvatarPath);
                if (previous != null) AssetDatabase.DeleteAsset(AvatarPath);
                AssetDatabase.CreateAsset(avatar, AvatarPath);
            }

            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var student = GameObject.Find("Student7");
            if (student == null) throw new InvalidOperationException("Student7 was not found in SampleScene; no scene objects were changed.");
            var oldTransform = student.transform;
            var parent = oldTransform.parent;
            var localPosition = oldTransform.localPosition;
            var localRotation = oldTransform.localRotation;
            var localScale = oldTransform.localScale;
            var sibling = oldTransform.GetSiblingIndex();
            var wasActive = student.activeSelf;
            var oldBounds = GetBounds(student);

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var machine = controller.layers[0].stateMachine;
            foreach (var state in machine.states) machine.RemoveState(state.state);
            var sitting = machine.AddState("Sitting", new Vector3(250f, 80f, 0f));
            sitting.motion = clip;
            machine.defaultState = sitting;

            var replacement = PrefabUtility.InstantiatePrefab(model, scene) as GameObject;
            if (replacement == null) throw new InvalidOperationException("Unity could not create the Sitting character instance.");
            replacement.name = "Student7";
            replacement.transform.SetParent(parent, false);
            replacement.transform.localPosition = localPosition;
            replacement.transform.localRotation = localRotation;
            replacement.transform.localScale = localScale;
            replacement.transform.SetSiblingIndex(sibling);

            var animator = replacement.GetComponent<Animator>();
            if (animator == null) animator = Undo.AddComponent<Animator>(replacement);
            animator.avatar = avatar;
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var newBounds = GetBounds(replacement);
            if (oldBounds.HasValue && newBounds.HasValue && newBounds.Value.size.y > 0.001f)
            {
                float fit = oldBounds.Value.size.y / newBounds.Value.size.y;
                if (!float.IsNaN(fit) && !float.IsInfinity(fit) && fit > 0.01f && fit < 100f)
                    replacement.transform.localScale = localScale * fit;
            }

            replacement.SetActive(wasActive);
            Undo.DestroyObjectImmediate(student);
            EditorUtility.SetDirty(animator);
            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene);
            Debug.Log("Replaced Student7 completely with Ch22 Sitting; position, orientation, and visual height were preserved.");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    private static Bounds? GetBounds(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return null;
        var bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }
}
