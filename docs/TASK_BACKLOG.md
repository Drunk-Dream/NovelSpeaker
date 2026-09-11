# NovelSpeaker 当前开发 Backlog

## 1. 阶段定位

当前进入 **代码库收敛与测试体系瘦身阶段**。规划基线：`33431ddcb2f998168b989516fefabb0131c675d9`。

上一轮 Application 模块边界收敛已完成：Cache、Playback、Books、Speech、Settings、Desktop 的职责和允许依赖方向已经稳定，Application module Architecture debt baseline 已清零，全量 Release test 为 955/955。

本阶段是日志、性能遥测与诊断系统开发前的最后一轮历史债务清理。目标不是继续调整架构，而是减少长期开发积累的无价值代码和脆弱测试，使后续横切能力接入时：

- 生产代码只有一套当前实现；
- 测试主要保护稳定行为、数据兼容和架构边界；
- 合理内部重构不会因为大量实现细节测试而产生连锁失败；
- TestKit、fake、fixture 和 helper 保持少而清晰；
- 完整测试仍能对关键风险提供可信保护。

**本阶段不实现或预埋日志、性能遥测、诊断会话、诊断导出及其 UI。**

### 清理原则

1. 不设置“必须降到 N 个测试”的机械数量目标；测试数量下降是结果，不是验收条件。
2. 优先删除 dead/unreachable code、已失去调用方的 abstraction/adapter/DTO/helper、迁移期 compatibility 代码、重复覆盖和实现细节测试。
3. 必须保留或等价覆盖：
   - Architecture Fitness Tests；
   - Playback session / Snapshot / checkpoint / command 关键语义；
   - Cache identity / Coverage / plan / ActiveCache / Export；
   - Settings / Rules 关键业务语义；
   - SQLite schema / migration / 关键 query；
   - 文件路径与外部 TXT 安全；
   - HTTP TTS / Jint 安全与限流；
   - navigation / ReturnRoute；
   - 大列表结构合同；
   - 真正依赖 WPF 的 navigation、binding、virtualization、scroll/focus/popup/theme 等行为。
4. 不因为测试难维护而删除唯一的高风险行为保护；若原测试层级错误，应迁移到更合适的层级后再删除旧测试。
5. 不为保留旧测试重新引入 production compatibility API。
6. 生产代码清理不得改变用户行为、持久化格式、SQLite migration、缓存身份、文件布局或现有架构 owner。
7. 本阶段结束后停止继续做“顺手架构优化”，直接进入日志 + 性能遥测 + 诊断系统规划/开发。

## 2. 状态

- `[ ]` 未开始
- `[-]` 进行中
- `[x]` 已完成，末尾追加“完成成果”
- `[!]` 阻塞，记录可复现证据

优先级：

- `P0`：清理前审计、关键测试保护、最终质量门禁；
- `P1`：生产代码和测试体系的主要瘦身；
- `P2`：低风险小型清理，仅在明显有收益时执行。

完成后的任务保留并标记完成；只有新的规划阶段才再次清空/重写 Backlog。

## 3. 通用执行规则

1. 一次只执行一个编号任务，完成后停止。
2. 开始前阅读 `AGENTS.md`、`docs/08_TESTING_AND_QUALITY.md` 和当前任务；涉及具体业务时再阅读对应 owner 文档。
3. 先证明“为什么可以删”，再删除。证据优先包括：无生产调用方、已被稳定实现替代、相同风险已有更合适层级覆盖、仅绑定旧迁移路径、helper/fake 已无消费者。
4. 如当前 Codex 环境支持 `explore` subagent，可在 T001 以及后续大范围审计时使用其并行探索代码库；`explore` 只负责调查和提供证据，不直接决定删除。不可用时由主 agent 完成同样审计，不构成阻塞。
5. 使用 subagent 时按清晰范围拆分，例如 Production、Application/Infrastructure tests、Presentation/WPF tests、TestKit/fixture，避免多个 agent 同时修改同一文件。
6. 不依据文件行数、测试数量、构造参数数量或“看起来复杂”直接删除/拆分。
7. 不创建新的通用 abstraction、Manager、Helper、EventBus 或 test framework 来“帮助清理”。
8. 不新增第三方测试/mocking 框架，本轮默认使用现有能力。
9. 删除测试前确认对应风险是否在其它层级已有稳定覆盖；重复测试只保留最接近真实 owner/技术边界的一层。
10. WPF tests 只保留 WPF 特有行为；纯业务/presentation 语义不得继续在 WPF 层重复验证。
11. Presentation tests 不实例化真实 WPF，不复制 Application 已稳定覆盖的纯业务算法。
12. Infrastructure Integration tests 聚焦真实 SQLite/file/HTTP/Jint/NAudio/serialization 技术边界，不重复 Application 纯逻辑。
13. Architecture Fitness Tests 是长期门禁，原则上不因本轮测试瘦身删除。
14. 测试合并时优先参数化或共享最小 fixture，但不要形成一个修改即导致大量无关测试同时失败的“万能 fixture”。
15. 测试 double 优先窄职责、局部使用；只有跨多个测试类复用且语义稳定时才进入 TestKit。
16. 不使用固定 `Task.Delay`/`Thread.Sleep`、放宽断言、任意 retry 或增大 timeout 掩盖 flaky test。
17. 发现真实产品 bug、数据兼容风险或架构违规时，不以“清理”为理由删除测试获得绿色。
18. 每个任务完成前删除临时脚本、trace、dump、coverage 输出、探索文档和其它一次性产物。
19. 所有文本文件保持 LF；不做与任务无关的全仓格式化。
20. 每项任务完成后记录：删除/合并内容、删除依据、保留的核心风险保护、测试结果、测试数量变化（仅统计）、剩余候选和未处理原因。
21. 本阶段不得实现日志、性能遥测、诊断系统，也不得为了下一阶段提前增加空接口、空目录或 speculative hook。

---

# Phase A：探索与清理基线

## [x] T001（P0）：审计生产代码与测试体系，建立可执行清理清单

目标：先基于当前真实代码和 955 个测试形成“保留 / 合并 / 删除 / 需要进一步确认”的证据化清单，避免边看边删造成误判。

允许使用 `explore` subagent，并建议在可用时并行探索，但不强制。

### 探索范围

**Production**

- 无生产调用方的 class/interface/record/enum/helper；
- 迁移后残留的旧 namespace、compat/forwarding/Obsolete 代码；
- 只有测试调用、生产路径已不使用的 API；
- 重复 mapper/formatter/projector/controller；
- orphan DI registration、factory、service extension；
- Feature-specific 能力错误留在全局 Shared；
- 已失去意义的 lifecycle flag、defensive state、旧 fallback；
- 临时 workaround、历史 TODO、一次性迁移分支；
- 能可靠证明无引用的资源/XAML style/key。

**Tests**

按测试项目和风险分类：

- 唯一核心行为保护；
- Architecture/安全/数据兼容保护；
- 与其它层级重复；
- 绑定实现细节；
- 只验证简单 property/转发/DI registration；
- 迁移期/compatibility 专用；
- flaky / 高维护成本但价值有限；
- 大型 fixture 导致广泛耦合；
- fake/stub/helper/TestKit 重复。

### 执行方式

1. 如可用，可使用数个 `explore` subagent 分范围调查；主 agent 汇总并交叉验证。
2. 不在仓库长期新增 audit/report 文档。
3. 如需临时清单，只在工作区临时保存并在任务结束前删除。
4. 最终摘要直接写入 T001 的“完成成果”，至少包含：
   - Production 高置信删除候选类别与代表性文件；
   - 各测试项目的保留重点；
   - 重复覆盖最严重的区域；
   - TestKit/fixture 的主要耦合点；
   - T002–T005 的重点是否需要微调。
5. 本任务原则上不大规模删除代码；仅允许删除极少数当场可证明无调用方、无兼容意义的明显垃圾文件。

验收：

- 清理方向可以映射到后续 T002–T005；
- 没有仅凭名称/行数判断“无用”的候选；
- ArchitectureTests 通过；
- 若修改生产/测试代码，相应 focused tests 通过。

完成成果：

- 生产代码未发现可凭静态证据直接删除的高置信 dead code、孤立 DI registration 或 forwarding wrapper。`AppStoragePathMigrationService`、`AudioCacheFormatResetService`、legacy path handling 和 `TtsRuleCompatibilityChecker` 均有活动调用方，分别保护启动迁移、缓存格式 reset、数据根安全和导入兼容性，列为保留合同。
- Domain/Application 测试保留 Playback session/checkpoint、Cache identity/invalidation、ActiveCache/Export 生命周期、Settings/Rules、导入恢复、文本处理和 TTS 安全合同；可执行候选为：无变化输入的 cache identity 测试、三项重复 filename fallback、误称 list identity 的断言、progress object-identity 断言、两个 stop-timer range 测试，以及重复的 typed source-change 测试。后续应合并/重写并保留实际行为保护。
- Infrastructure 测试保留 SQLite schema/migration/query、cache index/file 原子性、路径与外部 TXT 安全、JSON persistence、HTTP TTS、Jint sandbox、NAudio/MP3 和 export 边界。`AppDataDirectoryProviderTests` 的纯路径投影断言可与目录创建合同合并；PlaybackCoordinator 三组测试是有价值的 Application 编排测试，但当前错置在 Infrastructure，后续优先重定位/拆分而非删除。
- Presentation/WPF 审计确认 current-item locator、auto-scroll、大列表、navigation 和 WPF isolation/resource/theme 测试分属不同技术边界，不作为 T003 删除项；主要后续低风险候选是成对的 active-cache/export/mini-player/stop-timer test doubles 耦合，属于 T004 范围，不在本次用户请求内处理。
- ArchitectureTests + BehaviorDebtBaselineTests 基线通过：18/18（Release，沙箱外）；命令因 WSL socket 初始化错误先在沙箱内失败，随后完成沙箱外验证。基线测试总量保持 Backlog 所记 955，未因审计删除测试。

---

# Phase B：生产代码收敛

## [x] T002（P1）：清理生产代码中的 dead / legacy / duplicate 实现

依赖：T001。

目标：在不改变现有架构和用户行为的前提下，删除长期开发与迁移积累的生产代码遗留，使后续日志/遥测埋点面对唯一、清晰的真实执行路径。

### 优先清理

1. **Dead code**
   - 无任何生产调用方；
   - DI 未注册且无显式构造路径；
   - XAML/resource 无引用；
   - 已被当前实现完全替代。
2. **Migration residue**
   - old/new/v2/compat/legacy/forwarding wrapper；
   - 旧 namespace alias；
   - 已结束迁移的 fallback 分支；
   - 为旧测试保留的生产入口。
3. **Duplicate/simple abstraction**
   - 等价 formatter/mapper/helper；
   - 只做无语义转发且不构成真实边界的 interface/service；
   - orphan factory/registration/extension。
4. **Shared 污染**
   - 仅服务单 Feature/模块却遗留在全局 Shared 的类型；
   - 若仍在使用，应移动回真实 owner，而不是机械清空 Shared。
5. **Defensive state**
   - transient/single-owner 架构稳定后已不再需要的 duplicate initialized/subscribed/loaded flag；
   - 删除前必须证明生命周期已有唯一 owner 和测试保护。

### 禁止

- 不重新设计 Cache/Playback/Settings 等稳定模块边界；
- 不拆大类仅为了减少行数；
- 不改变 public product behavior；
- 不修改已发布 SQLite migration；
- 不改变 cache identity、file layout、reading checkpoint 等稳定数据语义；
- 不把局部代码提升成新的通用 abstraction。

### 验收

- 修改区域 focused tests 通过；
- ArchitectureTests 通过；
- 因 production 删除而失去意义的测试可同步删除，但不得提前做大范围测试瘦身；
- 完成成果记录主要删除项、保留的高风险候选及理由。

完成成果：

- 合并 `CacheManagementCompletenessFormatter` 中对 `PlanMissing` 与 `PlanUnavailable` 的重复分支，保留相同的“计划计算中”用户可见结果；没有新增 abstraction 或改变 Cache/Playback owner。
- 生产审计确认没有可凭调用图删除的 dead class、orphan registration、旧 namespace 或 forwarding wrapper。`AppStoragePathMigrationService`、`AudioCacheFormatResetService`、legacy path safety 和 TTS compatibility 都仍由启动/数据/导入路径使用，因此保留。
- 保留 `IAudioCache`/`IAudioCacheStore`、两类 cache formatter、`IHttpTtsClient`/`TtsExecutionService` 等看似相近但拥有不同消费者或技术边界的实现，未进行未经证实的合并。
- 验证通过：`CacheCompletenessFormatterTests`、`ArchitectureTests`、`BehaviorDebtBaselineTests` 合计 21/21（Release，沙箱外）。

---

# Phase C：核心业务与基础设施测试瘦身

## [x] T003（P1）：精简 Domain/Application/Infrastructure 测试，只保留稳定风险合同

依赖：T002。

目标：减少对内部调用步骤、辅助类型和重复逻辑的测试绑定，使 Application/Infrastructure 内部重构不再引发与真实行为无关的大面积测试失败。

### Domain/Application 保留重点

- Playback command / session / Snapshot / checkpoint；
- stale event、失败、取消、快速 session replacement；
- Cache identity / key / Coverage / plan；
- Cache invalidation/configuration-change 映射；
- ActiveCache / Export owner 生命周期与取消；
- Settings / Rules 的关键业务变化；
- cancellation/version/concurrency 中真正影响正确性的合同；
- 安全规则。

### Domain/Application 优先删除/合并

- 对简单 record/property/default value 的逐项测试；
- 对 internal helper 调用顺序的测试；
- 大量排列组合但没有新增风险维度的测试；
- 只为旧 constructor/interface/namespace 存在的测试；
- 多个测试类重复验证同一错误/取消路径；
- 已由更高价值状态机/流程测试包含的细碎单元测试。

### Infrastructure Integration 保留重点

- SQLite schema/migration/升级；
- 关键 query、排序、事务；
- Audio cache index/file 原子性与恢复；
- 用户数据根/路径安全；
- HTTP TTS 兼容与关键错误分类；
- Jint sandbox/security；
- NAudio/MP3 真实 adapter 边界；
- 真实 serialization/persistence format。

### Infrastructure 优先删除/合并

- 重复 Application 纯逻辑；
- repository/service 简单 passthrough 的多层重复验证；
- 已有真实 round-trip 覆盖下的逐字段机械测试；
- 只因旧 adapter/compat layer 产生的测试；
- fixture 初始化过程本身的测试，除非属于安全/隔离合同。

### 执行要求

1. 每组删除说明替代保护位于何处。
2. 参数化可合并同一风险的输入变体，但不要形成超大型 Theory。
3. 清理后删除无消费者的 test-only builder/fake/helper。
4. 不修改 production 以迁就测试，除非发现真实 bug。

验收：

- 核心业务/基础设施合同仍有清晰测试入口；
- ArchitectureTests 通过；
- 对应 test projects 完整通过；
- 测试数量变化只记录，不设目标值。

完成成果：

- Application 测试删除无变化输入的 `AudioCacheIdentity` tautological 断言；将六种 filename fallback 输入（不匹配、空模板、缺少 name、重复 placeholder、未知 placeholder、literal 不匹配）收敛为一个稳定合同测试；将四个 stop-timer 非法 duration 边界合并为一个合同测试；删除未独立断言 cache 依赖的重复 settings change 测试。
- Export progress 测试从 `Assert.Same` 实现细节改为验证 writer 报告的 `ExportChaptersProgress` 可观察行为；Chapter rule 测试名称同步真实断言，不再声称验证 list identity。
- Domain 测试全部保留；Infrastructure 没有删除真实技术边界测试，尤其保留 SQLite schema/migration/query、cache 原子性与路径安全、JSON persistence、HTTP TTS、Jint sandbox、NAudio/MP3 和 export 合同。错置但仍有价值的 PlaybackCoordinator 编排套件列为后续重定位候选，未在本切片删除。
- Application 测试由 171 减至 166（删除/合并 5 个测试入口）；Domain 15/15、Infrastructure 342/342 通过。ArchitectureTests + BehaviorDebtBaselineTests 18/18 通过；本切片合计保留 523 个 Domain/Application/Infrastructure 测试案例，较基线减少 5 个。

---

# Phase D：Presentation / WPF 测试瘦身

## [ ] T004（P1）：删除重复 Presentation/WPF 测试，保留 UI 生命周期和 WPF 特有风险

依赖：T003。

目标：解决 UI 层最容易出现的“内部稍改即大量测试失败”问题，让 Presentation 和 WPF 测试只保护各自真正拥有的风险。

### PresentationTests 应保留

- ViewModel activation/deactivation；
- 页面取消、迟到结果、版本保护；
- staged loading 关键顺序；
- snapshot/read-model → UI state 的重要 projection；
- selection/current/cache decoration 等用户状态；
- 复杂页面 interaction controller 的稳定行为；
- Architecture Fitness Tests。

### PresentationTests 优先删除/合并

- Application 已充分覆盖的纯业务算法；
- 简单 command 仅验证调用某 mock 一次；
- 简单 property passthrough；
- 绑定具体 private controller/helper 的测试；
- 每次 refactor 都需同步大量 constructor mock 的低价值 fixture；
- 多个 VM 测试重复验证同一 shared primitive。

### WpfTests 应保留

只保留真正依赖 WPF runtime 的合同：

- navigation composition/page lifecycle；
- binding/resource lookup；
- virtualization/container recycling；
- scroll/locator/auto-center；
- focus/keyboard/mouse hit testing；
- popup/dialog/flyout；
- theme/style/icon/resource；
- hidden isolated Desktop fail-closed；
- 真实 Dispatcher/layout/render 时序中的关键回归。

### WpfTests 优先删除/下移

- 不需要 WPF runtime 的 VM/command/state 测试；
- 与 PresentationTests 完全重复的业务行为；
- 仅验证控件存在、文本常量、简单 XAML 属性且无历史风险的测试；
- 依赖像素/时序但没有明确产品回归价值的脆弱断言；
- 为旧视觉实现/旧 workaround 存在的测试。

### 特别要求

- 不放宽 hidden Desktop 安全规则；
- 不用固定 Delay、任意 retry、增大 timeout“稳定”测试；
- 高价值 flaky WPF test 应先修同步/隔离/fixture，不能直接删除。

验收：

- PresentationTests 与 WpfTests 职责区分明显；
- 同一非 WPF 风险不在两层机械重复；
- ArchitectureTests 保留并通过；
- 两个测试项目完整通过；
- 测试数量变化只记录，不作为验收条件。

---

# Phase E：TestKit、fixture 与测试基础设施收敛

## [ ] T005（P1）：清理 TestKit / fake / fixture / helper，降低测试间耦合

依赖：T004。

目标：在测试数量瘦身后反向清理测试基础设施，使一个 production constructor、内部 service 或 fixture 的变化只影响真正相关的测试，而不是通过“大一统测试工具”扩散到整个套件。

### 清理方向

1. 删除 T003/T004 后无调用方的 fake、stub、builder、fixture、test host、assertion helper、test-only adapter。
2. 合并真正语义相同的重复 test double。
3. 拆除“万能 fixture”：
   - 同时知道 Books/Playback/Cache/Settings/UI 多个无关模块的 fixture，只保留实际共享的最小稳定部分；
   - Feature-specific fixture 回到对应测试目录。
4. TestKit 只保留跨测试项目复用且长期稳定的能力，例如：
   - 安全 WPF isolated Desktop；
   - 测试数据根/临时文件生命周期；
   - 少量稳定 Cache/audio fixture primitive。
5. 优先通过真实窄 read model / snapshot 创建测试数据，不建立复杂 fluent test DSL。
6. 删除为了 mock constructor 而暴露的 production test-only API。
7. 检查 test project package/reference，删除已无用途的测试依赖。

验收：

- TestKit 不承载 Feature-specific 业务框架；
- 无 orphan test utility；
- 修改单个 Feature 内部 constructor 不再要求无关测试项目批量更新；
- 全部测试项目可独立运行；
- ArchitectureTests 通过。

---

# Phase F：最终仓库清理与质量门禁

## [ ] T006（P0）：最终静态清理、重复覆盖复审与完整质量门禁

依赖：T005。

目标：确认代码库已从长期架构迁移状态收敛为适合开始日志、性能遥测和诊断系统开发的稳定基线。

### 最终静态审计

检查并处理：

- production dead code；
- orphan DI registration；
- unused internal abstraction；
- legacy/compat/obsolete/old/v2 命名或实现；
- 无消费者 Shared/TestKit helper；
- 无意义测试 fixture；
- 重复测试文件/重复风险覆盖；
- skipped/disabled test；
- 属于历史迁移的 TODO/FIXME；
- 临时 script/trace/dump/screenshot/coverage/benchmark artifact；
- 空目录和迁移遗留文件；
- test-only production API。

如环境支持，可再次使用 `explore` subagent 做独立复审，但必须由主 agent 验证删除建议。

### 核心保护复审

最终确认至少仍有清晰测试保护：

- 四层与 Application module Architecture Fitness Rules；
- Playback canonical state owner；
- ReadingProgress/checkpoint；
- Cache identity/Coverage/plan；
- ActiveCache/Export；
- Settings/Rules；
- SQLite migration；
- 文件/路径安全；
- HTTP TTS/Jint 安全；
- navigation/ReturnRoute；
- 大列表结构合同；
- WPF isolated Desktop；
- 关键 virtualization/scroll/theme/popup 行为。

### 完整质量门禁

```powershell
dotnet restore --locked-mode -r win-x64
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

若首次完整测试失败：

1. 判断是真实回归、测试环境波动还是 flaky test；
2. 不通过直接删除失败测试获得绿色；
3. 对 flaky test 按风险价值处理：
   - 高价值：修同步/隔离/fixture；
   - 低价值且已有等价保护：有证据地删除；
4. 完整套件最终必须稳定复跑通过。

### 完成成果

记录：

- Production 删除/合并的主要历史遗留；
- 各测试项目清理前后测试数量，仅作为统计；
- 删除的主要重复/实现细节测试类别；
- 最终保留的核心测试风险地图；
- TestKit/fixture 收敛结果；
- 完整 Release 门禁结果；
- 是否仍存在已知 flaky test；
- 是否存在会阻塞日志/性能遥测/诊断系统开发的问题。

若无阻塞问题，本任务完成后结束代码库清理阶段，下一轮直接进入 **日志 + 性能遥测 + 诊断系统**，不再追加常规架构/清理 Phase。
