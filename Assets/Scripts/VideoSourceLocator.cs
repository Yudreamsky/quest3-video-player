using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

/// <summary>
/// 查找可播放的本地视频文件。按顺序搜索：
/// 1. 应用私有目录（/sdcard/Android/data/&lt;包名&gt;/files），无需任何权限
/// 2. /sdcard/Movies 与 /sdcard/Download（需要存储权限）
/// 3. StreamingAssets 里打包进 APK 的 sample.mp4
/// </summary>
public static class VideoSourceLocator
{
    static readonly string[] Extensions = { ".mp4", ".m4v", ".mov", ".webm", ".mkv" };

    static readonly string[] SharedFolders =
    {
        "/sdcard/Movies",
        "/sdcard/Download",
    };

    public static void RequestStoragePermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        // Android 13+ 用 READ_MEDIA_VIDEO，旧版本用 READ_EXTERNAL_STORAGE（两者都在清单里声明）
        var missing = new List<string>();
        foreach (var p in new[] { "android.permission.READ_MEDIA_VIDEO", Permission.ExternalStorageRead })
            if (!Permission.HasUserAuthorizedPermission(p)) missing.Add(p);
        if (missing.Count > 0)
            Permission.RequestUserPermissions(missing.ToArray());
#endif
    }

    public static List<string> FindVideos()
    {
        var results = new List<string>();

        AddFrom(Application.persistentDataPath, results);
        foreach (var dir in SharedFolders)
            AddFrom(dir, results);

#if UNITY_EDITOR
        // 编辑器里方便调试：也搜一下用户的“影片/视频”目录
        AddFrom(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyVideos), results);
#endif

        // StreamingAssets 在 Android 上位于 APK 内部，不能用 Directory 枚举，直接按约定文件名使用
        string bundled = Path.Combine(Application.streamingAssetsPath, "sample.mp4");
#if UNITY_ANDROID && !UNITY_EDITOR
        results.Add(bundled);
#else
        if (File.Exists(bundled)) results.Add(bundled);
#endif
        return results;
    }

    static void AddFrom(string dir, List<string> results)
    {
        if (string.IsNullOrEmpty(dir)) return;
        try
        {
            if (!Directory.Exists(dir)) return;
            var files = new List<string>(Directory.GetFiles(dir));
            files.Sort(System.StringComparer.OrdinalIgnoreCase);
            foreach (var f in files)
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (System.Array.IndexOf(Extensions, ext) >= 0 && !results.Contains(f))
                    results.Add(f);
            }
        }
        catch (System.Exception e)
        {
            // 没有权限时会抛异常，忽略该目录即可
            Debug.Log($"[Quest3Player] 跳过目录 {dir}: {e.Message}");
        }
    }
}
