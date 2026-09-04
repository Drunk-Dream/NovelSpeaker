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

### 大列表

- 连续目录目标至少 10,000 章。
- 用户侧不显式分页。
- 使用 Immutable Catalog + Sparse Mutable Decoration。
- WPF virtualization 不替代 data/projection 规模控制。

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

- 保留 CacheCatalog、CacheStore、ActiveCache、Export 等明确 owner。
- 不建立大一统 CacheManager 或通用 BackgroundTaskManager。

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

## 5. 主要架构风险

### Playback owner 过宽

拆职责时必须保持 session owner 唯一，避免“拆类”变成“拆状态”。

### 大列表

Catalog、collection notification、cache decoration、locator 和 layout 必须一起按规模设计；只调 virtualization 参数不足够。

### Page activation

从 singleton VM 迁移 transient 时，必须把真正长期状态提升到正确 owner，而不是丢失状态或另建 cache。

### Cache 边界

Workspace、Active batch、Store/Index、Catalog/Status 名称相近，迁移时需逐条确定 owner/command/query/event。

### Interface 清理

不得仅按“单实现”机械删除 port；技术边界和 owner role view 仍然有价值。

### 测试清理

测试减少必须由行为/层级覆盖证明，不以减少数量本身为目标。

### 文档漂移

Codex 执行代码任务时不得顺手建立新的架构解释；若实现发现当前目标设计不成立，应在任务完成成果中记录阻塞，交由新的规划阶段调整 owner 文档。

## 6. 3000+ 章节卡顿

当前将其视为整体架构压力症状，不在架构迁移前继续做局部 workaround。

待 Books/read model、大列表、Playback/Player 等结构完成后，以 180/1000/3000+/10000 真实规模重新验证。如果仍有瓶颈，再基于 Dispatcher/CPU/memory/SQL profiling 处理。
