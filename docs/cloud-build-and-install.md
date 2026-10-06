# 云端自动打包与安装

不用在自己电脑上装 Unity：每次推送到 `main`，或者在 GitHub 的 **Actions → Build APK → Run workflow** 手动运行，GitHub 会在云端打包好 APK，并发布到 Releases。

头显浏览器里打开这个固定地址就能下载最新版：

```
https://github.com/Yudreamsky/quest3-video-player/releases/latest/download/Quest3Player.apk
```

Pull Request 的构建不发布 Release，APK 在那次运行页面底部的 **Artifacts** 里（需要登录 GitHub，解压后得到 APK）。

## 一次性设置：Unity 授权

云端打包需要用你的 Unity 账号激活一次 Unity（免费的 Personal 授权就行）。这些信息只存在仓库的加密 Secrets 里，不会出现在日志中，也不要发到聊天里。

1. 在自己电脑上装 Unity Hub，登录 Unity 账号，在 Hub 的 **Preferences → Licenses** 里添加一个 Personal 授权（没有的话 Hub 会引导你领取）。
2. 找到授权文件 `Unity_lic.ulf`：
   - Windows：`C:\ProgramData\Unity\Unity_lic.ulf`
   - macOS：`/Library/Application Support/Unity/Unity_lic.ulf`
3. 打开仓库 **Settings → Secrets and variables → Actions → New repository secret**，添加三个：
   - `UNITY_LICENSE`：`Unity_lic.ulf` 文件的全部内容（用记事本打开后全选复制）
   - `UNITY_EMAIL`：Unity 账号邮箱
   - `UNITY_PASSWORD`：Unity 账号密码
4. 到 **Actions → Build APK → Run workflow** 运行一次。

参考：[GameCI 激活说明](https://game.ci/docs/github/activation)。

## 可选：固定签名证书

不配置也能打包，但会用每次都不同的调试证书签名，装新版本前要先卸载旧版（视频文件放在应用目录的话也会一起被删）。配置固定证书后，新版本可以直接覆盖安装，以后做应用内自动更新也需要它。

在装了 JDK 的电脑上（Unity 自带 OpenJDK，或 Android Studio）运行：

```bash
keytool -genkeypair -v -keystore release.keystore -alias quest3player -keyalg RSA -keysize 2048 -validity 10000
# 转成 base64
base64 -w0 release.keystore > release.keystore.b64      # Linux
base64 -i release.keystore -o release.keystore.b64      # macOS
certutil -encode release.keystore tmp.b64 && findstr /v CERTIFICATE tmp.b64 > release.keystore.b64   # Windows
```

再添加四个 Secrets：`ANDROID_KEYSTORE_BASE64`（b64 文件内容）、`ANDROID_KEYSTORE_PASS`、`ANDROID_KEYALIAS_NAME`（上面的 `quest3player`）、`ANDROID_KEYALIAS_PASS`。`release.keystore` 自己妥善保管，丢了以后就不能覆盖安装了。

## 把 APK 装到 Quest 3

头显先打开开发者模式（手机 Meta Horizon App → 设备 → 头显设置 → 开发者模式）。

**只用头显（不需要电脑）**：Quest 自带的「文件」应用不能直接安装 APK，需要从 Meta Horizon 商店装一个能安装 APK 的文件管理器（例如 VR Android File Manager）。之后流程是：头显浏览器打开上面的固定地址下载 → 在文件管理器的 Download 目录点 `Quest3Player.apk` → 安装 → 在「应用库 → 未知来源」里打开。

**用电脑（第一次最稳）**：USB 连上头显，任选一种：
- Meta Quest Developer Hub：把 APK 拖进设备页面
- SideQuest：点「Install APK file from folder」
- 命令行：`adb install -r Quest3Player.apk`

## 下一步：应用内更新（构思）

装上第一版之后，后续版本可以由播放器自己更新，不再需要安装器：播放器设置里加一个「检查更新」，读取 GitHub Releases 的最新版本号，比当前 versionCode 新就下载 APK，调用系统安装界面，点一下「更新」就完成。需要固定签名证书和 `REQUEST_INSTALL_PACKAGES` 权限。调试时可以同时在应用里加一个日志面板，直接在头显里看报错。
