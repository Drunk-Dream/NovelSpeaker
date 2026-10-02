# T003：迁移 Speech Provider 与 Rules 批量管理

## 依赖

T001。

## 目标

把 Speech Provider 与四类 Rules workspace 从“普通选择直接兼任 Ctrl/Shift 多选”迁移为统一 Normal Mode + Management Mode。

范围：

- Speech Provider
- Chapter Rules
- Regex Replacement Rules
- File Name Metadata Rules
- Text Header Metadata Rules

## 必读

- `AGENTS.md`
- `docs/06_UI_AND_VISUAL_SYSTEM.md`
- `docs/specs/BATCH_MANAGEMENT.md`
- `docs/05_DATA_AND_COMPATIBILITY.md`
- `docs/specs/HTTP_TTS.md`

## Normal Mode

普通 click：

- Provider → 切换右侧正在编辑对象；
- Rule → 切换右侧正在编辑对象。

现有 drag reorder / editor / save / cancel 行为保持。

## 进入 Management Mode

- Ctrl/Shift click 或显式管理入口。
- 若有 Dirty Draft，复用已有 save/discard/cancel 保护；不建立第二套 draft system。
- 用户取消 draft 离开时，本次 Management Mode 进入也取消。

## Management Mode

- 普通 click 只 toggle selection；
- 不切换 Editor 当前对象；
- Select All 只作用于当前可见/manageable items；
- selected count=0 不退出；
- search/filter/visibility 变化后隐藏项移出 selection；
- right-click 语义遵守统一 spec；
- Page Header 提供 Export / Delete / Select All / Cancel。

Provider 与各 Rule workspace 必须是独立 selection scope。

## Batch actions

### Export

- 继续复用当前正式版本化 exchange document。
- 一个 batch 输出一个文档。
- 不支持分享的 Provider（例如 Microsoft Edge）可以被选中，但 Export 时 skipped/report。
- HTTP Provider 敏感凭据提示保持现有行为。

### Delete

- 一次 batch 只确认一次。
- 单项失败继续其它项。
- 删除 CurrentProvider 被允许；删除后 CurrentProvider=None。
- 不为了维持旧状态自动切到其它 Provider。
- 删除正在编辑/当前匹配的 Rule 被允许；后续由正常 workspace/runtime 状态处理。
- 不增加“当前对象不可删”的隐藏保护。

## 不做

- 不改变 Provider typed config。
- 不改变 Rule exchange schema。
- 不增加 fallback。
- 不把 Provider 和 Rule 合成一个万能管理页面。

## 测试

核心永久测试：

- Normal click 与 Management click 职责隔离；
- Dirty Draft 保护；
- unsupported item skip；
- CurrentProvider 删除 → None；
- batch delete partial continuation；
- filter 后隐藏 selected item 不参与 action。

非核心按钮布局/视觉只做临时验证。

## 自动验收

本任务不属于 breaking migration，完成时执行标准完整门禁。

完成后更新 `TASK_BACKLOG.md`，删除本 task 文件。
