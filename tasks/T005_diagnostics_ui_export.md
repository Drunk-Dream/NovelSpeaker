# T005 — 诊断悬浮控制条、主动截图与问题诊断导出

## 目标

把 T004 的诊断会话核心变成可由普通用户完成“准备 → 开始 → 复现 → 标记/截图（可选）→ 结束 → 导出”的完整产品能力，同时保持 UI 简单，不建设 Session 管理器。

## 必读

- `AGENTS.md`
- `TASK_BACKLOG.md` 中 T005
- `docs/02_RUNTIME_AND_NAVIGATION.md`
- `docs/05_DATA_AND_COMPATIBILITY.md`
- `docs/06_UI_AND_VISUAL_SYSTEM.md`
- `docs/07_OBSERVABILITY_AND_DIAGNOSTICS.md`
- `docs/08_QUALITY_AND_TESTING.md`
- T003/T004 完成后的代码

## 必须实现

### 1. Settings 入口

提供轻量“问题诊断”入口，至少支持：

- 打开诊断工具；
- 导出已有 `.nsdiag`；
- 打开诊断目录。

优先使用与现有 Settings 一致的简洁布局/Icon Button，不建设 Session 列表、状态 Dashboard 或复杂管理页。

### 2. 悬浮控制条

这是独立悬浮控制条，不是设置页中的常驻状态面板。

#### 准备状态

```text
[开始诊断] [容量上限/次级设置] [关闭]
```

- 打开工具时不采集。
- 用户可以先切换到要复现问题的页面。
- 容量上限使用少量 preset，避免自由参数面板；具体值自行选择并集中维护。

#### 采集中

```text
● 诊断中 00:03:21
[标记问题] [截取当前窗口] [结束]
```

- duration 是明显录制提示即可。
- 不增加“已经录制很久”提示。
- Marker/截图都可完全不使用。

#### 完成

```text
✓ 诊断已保存
[重新开始] [导出] [完成]
```

- “重新开始”创建新的 Session、新 `.nsdiag` 并立即进入新的采集，不 reopen 旧 Session。
- “导出”导出刚结束 Session。
- “完成”关闭控制条。

达到 hard cap 时明确显示采集已停止，并允许正常结束；不要自动删除/压缩旧记录。

### 3. 主动当前窗口截图

仅允许用户在 active Session 中主动触发。

硬约束：

- 只截 NovelSpeaker 自身窗口。
- 不截整个桌面。
- 不自动截图。
- 不连续截图。
- 不录屏。
- 不在 Marker 时隐式自动截图。
- 截图可能包含正文/书名等当前 UI 内容，这是明确的用户主动隐私例外。

推荐把截图作为 `.nsdiag` 内的附件 payload/Blob + metadata 保存，使 Session 仍然是单个权威文件；如果当前 SQLite/平台实现存在明确理由采用等价单会话附件策略，可以调整，但不得留下易丢失的无关联散件。附件字节计入 Session hard cap。

截图应记录 timestamp、window/client bounds、mime/type 等必要元数据，不记录额外用户文本标签。

### 4. 问题诊断导出

立即导出和以后从 Settings 文件选择器选 `.nsdiag` 导出必须复用同一 Export Service。

生成：

```text
NovelSpeaker-Problem-Diagnostics-*.zip
├─ summary.md
├─ timeline.md
├─ session.nsdiag
├─ logs.jsonl
├─ environment.json
├─ diagnostics-schema.json
└─ attachments/
```

要求：

- `summary.md` 是客观会话摘要，不尝试推断根因。
- `timeline.md` 精简展示 lifecycle、Activity、Marker、关键 Event/Snapshot/Warning/Error；不要展开每秒资源采样。
- `logs.jsonl` 优先按 diagnosticSessionId/process/activity correlation 提取。
- `diagnostics-schema.json` 从 Registry 自动生成本 Session 实际使用定义的数据字典。
- `attachments/` 解出用户主动截图。
- 派生 Markdown/JSON 只在导出时生成，不在运行时维护第二份真值。
- 导出失败不得损坏原 `.nsdiag`。

### 5. 人工可预览 + AI 可分析

无需开发独立 Diagnostic Viewer：

- 人：优先读 `summary.md`、`timeline.md`、必要时 `logs.jsonl`/截图。
- AI/开发者：可进一步读 schema、environment 和完整 `session.nsdiag`。

### 6. 目录管理

- 已结束 `.nsdiag` 第一版不自动删除。
- 用户使用系统文件管理器管理。
- 不建设 Session list/page/database catalog。

## 不在本任务范围

- 问题说明/严重程度/复现步骤表单；
- 自动截图；
- 录屏；
- Crash Dump；
- Session 列表页面；
- 自动上传；
- 在线 backend；
- “长时间录制”弹窗；
- 动态复杂采样等级。

## 自动验收

至少覆盖：

1. 打开诊断工具不开始采集，点击 Start 才创建 Session。
2. 控制条三个状态及其转换。
3. Marker 可以多次使用且不要求使用。
4. Restart 创建新 Session。
5. 当前窗口截图必须由显式用户命令触发；无任何自动截图路径。
6. 截图只针对 NovelSpeaker 窗口，附件与 Session 正确关联。
7. hard cap 包含附件并能有界停止。
8. 立即导出和旧文件导出复用同一逻辑。
9. ZIP 文件结构、summary/timeline/schema/attachment 输出正确。
10. 导出失败不改写/损坏源 `.nsdiag`。
11. 不存在 Session Manager 页面。
12. WPF 自动测试保持隐藏 Desktop/fail closed。

自动视觉测试可使用截图自检，但生成的临时验收截图/脚本必须在任务结束前删除。人工视觉验收仅可选，永远不阻塞完成。

运行 focused tests 和完整门禁。

## 完成

自动验收通过后更新 T005 完成成果并删除本文件。
