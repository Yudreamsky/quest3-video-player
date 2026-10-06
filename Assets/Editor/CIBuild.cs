using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.XR.Management;

/// <summary>
/// 云端自动打包入口（GitHub Actions / GameCI 调用），本地不用管它。
/// 做的事等于在编辑器里依次点 Quest3 Player 菜单 1、2、3，再加上版本号和签名。
/// 命令行参数由 game-ci/unity-builder 传入：-customBuildPath -buildVersion -androidVersionCode -androidKeystore* 等。
/// </summary>
public static class CIBuild
{
    const string ScenePath = "Assets/Scenes/Player.unity";

    public static void BuildQuestApk()
    {
        var args = ParseArgs();
        try
        {
            EnsureXRSettingsAsset();

            // 第一次调用会创建 OpenXR 设置资源，第二次才能勾上里面的功能
            Quest3PlayerSetup.ConfigureProject();
            AssetDatabase.Refresh();
            Quest3PlayerSetup.ConfigureProject();

            if (!File.Exists(ScenePath)) Quest3PlayerSetup.CreateScene();

            ApplyVersion(args);
            ApplySigning(args);
            AssetDatabase.SaveAssets();

            string output = Arg(args, "customBuildPath", "Builds/Quest3Player.apk");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            EditorUserBuildSettings.buildAppBundle = false;

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = output,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            });

            var s = report.summary;
            Debug.Log($"[CIBuild] 结果 {s.result}，错误 {s.totalErrors}，警告 {s.totalWarnings}，大小 {s.totalSize} 字节，输出 {output}");
            EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 101);
        }
        catch (Exception e)
        {
            Debug.LogError("[CIBuild] 打包异常: " + e);
            EditorApplication.Exit(102);
        }
    }

    /// <summary>
    /// 全新克隆的工程里没有 XR Plug-in Management 的设置资源，菜单 1 在编辑器里会靠打开设置页让它自动生成，
    /// 命令行模式下没有界面，这里手动创建好 Android 的那一份。
    /// </summary>
    static void EnsureXRSettingsAsset()
    {
        EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget);
        if (perTarget == null)
        {
            var guids = AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget");
            if (guids.Length > 0)
                perTarget = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
        if (perTarget == null)
        {
            Directory.CreateDirectory("Assets/XR");
            perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
            Debug.Log("[CIBuild] 已创建 XR 设置资源");
        }
        EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);

        if (!perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
            perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
        perTarget.SettingsForBuildTarget(BuildTargetGroup.Android).InitManagerOnStart = true;
        AssetDatabase.SaveAssets();
    }

    static void ApplyVersion(Dictionary<string, string> args)
    {
        string version = Arg(args, "buildVersion", null);
        if (!string.IsNullOrEmpty(version) && version != "none")
            PlayerSettings.bundleVersion = version;
        if (int.TryParse(Arg(args, "androidVersionCode", ""), out int code) && code > 0)
            PlayerSettings.Android.bundleVersionCode = code;
        Debug.Log($"[CIBuild] 版本 {PlayerSettings.bundleVersion} ({PlayerSettings.Android.bundleVersionCode})");
    }

    /// <summary>
    /// 有签名证书就用固定证书签名（这样新版本可以直接覆盖安装旧版本）；没有就用 Unity 的调试证书，
    /// 调试证书每次云端构建都不一样，覆盖安装会失败，需要先卸载旧版。
    /// </summary>
    static void ApplySigning(Dictionary<string, string> args)
    {
        string keystore = Arg(args, "androidKeystoreName", "");
        if (!string.IsNullOrEmpty(keystore) && !Path.IsPathRooted(keystore))
            keystore = Path.GetFullPath(keystore);

        if (string.IsNullOrEmpty(keystore) || !File.Exists(keystore))
        {
            PlayerSettings.Android.useCustomKeystore = false;
            Debug.LogWarning("[CIBuild] 没有配置签名证书，使用调试证书签名");
            return;
        }

        PlayerSettings.Android.useCustomKeystore = true;
        PlayerSettings.Android.keystoreName = keystore;
        PlayerSettings.Android.keystorePass = Arg(args, "androidKeystorePass", "");
        PlayerSettings.Android.keyaliasName = Arg(args, "androidKeyaliasName", "");
        PlayerSettings.Android.keyaliasPass = Arg(args, "androidKeyaliasPass", "");
        Debug.Log("[CIBuild] 使用固定签名证书");
    }

    static Dictionary<string, string> ParseArgs()
    {
        var result = new Dictionary<string, string>();
        var argv = Environment.GetCommandLineArgs();
        for (int i = 0; i < argv.Length; i++)
        {
            if (!argv[i].StartsWith("-")) continue;
            string key = argv[i].Substring(1);
            string value = i + 1 < argv.Length && !argv[i + 1].StartsWith("-") ? argv[++i] : "";
            result[key] = value;
        }
        return result;
    }

    static string Arg(Dictionary<string, string> args, string key, string fallback)
        => args.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v) ? v : fallback;
}
