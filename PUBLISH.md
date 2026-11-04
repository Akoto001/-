# 发布指南 / Publish guide

本仓库已经整理成可直接上 GitHub 的形态。**代码与许可证已全部就绪，只差推送**（需要你自己的 GitHub 账号授权）。

## 一、前置条件

1. 安装 Git：<https://git-scm.com/download/win>（或 `winget install Git.Git`）；
2. 在 GitHub 上建一个空仓库（**不要**勾选 README / .gitignore / LICENSE，否则会冲突），
   例如 `https://github.com/<你的账号>/geoviewer`；
3. 本地配置身份（只需一次）：

```bash
git config --global user.name  "Akoto"
git config --global user.email "you@example.com"
```

## 二、首次推送

```bash
cd GeometryViewer-OSS
git init
git add .
git commit -m "Initial commit: GeoViewer 1.7.3 — 几何体绘画参考（Windows + Android）"
git branch -M main
git remote add origin https://github.com/<你的账号>/geoviewer.git
git push -u origin main
```

推送时会要求登录：用 **Personal Access Token**（GitHub → Settings → Developer settings →
Personal access tokens → Fine-grained tokens，权限勾 `Contents: Read and write`）当密码用。

## 三、发 Release（挂二进制）

二进制**不要提交进仓库**（单文件 66 MB，仓库会迅速变大），放在 Release 附件里：

```bash
git tag -a v1.7.3 -m "1.7.3"
git push origin v1.7.3
```

然后网页上：Releases → Draft a new release → 选 `v1.7.3` → 上传 `release-1.7.3/` 里的三个文件：

| 文件 | 说明 |
|---|---|
| `GeometryViewer-1.7.3-win-x64.exe` | Windows 单文件自包含，双击即用 |
| `GeometryViewer-Android-2.1.apk` | Android（minSdk 24 / targetSdk 34） |
| `SHA256SUMS.txt` | 校验和，`certutil -hashfile <file> SHA256` 可比对 |

或使用 GitHub CLI：

```bash
gh release create v1.7.3 release-1.7.3/* \
  --title "GeoViewer 1.7.3" \
  --notes-file release-1.7.3/RELEASE_NOTES.md
```

## 四、发布后建议改的两处

1. **README 的下载表格**：把文件名换成 Release 直链，例如
   `https://github.com/<账号>/geoviewer/releases/latest/download/GeometryViewer-1.7.3-win-x64.exe`；
2. **仓库简介（About）**：建议写
   `给绘画练习用的 3D 几何体参考工具 · Windows + Android · 零依赖软件光线投射渲染器`，
   Topics 填 `drawing-reference`、`3d`、`wpf`、`dotnet`、`android`、`raytracing`、`art-tool`。

## 五、安全检查（推送前）

```bash
# 确认没有密钥、缓存、二进制混进去
git status --short
git ls-files | findstr /i "keystore jks pfx"    # 应为空
git ls-files | findstr /i "bin/ obj/ dist/"     # 应为空
```

`.gitignore` 已排除：`bin/ obj/ dist/ .build/ .dotnet8/ .android-sdk/ *.keystore *.jks *.zip`。
