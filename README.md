<p align="center">
  <img src="docs/assets/branding/logo.png" alt="NovelSpeaker logo" width="180" />
</p>

<h1 align="center">NovelSpeaker</h1>

<p align="center">把本地 TXT 小说变成可以持续收听的有声书。</p>

<p align="center">
  <a href="https://github.com/Drunk-Dream/NovelSpeaker/releases/latest"><img src="https://img.shields.io/github/v/release/Drunk-Dream/NovelSpeaker?display_name=tag" alt="最新版本" /></a>
  <img src="https://img.shields.io/badge/platform-Windows%2010%2F11-0078D4" alt="Windows 10/11" />
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-GPL--3.0--or--later-blue.svg" alt="GPL-3.0-or-later" /></a>
</p>

NovelSpeaker 是一款面向 Windows 的本地小说听书应用。导入 TXT 小说并配置 HTTP TTS 规则后，即可连续收听；应用会保存阅读进度，也可以提前缓存章节音频。它不内置语音服务或在线书源。

## 下载与安装

从 [最新 Release](https://github.com/Drunk-Dream/NovelSpeaker/releases/latest) 下载 `NovelSpeaker-vX.Y.Z-win-x64.zip`，解压到有写入权限的目录，运行 `NovelSpeaker.exe`。

- 支持 Windows 10 22H2 及更高版本、Windows 11，均需 x64 系统。
- 发布包已包含运行所需组件，无需另装 .NET Runtime。
- 当前程序未签名，首次运行可能出现 SmartScreen 提示。请仅从本仓库的 Release 页面获取程序。

应用数据保存在程序目录下的 `Data/`。删除程序目录会同时删除这些数据，请先备份需要保留的书籍、设置和缓存。

## 界面预览

| 书库 | 播放 |
| --- | --- |
| ![书库截图](docs/assets/screenshots/bookshelf.png) | ![播放截图](docs/assets/screenshots/player.png) |

| TTS 规则 | 设置 |
| --- | --- |
| ![TTS 规则截图](docs/assets/screenshots/ttsRules.png) | ![设置截图](docs/assets/screenshots/settings.png) |

## 开始使用

1. 在书库点击“导入小说”，选择本地 TXT 文件。应用会尝试识别编码，必要时可以手动选择。
2. 前往“设置 → TTS 规则”，新建或导入可用的 HTTP TTS 规则，点击“试听”确认可以朗读。
3. 打开书籍开始播放。可切换章节或段落、调整语速和应用内音量；下次打开时会恢复阅读进度。
4. 想提前准备音频时，在播放页选择“缓存”并勾选章节。已完整缓存的章节可在“设置 → 缓存与数据 → 缓存管理”导出为 MP3。

## 主要功能

- **整理书库**：识别 TXT 编码和章节，支持长章节目录；可按需要调整章节识别规则。
- **连续收听**：自动衔接后续段落，显示并定位当前正文；可选择朗读章节标题。
- **自定义朗读**：支持部分 Legado 风格 HTTP TTS 规则，并可分别替换显示文本和朗读文本。
- **缓存与导出**：批量缓存选中的章节，离开播放页后仍可继续；完整缓存的章节可分别导出为 MP3，已有同名文件不会被覆盖。
- **桌面控制**：支持 Windows 媒体键、系统媒体面板、迷你播放器、系统托盘和定时停止。
- **外观与操作**：提供深色、浅色和跟随系统主题，以及常用键盘快捷键。

## 快捷键

| 快捷键 | 操作 |
| --- | --- |
| `Ctrl+O` | 导入 TXT 小说 |
| `Space` | 播放或暂停（播放页） |
| `Ctrl+Left` / `Ctrl+Right` | 上一段 / 下一段（播放页） |
| `Ctrl+Shift+Left` / `Ctrl+Shift+Right` | 上一章 / 下一章（播放页） |
| `Ctrl+A` | 全选章节（选择缓存章节或管理缓存时） |
| `Alt+Left` / `Esc` | 返回或关闭当前临时界面 |
| `Ctrl+,` | 打开设置 |

在文本框中输入，或打开菜单、下拉框、弹出面板和对话框时，应用级快捷键不会触发。

## 遇到问题时如何反馈

请通过 [GitHub Issue](https://github.com/Drunk-Dream/NovelSpeaker/issues/new) 描述问题，并提供应用版本（“设置 → 诊断与关于”）、Windows 版本、复现步骤、预期与实际结果，以及发生的大致时间。“诊断与关于”中的“复制脱敏诊断摘要”可帮助补充环境信息。

遇到可以复现的问题时：

1. 在“设置 → 诊断与关于”点击“打开问题诊断工具”，选择容量上限，再点击“开始诊断”。
2. 重现问题，在问题发生前后点击“标记问题”。需要记录界面时，可自行点击“截取当前窗口”。
3. 点击“结束”并“导出”，将问题诊断包保存为 ZIP。检查内容后，如愿意可附在 Issue 中。

诊断会话在应用重启后可以继续；如果达到容量上限，结束后选择更大容量重新开始。若当时未导出，可从“诊断与关于”打开诊断目录，再使用“导出问题诊断包”选择对应的 `.nsdiag` 文件。对于偶发卡顿，可开启“性能遥测”，使用一段时间后点击“导出诊断信息”；关闭遥测后仍可导出已有信息。

诊断信息只保存在本地，不会自动上传。主动截取的窗口可能包含小说内容；分享诊断包前请检查截图和其他内容，不要在公开 Issue 中粘贴 TTS 凭据、规则原文或私人小说正文。

## 数据与隐私

NovelSpeaker 不会修改导入时选取的原始 TXT 文件。书籍副本、阅读进度、规则、设置、缓存和诊断数据保存在本地应用数据目录中。

朗读时，需要将待合成的文本发送给你配置的 HTTP TTS 服务，请选择信任的服务。性能遥测默认关闭；日志和诊断信息不会自动上传。TTS 规则中的凭据目前保存在本地应用数据中，尚未提供独立的加密凭据存储。

## 当前限制

- 仅支持本地 TXT，不支持 EPUB、PDF、MOBI 或在线小说书源。
- HTTP TTS 只兼容部分规则；语音由所配置的服务生成，音频需完成当前段落生成后才开始播放。
- 同一时间只能运行一个主动缓存任务，任务进度不会在应用重启后恢复。
- MP3 导出只使用当前规则、语速和文本处理配置下已完整缓存的章节，不会自动补全缺失音频。
- 系统媒体面板的上一项、下一项对应上一段、下一段；章节切换请在应用内操作。
- 暂不提供账户、云同步或自动更新。

## 许可证

NovelSpeaker 以 [GPL-3.0-or-later](LICENSE) 发布。第三方组件及许可证见 [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt)。
