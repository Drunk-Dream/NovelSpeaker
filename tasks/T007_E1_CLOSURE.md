# T007 — E1-B/E1-C 最终收口与完整门禁

- **类型/优先级：** Closure / P1。必须在 T003–T006 完成后执行；不负责追加新的产品功能或未经批准的架构重构。

## 收口核查

1. **HTTP TTS：** 真实 `HttpTtsClient` 禁止同源/跨源自动 redirect；302/307 等不会携带任何 Header/Body 发到 Location。3xx 返回既有失败路径且提示配置最终 URL，无密钥/URL Query 泄漏。`docs/specs/HTTP_TTS.md` 与已批准方案 A 一致；不保留手动重定向、allowlist 或绕过开关。
2. **后台任务启动：** Active Cache/Export 的同步/异步 observer 异常不能破坏 batch start/completion、取消、重试和 Dispose；progress snapshot 与真实结果一致；没有新增“为了兼容旧流程”而保留的双完成路径。
3. **退出生命周期：** Active Cache 纳入进程主动取消与有界排空；对合作/非合作 Provider 都有明确退出结果与限制；应用仍能有界退出，不误报 Cache 完成；页面离开不取消已接受的 Active Cache/Export；正常 Playback、Export、Repair 和诊断退出行为保留。
4. **WPF 虚拟化：** CacheManagement 书目选择器在 1k/10k 合成列表下真实容器数与 viewport 有关而不是全量 materialize；滚动定位、选择、主题、重新激活正常；10k 章节优化链路未被破坏。
5. **做减法/债务：** 只清除本轮产生的冗余保护、无用包装、重复 helper 和临时测试；不要因为审计候选存在就额外改 D001/D007/D008 或推翻成熟模块。对相关未实施候选在完成记录中简述“未授权/暂缓”即可，不复制整份报告。
6. 若发现已批准设计与实现有重大冲突、存在新高风险漏洞或需要改用户持久化语义，标记 `Needs Decision` 并提交证据，不能擅自选新架构；能在本轮已批准范围内修复的回归由 Codex 完成。

## 完整质量门禁

按 `AGENTS.md` 执行：

```powershell
dotnet restore --locked-mode -r win-x64
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

另做 `git diff --check`、LF 检查、直接/隐式旧 redirect 路径检查、无额外 schema migration、无临时测试/脚本/截图/trace、没有改动用户真实数据。必要时用受控合成数据/loopback 重新跑安全与生命周期关键用例；不使用真实 API Key 或外部网络端点。所有未执行/失败须有原始原因，不允许删测试、降安全约束或假报绿色。

## Codex 任务结果

- 在 `TASK_BACKLOG.md` 标记 T007 `[x]` 并记录总体成果、被删除的多余结构、关键测试/构建执行结果、任何环境限制和遗留风险；删除本任务规格。
- 已验证的重要设计合同由正式 `docs/` 维护；不把报告、性能图片、临时 harness 上传到 Git/资料库。
- 阶段结束后仅向用户说明成果及可选人工 UI 复核事项；U1 视觉设计由用户与 ChatGPT 另行讨论，**不要自动开启 U1 改动**。
