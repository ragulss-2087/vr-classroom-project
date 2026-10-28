using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Converts the bundled GLB files to Unity mesh/material assets and places their prefabs in SampleScene.
[InitializeOnLoad]
public static class GlbClassroomSetup
{
    const string Folder = "Assets/New folder";
    const string ScenePath = "Assets/Scenes/SampleScene.unity";
    static readonly string[] Files = { "high_school_student_v13.glb", "STUDENT_2 (1).glb", "high_school_student_v10.glb", "STUDENT_2.glb", "t-pose_student.glb", "male_character_in_caual_clothing.glb", "my_name_is_gustravo_and_you_called_me_sus.glb" };
    static string DoneKey { get { return "GlbClassroomSetup_v7_" + Application.dataPath; } }
    static string WalkingDoneKey { get { return "GlbWalkingSetup_v8_" + Application.dataPath; } }

    // Keep student placement explicit; avoid resetting scene placements on every editor domain reload.
    static GlbClassroomSetup()
    {
        EditorApplication.delayCall += AutoImportWalkingOnce;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }
    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
            EditorApplication.delayCall += AutoImportWalkingOnce;
    }
    static void AutoImportWalkingOnce()
    {
        if (EditorPrefs.GetBool(WalkingDoneKey, false)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        try { ImportWalkingModel(); EditorPrefs.SetBool(WalkingDoneKey, true); }
        catch (Exception e) { Debug.LogException(e); }
    }
    static void AutoRun()
    {
        if (EditorPrefs.GetBool(DoneKey, false) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        try { Run(); EditorPrefs.SetBool(DoneKey, true); }
        catch (Exception e) { Debug.LogException(e); }
    }

    [MenuItem("Tools/Classroom/Import student GLBs and place in scene")]
    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var prefabs = new List<GameObject>();
        foreach (string file in Files) prefabs.Add(BuildPrefab(Path.Combine(Folder, file)));
        var old = GameObject.Find("Added Students"); if (old != null) UnityEngine.Object.DestroyImmediate(old);
        var group = new GameObject("Added Students");
        Vector3[] positions = { new Vector3(-1.35f,0,2), new Vector3(-.45f,0,2), new Vector3(.45f,0,2), new Vector3(1.35f,0,2), new Vector3(2.25f,0,2), new Vector3(3.15f,0,2), new Vector3(4.05f,0,2) };
        for (int i=0;i<prefabs.Count;i++)
        {
            var go=(GameObject)PrefabUtility.InstantiatePrefab(prefabs[i],scene);
            go.name=Path.GetFileNameWithoutExtension(Files[i]); go.transform.SetParent(group.transform,true);
            go.transform.position=positions[i]; go.transform.rotation=Quaternion.Euler(0,180,0);
        }
        EditorSceneManager.MarkSceneDirty(scene); AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(scene);
        Debug.Log("Imported and placed all seven student GLBs in SampleScene.");
    }

    [MenuItem("Tools/Classroom/Import walking.glb and place in scene")]
    public static void ImportWalkingModel()
    {
        const string path = "Assets/New folder/WalkingReplacement/source/Walking.fbx";
        if (!File.Exists(path)) throw new FileNotFoundException("Walking model is missing", path);

        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null) throw new InvalidOperationException("Unity could not import the replacement FBX.");
        bool settingsChanged = false;
        if (!importer.importAnimation) { importer.importAnimation = true; settingsChanged = true; }
        if (importer.animationType != ModelImporterAnimationType.Generic) { importer.animationType = ModelImporterAnimationType.Generic; settingsChanged = true; }
        var importedClips = importer.defaultClipAnimations;
        if (importedClips != null && importedClips.Length > 0)
        {
            for (int i = 0; i < importedClips.Length; i++)
            {
                importedClips[i].loopTime = true;
                importedClips[i].loopPose = true;
            }
            importer.clipAnimations = importedClips;
            settingsChanged = true;
        }
        if (settingsChanged) importer.SaveAndReimport();

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) throw new InvalidOperationException("The replacement FBX did not produce a model prefab.");
        AnimationClip walkClip = null;
        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            var candidate = asset as AnimationClip;
            if (candidate != null && candidate.name != "__preview__") { walkClip = candidate; break; }
        }
        if (walkClip == null) throw new InvalidOperationException("The replacement FBX has no imported walking clip.");
        string repeatedClipPath = "Assets/New folder/WalkingReplacement/Walking_FourCycles.anim";
        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(repeatedClipPath) != null)
            AssetDatabase.DeleteAsset(repeatedClipPath);
        var repeatedWalkClip = CreateRepeatedWalkClip(walkClip, repeatedClipPath, 4);
        string controllerPath = "Assets/New folder/WalkingReplacement/Walking.controller";
        if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(controllerPath) != null) AssetDatabase.DeleteAsset(controllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        controller.AddMotion(repeatedWalkClip);
        var group = GameObject.Find("Added Students");
        if (group == null) group = new GameObject("Added Students");
        Vector3 savedLocalPosition = new Vector3(4.95f, 0f, 2f);
        Quaternion savedLocalRotation = Quaternion.Euler(0f, 180f, 0f);
        foreach (Transform child in group.transform)
            if (child.name == "walking")
            {
                savedLocalPosition = child.localPosition;
                savedLocalRotation = child.localRotation;
                UnityEngine.Object.DestroyImmediate(child.gameObject);
                break;
            }

        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        go.name = "walking";
        go.transform.SetParent(group.transform, true);
        go.transform.localPosition = savedLocalPosition;
        go.transform.localRotation = savedLocalRotation;
        Bounds modelBounds = GetRendererBounds(go.transform);
        if (modelBounds.size.y > 0.001f) go.transform.localScale = Vector3.one * (1.65f / modelBounds.size.y);
        modelBounds = GetRendererBounds(go.transform);
        var animator = go.GetComponent<Animator>();
        if (animator == null) animator = go.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        SetSafeSkinnedBounds(go.transform, modelBounds);

        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Replaced walking with the rigged Walking.fbx and its looping walk animation.");
    }

    static AnimationClip CreateRepeatedWalkClip(AnimationClip source, string assetPath, int repetitions)
    {
        var clip = new AnimationClip { name = "Walking_FourCycles", frameRate = source.frameRate,
            wrapMode = WrapMode.Loop };
        float cycleDuration = source.length;
        foreach (var binding in AnimationUtility.GetCurveBindings(source))
        {
            var sourceCurve = AnimationUtility.GetEditorCurve(source, binding);
            var keys = new List<Keyframe>();
            for (int cycle = 0; cycle < repetitions; cycle++)
            {
                foreach (var original in sourceCurve.keys)
                {
                    // Keep each shared boundary once so the curve has no duplicate key times.
                    if (cycle > 0 && original.time <= 0f) continue;
                    if (cycle < repetitions - 1 && original.time >= cycleDuration) continue;
                    var key = original;
                    key.time += cycle * cycleDuration;
                    keys.Add(key);
                }
            }
            var repeatedCurve = new AnimationCurve(keys.ToArray())
            { preWrapMode = sourceCurve.preWrapMode, postWrapMode = sourceCurve.postWrapMode };
            AnimationUtility.SetEditorCurve(clip, binding, repeatedCurve);
        }
        foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(source))
        {
            var sourceKeys = AnimationUtility.GetObjectReferenceCurve(source, binding);
            var keys = new List<ObjectReferenceKeyframe>();
            for (int cycle = 0; cycle < repetitions; cycle++)
                foreach (var original in sourceKeys)
                {
                    if (cycle > 0 && original.time <= 0f) continue;
                    if (cycle < repetitions - 1 && original.time >= cycleDuration) continue;
                    keys.Add(new ObjectReferenceKeyframe
                    { time = original.time + cycle * cycleDuration, value = original.value });
                }
            AnimationUtility.SetObjectReferenceCurve(clip, binding, keys.ToArray());
        }
        var settings = AnimationUtility.GetAnimationClipSettings(source);
        settings.startTime = 0f;
        settings.stopTime = cycleDuration * repetitions;
        settings.loopTime = true;
        settings.loopBlend = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        AssetDatabase.CreateAsset(clip, assetPath);
        return clip;
    }

    static Bounds GetRendererBounds(Transform root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return new Bounds(root.position, Vector3.zero);
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    static void SetSafeSkinnedBounds(Transform root, Bounds worldBounds)
    {
        var renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var renderer in renderers)
        {
            var localBounds = new Bounds();
            bool initialized = false;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 corner = worldBounds.center + Vector3.Scale(worldBounds.extents, new Vector3(x, y, z));
                Vector3 localCorner = renderer.transform.InverseTransformPoint(corner);
                if (!initialized) { localBounds = new Bounds(localCorner, Vector3.zero); initialized = true; }
                else localBounds.Encapsulate(localCorner);
            }
            renderer.localBounds = localBounds;
            // Keep the enlarged bounds so the actor stays visible throughout
            // its route, but skip skinning while a mesh is off camera.
            renderer.updateWhenOffscreen = false;
        }
    }

    static GameObject BuildPrefab(string path)
    {
        if(!File.Exists(path)) throw new FileNotFoundException("Student model is missing",path);
        byte[] bytes=File.ReadAllBytes(path); int p=12;
        int jl=BitConverter.ToInt32(bytes,p); p+=8;
        var doc=D.Obj(D.Parse(Encoding.UTF8.GetString(bytes,p,jl).TrimEnd('\0',' '))); p+=jl;
        int bl=BitConverter.ToInt32(bytes,p); p+=8; byte[] bin=new byte[bl]; Buffer.BlockCopy(bytes,p,bin,0,bl);
        string stem=Path.GetFileNameWithoutExtension(path), prefabPath=Folder+"/"+stem+"_Prefab.prefab";
        foreach(string guid in AssetDatabase.FindAssets("",new[]{Folder})) { string a=AssetDatabase.GUIDToAssetPath(guid); if(a.StartsWith(Folder+"/"+stem+"_Generated_",StringComparison.Ordinal))AssetDatabase.DeleteAsset(a); }
        var acc=D.List(doc,"accessors"); var views=D.List(doc,"bufferViews"); var nodes=D.List(doc,"nodes");
        var root=new GameObject(stem); var gos=new GameObject[nodes.Count];
        for(int i=0;i<nodes.Count;i++){var n=D.Obj(nodes[i]);gos[i]=new GameObject(D.Str(n,"name","Node_"+i));gos[i].transform.SetParent(root.transform,false);SetTransform(gos[i].transform,n);}
        for(int i=0;i<nodes.Count;i++)foreach(var child in D.List(D.Obj(nodes[i]),"children"))gos[Convert.ToInt32(child)].transform.SetParent(gos[i].transform,false);
        var mats=BuildMaterials(doc,views,bin,stem); var meshDefs=D.List(doc,"meshes"); var skins=D.List(doc,"skins");
        for(int ni=0;ni<nodes.Count;ni++)
        {
            var n=D.Obj(nodes[ni]); if(!n.ContainsKey("mesh"))continue;
            int mi=Convert.ToInt32(n["mesh"]), pi=0; var md=D.Obj(meshDefs[mi]);
            foreach(var raw in D.List(md,"primitives"))
            {
                var pr=D.Obj(raw); var at=D.Obj(pr["attributes"]); var skin=n.ContainsKey("skin")?D.Obj(skins[Convert.ToInt32(n["skin"])]):null;
                Mesh mesh=ReadMesh(at,pr,acc,views,bin,skin,stem=="walking"); mesh.name=stem+"_Generated_Mesh_"+mi+"_"+pi;
                AssetDatabase.CreateAsset(mesh,Folder+"/"+stem+"_Generated_Mesh_"+mi+"_"+pi+".asset");
                var part=new GameObject("Mesh_"+mi+"_"+pi);part.transform.SetParent(gos[ni].transform,false);
                int matIndex=pr.ContainsKey("material")?Convert.ToInt32(pr["material"]):-1;
                Material mat=matIndex>=0&&matIndex<mats.Length?mats[matIndex]:NewMaterial(stem+"_Generated_FallbackMaterial_"+pi);
                if(matIndex<0||matIndex>=mats.Length)AssetDatabase.CreateAsset(mat,Folder+"/"+stem+"_Generated_FallbackMaterial_"+pi+".mat");
                if(skin!=null)
                {
                    var r=part.AddComponent<SkinnedMeshRenderer>();r.sharedMesh=mesh;r.sharedMaterial=mat;
                    var js=D.List(skin,"joints");r.bones=js.ConvertAll(x=>gos[Convert.ToInt32(x)].transform).ToArray();
                    r.rootBone=skin.ContainsKey("skeleton")?gos[Convert.ToInt32(skin["skeleton"])].transform:r.bones[0];r.localBounds=mesh.bounds;
                }
                else {part.AddComponent<MeshFilter>().sharedMesh=mesh;part.AddComponent<MeshRenderer>().sharedMaterial=mat;}
                pi++;
            }
        }
        if(stem=="walking") AddGlbWalkAnimation(doc,acc,views,bin,root,gos,stem);
        var rs=root.GetComponentsInChildren<Renderer>(true);if(rs.Length>0){Bounds b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);if(b.size.y>.001f){float s=1.65f/b.size.y;root.transform.localScale=Vector3.one*s;foreach(Transform c in root.transform)c.localPosition+=Vector3.up*(-b.min.y);}}
        PrefabUtility.SaveAsPrefabAsset(root,prefabPath);UnityEngine.Object.DestroyImmediate(root);
        return AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
    }

    static void AddGlbWalkAnimation(Dictionary<string,object> doc,List<object> accessors,List<object> views,byte[] bin,GameObject root,GameObject[] nodes,string stem)
    {
        var animations=D.List(doc,"animations");
        if(animations.Count==0)return;
        string clipPath=Folder+"/"+stem+"_Walk.anim";
        string controllerPath=Folder+"/"+stem+"_Walk.controller";
        if(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(clipPath)!=null)AssetDatabase.DeleteAsset(clipPath);
        if(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(controllerPath)!=null)AssetDatabase.DeleteAsset(controllerPath);
        var animation=D.Obj(animations[0]);
        var samplers=D.List(animation,"samplers");
        var clip=new AnimationClip { name=stem+"_Walk", frameRate=30f, wrapMode=WrapMode.Loop };
        var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=true;settings.loopBlend=false;AnimationUtility.SetAnimationClipSettings(clip,settings);
        foreach(object rawChannel in D.List(animation,"channels"))
        {
            var channel=D.Obj(rawChannel);var target=D.Obj(channel["target"]);
            int nodeIndex=Convert.ToInt32(target["node"]);
            string property=D.Str(target,"path","");
            if(nodeIndex<0||nodeIndex>=nodes.Length||(property!="translation"&&property!="rotation"))continue;
            var sampler=D.Obj(samplers[Convert.ToInt32(channel["sampler"])]);
            float[] times=Floats(Convert.ToInt32(sampler["input"]),accessors,views,bin);
            var outputAccessor=D.Obj(accessors[Convert.ToInt32(sampler["output"])]);
            int width=property=="rotation"?4:3;
            float[] values=Floats(Convert.ToInt32(sampler["output"]),accessors,views,bin);
            string path=AnimationUtility.CalculateTransformPath(nodes[nodeIndex].transform,root.transform);
            if(property=="translation")
            {
                var x=new Keyframe[times.Length];var y=new Keyframe[times.Length];var z=new Keyframe[times.Length];
                for(int i=0;i<times.Length;i++){x[i]=new Keyframe(times[i],values[i*width]);y[i]=new Keyframe(times[i],values[i*width+1]);z[i]=new Keyframe(times[i],-values[i*width+2]);}
                SetWalkCurve(clip,path,"m_LocalPosition.x",x);SetWalkCurve(clip,path,"m_LocalPosition.y",y);SetWalkCurve(clip,path,"m_LocalPosition.z",z);
            }
            else
            {
                var x=new Keyframe[times.Length];var y=new Keyframe[times.Length];var z=new Keyframe[times.Length];var w=new Keyframe[times.Length];
                for(int i=0;i<times.Length;i++){x[i]=new Keyframe(times[i],-values[i*width]);y[i]=new Keyframe(times[i],-values[i*width+1]);z[i]=new Keyframe(times[i],values[i*width+2]);w[i]=new Keyframe(times[i],values[i*width+3]);}
                SetWalkCurve(clip,path,"m_LocalRotation.x",x);SetWalkCurve(clip,path,"m_LocalRotation.y",y);SetWalkCurve(clip,path,"m_LocalRotation.z",z);SetWalkCurve(clip,path,"m_LocalRotation.w",w);
            }
        }
        AssetDatabase.CreateAsset(clip,clipPath);
        var controller=AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        controller.AddMotion(clip);
        var animator=root.AddComponent<Animator>();
        animator.runtimeAnimatorController=controller;
        animator.applyRootMotion=false;
        animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        AssetDatabase.SaveAssets();
    }

    static void SetWalkCurve(AnimationClip clip,string path,string property,Keyframe[] keys)
    {
        var binding=EditorCurveBinding.FloatCurve(path,typeof(Transform),property);
        AnimationUtility.SetEditorCurve(clip,binding,new AnimationCurve(keys));
    }

    static bool HasExtremePositionBounds(Dictionary<string,object> attributes,List<object> accessors)
    {
        if(!attributes.ContainsKey("POSITION"))return true;
        var position=D.Obj(accessors[Convert.ToInt32(attributes["POSITION"])]);
        if(!position.ContainsKey("min")||!position.ContainsKey("max"))return false;
        var min=D.Floats(position["min"]);var max=D.Floats(position["max"]);
        for(int i=0;i<min.Length;i++)if(Mathf.Abs(min[i])>50f||Mathf.Abs(max[i])>50f)return true;
        return false;
    }

    static void SetTransform(Transform t,Dictionary<string,object> n)
    {
        if(n.ContainsKey("translation")){var v=D.Floats(n["translation"]);t.localPosition=new Vector3(v[0],v[1],-v[2]);}
        if(n.ContainsKey("rotation")){var v=D.Floats(n["rotation"]);t.localRotation=new Quaternion(-v[0],-v[1],v[2],v[3]);}
        if(n.ContainsKey("scale")){var v=D.Floats(n["scale"]);t.localScale=new Vector3(v[0],v[1],v[2]);}
    }

    static Mesh ReadMesh(Dictionary<string,object> at,Dictionary<string,object> pr,List<object> ac,List<object> vw,byte[] bin,Dictionary<string,object> skin,bool sanitizeExtreme=false)
    {
        var m=new Mesh();m.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;
        var pos=Vec3(Convert.ToInt32(at["POSITION"]),ac,vw,bin);var valid=new bool[pos.Length];for(int i=0;i<pos.Length;i++){valid[i]=!sanitizeExtreme||(Mathf.Abs(pos[i].x)<=5f&&Mathf.Abs(pos[i].y)<=5f&&Mathf.Abs(pos[i].z)<=5f);if(!valid[i])pos[i]=Vector3.zero;pos[i].z=-pos[i].z;}m.vertices=pos;
        if(at.ContainsKey("NORMAL")){var a=Vec3(Convert.ToInt32(at["NORMAL"]),ac,vw,bin);for(int i=0;i<a.Length;i++)a[i].z=-a[i].z;m.normals=a;}
        if(at.ContainsKey("TEXCOORD_0")){var a=Vec3(Convert.ToInt32(at["TEXCOORD_0"]),ac,vw,bin);var uv=new Vector2[a.Length];for(int i=0;i<a.Length;i++)uv[i]=new Vector2(a[i].x,1-a[i].y);m.uv=uv;}
        int[] ix=pr.ContainsKey("indices")?Indices(Convert.ToInt32(pr["indices"]),ac,vw,bin):Seq(pos.Length);
        int mode=pr.ContainsKey("mode")?Convert.ToInt32(pr["mode"]):4;
        if(mode==5){var q=new List<int>();for(int i=2;i<ix.Length;i++){if(i%2==0){q.Add(ix[i-2]);q.Add(ix[i-1]);}else{q.Add(ix[i-1]);q.Add(ix[i-2]);}q.Add(ix[i]);}ix=q.ToArray();}
        else if(mode==6){var q=new List<int>();for(int i=2;i<ix.Length;i++){q.Add(ix[0]);q.Add(ix[i-1]);q.Add(ix[i]);}ix=q.ToArray();}
        for(int i=0;i+2<ix.Length;i+=3){int a=ix[i+1];ix[i+1]=ix[i+2];ix[i+2]=a;}if(sanitizeExtreme){var clean=new List<int>();for(int i=0;i+2<ix.Length;i+=3)if(valid[ix[i]]&&valid[ix[i+1]]&&valid[ix[i+2]]){clean.Add(ix[i]);clean.Add(ix[i+1]);clean.Add(ix[i+2]);}ix=clean.ToArray();}m.triangles=ix;
        if(m.normals.Length==0)m.RecalculateNormals();
        if(skin!=null&&at.ContainsKey("JOINTS_0")&&at.ContainsKey("WEIGHTS_0"))
        {
            var j=Vec4(Convert.ToInt32(at["JOINTS_0"]),ac,vw,bin,true);var w=Vec4(Convert.ToInt32(at["WEIGHTS_0"]),ac,vw,bin,false);var bw=new BoneWeight[pos.Length];
            for(int i=0;i<bw.Length;i++){bw[i].boneIndex0=(int)j[i].x;bw[i].boneIndex1=(int)j[i].y;bw[i].boneIndex2=(int)j[i].z;bw[i].boneIndex3=(int)j[i].w;bw[i].weight0=w[i].x;bw[i].weight1=w[i].y;bw[i].weight2=w[i].z;bw[i].weight3=w[i].w;}m.boneWeights=bw;
            if(skin.ContainsKey("inverseBindMatrices")){var a=Floats(Convert.ToInt32(skin["inverseBindMatrices"]),ac,vw,bin);int count=D.List(skin,"joints").Count;var binds=new Matrix4x4[count];var f=Matrix4x4.identity;f.m22=-1;for(int b=0;b<count;b++){var mm=new Matrix4x4();for(int c=0;c<4;c++)for(int r=0;r<4;r++)mm[r,c]=a[b*16+c*4+r];binds[b]=f*mm*f;}m.bindposes=binds;}
        }
        m.RecalculateBounds();return m;
    }

    static Material[] BuildMaterials(Dictionary<string,object> d,List<object> views,byte[] bin,string stem)
    {
        var defs=D.List(d,"materials");var outm=new Material[defs.Count];
        for(int i=0;i<defs.Count;i++){var md=D.Obj(defs[i]);var m=NewMaterial(stem+"_Material_"+i);
            if(md.ContainsKey("pbrMetallicRoughness")){var p=D.Obj(md["pbrMetallicRoughness"]);if(p.ContainsKey("baseColorFactor")){var c=D.Floats(p["baseColorFactor"]);m.color=new Color(c[0],c[1],c[2],c[3]);}if(p.ContainsKey("metallicFactor")&&m.HasProperty("_Metallic"))m.SetFloat("_Metallic",Convert.ToSingle(p["metallicFactor"]));if(p.ContainsKey("roughnessFactor")){float smooth=1-Convert.ToSingle(p["roughnessFactor"]);if(m.HasProperty("_Smoothness"))m.SetFloat("_Smoothness",smooth);if(m.HasProperty("_Glossiness"))m.SetFloat("_Glossiness",smooth);}if(p.ContainsKey("baseColorTexture")){int ti=Convert.ToInt32(D.Obj(p["baseColorTexture"])["index"]);var tex=Texture(d,views,bin,ti,stem);if(tex!=null)m.mainTexture=tex;}}
            AssetDatabase.CreateAsset(m,Folder+"/"+stem+"_Generated_Material_"+i+".mat");outm[i]=m;}
        return outm;
    }
    static Material NewMaterial(string name){var sh=Shader.Find("Universal Render Pipeline/Lit");if(sh==null)sh=Shader.Find("Standard");var m=new Material(sh);m.name=name;return m;}
    static Texture2D Texture(Dictionary<string,object>d,List<object>views,byte[]bin,int ti,string stem)
    {
        var t=D.Obj(D.List(d,"textures")[ti]);var im=D.Obj(D.List(d,"images")[Convert.ToInt32(t["source"])]);if(!im.ContainsKey("bufferView"))return null;
        var v=D.Obj(views[Convert.ToInt32(im["bufferView"])]);int off=D.Int(v,"byteOffset",0),len=Convert.ToInt32(v["byteLength"]);var data=new byte[len];Buffer.BlockCopy(bin,off,data,0,len);
        var tex=new Texture2D(2,2,TextureFormat.RGBA32,true);if(!ImageConversion.LoadImage(tex,data))return null;tex.name=stem+"_Texture_"+ti;AssetDatabase.CreateAsset(tex,Folder+"/"+stem+"_Generated_Texture_"+ti+".asset");return tex;
    }
    static Vector3[] Vec3(int i,List<object>a,List<object>v,byte[]b){var x=Floats(i,a,v,b);var d=D.Obj(a[i]);int c=D.TypeCount(Convert.ToString(d["type"])),n=Convert.ToInt32(d["count"]);var r=new Vector3[n];for(int q=0;q<n;q++)r[q]=new Vector3(x[q*c],c>1?x[q*c+1]:0,c>2?x[q*c+2]:0);return r;}
    static Vector4[] Vec4(int i,List<object>a,List<object>v,byte[]b,bool integer){var d=D.Obj(a[i]);var view=D.Obj(v[Convert.ToInt32(d["bufferView"])]);int n=Convert.ToInt32(d["count"]),c=D.TypeCount(Convert.ToString(d["type"])),ct=Convert.ToInt32(d["componentType"]),sz=D.Size(ct),st=D.Int(view,"byteOffset",0)+D.Int(d,"byteOffset",0),stride=D.Int(view,"byteStride",c*sz);var r=new Vector4[n];for(int q=0;q<n;q++){float[] z=new float[4];for(int k=0;k<c;k++){int pos=st+q*stride+k*sz;if(integer)z[k]=ct==5121?b[pos]:ct==5123?BitConverter.ToUInt16(b,pos):BitConverter.ToUInt32(b,pos);else z[k]=D.Read(b,pos,ct,D.Bool(d,"normalized",false));}r[q]=new Vector4(z[0],z[1],z[2],z[3]);}return r;}
    static float[] Floats(int i,List<object>a,List<object>v,byte[]b){var d=D.Obj(a[i]);var view=D.Obj(v[Convert.ToInt32(d["bufferView"])]);int n=Convert.ToInt32(d["count"]),c=D.TypeCount(Convert.ToString(d["type"])),ct=Convert.ToInt32(d["componentType"]),sz=D.Size(ct),st=D.Int(view,"byteOffset",0)+D.Int(d,"byteOffset",0),stride=D.Int(view,"byteStride",c*sz);var r=new float[n*c];for(int q=0;q<n;q++)for(int k=0;k<c;k++)r[q*c+k]=D.Read(b,st+q*stride+k*sz,ct,D.Bool(d,"normalized",false));return r;}
    static int[] Indices(int i,List<object>a,List<object>v,byte[]b){var d=D.Obj(a[i]);var view=D.Obj(v[Convert.ToInt32(d["bufferView"])]);int n=Convert.ToInt32(d["count"]),ct=Convert.ToInt32(d["componentType"]),sz=D.Size(ct),st=D.Int(view,"byteOffset",0)+D.Int(d,"byteOffset",0);var r=new int[n];for(int q=0;q<n;q++)r[q]=(int)(ct==5121?b[st+q]:ct==5123?BitConverter.ToUInt16(b,st+q*sz):BitConverter.ToUInt32(b,st+q*sz));return r;}
    static int[] Seq(int n){var r=new int[n];for(int i=0;i<n;i++)r[i]=i;return r;}

    static class D
    {
        public static Dictionary<string,object> Obj(object x){return (Dictionary<string,object>)x;}
        public static List<object> List(Dictionary<string,object>x,string k){return x.ContainsKey(k)?(List<object>)x[k]:new List<object>();}
        public static string Str(Dictionary<string,object>x,string k,string f){return x.ContainsKey(k)?Convert.ToString(x[k]):f;}
        public static int Int(Dictionary<string,object>x,string k,int f){return x.ContainsKey(k)?Convert.ToInt32(x[k]):f;}
        public static bool Bool(Dictionary<string,object>x,string k,bool f){return x.ContainsKey(k)?Convert.ToBoolean(x[k]):f;}
        public static float[] Floats(object x){var a=(List<object>)x;var r=new float[a.Count];for(int i=0;i<r.Length;i++)r[i]=Convert.ToSingle(a[i]);return r;}
        public static int TypeCount(string t){switch(t){case "SCALAR":return 1;case "VEC2":return 2;case "VEC3":return 3;case "VEC4":return 4;case "MAT4":return 16;default:throw new Exception("Unsupported GLB accessor "+t);}}
        public static int Size(int t){return t==5120||t==5121?1:t==5122||t==5123?2:4;}
        public static float Read(byte[]b,int p,int t,bool norm){if(t==5126)return BitConverter.ToSingle(b,p);if(t==5120){sbyte v=(sbyte)b[p];return norm?Mathf.Max(-1,v/127f):v;}if(t==5121)return norm?b[p]/255f:b[p];if(t==5122){short v=BitConverter.ToInt16(b,p);return norm?Mathf.Max(-1,v/32767f):v;}if(t==5123){ushort v=BitConverter.ToUInt16(b,p);return norm?v/65535f:v;}return BitConverter.ToUInt32(b,p);}
        public static object Parse(string s){return new Parser(s).Read();}
        sealed class Parser{readonly string s;int p;public Parser(string x){s=x;}void Ws(){while(p<s.Length&&char.IsWhiteSpace(s[p]))p++;}public object Read(){Ws();char c=s[p];if(c=='{')return Object();if(c=='[')return Array();if(c=='"')return String();if(c=='t'){p+=4;return true;}if(c=='f'){p+=5;return false;}if(c=='n'){p+=4;return null;}return Number();}Dictionary<string,object> Object(){p++;var d=new Dictionary<string,object>();Ws();while(s[p]!='}'){Ws();string k=String();Ws();p++;Ws();d[k]=Read();Ws();if(s[p]==','){p++;continue;}break;}p++;return d;}List<object> Array(){p++;var a=new List<object>();Ws();while(s[p]!=']'){a.Add(Read());Ws();if(s[p]==','){p++;continue;}break;}p++;return a;}string String(){p++;var b=new StringBuilder();while(s[p]!='"'){char c=s[p++];if(c=='\\'){char e=s[p++];if(e=='u'){b.Append((char)Convert.ToInt32(s.Substring(p,4),16));p+=4;}else b.Append(e=='n'?'\n':e=='r'?'\r':e=='t'?'\t':e);}else b.Append(c);}p++;return b.ToString();}object Number(){int a=p;while(p<s.Length&&"-+0123456789.eE".IndexOf(s[p])>=0)p++;return double.Parse(s.Substring(a,p-a),System.Globalization.CultureInfo.InvariantCulture);}}
    }
}





