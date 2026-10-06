using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 一键配置工程、生成播放场景、打包 APK。菜单：Quest3 Player/...
/// 第 1 步之后请再打开 Meta 的 Project Setup Tool 点一次 "Fix All"，它会补齐 SDK 版本相关的其余设置。
/// </summary>
public static class Quest3PlayerSetup
{
    const string ScenePath = "Assets/Scenes/Player.unity";
    const string MaterialPath = "Assets/Materials/Screen.mat";
    const string ApkPath = "Builds/Quest3Player.apk";
    const string PackageName = "com.yudreamsky.quest3player";

    // ---------------------------------------------------------------- 1. 工程设置

    [MenuItem("Quest3 Player/1. 配置工程 (Android + OpenXR)", priority = 1)]
    public static void ConfigureProject()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

        PlayerSettings.companyName = "Yudreamsky";
        PlayerSettings.productName = "Quest3 Player";
        PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, PackageName);
        PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { UnityEngine.Rendering.GraphicsDeviceType.Vulkan });
        PlayerSettings.Android.forceSDCardPermission = true; // 声明 READ_EXTERNAL_STORAGE
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
        EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;

        bool xrOk = EnableOpenXRLoader();
        int features = EnableOpenXRFeatures();
        EnablePassthroughInProjectConfig();

        AssetDatabase.SaveAssets();
        string msg = "工程设置完成。\n\n" +
                     (xrOk ? "✓ 已启用 OpenXR（Android）\n" : "✗ 未能自动启用 OpenXR，请到 Project Settings > XR Plug-in Management > Android 勾选 OpenXR\n") +
                     $"✓ 已启用 {features} 个 OpenXR 功能\n\n" +
                     "下一步：打开 Meta > Tools > Project Setup Tool，点 Fix All（以及 Apply All），然后运行菜单 2。";
        Debug.Log("[Quest3Player] " + msg);
        EditorUtility.DisplayDialog("Quest3 Player", msg, "好");
    }

    static bool EnableOpenXRLoader()
    {
        try
        {
            // 通过反射调用，避免 XR Management 版本差异导致编译失败
            var perTarget = FindType("UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget");
            var store = FindType("UnityEditor.XR.Management.Metadata.XRPackageMetadataStore");
            if (perTarget == null || store == null) return false;

            var getSettings = perTarget.GetMethod("XRGeneralSettingsForBuildTarget", new[] { typeof(BuildTargetGroup) });
            var general = getSettings?.Invoke(null, new object[] { BuildTargetGroup.Android });
            if (general == null)
            {
                // 首次使用：先打开一次 XR 设置页，让 XR Management 创建设置资源
                SettingsService.OpenProjectSettings("Project/XR Plug-in Management");
                general = getSettings?.Invoke(null, new object[] { BuildTargetGroup.Android });
                if (general == null) return false;
            }
            var manager = general.GetType().GetProperty("Manager")?.GetValue(general);
            if (manager == null) return false;

            var assign = store.GetMethods().FirstOrDefault(m => m.Name == "AssignLoader" && m.GetParameters().Length == 3);
            var result = assign?.Invoke(null, new object[] { manager, "UnityEngine.XR.OpenXR.OpenXRLoader", BuildTargetGroup.Android });
            return result is bool b && b;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Quest3Player] 自动启用 OpenXR 失败: " + e.Message);
            return false;
        }
    }

    static int EnableOpenXRFeatures()
    {
        int count = 0;
        try
        {
            var settingsType = FindType("UnityEngine.XR.OpenXR.OpenXRSettings");
            var get = settingsType?.GetMethod("GetSettingsForBuildTargetGroup", new[] { typeof(BuildTargetGroup) });
            var settings = get?.Invoke(null, new object[] { BuildTargetGroup.Android });
            if (settings == null) return 0;

            var features = settingsType.GetMethod("GetFeatures", Type.EmptyTypes)?.Invoke(settings, null) as Array;
            if (features == null) return 0;

            // Meta Quest 支持、Meta XR 功能（OVRPlugin on OpenXR）、Touch 手柄交互配置
            string[] wanted = { "MetaQuestFeature", "MetaXRFeature", "OculusTouchControllerProfile", "MetaQuestTouchPlusControllerProfile" };
            foreach (var f in features)
            {
                if (f == null) continue;
                if (!wanted.Contains(f.GetType().Name)) continue;
                var enabled = f.GetType().GetProperty("enabled");
                enabled?.SetValue(f, true);
                EditorUtility.SetDirty((UnityEngine.Object)f);
                count++;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Quest3Player] 自动启用 OpenXR 功能失败: " + e.Message);
        }
        return count;
    }

    static void EnablePassthroughInProjectConfig()
    {
        try
        {
            var cfgType = FindType("OVRProjectConfig");
            if (cfgType == null) return;
            object cfg = cfgType.GetProperty("CachedProjectConfig")?.GetValue(null)
                         ?? cfgType.GetMethod("GetProjectConfig", Type.EmptyTypes)?.Invoke(null, null);
            if (cfg == null) return;

            var field = cfgType.GetField("insightPassthroughSupport");
            if (field != null)
                field.SetValue(cfg, Enum.Parse(field.FieldType, "Required"));

            var commit = cfgType.GetMethod("CommitProjectConfig");
            commit?.Invoke(null, new[] { cfg });
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Quest3Player] 设置透视支持失败（可在 OVRManager 的 Quest Features 里手动勾选）: " + e.Message);
        }
    }

    // ---------------------------------------------------------------- 2. 场景

    [MenuItem("Quest3 Player/2. 生成播放场景", priority = 2)]
    public static void CreateScene()
    {
        var prefab = FindPrefab("OVRCameraRig");
        if (prefab == null)
        {
            EditorUtility.DisplayDialog("Quest3 Player", "找不到 OVRCameraRig 预制体。请确认 Meta XR Core SDK 已导入完成。", "好");
            return;
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RenderSettings.ambientLight = Color.white;

        // 相机 Rig
        var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        rig.name = "OVRCameraRig";
        var ovrManager = rig.GetComponent<OVRManager>() ?? rig.AddComponent<OVRManager>();
        ovrManager.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;
        ovrManager.isInsightPassthroughEnabled = true;

        var passthrough = rig.AddComponent<OVRPassthroughLayer>();
        passthrough.overlayType = OVROverlay.OverlayType.Underlay;
        passthrough.compositionDepth = 0;

        var centerEye = rig.GetComponentsInChildren<Transform>(true).First(t => t.name == "CenterEyeAnchor");
        var cam = centerEye.GetComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0, 0, 0, 0); // 透视必须 alpha=0
        cam.nearClipPlane = 0.05f;

        // 屏幕
        var screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
        screen.name = "VideoScreen";
        UnityEngine.Object.DestroyImmediate(screen.GetComponent<Collider>());
        screen.transform.position = new Vector3(0, 1.6f, 2.5f);
        screen.GetComponent<MeshRenderer>().sharedMaterial = GetOrCreateScreenMaterial();

        // 状态文字
        var textGo = new GameObject("StatusText");
        var text = textGo.AddComponent<TextMesh>();
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.characterSize = 0.02f;
        text.fontSize = 60;
        text.color = Color.white;
        text.text = "Loading...";
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font != null)
        {
            text.font = font;
            textGo.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }

        var ctrl = screen.AddComponent<VideoScreenController>();
        ctrl.head = centerEye;
        ctrl.eyeCamera = cam;
        ctrl.passthroughLayer = passthrough;
        ctrl.statusText = text;

        Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();

        Debug.Log("[Quest3Player] 场景已生成: " + ScenePath);
        EditorUtility.DisplayDialog("Quest3 Player", "场景已生成并加入 Build Settings：\n" + ScenePath, "好");
    }

    static Material GetOrCreateScreenMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat != null) return mat;
        Directory.CreateDirectory("Assets/Materials");
        // 不受光照影响，视频颜色原样显示；材质资源被场景引用，打包时着色器不会被剔除
        mat = new Material(Shader.Find("Unlit/Texture"));
        AssetDatabase.CreateAsset(mat, MaterialPath);
        return mat;
    }

    // ---------------------------------------------------------------- 3. 打包

    [MenuItem("Quest3 Player/3. 打包 APK", priority = 3)]
    public static void BuildApk()
    {
        if (!File.Exists(ScenePath)) CreateScene();
        Directory.CreateDirectory(Path.GetDirectoryName(ApkPath));
        EditorUserBuildSettings.buildAppBundle = false;

        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = ApkPath,
            target = BuildTarget.Android,
            options = BuildOptions.None,
        };
        var report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result == BuildResult.Succeeded)
        {
            string full = Path.GetFullPath(ApkPath);
            Debug.Log("[Quest3Player] 打包成功: " + full);
            EditorUtility.RevealInFinder(full);
        }
        else
        {
            Debug.LogError("[Quest3Player] 打包失败: " + report.summary.result + "，详见 Console");
        }
    }

    // ---------------------------------------------------------------- 工具

    static GameObject FindPrefab(string name)
    {
        foreach (var guid in AssetDatabase.FindAssets(name + " t:Prefab"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(path) == name)
                return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
        return null;
    }

    static Type FindType(string fullName)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType(fullName);
            if (t != null) return t;
        }
        return null;
    }
}
