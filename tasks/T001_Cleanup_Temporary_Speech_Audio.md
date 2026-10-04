# T001：清理异常退出遗留的临时语音文件

## 目标

为应用生成的临时语音文件建立明确的残留清理边界，避免进程异常退出后，RuleTests、ProviderPreviews 或 TTS 临时路径中的合成音频无限期留存。

## 背景与证据

审计报告 F001：`TemporaryAudioStore` 在 `Cache/RuleTests` 创建 `.tmp` 及 `.mp3` / `.wav` / `.audio` 候选文件；`ProviderPreviewAudioPlayer` 在 `Cache/ProviderPreviews` 保留当前预览文件。现有 `AudioCacheFileStore.DeleteResidualTemporaryFiles` 仅清理 TTS storage key 下的 `*.tmp`。正常取消、替换和 Dispose 已有清理逻辑，异常退出会绕过这些 owner 清理。

## 范围

- 检查现有 app 单实例、启动顺序和临时文件 owner 生命周期，确认启动清理不会删除另一个仍运行实例正在使用的文件。
- 扩展或调整现有 Infrastructure 文件清理 owner，使规则测试响应、候选音频和试听音频的残留有明确、受限的清理策略。
- 保留正常成功、失败、取消、试听替换和 Dispose 路径的即时清理。
- 将启动清理限制在应用明确拥有的临时目录/文件命名范围；不得遍历或清理用户选择的目录、书籍正文或其它普通缓存。
- Clear All 不得删除仍在使用的试听/请求文件，也不得借此扩大为 Local Source 内容清理。

## 不在范围

- 不新增临时文件数据库、通用清理框架、日志/遥测记录或持久化 schema。
- 不记录路径、文件名、TTS 文本或音频内容。
- 不以固定年龄阈值替代进程/owner 生命周期判断，除非已有合同明确证明该阈值安全。

## 验收

- focused Infrastructure tests 覆盖：每类已知残留可清理；当前实例 active 文件保留；普通持久音频缓存及 Local Source 文件保留；重复清理安全；正常完成/取消/Dispose 仍删除 owner 文件。
- 现有 `TtsResponseValidatorTests`、`ProviderPreviewAudioPlayerTests` 及相关 cache maintenance tests 通过。
- 若无法证明多实例下启动清理安全，停止扩大清理范围，改为安全的 owner 命名/租约方案或记录需要的产品边界，不得冒险批量删除共享目录内容。
- 执行与风险匹配的 focused tests、format 和 Release build；清除临时 fixture。

## 长期合同

- `docs/02_RUNTIME_AND_NAVIGATION.md`：Process/Operation owner 与启动生命周期。
- `docs/05_DATA_AND_COMPATIBILITY.md`：可重建缓存和隐私边界。
- `docs/07_OBSERVABILITY_AND_DIAGNOSTICS.md`：不得把内容型诊断数据引入清理路径。

## 依赖与风险

无前置任务。主要风险是多进程共享数据根时误删活跃文件；实现前必须确认实际实例约束。
