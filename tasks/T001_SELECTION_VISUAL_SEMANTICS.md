# T001：统一 Selection/Current 视觉语义并收口相关 UI

## 目标

全局解决 `Current`、`Selected`、Hover、Focus 目前复用同一 Accent Surface 导致的语义冲突，并把新语义应用到所有相关列表；同时完成本轮已确认的章节标题和 Provider Popup 视觉调整。

## 权威参考

- `docs/06_UI_AND_VISUAL_SYSTEM.md`
- `docs/03_BOOKS_PLAYBACK_AND_PROGRESS.md`
- `docs/specs/HTTP_TTS.md`
- `AGENTS.md`

## 必须实现

### 1. 全局状态语义

统一共享 Selection 资源，不在 Feature 内各自造近似状态：

- Current：左侧约 3 DIP Accent rail；不再使用 Selected 背景/Accent 边框表示 Current。
- Hover：浅色 `Interaction.Surface.Hover`。
- Selected：新增 `Interaction.Surface.Selected` / `Selected.Hover` 中性背景。
- Focus：现有 Focus brush/chrome。
- Current+Selected：Accent rail + Selected background。
- Current+Selected+Focus：再叠加 Focus border。
- Pressed/Hover 不得抹掉 Current/Selected 事实。

采用长期文档中的 Light/Dark 色值，并为 High Contrast 提供可区分的系统资源映射。

审计并调整至少：

- Playback 章节列表与主动缓存选择；
- BookDetails 章节目录；
- SpeechServices Provider 列表；
- 播放页 Provider 选择 Popup；
- Chapter/Regex Rule 工作台；
- CacheManagement 现有选择；
- StyleGallery 中 Selected/Current 的示例和文案；
- 其它实际使用 `App.Selection.*` 的生产列表。

不要把所有 Feature 强行合并为一个万能控件；共享的是视觉 primitive 和稳定选择手势。

### 2. 章节标题显示

- 删除 BookDetails 目录中基于 `ChapterIndex + 1` 生成的“第 N 章”展示。
- 审计 Player、CacheManagement、Export 等用户可见章节列表，确保只显示 `Chapter.Title`。
- 内部 ChapterIndex、排序、跳转、缓存和进度逻辑不变。
- 系统生成的“第 N 节”属于真实 Chapter.Title，正常显示。

### 3. Provider Popup

保持现有功能，只优化视觉：

- Provider 行 MinHeight 约 50 DIP，水平 Padding 约 16 DIP；
- 使用 ItemTitle 或同等级字体，垂直居中；
- 长名称 CharacterEllipsis；
- 列表间距保持紧凑；
- 多项时内部滚动；
- CurrentProvider 使用新的 Current rail；
- 底部改为 Divider + 图标 +“语音服务管理”+ Chevron 的导航行；
- 遵守 Single Surface，不新增嵌套 Card。

## 不要做

- 不修改 Provider 选择业务逻辑。
- 不引入 Check 图标作为全局 Selected 的必要条件。
- 不用 Accent border 同时承担 Selected 和 Focus。
- 不为每个页面创建独立 Current/Selected 色值。
- 不因视觉整理重构无关 ViewModel/Controller。

## 验收

自动检查至少覆盖：

- 现有核心 UI/WPF 测试继续通过；
- Current 与 Selected 的共享 Style 不再映射到同一 Accent background/border 语义；
- Provider Popup 可以真实实例化并打开；
- ChapterIndex 不再生成用户可见“第 N 章”文案。

允许使用 StyleGallery/临时截图验证 Light/Dark 的 Rest、Hover、Selected、Current、Current+Selected、Focus；完成后删除临时截图、脚本和临时测试。不要新增精确像素永久测试。

阶段完成前执行完整质量门禁。
