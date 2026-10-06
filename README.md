# Quest 3 Video Player

一个 Meta Quest 3 视频播放器。当前是第一阶段的最小可用版本：在透视（Passthrough）混合现实模式下，把本地 MP4 播放在一块虚拟屏幕上。

技术栈：Unity 6.3 LTS（C#）+ Meta XR Core SDK + Unity OpenXR，内置渲染管线。视频解码使用 Unity VideoPlayer，在 Quest 上走 Android MediaCodec 硬件解码。

## 版本

| 组件 | 版本 | 说明 |
|---|---|---|
| Unity Editor | **6000.3.16f1**（6.3 LTS） | 社区确认可用于 Quest 3 透视打包；6000.4 / 6.5 目前有已知打包问题，先别升级 |
| Meta XR Core SDK | `com.meta.xr.sdk.core` **207.0.0** | 通过 Meta 的 npm 源安装，已写在 `Packages/manifest.json` |
| Unity OpenXR Plugin | `com.unity.xr.openxr` **1.15.1** | Meta 官方推荐版本（Oculus XR Plugin 已弃用） |
| XR Plugin Management | `com.unity.xr.management` **4.5.1** | |

如果打开工程时 Unity 提示某个包版本不可用，在 Package Manager 里把它改成提示的最新版本即可。

## 第一次在自己电脑上构建

### 1. 准备环境（只需一次）

1. 安装 [Unity Hub](https://unity.com/download)，在 Hub 里安装 **Unity 6000.3.16f1**，勾选模块：
   - Android Build Support
   - OpenJDK
   - Android SDK & NDK Tools
2. 打开 Quest 3 开发者模式：手机上的 Meta Horizon App → 设备 → 头显设置 → 开发者模式 → 打开（需要先在 developers.meta.com 注册一个开发者组织，免费）。
3. 用 USB-C 线连接 Quest 3 和电脑，戴上头显，在弹窗里选择“允许 USB 调试”（勾选“始终允许”）。
4. 确认电脑能看到设备：`adb devices` 应列出一台设备（adb 在 Unity 安装目录的 `Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/` 里，或者单独装 Android platform-tools）。

### 2. 打开工程

1. 克隆本仓库。
2. Unity Hub → Add → 选择仓库根目录（含 `Assets`、`Packages` 的那一层），用 6000.3.16f1 打开。
3. 第一次打开会下载 Meta XR SDK 和 OpenXR 包，等进度条结束。如果弹窗问是否启用新的输入系统或重启编辑器，选 Yes。

### 3. 三步菜单

工程里带了一个菜单 **Quest3 Player**，按顺序点：

1. **Quest3 Player → 1. 配置工程 (Android + OpenXR)**
   切到 Android 平台，设置 IL2CPP / ARM64 / Vulkan / Linear / minSdk 32 / 包名 `com.yudreamsky.quest3player`，启用 OpenXR 和 Meta Quest 相关功能，打开透视支持。
   完成后打开 **Meta → Tools → Project Setup Tool**，选 Android 页签，点 **Fix All**，如果还有黄色建议项再点 **Apply All**。这一步会补齐 SDK 版本相关的其余设置。
2. **Quest3 Player → 2. 生成播放场景**
   生成 `Assets/Scenes/Player.unity`：OVRCameraRig + 透视层 + 一块 16:9 的虚拟屏幕，并加入 Build Settings。
3. **Quest3 Player → 3. 打包 APK**
   输出到 `Builds/Quest3Player.apk`。也可以用 File → Build Profiles 里的 **Build And Run** 直接装到头显。

### 4. 安装和放视频

```bash
adb install -r Builds/Quest3Player.apk

# 推荐：放到应用私有目录，不需要任何权限
adb shell mkdir -p /sdcard/Android/data/com.yudreamsky.quest3player/files
adb push 你的视频.mp4 /sdcard/Android/data/com.yudreamsky.quest3player/files/

# 也可以放到 Movies 或 Download，首次启动时会请求存储权限
adb push 你的视频.mp4 /sdcard/Movies/
```

在头显里：应用库 → 右上角下拉选“未知来源” → Quest3 Player。

也可以把一个视频命名为 `sample.mp4` 放进 `Assets/StreamingAssets/`，它会被打包进 APK，作为找不到其他视频时的兜底（该文件已在 `.gitignore` 里忽略）。

## 操作

| 按键 | 功能 |
|---|---|
| 右扳机 / A | 播放 / 暂停 |
| 右摇杆 左 / 右 | 后退 / 前进 10 秒 |
| 右摇杆 上 / 下 | 放大 / 缩小屏幕 |
| 右握把 | 把屏幕重新摆到正前方 |
| B | 切换 透视（看到房间）/ 全黑影院 |
| X | 下一个视频 |

视频搜索顺序：应用私有目录 → `/sdcard/Movies` → `/sdcard/Download` → 内置 `sample.mp4`。

## 工程结构

```
Assets/
  Scripts/
    VideoScreenController.cs   虚拟屏幕：VideoPlayer → RenderTexture，手柄控制，透视切换
    VideoSourceLocator.cs      查找本地视频、请求存储权限
  Editor/
    Quest3PlayerSetup.cs       “Quest3 Player”菜单：配置工程 / 生成场景 / 打包
    AndroidManifestPermissions.cs  打包时加入 READ_MEDIA_VIDEO 权限
Packages/manifest.json         包依赖（含 Meta npm 源）
ProjectSettings/ProjectVersion.txt  Unity 版本
```

`ProjectSettings` 里的其余文件、`Assets/Scenes`、`Assets/Materials` 会在第一次打开和执行菜单后生成，生成后可以一起提交。

## 常见问题

- **头显里一片黑，看不到房间**：确认 Project Setup Tool 里没有剩余的红色问题；OVRCameraRig 上 OVRManager 的 Quest Features → Passthrough Support 应为 Supported/Required，且勾选了 Enable Passthrough。
- **提示 No video found**：按上面的 adb 命令放视频，然后按 X 重新扫描。
- **能播但没声音**：确认视频音轨是 AAC；部分 AC3/DTS 音轨 MediaCodec 不支持，后续阶段会接入 ExoPlayer 解决。
- **MKV、字幕、多音轨、180/360/3D**：在第二阶段实现。

## 路线图

1. ✅ 项目骨架：透视模式下虚拟屏幕播放本地 MP4（本阶段）
2. 播放能力：180/360/3D、字幕、多音轨、文件浏览和媒体库
3. 网络与打磨：SMB/WebDAV/DLNA、手势交互、影院环境、设置页
