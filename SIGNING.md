# 为什么 Windows 会提示「已保护你的电脑」，以及怎么根治

## 一、提示到底是什么

那是 **Microsoft Defender SmartScreen**，不是杀毒报警。它拦的是一件事：

> 这个 exe **没有可信的数字签名**，而且「下载量 / 使用人数」还不足以进入微软的信誉白名单。

它在你双击后的行为取决于文件上的 **Mark-of-the-Web（MOTW）**：从浏览器、聊天软件、邮件里下载的文件会被
NTFS 附加一个 `Zone.Identifier` 备用数据流，SmartScreen 就照着这个流去查信誉。

查看有没有 MOTW：

```powershell
Get-Item .\GeometryViewer.exe -Stream *
```

只有 `:$DATA` → 没有 MOTW，正常情况下不会弹提示；
多出 `Zone.Identifier` → 有 MOTW，会按信誉决定拦不拦。

## 二、用户侧的三个办法（立刻可用，但都是绕过）

| 办法 | 操作 | 适用 |
|---|---|---|
| 解除锁定 | 右键 exe → 属性 → 底部勾「解除锁定」→ 确定 | 最干净，一次即可 |
| 仍要运行 | 弹窗点「更多信息」→「仍要运行」 | 没有「更多信息」时说明是【智能应用控制】在拦，需去 设置 → 隐私和安全性 → Windows 安全中心 → 应用和浏览器控制 → 智能应用控制 → 关闭 |
| 启动器 | 双击仓库里的 `启动.cmd`，它会 `Unblock-File` 后再启动 | 分发给别人时最省事 |

这三个办法**都不改变信誉**，换一台电脑、换一个下载渠道还会再弹。

## 三、真正根治：给 exe 做代码签名

SmartScreen 只认「受信任的证书颁发机构签发、且证书与发布者匹配」的签名。四条现实路径：

| 方案 | 价格 | 门槛 | SmartScreen 效果 |
|---|---|---|---|
| **Azure Trusted Signing** | 约 $9.99/月 | 组织需有 **3 年可验证经营历史** + 营业执照验证；个人版（Public Trust）目前门槛更低但仍在预览 | 签完即有信誉，最推荐的低成本合规方案 |
| **OV 代码签名证书**（Sectigo / DigiCert 等） | $200–400/年 | 需要**企业主体**、电话/邮件核验；证书装在 HSM 或云签名服务里 | 需要累积下载量才逐步变白，初期仍可能提示 |
| **EV 代码签名证书** | $250–600/年 | 同上 + 更严格的身份核验，必须用硬件 token | **签完立刻**获得信誉（这是 EV 的主要卖点） |
| **Microsoft Store 上架** | 个人开发者 $19 一次性 | 需要 MSIX 打包 + 商店审核 | 商店分发的包不会有这个提示 |

> **自签名证书没用**，而且更糟：根证书不在用户机器上，SmartScreen 会当成「未知发布者」，
> 甚至比不签名更容易被拦。不要走这条路。

## 四、不花钱、但确实能减轻的做法

1. **积累下载信誉**：把 exe 放到 GitHub Releases 并长期稳定分发。SmartScreen 的信誉与「这个具体文件哈希被多少人下载过」相关，用户越多越容易变白（通常需要几百到上千次下载，时间不保证）。
2. **提交微软分析**：<https://www.microsoft.com/en-us/wdsi/filesubmission> 选「Software developer」上传 exe，可解决**被误报为恶意软件**的情况（注意：这不会让 SmartScreen 立刻放行未签名程序）。
3. **提供 SHA256 校验和**，并在 README 说明「首次运行可能提示，属未签名新程序的正常现象」——降低用户恐慌。
4. **用 zip 分发 + 启动器**：`启动.cmd` 里先 `Unblock-File` 再 `start` exe，用户只需要双击一个 `.cmd`（本仓库已包含这种做法）。
5. **发到 winget 社区仓库**：`winget install <id>` 的用户群体里，命令行安装不会触发 SmartScreen。

## 五、拿到证书之后怎么签（本仓库的接入点）

1. 把证书导出为 `.pfx`（或使用 Azure Trusted Signing 的签名服务）；
2. 编辑 `sign.cmd`，填入证书路径与时间戳服务器；
3. 发布前执行：

```bat
sign.cmd dist\GeometryViewer.exe
```

签名后用下面命令确认（应输出 `Successfully verified` 并且有 `SignerCertificate`）：

```powershell
Get-AuthenticodeSignature .\GeometryViewer.exe | Format-List Status, SignerCertificate
```

## 六、本项目当前的实际状态（诚实说明）

- 分发的 `GeometryViewer.exe` **未签名**，所以首次在别的电脑上运行会弹 SmartScreen 提示；
- 仓库与 Release 附件里**不包含任何证书或私钥**（`.gitignore` 已排除 `*.pfx *.keystore *.jks`）；
- 因此现在的正确说法是：「提示是未签名的必然结果，按上面第二节解除锁定即可安全运行；要彻底消除，需要购买证书或上架应用商店」。
