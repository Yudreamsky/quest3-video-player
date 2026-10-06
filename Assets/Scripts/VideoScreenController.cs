using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// 虚拟屏幕播放器（MVP）。挂在一个 Quad 上：
/// 用 Unity VideoPlayer（Android 上走 MediaCodec 硬解）把视频渲染到 RenderTexture，再显示在 Quad 上。
///
/// 手柄操作：
///   右扳机 / A      播放 / 暂停
///   右摇杆 左/右    后退 / 前进 10 秒
///   右摇杆 上/下    放大 / 缩小屏幕
///   右握把          把屏幕重新摆到正前方
///   B               切换 透视（看到真实房间）/ 全黑影院
///   X               下一个视频
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
public class VideoScreenController : MonoBehaviour
{
    [Header("场景引用")]
    [Tooltip("头部位置（OVRCameraRig/TrackingSpace/CenterEyeAnchor）")]
    public Transform head;
    [Tooltip("透视层，B 键切换开关")]
    public OVRPassthroughLayer passthroughLayer;
    [Tooltip("渲染用的相机（CenterEyeAnchor 上的 Camera）")]
    public Camera eyeCamera;
    [Tooltip("用来显示状态信息的文字（可选）")]
    public TextMesh statusText;

    [Header("屏幕参数")]
    public float distance = 2.5f;      // 米
    public float screenHeight = 1.4f;  // 米，约等于 2.5 米外 63 英寸
    public float minHeight = 0.5f;
    public float maxHeight = 4.0f;
    public float seekSeconds = 10f;

    VideoPlayer _player;
    AudioSource _audio;
    RenderTexture _rt;
    List<string> _videos = new List<string>();
    int _index = -1;
    float _aspect = 16f / 9f;
    bool _placedOnce;
    bool _passthroughOn = true;

    void Awake()
    {
        _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        _audio.spatialBlend = 0f; // MVP 先用立体声直出，空间音频放到后续线程

        _player = gameObject.AddComponent<VideoPlayer>();
        _player.playOnAwake = false;
        _player.source = VideoSource.Url;
        _player.renderMode = VideoRenderMode.RenderTexture;
        _player.audioOutputMode = VideoAudioOutputMode.AudioSource;
        _player.controlledAudioTrackCount = 1;
        _player.EnableAudioTrack(0, true);
        _player.SetTargetAudioSource(0, _audio);
        _player.isLooping = true;
        _player.skipOnDrop = true;
        _player.prepareCompleted += OnPrepared;
        _player.errorReceived += (vp, msg) => SetStatus($"Playback error:\n{msg}");

        ApplyScale();
    }

    void Start()
    {
        VideoSourceLocator.RequestStoragePermission();
        Rescan();
        if (_videos.Count > 0) PlayIndex(0);
    }

    void OnApplicationFocus(bool focus)
    {
        // 权限弹窗关闭后应用会重新获得焦点，这时再扫一遍目录
        if (focus && _index < 0)
        {
            Rescan();
            if (_videos.Count > 0) PlayIndex(0);
        }
    }

    void Rescan()
    {
        _videos = VideoSourceLocator.FindVideos();
        Debug.Log($"[Quest3Player] 找到 {_videos.Count} 个视频: {string.Join(", ", _videos)}");
        if (_videos.Count == 0)
            SetStatus("No video found.\nadb push your.mp4 to\n" + Application.persistentDataPath);
    }

    void PlayIndex(int i)
    {
        if (_videos.Count == 0) return;
        _index = (i % _videos.Count + _videos.Count) % _videos.Count;
        string path = _videos[_index];
        SetStatus("Loading " + Path.GetFileName(path) + " ...");
        _player.Stop();
        _player.url = path;
        _player.Prepare();
    }

    void OnPrepared(VideoPlayer vp)
    {
        int w = (int)vp.width, h = (int)vp.height;
        if (w <= 0 || h <= 0) { w = 1920; h = 1080; }
        _aspect = (float)w / h;

        if (_rt == null || _rt.width != w || _rt.height != h)
        {
            if (_rt != null) _rt.Release();
            _rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
            _rt.useMipMap = true;        // 远处看大屏时减少闪烁
            _rt.autoGenerateMips = true;
            _rt.anisoLevel = 4;
            _rt.Create();
        }
        vp.targetTexture = _rt;
        GetComponent<MeshRenderer>().material.mainTexture = _rt;

        ApplyScale();
        if (!_placedOnce) { PlaceInFront(); _placedOnce = true; }
        SetStatus(null);
        vp.Play();
    }

    void Update()
    {
        // 播放 / 暂停
        if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch) ||
            OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch))
        {
            if (_player.isPlaying) _player.Pause();
            else if (_player.isPrepared) _player.Play();
        }

        // 快退 / 快进
        if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstickRight, OVRInput.Controller.RTouch)) Seek(+seekSeconds);
        if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstickLeft, OVRInput.Controller.RTouch)) Seek(-seekSeconds);

        // 屏幕大小（摇杆上下连续调节）
        float y = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch).y;
        if (Mathf.Abs(y) > 0.5f)
        {
            screenHeight = Mathf.Clamp(screenHeight * (1f + y * Time.deltaTime), minHeight, maxHeight);
            ApplyScale();
        }

        // 重新居中
        if (OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch)) PlaceInFront();

        // 透视 / 黑色影院
        if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch)) SetPassthrough(!_passthroughOn);

        // 下一个视频
        if (OVRInput.GetDown(OVRInput.Button.Three, OVRInput.Controller.LTouch))
        {
            Rescan();
            PlayIndex(_index + 1);
        }

        // 头显还没开始追踪时第一帧位置可能是 0，等追踪就绪后再摆一次
        if (!_placedOnce && head != null && head.position.sqrMagnitude > 0.01f)
        {
            PlaceInFront();
            _placedOnce = true;
        }
    }

    void Seek(float delta)
    {
        if (!_player.isPrepared || !_player.canSetTime) return;
        double t = _player.time + delta;
        double len = _player.length;
        if (len > 0) t = System.Math.Max(0, System.Math.Min(len - 1, t));
        _player.time = t;
    }

    void ApplyScale()
    {
        transform.localScale = new Vector3(screenHeight * _aspect, screenHeight, 1f);
    }

    public void PlaceInFront()
    {
        if (head == null) return;
        // 只取水平朝向，屏幕保持竖直，高度与眼睛平齐
        Vector3 fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
        fwd.Normalize();
        transform.position = head.position + fwd * distance;
        transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
        if (statusText != null)
        {
            statusText.transform.position = transform.position - fwd * 0.05f;
            statusText.transform.rotation = transform.rotation;
        }
    }

    void SetPassthrough(bool on)
    {
        _passthroughOn = on;
        if (passthroughLayer != null) passthroughLayer.enabled = on;
        if (eyeCamera != null)
        {
            eyeCamera.clearFlags = CameraClearFlags.SolidColor;
            // 透视需要 alpha=0 的清屏色，否则相机画面会挡住透视层
            eyeCamera.backgroundColor = on ? new Color(0, 0, 0, 0) : Color.black;
        }
    }

    void SetStatus(string msg)
    {
        if (msg != null) Debug.Log("[Quest3Player] " + msg);
        if (statusText == null) return;
        statusText.gameObject.SetActive(!string.IsNullOrEmpty(msg));
        statusText.text = msg ?? "";
    }

    void OnDestroy()
    {
        if (_rt != null) _rt.Release();
    }
}
