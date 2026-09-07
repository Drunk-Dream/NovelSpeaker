# 已确认决策与风险

## 1. 架构优化阶段决策

### 顶层结构

- 保留 Domain / Application / Infrastructure / App 四层。
- 不继续按 Feature 拆程序集。
- 允许大幅内部破坏性重构。
- 内部兼容性不是目标；旧实现、wrapper、adapter 和无价值 abstraction 应直接删除。

### Feature

- App 按 Books / Playback / Cache / Rules / Settings / Diagnostics 收敛。
- 禁止 Feature 双向依赖。
- Shared 只承载真实跨业务域能力。

### 生命周期

- 普通 Page/ViewModel 默认 transient。
- 只有真实 process/session/background owner 使用 Singleton。
- 页面 UI 状态不依赖 singleton VM 持久化。

### 状态通信

- 保留 typed snapshot / typed event / typed port。
- 不引入通用 EventBus/Messenger。

### Interface

- “有真实边界才有接口”。
- Feature-local controller/projector 默认 concrete internal type。

### 成熟能力优先

- 不重新实现平台/框架已经稳定提供的基础设施能力。
- 优先 .NET/Windows/WPF/Wpf.Ui 和项目已有能力；存在已证实缺口时才评估成熟依赖或自定义实现。
- 自定义实现必须说明标准能力缺口、ownership、生命周期、测试和长期维护成本。
- 不以“更可控”为理由复制框架内部状态机；应用只拥有业务/presentation 必需状态。

### 大列表

- 连续目录目标至少 10,000 章。
- 用户侧不显式分页。
- 使用 Immutable Catalog + Sparse Mutable Decoration。
- WPF virtualization 不替代 data/projection 规模控制。
- WPF UI virtualization 默认由标准虚拟化控件负责，不由 Feature 自己接管 ItemContainerGenerator/IScrollInfo。

### Library

- 保留响应式多列卡片视觉。
- 使用轻量 responsive row projection 计算列数、card width 和 row grouping。
- Row 使用 WPF 标准 `VirtualizingStackPanel` 做 recycling virtualization。
- Row 内少量 Card 不建立第二套 virtualization。
- scroll state 使用逻辑 `BookId` anchor + row lookup + 标准 ScrollIntoView/BringIntoView。
- 删除/避免自定义 container generation、realized-range、extent/viewport/offset 状态机。

### Query

- 使用轻量 CQRS-style read model。
- 不引入 CQRS 框架/MediatR/CommandBus/QueryBus。

### 页面加载

- 复杂页面使用 staged loading。
- 首个可交互帧不等待全部动态 enrichment。

### Playback

- `PlaybackCoordinator` 保持唯一 session owner/facade。
- 内部拆 SessionState、Command、Audio、Content、Prefetch、Progress、Timer。
- 不拆出多个 current-state owner。

### Player

- XAML 继续绑定单一 PlayerViewModel。
- 内部使用 Feature-local presentation controllers。

### Cache

- CacheStore 只拥有物理 cache/index/file 真值；CacheCatalog 只提供物理 read model。
- current-configuration Coverage 与物理统计分离，由独立只读 query 计算。
- 缺失/过期 Speech Plan 补建由唯一 process owner 管理，query 本身不启动后台副作用。
- cache invalidation 只表达最窄已知范围与失效方面，不携带第二套统计真值。
- 高频 cache mutation 使用 Cache-local 短窗口合并，实现用户感知实时而非逐 entry 严格实时。
- 页面 selection 与 catalog/physical/Coverage decoration 独立；cache 刷新不得无条件清空选择。
- 不建立大一统 CacheManager、通用 EventBus/Messenger 或通用 BackgroundTaskManager。

### Rules

- 三类 Rules 共享编辑生命周期。
- 不建立复杂泛型 Rule Framework。

### Settings

- process Settings snapshot + transient Settings VM。

### 测试

- 允许架构重构后测试数量减少。
- 以风险/行为合同覆盖为目标。
- 建立长期 Architecture Fitness Tests。

### 文档

- 文档是架构收敛的一部分。
- 一条稳定规则一个 owner 文档。
- 任务过程和诊断不进入长期编号文档。

## 2. 产品/数据稳定决策

- 产品聚焦本地 TXT + HTTP TTS。
- 不建立 EPUB/在线书源/云同步/账号体系。
- 外部 TXT 不写回。
- 已发布 SQLite migration append-only。
- ReadingProgress 是持久化 checkpoint；matching PlaybackSnapshot 是当前活动书籍即时显示真值。
- Cache 是可重建数据。
- 当前播放 > Prefetch > Active cache。
- Active cache 与 Export 是独立后台 owner。

## 3. 导航决策

- 不维护浏览器式历史。
- 强类型 `AppRoute` 是业务导航状态。
- 普通页面固定 parent route。
- Player 携带一次性 ReturnRoute。
- Wpf.Ui 内部 history/cache 不作为业务状态。

## 4. UI 决策

- 保留 Wpf.Ui provider 作为标准控件视觉 owner。
- NovelSpeaker 使用 palette/token/具名 style/自有控件扩展。
- 不用应用级隐式样式接管标准控件。
- Dialog/Flyout/Popup 使用 Single Surface。
- 主题切换不通过代码重写标准 ControlTemplate。
- 标准 WPF 控件已经提供的容器、滚动和虚拟化生命周期由框架负责，应用只做必要的布局/数据投影。

## 5. 主要架构风险

### Playback owner 过宽

拆职责时必须保持 session owner 唯一，避免“拆类”变成“拆状态”。

### 大列表

Catalog、collection notification、cache decoration、locator 和 layout 必须一起按规模设计；只调 virtualization 参数不足够。

### 重复实现框架基础设施

Feature 自行接管 WPF container generation、recycling、scroll extent/offset 或类似框架级状态机会显著增加时序、生命周期和维护风险。出现这类实现时，优先重新设计数据/presentation 形状以使用标准框架能力，而不是继续修补内部状态。

### Page activation

从 singleton VM 迁移 transient 时，必须把真正长期状态提升到正确 owner，而不是丢失状态或另建 cache。

### Cache 边界

物理 Store/Catalog、Coverage、Speech Plan repair、Active batch、Export 和页面 selection 生命周期不同；迁移时必须保持 owner/query/command/invalidation 清晰。尤其禁止通过全量重载把 cache mutation、Coverage 更新和用户选择重新耦合。

### Interface 清理

不得仅按“单实现”机械删除 port；技术边界和 owner role view 仍然有价值。

### 测试清理

测试减少必须由行为/层级覆盖证明，不以减少数量本身为目标。

### 文档漂移

Codex 执行代码任务时不得顺手建立新的架构解释；若实现发现当前目标设计不成立，应在任务完成成果中记录阻塞，交由新的规划阶段调整 owner 文档。

## 6. 3000+ 章节卡顿

此前 Player → Back → BookDetails 的 3000+ 章节返回卡顿已确认解决，不再作为待修复问题，也不追加专项 workaround。

后续在 180/1000/3000+/10000 真实规模中保留该场景作为性能回归；只有能够稳定复现退化时，再基于 Dispatcher/CPU/memory/SQL profiling 进入新的性能诊断。
