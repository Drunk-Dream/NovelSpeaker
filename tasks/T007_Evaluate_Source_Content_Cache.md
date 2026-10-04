# T007：评估 SourceContentReader 的整本正文缓存收益

## 目标

量化 `SourceContentReader` 进程级保留最近整本正文的内存成本和重复章节读取收益，并据此决定保留现状或安排有界生命周期改进。

## 背景与证据

审计报告 D002：reader 注册为 singleton，读取章节时加载完整规范化 source string，并将 `_cachedText` 保留到另一个 source 覆盖。单条缓存可控制条目数，但不限制正文字节数；T026 的导入测量范围不等同播放内存收益。

## 范围

- 追踪所有 reader 调用场景、实例生命周期和缓存命中条件。
- 使用脱敏合成文本测量代表性 source 大小下的内存保留及重复章节读取 I/O/CPU；区分导入管线和播放路径。
- 基于结果决定保留、缩短缓存生命周期或提出最小有界实现；如建议改代码，另立实施任务或明确任务 spec 更新后再做。

## 验收

- 提供可复现的测量方法、数据规模、缓存命中/未命中对比及结论；不得使用小说正文或将源文本/完整路径写入日志。
- 不因“singleton 不理想”直接改变注册生命周期；验证任何建议不会破坏并发章节读取和 cancellation。
- 本调查不引入长期基准框架，不改持久化数据；临时 harness/sample 完成后清理。
- 如现有收益无法量化，结论为保留现状并记录不确定因素。

## 长期合同

- `docs/01_SYSTEM_ARCHITECTURE.md` 第 4、5 节：状态 owner 与生命周期边界。
- `docs/02_RUNTIME_AND_NAVIGATION.md` 第 6、7 节：异步边界和播放 session owner。
- `docs/05_DATA_AND_COMPATIBILITY.md` 第 3、6 节：Source 正文权威来源与导入语义。
- `docs/08_QUALITY_AND_TESTING.md` 第 4、9 节：临时验证清理和异步/压力测试。

## 依赖与风险

无前置任务。该项是证据收集任务，不预设必须删缓存。
