<p align="center">
  <img src="docs/assets/branding/logo.png" alt="NovelSpeaker logo" width="180" />
</p>

<h1 align="center">NovelSpeaker</h1>

<p align="center">面向 Windows 10/11 的本地 TXT 小说听书应用。</p>

<p align="center">
  <a href="https://github.com/Drunk-Dream/NovelSpeaker/actions/workflows/ci.yml"><img src="https://github.com/Drunk-Dream/NovelSpeaker/actions/workflows/ci.yml/badge.svg" alt="CI" /></a>
  <a href="https://github.com/Drunk-Dream/NovelSpeaker/releases/latest"><img src="https://img.shields.io/github/v/release/Drunk-Dream/NovelSpeaker?display_name=tag" alt="Latest release" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-GPL--3.0--or--later-blue.svg" alt="GPL-3.0-or-later" /></a>
  <img src="https://img.shields.io/badge/platform-Windows%2010%2F11-0078D4" alt="Windows 10/11" />
  <img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET 10" />
</p>

NovelSpeaker 专注于一条清晰可靠的链路：导入本地 TXT 小说，选择 HTTP TTS 规则，完整下载音频后连续播放，并在本地缓存音频与阅读进度。它不是通用电子书阅读器，也不抓取在线小说。

## 下载与安装

从 [Releases](https://github.com/Drunk-Dream/NovelSpeaker/releases/latest) 下载 `NovelSpeaker-vX.Y.Z-win-x64.zip`，解压到任意有写入权限的目录后运行 `NovelSpeaker.App.exe`。

- 适用系统：Windows 10 22H2+ 或 Windows 11，x64。
- 发布包是自包含的，不需要另装 .NET Runtime。
- 未进行代码签名；首次运行时 Windows SmartScreen 可能提示，请仅从本仓库 Release 下载。

应用数据保存在 `%LocalAppData%\NovelSpeaker`；卸载或删除程序目录不会自动删除书籍、缓存和设置。

## 快速开始

1. 在书库页点击“导入小说”图标，选择本地 TXT。常见编码会直接导入，无法确定时再选择编码。
2. 在“设置 → TTS 规则”点击新建或导入图标，导入或新建兼容 Legado 风格的 HTTP GET/POST 规则，并使用“试听”验证。
3. 打开书籍后开始播放；目录可跳章，播放页可跳段、调节语速和恢复当前位置。
4. 在“设置 → 导入与文本”管理章节规则和正则替换；在“缓存与数据”查看或清理缓存。

## 界面预览

| 书库 | 播放 |
| --- | --- |
| ![书库截图](docs/assets/screenshots/bookshelf.png) | ![播放截图](docs/assets/screenshots/player.png) |

| TTS 规则 | 设置 |
| --- | --- |
| ![TTS 规则截图](docs/assets/screenshots/ttsRules.png) | ![设置截图](docs/assets/screenshots/settings.png) |

## 主要能力

- 本地 TXT 导入、编码检测、章节识别、文本分段和进度恢复。
- Legado 风格 HTTP TTS 规则：GET、POST JSON、POST Form、Header、Body、模板变量和受限 JavaScript 表达式。
- 完整下载后播放、后续段落预取、NAudio 本地播放与本地音频 LRU 缓存。
- 全局正则替换：分别处理展示文本和朗读文本，不需要重新导入小说。
- 深色、浅色和跟随系统主题；键盘快捷键与基础可访问性支持。
- “诊断与关于”提供日志目录、脱敏诊断摘要和第三方许可证入口。

## 隐私与安全

TTS 规则可能包含服务凭据。NovelSpeaker 会对常规日志、错误摘要和诊断摘要中的 Header、Cookie、Token、LoginInfo、正文类字段进行脱敏；不要在截图、Issue 或日志中主动分享规则原文或凭据。

当前版本不提供 SecretStore：规则中的敏感值存储在本地 SQLite 中，尚未进行静态加密。规则脚本由受限 Jint 引擎执行，不能访问 CLR、文件系统、进程或反射。

## 快捷键

| 快捷键 | 操作 |
| --- | --- |
| `Ctrl+O` | 导入 TXT 小说 |
| `Space` | 播放/暂停（仅播放页） |
| `Ctrl+Left` / `Ctrl+Right` | 上一段 / 下一段（仅播放页） |
| `Ctrl+Shift+Left` / `Ctrl+Shift+Right` | 上一章 / 下一章（仅播放页） |
| `Alt+Left` / `Esc` | 返回或关闭当前临时界面 |
| `Ctrl+,` | 打开设置 |

文本输入、下拉框、菜单和对话框打开时不会触发应用级快捷键。

## 当前限制

- 仅支持 TXT；不支持 EPUB、PDF、MOBI 和在线书源。
- 仅支持 HTTP GET/POST TTS，不支持 WebSocket 和真正的边生成边播放。
- 当前不支持会话 Cookie、Cookie 持久化、LoginInfo、`jsLib` 或复杂 `source.get/put` 规则。
- 不提供用户账户、云同步、语音克隆、有声书导出、插件市场、自动更新或 Windows 本地 TTS 回退。
- 首发版本未进行代码签名，且规则敏感值尚未静态加密。

## 本地开发

需要 Windows、.NET SDK `10.0.301`（由 `global.json` 固定）和 x64 环境。

```powershell
dotnet restore --locked-mode -r win-x64
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
dotnet run --project src/NovelSpeaker.App
```

仓库级 `RuntimeIdentifiers` 会让命令行和 IDE 的隐式还原都保留 `win-x64` 锁文件目标；日常启动不需要额外指定 `-r` 或 `--no-restore`。

## 架构与文档

项目使用 C#、.NET 10、WPF、CommunityToolkit.Mvvm、Microsoft.Data.Sqlite.Core、SQLitePCLRaw.bundle_winsqlite3、Jint 和 NAudio。业务逻辑不写在 code-behind；ViewModel 不直接访问 HTTP 或 SQLite；规则引擎、播放器和文本切分保持独立边界。

详细设计见 [docs/README.md](docs/README.md)。

## 许可证

NovelSpeaker 以 [GPL-3.0-or-later](LICENSE) 发布。第三方组件及其许可证见 [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt)。
