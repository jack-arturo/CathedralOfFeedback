using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using CathedralOfFeedback;

/// <summary>
/// The scene is code. Cathedral > Build Scene regenerates Assets/Scenes/Cathedral.unity and
/// its materials from scratch, so nothing in the scene file is hand-edited and a batch build
/// is fully reproducible: Unity -batchmode -quit -executeMethod CathedralBuilder.BuildAndroid
/// </summary>
public static class CathedralBuilder
{
    public const string ScenePath = "Assets/Scenes/Cathedral.unity";
    const string MatDir = "Assets/Materials";
    const string CorePkg = "Packages/com.meta.xr.sdk.core/Prefabs/";

    static Material MakeMat(string name, string shader, System.Action<Material> cfg = null)
    {
        Directory.CreateDirectory(MatDir);
        var sh = Shader.Find(shader);
        if (sh == null) throw new System.Exception("Shader not found: " + shader);
        var m = new Material(sh);
        cfg?.Invoke(m);
        AssetDatabase.CreateAsset(m, $"{MatDir}/{name}.mat");
        return m;
    }

    static void Set(Object target, string prop, Object value)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(prop);
        if (p == null) throw new System.Exception($"{target.GetType().Name} has no serialized property '{prop}'");
        p.objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetBool(Object target, string prop, bool value)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(prop);
        if (p == null) throw new System.Exception($"{target.GetType().Name} has no serialized property '{prop}'");
        p.boolValue = value; so.ApplyModifiedPropertiesWithoutUndo();
    }

    static int GetInt(Object target, string prop) => new SerializedObject(target).FindProperty(prop).intValue;

    static void SetInt(Object target, string prop, int value)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(prop);
        if (p == null) throw new System.Exception($"{target.GetType().Name} has no serialized property '{prop}'");
        p.intValue = value; so.ApplyModifiedPropertiesWithoutUndo();
    }

    [MenuItem("Cathedral/Configure Project")]
    public static void ConfigureProject()
    {
        PlayerSettings.companyName = "AutoJack";
        PlayerSettings.productName = "Cathedral of Feedback";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.autojack.cathedraloffeedback");
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
        // Lightweight defaults for standalone Quest: everything is unlit, nothing casts shadows.
        QualitySettings.vSyncCount = 0;
        QualitySettings.antiAliasing = 4;
        QualitySettings.shadows = ShadowQuality.Disable;
        QualitySettings.pixelLightCount = 0;
        AssetDatabase.SaveAssets();
    }

    static GameObject Part(Transform parent, string name, Material mat)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        Strip(go);
        return go;
    }

    [MenuItem("Cathedral/Build Scene")]
    public static void BuildScene()
    {
        ConfigureProject();
        Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var glassMat = MakeMat("Glass", "Cathedral/Glass");
        var stoneMat = MakeMat("Stone", "Cathedral/Stone");
        var floorMat = MakeMat("Floor", "Cathedral/Floor");
        var stepMat = MakeMat("FeedbackStep", "Hidden/Cathedral/FeedbackStep");
        var coreMat = MakeMat("OrbCore", "Cathedral/Orb", m => { m.SetFloat("_Power", 1.6f); m.SetFloat("_Rim", 0f); m.SetFloat("_Hot", 0.7f); });
        var haloMat = MakeMat("OrbHalo", "Cathedral/Orb", m => { m.SetFloat("_Power", 3.5f); m.SetFloat("_Rim", 0f); m.SetFloat("_Hot", 0f); });
        var ghostMat = MakeMat("Ghost", "Cathedral/Orb", m => { m.SetFloat("_Power", 1.8f); m.SetFloat("_Rim", 1f); m.SetFloat("_Hot", 0f); });
        var handMat = MakeMat("Hand", "Cathedral/Orb", m =>
        {
            m.SetFloat("_Power", 2.2f); m.SetFloat("_Rim", 1f); m.SetFloat("_Hot", 0f); m.SetFloat("_Prepass", 1f);
            m.SetColor("_Color", new Color(0.45f, 0.8f, 1f, 0.9f));
        });
        var trailMat = MakeMat("Trail", "Cathedral/Glow", m => m.SetColor("_Color", new Color(1f, 1f, 1f, 0.9f)));

        // Audio: one synth, one clock, one analyser.
        var audio = new GameObject("Audio");
        var src = audio.AddComponent<AudioSource>();
        src.playOnAwake = false; src.spatialBlend = 0f;
        var host = audio.AddComponent<CathedralAudio>();
        var analyzer = audio.AddComponent<SpectrumAnalyzer>();
        Set(analyzer, "audioHost", host);

        // The building. Meshes are generated at runtime by CathedralArchitect.
        var root = new GameObject("Cathedral");
        var architect = root.AddComponent<CathedralArchitect>();
        architect.shell = Part(root.transform, "Shell", glassMat).GetComponent<MeshFilter>();
        architect.stone = Part(root.transform, "Stone", stoneMat).GetComponent<MeshFilter>();
        architect.floor = Part(root.transform, "Floor", floorMat).GetComponent<MeshFilter>();
        architect.shafts = Part(root.transform, "Shafts", MakeMat("Shaft", "Cathedral/Shaft")).GetComponent<MeshFilter>();
        var slab = new GameObject("FloorCollider", typeof(BoxCollider));
        slab.transform.SetParent(root.transform, false);
        slab.transform.position = new Vector3(0f, -0.25f, 0f);
        slab.GetComponent<BoxCollider>().size = new Vector3(24f, 0.5f, 24f);
        var field = root.AddComponent<FeedbackField>();
        Set(field, "stepMaterial", stepMat);
        root.AddComponent<RoomCamera>();
        var director = root.AddComponent<CathedralDirector>();
        director.shellRenderer = architect.shell.GetComponent<Renderer>();

        // Two orbs: a lead and a deeper voice, one per hand.
        MakeOrb("OrbLead", 0, 220f, 0.09f, new Vector3(0.22f, 1.15f, 0.42f), coreMat, haloMat, trailMat, ghostMat);
        MakeOrb("OrbDeep", 1, 110f, 0.55f, new Vector3(-0.22f, 1.15f, 0.42f), coreMat, haloMat, trailMat, ghostMat);
        MakeMotes(root.transform, MakeMat("Mote", "Cathedral/Mote"));

        // Planted notes (the room as a step sequencer) and the columns as drums.
        var garden = root.AddComponent<SeedGarden>();
        Set(garden, "seedMaterial", coreMat);
        var columns = new GameObject("ColumnDrums");
        columns.transform.SetParent(root.transform, false);
        for (int k = 0; k < 8; k++)
        {
            float phi = 2f * Mathf.PI * (k + 0.5f) / 8f;
            var cgo = new GameObject("Column" + k, typeof(CapsuleCollider), typeof(ColumnDrum));
            cgo.transform.SetParent(columns.transform, false);
            cgo.transform.position = new Vector3(Mathf.Cos(phi) * Nave.CanopyRadius, 3.2f, Mathf.Sin(phi) * Nave.CanopyRadius);
            var cap = cgo.GetComponent<CapsuleCollider>();
            cap.radius = 0.3f; cap.height = 6.4f; cap.direction = 1;
            cgo.GetComponent<ColumnDrum>().index = k;
        }

        // The string between two held orbs.
        var stringGo = new GameObject("OrbString", typeof(LineRenderer));
        stringGo.transform.SetParent(root.transform, false);
        var lr = stringGo.GetComponent<LineRenderer>();
        lr.sharedMaterial = trailMat; lr.useWorldSpace = true; lr.widthMultiplier = 0.01f; lr.numCapVertices = 2;
        lr.shadowCastingMode = ShadowCastingMode.Off; lr.receiveShadows = false; lr.alignment = LineAlignment.View;
        stringGo.AddComponent<OrbString>();

        // Mode prism: poke it to step through the room modes.
        var prism = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(prism.GetComponent<Collider>());
        prism.name = "ModePrism"; prism.transform.position = new Vector3(-0.44f, 1.05f, 0.34f);
        prism.transform.rotation = Quaternion.Euler(35f, 45f, 0f); prism.transform.localScale = Vector3.one * 0.05f;
        prism.GetComponent<Renderer>().sharedMaterial = ghostMat; Strip(prism);
        var mp = prism.AddComponent<ModePrism>();
        mp.director = director; mp.body = prism.GetComponent<Renderer>();

        // Rig
        var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CorePkg + "OVRCameraRig.prefab");
        GameObject rig;
        if (rigPrefab != null)
        {
            rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab);
            // Floor-level origin: the scene's y = 0 is the real floor, so the building has its real height.
            var mgr = rig.GetComponent<OVRManager>();
            if (mgr != null)
            {
                SetInt(mgr, "_trackingOriginType", (int)OVRManager.TrackingOrigin.FloorLevel);
                SetBool(mgr, "isInsightPassthroughEnabled", true);
            }
            // Passthrough sits under the eye buffer and stays hidden until the player asks for it.
            var layer = rig.AddComponent<OVRPassthroughLayer>();
            layer.overlayType = OVROverlay.OverlayType.Underlay; layer.hidden = true;
            director.passthroughLayer = layer;
            AttachHand(rig.transform, "RightHandAnchor", OVRInput.Controller.RTouch, OVRHand.Hand.HandRight, handMat, 0.09f);
            AttachHand(rig.transform, "LeftHandAnchor", OVRInput.Controller.LTouch, OVRHand.Hand.HandLeft, handMat, 0.55f);
            field.viewer = FindDeep(rig.transform, "CenterEyeAnchor");
            director.head = field.viewer;
        }
        else
        {
            Debug.LogWarning("OVRCameraRig prefab not found; using plain camera");
            rig = new GameObject("Rig"); var cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cam.tag = "MainCamera"; cam.transform.SetParent(rig.transform); cam.transform.localPosition = Vector3.up * 1.6f;
            field.viewer = cam.transform;
        }
        rig.name = "OVRCameraRig";
        foreach (var c in rig.GetComponentsInChildren<Camera>())
        {
            // Alpha 0 clear + no HDR: required for passthrough compositing and keeps MSAA on the eye buffer.
            c.clearFlags = CameraClearFlags.SolidColor; c.backgroundColor = new Color(0f, 0f, 0f, 0f);
            c.nearClipPlane = 0.05f; c.farClipPlane = 60f; c.allowHDR = false; c.allowMSAA = true;
        }

        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black;
        RenderSettings.fog = false;
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        Debug.Log("Scene built: " + ScenePath);
    }

    static void MakeOrb(string name, int index, float rootHz, float hue, Vector3 pos,
        Material coreMat, Material haloMat, Material trailMat, Material ghostMat)
    {
        var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        orb.name = name; orb.transform.position = pos; orb.transform.localScale = Vector3.one * 0.1f;
        orb.GetComponent<Renderer>().sharedMaterial = coreMat; Strip(orb);
        var bounce = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(MatDir + "/OrbBounce.physicMaterial");
        if (bounce == null)
        {
            bounce = new PhysicsMaterial("OrbBounce") { bounciness = 0.85f, bounceCombine = PhysicsMaterialCombine.Maximum, dynamicFriction = 0f, staticFriction = 0f };
            AssetDatabase.CreateAsset(bounce, MatDir + "/OrbBounce.physicMaterial");
        }
        orb.GetComponent<Collider>().sharedMaterial = bounce;
        var rb = orb.AddComponent<Rigidbody>();
        rb.useGravity = false; rb.linearDamping = 0.35f; rb.isKinematic = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        var halo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.DestroyImmediate(halo.GetComponent<Collider>());
        halo.name = "Halo"; halo.transform.SetParent(orb.transform, false); halo.transform.localScale = Vector3.one * 3.2f;
        halo.GetComponent<Renderer>().sharedMaterial = haloMat; Strip(halo);

        var trailGo = new GameObject("Trail"); trailGo.transform.SetParent(orb.transform, false);
        var tr = trailGo.AddComponent<TrailRenderer>();
        tr.time = 1.4f; tr.widthMultiplier = 0.05f; tr.sharedMaterial = trailMat; tr.minVertexDistance = 0.01f;
        tr.widthCurve = new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 0));
        tr.shadowCastingMode = ShadowCastingMode.Off; tr.receiveShadows = false; tr.emitting = false;

        var g = orb.AddComponent<GrabbableOrb>();
        g.orbIndex = index; g.rootHz = rootHz; g.baseHue = hue; g.sideOffset = pos.x;
        g.core = orb.GetComponent<Renderer>(); g.halo = halo.GetComponent<Renderer>(); g.Trail = tr;
        var echo = orb.AddComponent<TrailEcho>();
        Set(echo, "ghostMaterial", ghostMat);
    }

    /// <summary>A few hundred drifting dust motes around the player, lit by the glass behind them.</summary>
    static void MakeMotes(Transform parent, Material mat)
    {
        var go = new GameObject("Motes");
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(0f, 2.6f, 0f);
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true; main.prewarm = true; main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(10f, 18f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.01f, 0.05f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.034f);
        main.startColor = Color.white;
        main.maxParticles = 420;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0f;
        var emission = ps.emission; emission.rateOverTime = 30f;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(9f, 4.8f, 9f);
        var noise = ps.noise; noise.enabled = true; noise.strength = 0.06f; noise.frequency = 0.25f; noise.scrollSpeed = 0.05f;
        var col = ps.colorOverLifetime; col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat; r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    static void AttachHand(Transform rig, string anchor, OVRInput.Controller c, OVRHand.Hand side, Material handMat, float hue)
    {
        var t = FindDeep(rig, anchor);
        if (t == null) { Debug.LogWarning("Missing anchor " + anchor); return; }
        var g = t.gameObject.AddComponent<OrbGrabber>();
        g.controller = c; g.paintHue = hue;

        // Tracked hand: Meta's runtime skeleton + mesh, drawn with our glow shader.
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CorePkg + "OVRHandPrefab.prefab");
        if (prefab == null) { Debug.LogWarning("OVRHandPrefab not found; hand tracking disabled"); return; }
        var hgo = (GameObject)PrefabUtility.InstantiatePrefab(prefab, t);
        bool left = side == OVRHand.Hand.HandLeft;
        var hand = hgo.GetComponent<OVRHand>();
        SetInt(hand, "HandType", (int)side);
        // OVRHand.OnValidate is what propagates the hand side and skeleton version to the
        // skeleton and mesh. A skeleton whose type disagrees with its hand never initialises,
        // so check the result and fail the build rather than ship a dead hand.
        hand.OnValidate();
        var skel = hgo.GetComponent<OVRSkeleton>();
        var mesh = hgo.GetComponent<OVRMesh>();
        var wantSkel = left ? OVRSkeleton.SkeletonType.XRHandLeft : OVRSkeleton.SkeletonType.XRHandRight;
        var wantMesh = left ? OVRMesh.MeshType.XRHandLeft : OVRMesh.MeshType.XRHandRight;
        if (skel.GetSkeletonType() != wantSkel) SetInt(skel, "_skeletonType", (int)wantSkel);
        if (GetInt(mesh, "_meshType") != (int)wantMesh) SetInt(mesh, "_meshType", (int)wantMesh);
        if (skel.GetSkeletonType() != wantSkel || GetInt(mesh, "_meshType") != (int)wantMesh)
            throw new System.Exception($"{side}: skeleton {skel.GetSkeletonType()} / mesh {GetInt(mesh, "_meshType")}, expected {wantSkel} / {wantMesh}");
        Debug.Log($"Hand {side}: skeleton {skel.GetSkeletonType()}, mesh type {GetInt(mesh, "_meshType")}");
        // Unpack so these values are written into the scene itself. As prefab overrides they
        // are only saved when they differ from the in-memory prefab, which OnValidate has
        // already touched, so the left hand's type silently went missing from the scene file.
        PrefabUtility.UnpackPrefabInstance(hgo, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        var smr = hgo.GetComponent<SkinnedMeshRenderer>();
        if (smr != null) { smr.sharedMaterial = handMat; smr.shadowCastingMode = ShadowCastingMode.Off; smr.receiveShadows = false; }
        g.hand = hand; g.skeleton = skel;
    }

    static Transform FindDeep(Transform t, string n)
    {
        if (t.name == n) return t;
        foreach (Transform c in t) { var r = FindDeep(c, n); if (r) return r; }
        return null;
    }

    static void Strip(GameObject g)
    {
        var r = g.GetComponent<Renderer>();
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    public static void BuildAndroid()
    {
        BuildScene();
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        Directory.CreateDirectory("Builds");
        var rep = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath }, locationPathName = "Builds/CathedralOfFeedback.apk",
            target = BuildTarget.Android, options = BuildOptions.None
        });
        Debug.Log("Build result: " + rep.summary.result + " errors=" + rep.summary.totalErrors);
        EditorApplication.Exit(rep.summary.result == BuildResult.Succeeded ? 0 : 1);
    }
}
