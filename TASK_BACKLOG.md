# NovelSpeaker 当前开发 Backlog

## 1. 当前阶段：Stabilization & Refinement — E1-B 实施与 E1-C 收口

- **决策基线：** 2026-10-11，`dev` @ `2c46304b4eece3f346403d435155f78c48aa9f05`。远端 Git 仍为此提交；E1-A T001/T002 完成信息来自用户交付的仓库外临时报告。Codex 执行时以**本地实际 HEAD、工作区改动**为准，不能将本地未提交的 E1-A 收口覆盖掉。
- **已完成研究：** E1-A T001 全项目广覆盖静态审计；T002 对高风险和复杂度候选进行隔离动态验证。两轮临时报告由用户手动管理，**不提交 Git/不迁入项目 docs**。20 个领域的初审尚有部分覆盖缺口，E1-A 收口不等于项目通过逐行审计。
- **本轮定位：** 只实施已由用户与 ChatGPT 筛出的四项具体问题，以及用户于 2026-10-13 插入的 Edge 连接协议修复；不是新的全项目重构，也不开始在线书源功能。工程专项结束后转 U1 全局视觉与交互体系设计。
- **已批准的产品决策：HTTP Provider 完全禁止自动重定向（方案 A）。** 任意重定向 Location 均不能自动发出后续请求，包括同源；已有依赖 redirect 的规则需用户自行改为最终 URL。不增加允许重定向的开关、跨源例外、手动跟随链或凭据过滤框架。该合同在 T003 同步写入 `docs/specs/HTTP_TTS.md`。
- **其它三项授权：** 修正 Active Cache / Export 启动通知异常导致的 stuck slot；把 Active Cache 纳入现有进程退出取消/有界排空路径；将 CacheManagement 的书目选择器替换为真正支持虚拟化的 WPF 列表，不重做视觉体系。
- **长期边界：** 保护正式用户数据、本地 TXT、Book/Source/CurrentCatalog、Playback target-first、背景缓存跨页面运行和已有正常用户行为。只做当前问题所需的最小设计变化，避免新通用抽象/Compat/V2 层。详细合同见对应 `tasks/` 文件，`AGENTS.md` 及有关正式 `docs/`。

## 2. 执行规则

- `[ ]` 未开始；`[-]` 进行中；`[x]` 已完成；`[!]` 仅表示遇到超出授权边界、必须由用户与 ChatGPT 决定的阻断问题。
- **默认串行 T008（插入修复）→ T003 → T004 → T005 → T006 → T007。** T004 与 T005 涉及同一后台 job 生命周期，先处理启动完成责任再接退出 drain。每个任务结束应保持可构建、可测试；本轮不启用 staged breaking migration window。
- Codex 只负责实施已经确定的行为和局部技术判断，不自行修改 Provider 兼容策略、模块所有权、用户数据格式、视觉风格或功能范围。非关键低层实现细节由 Codex 选择。发现超出合同的设计冲突，记录 `Needs Decision` 及证据，继续可独立执行的工作。
- 永久测试只保护安全边界、关键生命周期和真实规模功能。临时 harness、截图、模拟服务、性能数据在验收后清除；不为本轮设置新增测试数量目标或复活脆弱的实现细节测试。人工体验验收可选、不阻塞自动任务。
- 按 `AGENTS.md` 执行相应 focused tests 和任务收口门禁；T007 执行完整标准门禁。文本统一 LF。不得未经授权 push、PR、Release 或 tag。
- 每任务完成时在本文件保留 `[x]` 与**简短完成成果、测试结果和限制**，删除该任务在 `tasks/` 的临时规格。用户保留的审计原报告不加入 Git；Codex 可在任务执行期间使用仓库外临时工作记录，最终清除无用副产物。

## 3. 任务安排

### Phase A — E1-A 已完成研究

- [x] **T001（Research）— 全项目广覆盖审计**
  成果：形成 Domain/Application/Infrastructure/WPF、工具/测试的系统地图；查找状态所有权、生命周期、复杂度、历史残留与做减法机会，保留扫描覆盖缺口。静态调查未认定已验证 P0/P1。
- [x] **T002（Research）— 高风险问题动态补证及复杂度深挖**
  成果：真实 coordinator/假 Provider 证明 Active Cache 退出等待风险，合成 observer 复现 Cache/Export stuck slot；loopback 证实跨源 redirect 转发 X-Api-Key/Cookie/自定义 Header；受控 Playback 内容解析证明命令 gate 等待；WPF 合成 10k 书目全部 realized，Cache VM staged 机制具有实际必要性。限制：多数实验为隔离样本，非真实用户故障发生率或生产性能统计。详见由用户保存的 `E1_AUDIT_FOLLOWUP_REPORT.md`。

### Phase B — E1-B 定向实施

- [x] **T008（P1 / Reliability，插入修复）— Edge 新端点与 Sec-MS-GEC v2**
  成果：按 ms-ra-forwarder `fix/drudream` @ `4936d3e1` 更新 Infrastructure 固定 profile 为 `edge-readaloud-155-v2`，采用新合成端点、Subscription Key 参数及 v2 认证，删除 v1 算法/握手；Voice 配置、合成身份、缓存与数据格式保持原合同。新增上游独立计算的 6 个认证向量及新握手离线回归，保留现有协议/Provider 核心测试。锁定还原、format、Release build（0 警告/错误）、Infrastructure Speech/Provider 66 项、Application Edge/TTS 11 项、架构 19 项及 diff 检查通过。2026-10-13 Windows/.NET 生产 Voice List → Runtime → transport → MP3/解码全部成功（晓晓，35,424 bytes，约 2.95 秒）；初次环境代理导致失败，仅在临时验证进程绕过代理后通过，未修改产品/系统代理设置。临时 harness、音频、参考副本及任务规格已清理；未执行全量 test（阶段完整门禁仍由 T007 执行），无其它环境未执行项或文档冲突。
- [x] **T003（P1 / Security）— HTTP Provider 完全禁止自动重定向**
  成果：生产共享 SocketsHttpHandler 禁止自动重定向；所有 3xx 走既有失败路径，提示配置最终 URL，不读取重定向正文或暴露 Location。HTTP TTS 正式合同补充正文处理边界，无配置/数据迁移。保留现有 Speech 核心测试，新增一组真实 loopback 安全回归（6 场景：301/302/303/307/308、同源/跨源、GET/POST 与合成凭据），确认 Location 端点零请求且不重试。锁定还原、format、Release build（0 警告/错误）、Infrastructure Speech 70 项、Application Provider/TTS/Speech 49 项及 diff 检查通过。未遗留临时产物；全量 test 留待 T007，无环境未执行项或文档冲突。
- [ ] **T004（P2 / Reliability）— Active Cache / Export 启动与事件通知异常隔离**
  规格：[`tasks/T004_BACKGROUND_START_ATOMICITY.md`](tasks/T004_BACKGROUND_START_ATOMICITY.md)。使 UI 观察者异常无法导致 worker 未启动但 active slot 永远不完成；保持 job owner / 取消 / 快照语义。
- [ ] **T005（P2 / Lifecycle）— Active Cache 进程退出取消与有界排空**
  规格：[`tasks/T005_ACTIVE_CACHE_SHUTDOWN.md`](tasks/T005_ACTIVE_CACHE_SHUTDOWN.md)。与现有 Export/repair drain 协调，处理不响应取消的 Provider，保护已成功缓存的派生数据和有界退出。
- [ ] **T006（P2 / Performance）— CacheManagement 书目选择器 WPF 虚拟化**
  规格：[`tasks/T006_CACHE_BOOK_LIST_VIRTUALIZATION.md`](tasks/T006_CACHE_BOOK_LIST_VIRTUALIZATION.md)。只修改技术容器与必要绑定/滚动逻辑，不引入 U1 视觉设计变更；用隔离真实 WPF host 进行合成规模验证。

### Phase C — E1-C 工程收口

- [ ] **T007（P1 / Closure）— E1-B 回归、做减法检查与完整门禁**
  规格：[`tasks/T007_E1_CLOSURE.md`](tasks/T007_E1_CLOSURE.md)。合并验证四项修复与现有 Book、Playback、Speech、Cache、数据边界；清除一次性产物；完整质量门禁；仅根据确证情况修正本轮残留。

## 4. 本轮明确不实施

- 不重构 Playback 命令 gate（慢内容解析虽可复现，但尚未证明常见本地使用下有用户可感知收益）；不调整 target-first/迟到音频身份规则。
- 不删除 CacheManagementViewModel 的 `>512` staged 更新、deferred refresh 或大目录保护；T002 已验证其必要工作路径。
- 不重写约 3,000 行架构检查器为 Roslyn，不扩大永久测试体系；该项进入后续 E5 候选。
- 不修改 Book committed event 语义、Library 局部重复查询、Provider/Settings 跨 SQLite/JSON 所有权、v8 既定 migration 策略、SourceHash、orphan temp 等。本阶段均未批准整改。
- 不开发 Online Source、搜索/刷新或在线缓存；不做 U1 色彩、字体、圆角、视觉风格的重设计；不新增数据库 migration/正式数据格式更改。

## 5. 阶段完成标准

- T003–T006 的核心行为均有可重复的自动验证及必要的反证/限制记录，安全凭据不因 HTTP redirect 发往 Location。
- Active Cache/Export 的 listener 异常不破坏任务完成责任；Active Cache 退出有明确取消与有界清理机制，不依赖无限等待；正常前台/后台操作不退化。
- CacheManagement 的大书目容器实际虚拟化，原有选择、进度、主题、页面激活及 10k 章节列表行为保留。
- 新增/保留的测试符合核心行为准入，未遗留一次性测试、兼容层、临时诊断脚本/文件；必要长期文档与实现一致。
- T007 完整质量门禁通过；无法运行的项按环境限制如实记录，不假报绿色。E1 结束后由用户与 ChatGPT 审核并进入 U1。

## 6. 上轮里程碑

Book / Source / CurrentCatalog / SQLite v13 基础重构与删除/音频缓存收口（历史 T001–T005A）已完成，详细过程留在 Git 历史，本 Backlog 不重复复制长篇旧实施日志；各编号以阶段为作用域。
