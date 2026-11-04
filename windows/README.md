# 几何体检视器 · 源码工程

用于绘画参考的 3D 几何体检视器。五种标准体（球体／圆柱体／正方体／长方体／圆锥体）以正方体棱长 `a`
为基准定义，一次显示一个，可切换外观（不透明／半透明／仅轮廓线）与结构线显示。

* 技术栈：C# + WPF，.NET 8，**零第三方依赖**
* 渲染：自写软件光线投射（解析求交 + 超采样 + 隐藏线消除），不依赖任何 3D 库
* 产物：单个自包含 exe（约 63 MB，免安装 .NET）

---

## 一、怎么编译

1. 装 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)（只要 SDK，不用装运行时）。
2. 双击本目录下的 **`编译.cmd`**。
3. 成功后 exe 在 `dist\GeometryViewer.exe`。

`编译.cmd` 做的事情等价于：

```
dotnet publish src\GeoViewer.csproj -c Release -r win-x64 --self-contained true ^
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true -p:DebugType=none -o dist
```

只编译不打包（快速验证语法）用：

```
dotnet build src\GeoViewer.csproj -c Release
```

> 编译时会把 NuGet 缓存放在本工程的 `.build\` 里，不污染用户目录；
> 首次编译需要联网下载 win-x64 运行时包，之后离线可编译。

## 二、目录结构

```
src\
  GeoViewer.csproj        工程文件（单文件发布参数、图标、版本信息）
  app.manifest            高 DPI 声明
  app.ico                 程序图标
  App.xaml(.cs)           入口；解析命令行，--shot/--ui 离屏出图
  Cli.cs                  命令行参数（离屏渲染用）
  Core\
    V3.cs                 向量/数学/色彩空间小工具
    Camera.cs             相机（方位角/仰角/滚转、视场角、透视投影、fit 取景）
    Optics.cs             焦距（35mm 等效）与垂直视野角的换算、镜头预设
    Solid.cs              五种几何体 + 阿萨罗头：解析/网格求交、法线、包围盒
    MeshSolid.cs          三角网格实体（BVH 求交、平面法线、奇偶包含判定）
    AsaroHead.cs          阿萨罗头（Planes of the Head）面块建模：环表 + 鼻/眼/唇/耳
    Scene.cs              文档模型：选中的体、外观、结构线、透视线、焦距、取景
  Render\
    Renderer.cs           光线投射渲染器（色调映射、地面接触阴影、环境光）
    LineGen.cs            棱线/轮廓线/结构线/红色中线/中轴/透视引导线的线段生成
    LineCanvas.cs         线段光栅化 + 深度测试（隐藏线消除）
  UI\
    Theme.xaml            暗色主题控件样式
    MainWindow.xaml(.cs)  主窗口与右侧边栏
    ViewportControl.cs    3D 视口（拖拽旋转、滚轮缩放、平移、异步渲染）
    TopViewControl.cs     俯视图（占位、摄像机位置、视野射线）
```

## 三、想改哪里

| 想改的东西 | 位置 |
|---|---|
| 初始视角、初始选中的体 | `src\Core\Scene.cs` 的 `InitialAzimuth` / `InitialElevation` / `DefaultSelected` |
| 几何体尺寸公式 | `src\Core\Solid.cs` 的 `Create(...)` / `UpdateBounds()` |
| 阿萨罗头的面块与比例 | `src\Core\AsaroHead.cs`（环表 `Rings`、`AddNose`/`AddEyes`/`AddLips`/`AddEars`、`Dent(...)` 眼窝）；网格求交器在 `src\Core\MeshSolid.cs` |
| 摄像机角度下拉的候选值 | `src\UI\MainWindow.xaml.cs` 的 `BuildAngleCombos()` |
| 角度行的 −/+ 步进按钮 | `src\UI\MainWindow.xaml.cs` 的 `StepPitch/StepYaw/StepRoll` + `Snap()`（15° 一档） |
| 焦距预设、可调范围 | `src\Core\Optics.cs` 的 `PresetFocalMm` / `MinFovYDeg` / `MaxFovYDeg` / `DefaultFocalMm`；下拉项在 `MainWindow.xaml.cs` 的 `BuildFovCombo()`、步进在 `StepFov()` |
| 背景/地面/物体配色、光照 | `src\Render\Renderer.cs` 的 `Palette` |
| 结构线颜色、粗细、浓淡 | `src\Render\LineCanvas.cs` 的 `StyleFor(...)`；线段几何在 `LineGen.cs` 的 `AddStructure(...)` |
| 红色加粗中线的画法与粗细 | 几何在 `LineGen.cs` 的 `AddMidLines(...)`（`LineKind.Mid`），颜色 `Palette.MidRed`，粗细/浓淡在 `LineCanvas.cs` 的 `StyleFor(...)` 里 `LineKind.Mid` 那一行 |
| 穿过实体的构造线（球心十字/极轴不隐藏） | `Segment.Through`；`LineCanvas.DrawSegment(...)` 的 `through` 参数（深度测试直接放行） |
| 换角度后自动取景、物体始终入画 | `src\Core\Scene.cs` 的 `Fit(width, height, margin)`：环绕中心固定为五体并集包围盒中心，按 8 个角点解出所需距离（`margin` 默认 1.16 是留边） |
| 透视线的画法与颜色 | `LineGen.cs` 的 `AddPerspective(...)`/`AddBoxPerspective(...)`/`AddVerticalPerspective(...)`；消失点标记在 `LineCanvas.cs` 的 `DrawVanishingPoints(...)` |
| 透视类型（一点/两点/三点/鱼眼）、锁定的朝向与默认焦距 | `src\Core\Lens.cs` 的 `LensKind` / `PresetOrientation(...)` / `IsPrescribed(...)`；鱼眼投影在 `Camera.RayFor` 与 `Camera.Project` |
| 侧栏布局与文案 | `src\UI\MainWindow.xaml` |
| 默认窗口大小 | `src\UI\MainWindow.xaml.cs` 构造函数 |

## 四、命令行（可选）

离屏出图，不弹窗口：

```
GeometryViewer.exe --shot out.png --w 1600 --h 1000 --ss 2 --solid cone --mode outline --el 20 --az 25
```

| 参数 | 说明 |
|---|---|
| `--shot <png>` | 渲染一张图存盘后退出 |
| `--ui <png>` | 起窗口、截图内容区后退出（用于检查界面） |
| `--w/--h` | 输出尺寸（默认 1280×800） |
| `--ss` | 超采样倍数（默认 2） |
| `--solid` | `sphere` / `cylinder` / `cube` / `cuboid` / `cone` |
| `--show 01000` | 旧的按位选体写法（兼容保留） |
| `--mode` | `opaque` / `translucent` / `outline` |
| `--struct 0/1` | 结构线开关 |
| `--persp 0/1` | 透视线（消失点连线）开关 |
| `--el/--az/--roll` | 相对初始视角的偏移角度（与界面下拉一致） |
| `--mm <焦距>` | 35mm 等效焦距，如 `--mm 24`（等同广角） |
| `--fov <角度>` | 直接给垂直视野角（度）；与 `--mm` 同时给时以 `--fov` 为准 |
| `--dist` | 手动指定相机距离 |
| `--nofit` | 不自动取景 |

## 五、关于 Windows 的“不安全”提示

这个 exe 没有购买代码签名证书（个人开发者的证书一年要几千元），所以：

* **首次运行** Windows 可能弹“Windows 已保护你的电脑 / 未知发布者”。
  点 **「更多信息」→「仍要运行」** 即可，之后不会再问。
* 如果文件是从网上/聊天软件传过来的，右键属性里可能有「解除锁定」，
  勾上并确定，提示会消失（`编译.cmd` 自己生成的 exe 不会有这个锁定）。
* 单文件 exe 第一次启动要把运行库解包到 `%TEMP%\.net`，杀软偶尔会多看一眼，属正常行为。
