# T006：建立 Speech Provider 核心模型、持久化与 Runtime 边界

## 目标

把现有“HTTP TTS Rule 是完整语音后端”的模型替换为 Provider Type + Provider Instance，并建立后续 HTTP/Edge 共用的稳定 Provider Runtime 边界。

## 需要先做的代码审计

先读取当前 `Speech`、`Playback`、`Cache`、`Settings` 与 SQLite migration 相关实现，定位：

- `HttpTtsRule` / repository / selection / runtime 的真实依赖面；
- `SelectedTtsRuleId` 的所有读写位置；
- `TtsRuleFingerprint` / `SynthesisProfileFingerprint` / `SynthesisProfiles` 与 AudioCache 的关系；
- 当前规则列表实际排序来源；
- 现有 migration 能否安全一次性转换 HTTP TTS Rule。

不要在审计前先复制旧模型再加一层 `Provider` wrapper。

## 必须实现

- 建立 Provider Type 与 Provider Instance。
- Provider 公共身份至少包含 ProviderId、ProviderType、Name、SortOrder 与必要持久元数据。
- Provider 名称全局唯一且大小写不敏感；保留固定名称 `Microsoft Edge`。
- CurrentProvider 是全局 `ProviderId?`，无 Enabled 状态，无 fallback。
- Provider typed config 不使用万能 Domain/Application JSON 字典。
- 建立 Provider Runtime/Resolver，让上层后续只面向 Provider，不直接依赖 HTTP/Edge 具体 transport。
- 建立类型负责的 `ProviderSynthesisFingerprint` 边界；ProviderId、Name、SortOrder、HTTP RateLimit 等不得进入合成指纹。
- `SelectedTtsRuleId` 迁移为 `CurrentProviderId`。

## 数据迁移

新增 append-only SQLite migration，对当前 `HttpTtsRules` 逐项进行一次性转换；只迁移能按新 HTTP Provider 合同安全表达的配置，不建立旧格式运行时兼容。

建议目标形态：

```text
SpeechProviders
+ HttpSpeechProviderConfigs
+ 后续 EdgeSpeechProviderConfigs
```

不要用万能 `ConfigJson` 代替 typed Application/Domain model；Infrastructure 层允许使用 JSON 作为局部持久化技术细节，但不能泄露成业务合同。

迁移原则：

- 尽量保留旧 HTTP Rule 名称和有效请求配置。转换后必须通过新 Provider 的本地校验；依赖已移除的 `source` / `java.*`、无法按新 Body / Header 语义等价表达或数据损坏的项应跳过，不以 compatibility 字段或旧编译器继续执行。
- 旧表不保证名称唯一；按旧管理页稳定查询顺序处理，遇到大小写不敏感的重名时生成确定性的唯一名称，不因此跳过可转换项。
- 旧排序若实际不存在稳定 SortOrder，则按旧管理页当前稳定查询顺序生成新 SortOrder。
- 旧 `IsEnabled` 不进入新 Provider 状态；原本禁用但可转换的规则会成为普通 HTTP Provider，但不自动成为 CurrentProvider。
- 旧 SelectedTtsRuleId 只有在对应规则能够成功迁移并原本可供播放时才映射为 CurrentProvider；否则为 None。
- 不为 LastUpdateTime、旧 Legado 兼容状态建立新字段。
- 升级后向用户展示一次迁移结果：成功和跳过数量，以及每个跳过项的旧规则标识和简短原因。报告只服务于本次升级，不成为长期旧规则查询或恢复接口；日志、遥测和诊断中仅可记录不含配置内容的汇总与原因码，不记录 URL、Header、Body 或凭据。
- migration 后旧表是否保留由 SQLite 安全与简洁性决定；运行代码不得继续双读/双写旧表。
- 不建立 Old/New/V2/Compat forwarding wrapper。

## 与现有 Cache 的关系

本任务只建立新的 fingerprint 边界，不要求强制删除旧缓存。

如果保持旧 HTTP 有效请求合同 fingerprint 的成本低且语义仍正确，可以复用算法；不要为了兼容旧内部类型保留旧公共 API。

## 自动验收

永久测试只增加/更新核心边界：

- migration 后有效旧 HTTP 配置成为 HTTP Provider；
- 重名规则确定性改名；不可转换项跳过并产生用户可见的迁移结果；原本禁用的有效规则不成为 CurrentProvider；
- CurrentProvider 映射正确；
- Provider 名称唯一与 SortOrder 持久化；
- Provider Runtime 能按 Type 解析实例，缺失/不可用 Provider 安全返回不可用状态；
- rename/reorder 不改变 ProviderSynthesisFingerprint，真实合成配置变化会改变。

可以建立临时 migration/映射测试，任务完成前必须删除不具长期价值的测试。

至少运行：

```powershell
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
```

以及所有受影响的 migration / Speech / Settings focused tests。

## 完成要求

- 更新 `TASK_BACKLOG.md` 状态和简短完成成果；
- 删除临时测试、fixture、脚本；
- 删除本任务文件；
- 不等待人工验收。
