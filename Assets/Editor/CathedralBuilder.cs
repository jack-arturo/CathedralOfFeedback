using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using CathedralOfFeedback;

public static class CathedralBuilder
{
    const string ScenePath = "Assets/Scenes/Cathedral.unity";
    const string MatDir = "Assets/Materials";

    static Material MakeMat(string name, string shader, System.Action<Material> cfg = null)
    {
        Directory.CreateDirectory(MatDir);
        var path = $"{MatDir}/{name}.mat";
        var m = new Material(Shader.Find(shader));
        cfg?.Invoke(m);
        AssetDatabase.CreateAsset(m, path);
        return m;
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
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
        // Lightweight defaults for standalone Quest.
        QualitySettings.vSyncCount = 0;
        QualitySettings.antiAliasing = 4;
        QualitySettings.shadows = ShadowQuality.Disable;
        QualitySettings.pixelLightCount = 0;
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Cathedral/Build Scene")]
    public static void BuildScene()
    {
        ConfigureProject();
        Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var wallMat = MakeMat("FractalWall", "Cathedral/FractalWall");
        var latticeMat = MakeMat("Lattice", "Cathedral/Lattice");
        var floorMat = MakeMat("Floor", "Cathedral/FractalWall", m => { m.SetFloat("_TileScale", 8); m.SetColor("_ColorB", new Color(0.2f, 0.8f, 1f)); m.SetFloat("_Pulse", 0.5f); });
        var glow = MakeMat("OrbGlow", "Cathedral/Glow", m => m.SetColor("_Color", new Color(1f, 0.55f, 0.15f, 1.2f)));
        var ghostGlow = MakeMat("EchoGlow", "Cathedral/Glow", m => m.SetColor("_Color", new Color(0.4f, 0.8f, 1f, 0.8f)));

        // Audio + analyzer
        var audio = new GameObject("Audio");
        var src = audio.AddComponent<AudioSource>();
        src.playOnAwake = false; src.spatialBlend = 0f;
        audio.AddComponent<ProceduralBeat>();
        audio.AddComponent<SpectrumAnalyzer>();

        // Cathedral: octagonal ring of wall tiles, floor, spinning lattice ceiling.
        var root = new GameObject("Cathedral").transform;
        const int sides = 8; const float radius = 5f, height = 6f;
        float width = 2f * radius * Mathf.Tan(Mathf.PI / sides);
        for (int i = 0; i < sides; i++)
        {
            float a = i * 360f / sides;
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(q.GetComponent<Collider>());
            q.name = "WallTile" + i;
            q.transform.SetParent(root);
            q.transform.position = Quaternion.Euler(0, a, 0) * new Vector3(0, height / 2f, radius);
            q.transform.rotation = Quaternion.Euler(0, a + 180f, 0); // face inward
            q.transform.localScale = new Vector3(width, height, 1);
            q.GetComponent<Renderer>().sharedMaterial = wallMat;
            Strip(q);
        }
        var floor = GameObject.CreatePrimitive(PrimitiveType.Quad);
        floor.name = "Floor"; floor.transform.SetParent(root);
        floor.transform.rotation = Quaternion.Euler(90, 0, 0);
        floor.transform.localScale = new Vector3(radius * 2.2f, radius * 2.2f, 1);
        floor.GetComponent<Renderer>().sharedMaterial = floorMat; Strip(floor);
        // keep floor collider so dropped orbs bounce
        var ceil = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.DestroyImmediate(ceil.GetComponent<Collider>());
        ceil.name = "CeilingLattice"; ceil.transform.SetParent(root);
        ceil.transform.position = new Vector3(0, height, 0);
        ceil.transform.rotation = Quaternion.Euler(-90, 0, 0); // face down
        ceil.transform.localScale = new Vector3(radius * 2.2f, radius * 2.2f, 1);
        ceil.GetComponent<Renderer>().sharedMaterial = latticeMat; Strip(ceil);

        // Orb
        var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        orb.name = "Orb"; orb.transform.position = new Vector3(0.3f, 1.2f, 0.6f);
        orb.transform.localScale = Vector3.one * 0.12f;
        orb.GetComponent<Renderer>().sharedMaterial = glow; Strip(orb);
        var rb = orb.AddComponent<Rigidbody>(); rb.useGravity = false; rb.linearDamping = 0.8f;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        var trailGo = new GameObject("Trail"); trailGo.transform.SetParent(orb.transform, false);
        var tr = trailGo.AddComponent<TrailRenderer>();
        tr.time = 1.5f; tr.widthMultiplier = 0.06f; tr.sharedMaterial = glow;
        tr.colorGradient = new Gradient { colorKeys = new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(0.8f, 0.2f, 1f), 1) },
            alphaKeys = new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) } };
        tr.shadowCastingMode = ShadowCastingMode.Off; tr.receiveShadows = false; tr.emitting = false;
        var go = orb.AddComponent<GrabbableOrb>(); go.Trail = tr;
        var echo = orb.AddComponent<TrailEcho>();
        new SerializedObject(echo).FindProperty("ghostMaterial").objectReferenceValue = ghostGlow;
        var so = new SerializedObject(echo); so.FindProperty("ghostMaterial").objectReferenceValue = ghostGlow; so.ApplyModifiedProperties();

        // Rig
        var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab");
        GameObject rig;
        if (rigPrefab != null)
        {
            rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab);
            AttachGrabber(rig.transform, "RightHandAnchor", OVRInput.Controller.RTouch);
            AttachGrabber(rig.transform, "LeftHandAnchor", OVRInput.Controller.LTouch);
        }
        else
        {
            Debug.LogWarning("OVRCameraRig prefab not found; using plain camera");
            rig = new GameObject("Rig"); var cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cam.tag = "MainCamera"; cam.transform.SetParent(rig.transform); cam.transform.localPosition = Vector3.up * 1.6f;
        }
        rig.name = "OVRCameraRig";
        foreach (var c in rig.GetComponentsInChildren<Camera>()) { c.clearFlags = CameraClearFlags.SolidColor; c.backgroundColor = Color.black; c.nearClipPlane = 0.05f; c.farClipPlane = 30f; }

        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black;
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        Debug.Log("Scene built: " + ScenePath);
    }

    static void AttachGrabber(Transform rig, string anchor, OVRInput.Controller c)
    {
        var t = FindDeep(rig, anchor);
        if (t == null) { Debug.LogWarning("Missing anchor " + anchor); return; }
        var g = t.gameObject.AddComponent<OrbGrabber>(); g.controller = c;
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
