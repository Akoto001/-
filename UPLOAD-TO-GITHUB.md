# 上传到 GitHub —— 两种做法

仓库内容就是本目录（`GeometryViewer-OSS`）。里面已经准备好 `README.md`、`LICENSE`（MIT）、
`.gitignore`（排除了 `bin/ obj/ dist/ .build/ *.keystore` 等）、`CHANGELOG.md`、`SIGNING.md`。

体积提醒：编译好的 `GeometryViewer.exe`（约 66 MB）和 `GeometryViewer-Android.apk`（约 35 MB）
**不要**放进仓库源码里，两个都塞进去会让仓库变得很重。发布方式见文末「发版」。

---

## 方法一：网页拖拽上传（不装任何软件，5 分钟）

1. 打开 https://github.com/new
   - Repository name：`GeometryViewer`（或你喜欢的名字）
   - 选 **Public**（开源）或 Private
   - **不要**勾选 “Add a README file”“Add .gitignore”“Choose a license”（我们已经有）
   - 点 **Create repository**
2. 在新仓库页面点 **uploading an existing file**（或地址栏直接进
   `https://github.com/<你的用户名>/GeometryViewer/upload/main`）
3. 把本目录里的 **文件与文件夹整体拖进浏览器**（`windows`、`android`、`docs`、
   `README.md`、`LICENSE`、`.gitignore`、`CHANGELOG.md`、`SIGNING.md`、`PUBLISH.md`、`sign.cmd`）
   - 注意：`.gitignore` 是隐藏文件，在资源管理器里要勾「查看 → 隐藏的项目」才看得见；
     拖不进去也不致命，可以之后在网页上新建同名文件把内容粘进去。
4. 下方 Commit changes 填一句说明，点 **Commit changes** 即可。

网页法适合只上传一次。以后每次改动都要重新拖，比较麻烦，长期维护建议用方法二。

---

## 方法二：命令行（推荐，可长期更新）

### 第一步：装 Git（本机目前没有）

```powershell
winget install --id Git.Git -e --source winget
```
装完**关掉并重开**终端，`git --version` 能打印版本即成功。
（如果它要求管理员权限而你没有，改用 Git for Windows 的 per-user 安装包：
https://git-scm.com/download/win ，安装时选 “Use Git from Windows Command Prompt”。）

### 第二步：先在 GitHub 建空仓库

同方法一第 1 步，创建时不要初始化任何文件。建完页面会给出仓库地址，例如
`https://github.com/akoto/GeometryViewer.git`。

### 第三步：在本机提交并推送

在 `GeometryViewer-OSS` 目录里执行（把地址和邮箱换成你自己的）：

```powershell
cd "C:\Users\Akoto\Documents\deepseek-harness\default-workspace\GeometryViewer-OSS"

git init -b main
git config user.name  "你的名字"
git config user.email "你的邮箱"

git add .
git status                    # 确认没有 bin/ obj/ dist/ *.keystore 被加进来
git commit -m "首次提交：几何体检视器源码"

git remote add origin https://github.com/<你的用户名>/GeometryViewer.git
git push -u origin main
```

`git push` 时会弹出登录窗口：用**浏览器授权**（Sign in with your browser）最省事；
也可以提前在 GitHub 生成 Personal Access Token，密码位置填这个 token。

### 之后每次改动

```powershell
git add .
git commit -m "说明这次改了什么"
git push
```

---

## 发版（把 exe / apk 放出去）

不要提交二进制到仓库，用 **Releases**：

1. 先打标签并推送：
   ```powershell
   git tag -a v1.9.4 -m "v1.9.4"
   git push origin v1.9.4
   ```
2. 打开 `https://github.com/<你的用户名>/GeometryViewer/releases/new`
   - Choose a tag：选刚推的 `v1.9.4`
   - Release title：`几何体检视器 v1.9.4`
   - 描述可以粘贴 `RELEASE_NOTES.md` 的内容
   - **Attach binaries**：拖入 `GeometryViewer.exe` 和 `GeometryViewer-Android.apk`
     （单个附件上限 2 GB，出问题的只会是仓库体积，Release 附件不影响仓库大小）
3. 点 **Publish release**

想让别人校验下载是否完整，可以把 `SHA256SUMS.txt` 一起作为附件传上去：

```powershell
Get-FileHash .\GeometryViewer.exe -Algorithm SHA256
Get-FileHash .\GeometryViewer-Android.apk -Algorithm SHA256
```

---

## 常见问题

- **推送要密码？** GitHub 早已不接受账户密码，必须用 Personal Access Token，或直接用浏览器授权。
- **`git push` 报 `src refspec main does not match any`**：说明还没提交，先 `git add .` + `git commit`。
- **中文文件名乱码**：执行一次 `git config --global core.quotepath false`。
- **想改仓库名/描述**：仓库页 Settings → 顶部 Rename / Description。
- **想让别人也能装安卓包**：Release 里放 APK 即可；未签名或自签名的 APK 安装时手机可能提示风险。
