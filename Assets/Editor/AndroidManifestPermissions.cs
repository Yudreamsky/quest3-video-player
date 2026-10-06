using System.IO;
using System.Xml;
using UnityEditor.Android;

/// <summary>
/// 打包时往 Android 清单里加入 READ_MEDIA_VIDEO（Android 13+ 读取 /sdcard/Movies 需要）。
/// </summary>
public class AndroidManifestPermissions : IPostGenerateGradleAndroidProject
{
    const string AndroidNs = "http://schemas.android.com/apk/res/android";
    static readonly string[] Permissions =
    {
        "android.permission.READ_MEDIA_VIDEO",
        "android.permission.READ_EXTERNAL_STORAGE",
    };

    public int callbackOrder => 100;

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        string manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
        if (!File.Exists(manifestPath)) return;

        var doc = new XmlDocument();
        doc.Load(manifestPath);
        var root = doc.DocumentElement;
        if (root == null) return;

        foreach (var perm in Permissions)
        {
            bool exists = false;
            foreach (XmlNode n in root.SelectNodes("uses-permission"))
                if (n.Attributes?["android:name"]?.Value == perm) exists = true;
            if (exists) continue;

            var el = doc.CreateElement("uses-permission");
            el.SetAttribute("name", AndroidNs, perm);
            root.PrependChild(el);
        }
        doc.Save(manifestPath);
    }
}
