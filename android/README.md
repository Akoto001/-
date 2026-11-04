# 几何体检视器 · 安卓版（源码）

和电脑版共用同一套几何与渲染代码（`..\GeometryViewer-Project\src\Core` 与 `src\Render`），
只重写了界面层。产物是一个可直接安装的 APK。

## 一、重新编译

双击 `编译.cmd`（纯 ASCII 批处理，32 秒左右），成功后会把 APK 复制到工作区根目录
`..\GeometryViewer-Android.apk`。

等价的命令行（在 PowerShell 里跑要先把环境变量设好）：

```powershell
$ws   = 'C:\Users\Akoto\Documents\deepseek-harness\default-workspace'
$env:DOTNET_CLI_HOME        = "$ws\.dotnet8-home"
$env:NUGET_PACKAGES         = "$ws\.dotnet8-home\packages"
$env:NUGET_HTTP_CACHE_PATH  = "$ws\.dotnet8-home\nuget-http"
$env:TEMP                   = "$ws\.dotnet8-home\tmp"      # 沙箱不允许写 %TEMP%
$env:TMP                    = $env:TEMP
$env:JAVA_HOME              = 'C:\Program Files\Java\jdk-17'

cd "$ws\GeometryViewer-Android"
& "$ws\.dotnet8\dotnet.exe" build GeoViewerAndroid.csproj -c Release -f net8.0-android `
  -p:AndroidSdkDirectory="$ws\.android-sdk" `
  -p:JavaSdkDirectory="$env:JAVA_HOME" `
  -p:AppSettingsDirectory="$ws\.dotnet8-home\appdata" `
  -p:AndroidKeyStore=true `
  -p:AndroidSigningKeyStore="$ws\GeometryViewer-Android\geo-release.keystore" `
  -p:AndroidSigningKeyAlias=geo `
  -p:AndroidSigningKeyPass=geoviewer `
  -p:AndroidSigningStorePass=geoviewer
```

几个参数不是可选的，写在这里免得下次踩坑：

| 参数 / 变量 | 为什么必须 |
|---|---|
| `-p:AndroidSdkDirectory=…` | Android SDK 装在 `%ws%\.android-sdk`（不是默认位置） |
| `-p:JavaSdkDirectory=…` | 用本机已有的 `C:\Program Files\Java\jdk-17` |
| `-p:AppSettingsDirectory=…` | 默认会往 `%LOCALAPPDATA%\Xamarin\Mono for Android` 写调试签名，沙箱拒绝 |
| `$env:TEMP` | 构建过程要临时目录，`%LOCALAPPDATA%\Temp` 被沙箱拒绝 |
| `-p:AndroidKeyStore=true` + 四个签名参数 | 不指定就会去用默认调试密钥（同样写不进 `%LOCALAPPDATA%`）；这里用工程自带的 `geo-release.keystore`（口令 `geoviewer`，别名 `geo`）。想换自己的密钥，用 `keytool -genkeypair` 生成一个再改这几行即可 |

工具链位置（都在工作区里，不动系统）：

| 东西 | 位置 | 备注 |
|---|---|---|
| .NET SDK 8.0.427 | `..\.dotnet8\` | 从 dotnet-sdk zip 解出来，装不了系统级 workload 也不影响 |
| android workload | `..\.dotnet8\`（同一根） | `dotnet workload install android`（34.0.154） |
| Android SDK | `..\.android-sdk\` | build-tools 34.0.0、platform android-34、platform-tools、cmdline-tools |
| JDK 17 | `C:\Program Files\Java\jdk-17` | 系统已有 |

## 二、目录

```
GeometryViewer-Android\
  GeoViewerAndroid.csproj      工程（<Compile Include="..\GeometryViewer-Project\src\Core\*.cs"> 等方式复用）
  MainActivity.cs              界面：竖屏上下 / 横屏左右的自适应布局 + 控制面板 + 预设对话框
                               渲染调度、手势（含双指平面平移）、横屏按钮、面板收起三角形
  ViewportView.cs              旧的自定义视口（已不参与编译；自定义 View 在 Android 16 上会崩）
  AndroidManifest.xml          清单（包名、图标、label、largeHeap）
  Resources\drawable-nodpi\icon.png  图标
  编译.cmd                      一键编译
  geo-release.keystore          签名密钥（口令 geoviewer，别名 geo）
```

渲染线程：拖动时按视口 34% 分辨率出预览图，松手后按设备像素出精细图；`Renderer.Render`
每次都会 clone 相机，所以在后台线程渲染不会和交互抢状态。界面只用系统原生控件
（`ImageView` + `FrameLayout` + 按钮），**不要引入自定义 `View` 或自定义 `[Application]`
子类**——这两者都在 Android 16 上导致启动即闪退（已实测）。

## 三、想改哪里

| 想改的东西 | 位置 |
|---|---|
| 面板里有哪些控件、顺序、文案 | `MainActivity.cs` 的 `BuildPanel()` |
| 竖屏/横屏的分配比例（画面 vs 面板） | `MainActivity.cs` 的 `ApplyOrientation()`（竖屏 `_frame` 权重 1 + 面板 0.9；横屏面板固定 `Dp(310)`，收起时 `Dp(18)`） |
| 强制横屏 / 沉浸式全屏 | `MainActivity.cs` 的 `ToggleLandscape()` 与 `SetBarsHidden(...)` |
| 参数面板收起按钮（▼ / ▲） | `MainActivity.cs` 的 `TogglePanel()` 与 `ApplyOrientation()` 里的 `_foldButton.Text` |
| 颜色 | `MainActivity.cs` 顶部几个 `Color` 常量 |
| 拖动灵敏度、双指缩放/平移 | `MainActivity.cs` 的 `HandleTouch(MotionEvent)`（`perPixel = 200.0 / Math.Max(120, _image.Width)`） |
| 预览分辨率 | `ViewportView.RenderOnce` 里的 `scale = final ? 1.0 : 0.34` |
| 俯视图小窗的大小、内容 | `ViewportView.DrawTopView` |
| 几何、相机、渲染、结构线、透视线、焦距 | 全部在 `..\GeometryViewer-Project\src\` 里，改完电脑版和安卓版同时生效 |

## 四、安装

APK 用 v2+v3 签名，`minSdk 24`（Android 7.0 起）、`targetSdk 34`，
首次安装需要在系统里允许「安装未知应用」。详细步骤见工作区根目录的 `安卓使用说明.txt`。
