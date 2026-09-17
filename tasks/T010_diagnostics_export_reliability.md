# T010 — 统一诊断导出与失败边界

## 目标

统一普通诊断信息与问题诊断的 ZIP 生成/提交策略，修复导出链路脆弱性，并让诊断系统自身失败留下可定位但脱敏的生产日志。

## 必须完成

### 1. 导出职责

抽取共享的 Bundle/atomic-output 基础设施：

- 接收用户通过 SaveFileDialog 选择的最终目标路径；
- 在目标同目录创建唯一临时文件；
- ZIP 完整生成并关闭后原子 Move/Replace 到最终目标；
- 失败清理临时文件；
- 不把内部 data root 限制错误应用到用户导出目标。

普通诊断和问题诊断不得各自复制一套临时文件/提交/覆盖逻辑。

### 2. 文件名

默认建议文件名使用本地时间：

- `NovelSpeaker-Diagnostics-yyyyMMdd-HHmmss.zip`
- `NovelSpeaker-Problem-Diagnostics-yyyyMMdd-HHmmss.zip`

时间获取使用可注入 TimeProvider 或独立命名服务，便于测试。

### 3. 日志读取

- 正在写入、轮转、已删除或单个损坏日志文件采用 best effort。
- 单文件失败不得导致整个诊断 ZIP 失败。
- 可以在 summary/environment 中记录有限 degraded 状态，但不得写完整路径。

### 4. 诊断系统自身日志

为至少以下用户可感知失败建立稳定 LogEvent/Failure boundary：

- 普通诊断导出；
- 问题诊断导出；
- Session start/recover/end；
- Problem Marker；
- 主动截图。

日志包括 operation/stage、异常 type、HRESULT、脱敏异常链；禁止完整路径、书名、正文、URL、token。

### 5. 当前性能导出故障

不要根据现有通用 UI 错误提示猜测根因并增加特例。先完成导出基础设施和自诊断日志。如果重构后仍可复现，再由新增日志建立新的独立修复任务。

## 自动验收

- 导出到 data root 外任意临时目录成功。
- 最终目标已有文件时覆盖语义明确且无半文件。
- ZIP 构建中 fault injection 后最终目标保持原文件/不存在，临时文件被清理。
- 日志正在写入/轮转时普通诊断导出成功。
- 单个 malformed/unreadable 日志文件不阻塞 ZIP。
- 默认建议文件名包含本地时间戳。
- 导出失败产生脱敏结构化生产日志。
