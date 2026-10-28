using System;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

public sealed class XBotTalkingImporter : AssetPostprocessor
{
    const string ClipPath = "Assets/New folder/X Bot@Talking.fbx";

    void OnPreprocessModel()
    {
        if (!assetPath.Equals(ClipPath, StringComparison.OrdinalIgnoreCase)) return;
        var importer = (ModelImporter)assetImporter;
        importer.importAnimation = true;
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        var clips = importer.clipAnimations;
        if (clips.Length == 0) clips = importer.defaultClipAnimations;
        foreach (var clip in clips)
        {
            clip.loopTime = true;
            clip.lockRootPositionXZ = true;
            clip.lockRootHeightY = true;
            clip.lockRootRotation = true;
        }
        if (clips.Length > 0) importer.clipAnimations = clips;
    }

    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (imported.Any(path => path.Equals(ClipPath, StringComparison.OrdinalIgnoreCase)))
            EditorApplication.delayCall += XBotTeacherAnimationSetup.Apply;
    }
}

[InitializeOnLoad]
public static class XBotTeacherAnimationSetup
{
    const string ScenePath = "Assets/Scenes/SampleScene.unity";
    const string SourcePath = "Assets/New folder/X Bot@Talking.fbx";
    const string AvatarPath = "Assets/New folder/Teacher_XBot_HumanoidAvatar.asset";
    const string ControllerPath = "Assets/New folder/Teacher_XBot_Talking.controller";
    static bool applying;

    static XBotTeacherAnimationSetup() { EditorApplication.delayCall += Apply; }

    [MenuItem("Tools/Classroom/Add X Bot Talking animation to Teacher")]
    public static void Apply()
    {
        if (applying || EditorApplication.isPlayingOrWillChangePlaymode) return;
        applying = true;
        try
        {
            var importer = AssetImporter.GetAtPath(SourcePath) as ModelImporter;
            if (importer == null) return;
            if (EnsureLoopingImport(importer)) return;
            var assets = AssetDatabase.LoadAllAssetsAtPath(SourcePath);
            var sourceAvatar = assets.OfType<Avatar>().FirstOrDefault(a => a.isValid && a.isHuman);
            var clip = assets.OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase));
            if (sourceAvatar == null || clip == null)
            {
                Debug.LogError("X Bot@Talking.fbx must import as a valid Humanoid Avatar with an animation clip. The Teacher was left unchanged.");
                return;
            }

            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var teacher = Resources.FindObjectsOfTypeAll<GameObject>()
                .FirstOrDefault(go => go.scene.IsValid() && go.scene.name == "SampleScene" && go.name == "Teacher");
            if (teacher == null)
            {
                Debug.LogError("Could not find Teacher in SampleScene. No model assets were changed.");
                return;
            }

            var prefabSource = PrefabUtility.GetCorrespondingObjectFromSource(teacher);
            var prefabPath = AssetDatabase.GetAssetPath(prefabSource);
            if (string.IsNullOrEmpty(prefabPath))
            {
                Debug.LogError("Could not locate the Teacher prefab to build its Humanoid Avatar. The Teacher was left unchanged.");
                return;
            }

            Avatar avatar;
            var prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            try { avatar = BuildTeacherAvatar(prefabRoot); }
            finally { PrefabUtility.UnloadPrefabContents(prefabRoot); }
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                if (avatar != null) UnityEngine.Object.DestroyImmediate(avatar);
                Debug.LogError("Unity could not build a valid Humanoid Avatar from the Teacher's own skeleton. The Teacher was left unchanged to prevent body distortion.");
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<Avatar>(AvatarPath) != null) AssetDatabase.DeleteAsset(AvatarPath);
            avatar.name = "Teacher_XBot_HumanoidAvatar";
            AssetDatabase.CreateAsset(avatar, AvatarPath);

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var layer = controller.layers[0];
            layer.defaultWeight = 1f;
            var state = layer.stateMachine.states
                .Select(child => child.state)
                .FirstOrDefault(existing => existing.name == "Talking");
            if (state == null) state = layer.stateMachine.AddState("Talking", new Vector3(250f, 80f, 0f));
            state.motion = clip;
            layer.stateMachine.defaultState = state;
            var layers = controller.layers;
            layers[0] = layer;
            controller.layers = layers;

            var position = teacher.transform.position;
            var rotation = teacher.transform.rotation;
            var scale = teacher.transform.localScale;
            var animator = teacher.GetComponent<Animator>();
            if (animator == null) animator = Undo.AddComponent<Animator>(teacher);
            animator.avatar = avatar;
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.enabled = true;
            teacher.transform.SetPositionAndRotation(position, rotation);
            teacher.transform.localScale = scale;

            EditorUtility.SetDirty(animator);
            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("Added the X Bot Talking clip through a Humanoid Avatar built from the Teacher's own skeleton. The Teacher model and meshes were preserved, with root motion disabled.");
        }
        catch (Exception exception) { Debug.LogException(exception); }
        finally { applying = false; }
    }

    static bool EnsureLoopingImport(ModelImporter importer)
    {
        var clips = importer.clipAnimations;
        if (clips.Length == 0) clips = importer.defaultClipAnimations;
        bool changed = clips.Any(clip => !clip.loopTime || !clip.lockRootPositionXZ ||
                                         !clip.lockRootHeightY || !clip.lockRootRotation);
        if (!changed) return false;
        foreach (var clip in clips)
        {
            clip.loopTime = true;
            clip.lockRootPositionXZ = true;
            clip.lockRootHeightY = true;
            clip.lockRootRotation = true;
        }
        importer.clipAnimations = clips;
        importer.SaveAndReimport();
        return true;
    }

    static Avatar BuildTeacherAvatar(GameObject root)
    {
        var transforms = root.GetComponentsInChildren<Transform>(true);
        var human = new List<HumanBone>();
        bool Add(string humanName, string boneToken, bool required = false)
        {
            var bone = transforms.FirstOrDefault(t => t.name.StartsWith("ValveBiped.Bip01_" + boneToken + "_", StringComparison.OrdinalIgnoreCase));
            if (bone == null) return !required;
            human.Add(new HumanBone { humanName = humanName, boneName = bone.name, limit = new HumanLimit { useDefaultValues = true } });
            return true;
        }

        if (!Add("Hips", "Pelvis", true) || !Add("Spine", "Spine", true) ||
            !Add("Chest", "Spine1", true) || !Add("Neck", "Neck1", true) ||
            !Add("Head", "Head1", true) || !Add("LeftUpperArm", "L_UpperArm", true) ||
            !Add("LeftLowerArm", "L_Forearm", true) || !Add("LeftHand", "L_Hand", true) ||
            !Add("RightUpperArm", "R_UpperArm", true) || !Add("RightLowerArm", "R_Forearm", true) ||
            !Add("RightHand", "R_Hand", true) || !Add("LeftUpperLeg", "L_Thigh", true) ||
            !Add("LeftLowerLeg", "L_Calf", true) || !Add("LeftFoot", "L_Foot", true) ||
            !Add("RightUpperLeg", "R_Thigh", true) || !Add("RightLowerLeg", "R_Calf", true) ||
            !Add("RightFoot", "R_Foot", true)) return null;

        Add("UpperChest", "Spine2");
        Add("LeftShoulder", "L_Clavicle"); Add("RightShoulder", "R_Clavicle");
        Add("LeftToes", "L_Toe0"); Add("RightToes", "R_Toe0");

        string[] fingers = { "Thumb", "Index", "Middle", "Ring", "Little" };
        string[] suffixes = { "Proximal", "Intermediate", "Distal" };
        for (int side = 0; side < 2; side++)
        {
            string prefix = side == 0 ? "Left" : "Right";
            string tokenSide = side == 0 ? "L" : "R";
            for (int finger = 0; finger < fingers.Length; finger++)
                for (int segment = 0; segment < suffixes.Length; segment++)
                {
                    string token = "Finger" + finger + (segment == 0 ? "" : segment.ToString());
                    Add(prefix + fingers[finger] + suffixes[segment], tokenSide + "_" + token);
                }
        }

        var skeleton = transforms.Select(t => new SkeletonBone
        {
            name = t.name,
            position = t.localPosition,
            rotation = t.localRotation,
            scale = t.localScale
        }).ToArray();
        var description = new HumanDescription
        {
            human = human.ToArray(), skeleton = skeleton,
            armStretch = 0.05f, legStretch = 0.05f,
            upperArmTwist = 0.5f, lowerArmTwist = 0.5f,
            upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
            feetSpacing = 0f, hasTranslationDoF = false
        };
        return AvatarBuilder.BuildHumanAvatar(root, description);
    }
}
