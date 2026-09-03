# NovelSpeaker 当前开发 Backlog

## 1. 阶段定位

当前阶段处理 **Shell 导航模型简化与播放页返回语义修复**。当前 `dev` 基线为 `9ddbf4b5c93268f1c3fb8b5b0c1cd2bda37ee2f0`。

本阶段不再维护浏览器式/页面栈式应用导航历史。目标模型固定为：

```text
当前完整 AppRoute
    +
普通页面固定 ParentRoute
    +
PlayerRoute 显式 ReturnRoute
```

本轮直接解决已知缺陷：从书籍详情页点击章节进入播放页后，点击返回、`Alt+Left` 或在无其它临时交互消费时按 `Esc`，必须能够使用原 `BookDetailsRoute(BookId)` 返回并重新加载同一本书，不能因为 Wpf.Ui back stack 只恢复 Page 类型而丢失 `BookId`。

本轮边界：

- 不建立 `Stack<AppRoute>`、浏览器式 Back/Forward history、页面实例栈或自定义导航缓存。
- 不把 Page/ViewModel 改成 Singleton 来规避参数丢失。
- 不依赖 Wpf.Ui `INavigationService.GoBack()` 恢复应用级路由参数。
- Wpf.Ui `NavigationView` 继续负责页面宿主、一级导航交互和选中态；其内部 history/cache 不是 NovelSpeaker 的业务状态来源。
- 普通页面按稳定信息架构返回；只有播放页需要保存一次性的动态来源路由。
- 本轮不改变播放会话、阅读进度、缓存、TTS、页面视觉或持久化格式。

目标返回层级：

| 当前路由 | 返回目标 |
|---|---|
| `BookDetailsRoute(bookId)` | `Library` |
| `PlaybackSettings` / `TtsRules` / `ImportTextSettings` / `ChapterRules` / `CacheAndData` / `GeneralSettings` / `AppearanceSettings` / `DiagnosticsAbout` | `Settings` |
| `RegexReplacementRules` | `ImportTextSettings` |
| `CacheManagement` | `CacheAndData` |
| `PlayerRoute` | 当前 `PlayerRoute.ReturnRoute` |
| `Library` / `Settings` | 无父级，返回操作不导航 |

播放页来源语义：

- 从书库点击书籍进入播放：`ReturnRoute = Library`。
- 从书籍详情点击章节进入播放：`ReturnRoute = BookDetailsRoute(同一 BookId)`。
- 从 Shell 左下角“正在播放”入口进入：在导航到 Player **之前**捕获当前完整 `CurrentRoute`，并作为 `ReturnRoute`；因此从设置子页进入时能够返回同一设置子页。
- 已经位于 Player 时不得再构造 `Player -> Player` 的 ReturnRoute 链。

## 2. 状态与优先级

- `[ ]`：未开始。
- `[-]`：进行中。
- `[x]`：已完成；任务末尾必须附简短“完成成果”。
- `[!]`：存在阻塞，必须记录可复现原因。
- `P0`：影响导航正确性、路由参数、Dirty State/guard 或核心页面返回行为。
- `P1`：长期维护性、冗余清理或补充性测试。

Codex 完成任务后保留条目并标记 `[x]`，在对应任务末尾追加“完成成果”；不得自行删除其它任务。只有后续新的规划阶段才允许再次清空或重写 Backlog。

## 3. Codex 执行规则

1. 默认一次只执行一个编号任务；完成后停止，不自动开始下一项。
2. 开始 T001 前至少阅读：`AGENTS.md`、`docs/02_TECH_STACK_AND_ARCHITECTURE.md`、`docs/06_UI_AND_USER_FLOWS.md`、`docs/09_TESTING_AND_QUALITY.md`、`docs/11_DECISIONS_RISKS_OPEN_QUESTIONS.md`，以及 `src/NovelSpeaker.App/Shell/Navigation/` 下全部导航代码。
3. 每个任务开始前重新审计直接调用方，不仅按本文列出的文件机械修改；优先使用 `rg "GoBackAsync|\.GoBack\(|new PlayerRoute|PlayerRoute\(" src tests` 建立真实调用清单。
4. 不新增通用导航历史集合、Back/Forward 栈、页面缓存服务、Service Locator 或“最后访问页面”字典。
5. `CurrentRoute` 必须保存完整强类型 `AppRoute`；不能只保存 `AppRouteId` 后在返回时重新猜测参数。
6. 只有实际导航成功后才能提交新的 `CurrentRoute`。导航 guard 拒绝、取消、异常或 Wpf.Ui 导航返回失败时，当前路由和 Shell 选中态必须保持一致。
7. 参数化路由优先通过类型系统保证完整性。若 `PlayerRoute.ReturnRoute` 在正常入口中理论上必需，优先让构造调用方显式提供，而不是用静默 `null` 掩盖遗漏。
8. `PlayerRoute.ReturnRoute` 只允许表达一次返回目标，不允许指向另一个 `PlayerRoute`，也不递归保存更深的访问链。
9. 返回按钮、`Alt+Left` 与 `Esc` 的导航回退最终必须复用同一个应用级返回入口；但 `Esc` 仍允许被当前选择模式、Popup/Flyout 或其它临时交互先消费。
10. Dirty State / `INavigationGuardService` 仍是唯一离开保护边界；不要在各页面复制确认逻辑，也不要为了返回重构绕过现有 guard。
11. 测试优先验证用户可观察的路由转换、参数和 guard 结果，不冻结私有 helper 形状。
12. 本轮不是视觉任务，不需要生成截图或视觉验收资产。如调试过程中生成任何临时文件、日志、dump 或截图，任务结束前删除并用 `git status --short` 审计。
13. WPF 自动测试继续使用隐藏 Desktop；不得自行设置 `NOVELSPEAKER_TEST_ALLOW_VISIBLE_WINDOWS=1`。
14. 每个任务完成后更新自身状态并写“完成成果”，记录主要实现、专项测试和发现的问题。

## Phase A：导航核心收敛

## [x] T001（P0）：以完整 CurrentRoute + 固定 ParentRoute 替换历史栈式返回

目标：

- 让 NovelSpeaker 的应用导航拥有明确且唯一的当前路由状态。
- 普通页面返回由固定信息架构解析，不再调用 Wpf.Ui `GoBack()`。
- 为后续 Player 动态返回来源建立稳定基础。

实施方向：

1. 审计并调整 `AppRoute.cs`、`IAppNavigator.cs`、`IShellNavigationAdapter.cs`、`ShellNavigationAdapter.cs` 及对应 PresentationTests。
2. 将 Shell 导航状态收敛为完整 `AppRoute CurrentRoute`：
   - 初始路由为 `Library`。
   - `CurrentRouteId` 若仍有调用价值，应改为 `CurrentRoute.Id` 的派生投影，不再成为独立可变状态。
   - `ApplySelection`/Shell 选中态同步不得顺带把参数化当前路由降级成只有 `AppRouteId` 的状态。
3. 用集中式 `ResolveParentRoute(AppRoute)`（具体名称可按现有风格调整）表达普通页面父级，至少覆盖阶段定位表中的全部页面；不要把父级逻辑分散到 Page/ViewModel。
4. 将 `IAppNavigator.GoBackAsync(...)` 收敛/替换为语义明确的 `NavigateBackAsync(...)`：
   - 普通路由解析固定父级后重新走 `NavigateAsync(targetRoute, ...)`。
   - 根路由没有父级时返回 `false`，不隐式跳转到书库。
   - T001 可暂时为 Player 保留清晰的未完成分支/委托点，由 T002 接入 `ReturnRoute`，但不能继续调用 Wpf.Ui `GoBack()`。
5. `NavigateAsync` 先通过 guard，再调用 Wpf.Ui 显式导航；只有框架导航确认成功后才更新 `CurrentRoute` 和一级选中态。失败/取消不能提前污染状态。
6. 检查 `NavigateFromShellAsync` 与 `SynchronizeSelection`：
   - Shell 菜单只能创建无参数一级/设置路由，不能构造 `BookDetails`/`Player`。
   - Wpf.Ui `Navigated`/选中事件可用于同步视觉选中态，但不能成为恢复强类型路由参数的来源。
7. 不新增应用级 back stack。若 Wpf.Ui 内部仍维护自己的页面类型 history，只要应用不读取/依赖它即可；不要为了“清空 history”再引入新的复杂桥接。

专项测试/验收：

- `CurrentRoute` 在成功导航后保存完整 `BookDetailsRoute(BookId)`，而不是只有 `BookDetails` ID。
- guard 拒绝和框架导航失败时 `CurrentRoute` 不变化。
- 固定父级至少覆盖：BookDetails→Library、普通设置二级页→Settings、RegexReplacementRules→ImportTextSettings、CacheManagement→CacheAndData、根路由→无返回。
- 导航适配器的返回路径不调用 Wpf.Ui `GoBack()`。
- Shell 一级选中态在显式返回后仍与目标路由一致。

完成成果：引入完整强类型 `CurrentRoute` 和集中固定父级解析，应用级返回统一为 `NavigateBackAsync` 并通过显式导航完成；guard、取消和框架导航失败均不污染路由状态。`AppRouteNavigationTests`、`GuardedNavigationServiceTests` 及相关 WPF 导航测试通过。

## Phase B：播放页动态 ReturnRoute

## [x] T002（P0）：让 PlayerRoute 显式携带一次性返回目标并迁移全部入口

依赖：T001。

目标：

- 播放页作为唯一动态返回页面，在进入时明确知道“从哪里来”。
- 三种入口都能稳定返回，且不建立历史链。

实施方向：

1. 扩展 `PlayerRoute`，加入强类型 `ReturnRoute`。优先设计成正常构造必须显式提供；如果发现确有不能立即确定来源的合法入口，先确认该入口的产品语义，再使用明确、可测试的 fallback，不能默认依赖历史栈。
2. 对 `ReturnRoute` 建立约束：
   - 不能为 `PlayerRoute`。
   - 必须是已注册、可显式导航的 `AppRoute`。
   - 只保存当前这一次播放页退出时的目标，不保存链表/栈。
3. 全仓审计所有 `PlayerRoute` 创建点并迁移：
   - Library 直接打开书籍：显式 `ReturnRoute = AppRoutes.Library`。
   - BookDetails 点击章节：显式 `ReturnRoute = new BookDetailsRoute(bookId)`。
   - Shell 左下角“正在播放”：先读取并冻结 `_navigator.CurrentRoute`，再导航到 `PlayerRoute(..., ReturnRoute: capturedRoute)`；捕获动作必须发生在 CurrentRoute 被切到 Player 之前。
4. 如果 Shell Footer 入口在当前已经是 Player 时仍可触发，应 no-op/恢复现有 Player，而不是生成 `Player(ReturnRoute = Player)`。
5. 审计 Player 内部所有可能重建/替换 `PlayerRoute` 的章节、段落或会话恢复逻辑；只要仍处于同一次 Player 页面访问，就必须继承原 `ReturnRoute`，不能在切章后丢失来源。
6. 将 T001 的 Player 返回分支接到 `PlayerRoute.ReturnRoute`，仍通过正常 `NavigateAsync` 和 guard 完成显式导航。

专项测试/验收：

- Library→Player→Back = Library。
- BookDetails(book-A)→Player(book-A)→Back = BookDetails(book-A)，并且返回导航携带完整 `BookDetailsRoute("book-A")`。
- Settings/任一设置子页→Shell“正在播放”→Player→Back = 原设置路由。
- Player 内切章/切段后返回目标不变化。
- 不能构造或执行 Player→Player 的 ReturnRoute。
- 不因 ReturnRoute 引入页面实例缓存或第二套历史集合。

完成成果：`PlayerRoute` 现在强制携带经过校验的一次性 `ReturnRoute`；书库、书籍详情和 Shell Footer 入口分别写入固定或捕获的完整返回路由，Player 返回统一走该目标且不会生成 Player→Player 链。补充了路由约束、三类入口和页面生命周期回归测试。

## Phase C：统一返回入口与键盘语义

## [x] T003（P0）：统一 PageHeader、Alt+Left 与 Esc 的返回行为并保持临时交互优先级

依赖：T001、T002。

目标：

- 所有“离开当前页面”的返回行为最终调用同一个 `NavigateBackAsync`。
- `Esc` 既保留关闭临时交互/清除选择的既有能力，也能在没有局部消费者时执行页面返回。
- 修复书籍详情→播放→返回/ESC 后详情页没有 BookId 的实际缺陷。

实施方向：

1. 审计 `PlayerViewModel.BackCommand`、各 `AppPageHeader` BackCommand/Click、`KeyboardShortcutCoordinator` 以及其它 `GoBackAsync` 调用方，将应用级返回统一迁移到 `NavigateBackAsync`。
2. 删除“GoBack 失败就统一 Navigate Library”的旧 fallback。根路由没有父级时应保持原页；只有显式路由规则要求 Library 时才导航 Library。
3. `Alt+Left` 直接表达应用级返回，经过统一 guard。
4. `Esc` 使用清晰的优先级：
   - 当前选择模式、Popup/Flyout、编辑器临时态等已经有明确 Esc 消费者时，先由局部交互处理并停止传播。
   - 没有局部消费者时，调用 `NavigateBackAsync`。
   - 在 Player 中因此按 Esc 与返回按钮得到相同 `ReturnRoute`；在 `Library`/`Settings` 根页则不发生导航。
5. 保证 guard 只执行一次。Dirty State 用户选择“取消离开”时，页面、`CurrentRoute`、Shell 选中态都保持不变。
6. BookDetails 返回后的加载仍以导航传入的 `BookDetailsRoute` 为唯一可靠参数来源；不要通过把 Page/ViewModel 改成 Singleton、保留旧 `LastRequest` 或从播放会话反推 BookId 来掩盖路由缺陷。

专项测试/验收：

- BookDetails(book-A)→Player→返回按钮，详情页收到 `BookDetailsRoute("book-A")` 并正常加载。
- 同一路径使用 `Alt+Left` 与无局部消费者时的 `Esc` 得到相同结果。
- Esc 在章节多选/临时界面可消费时只退出该局部状态，不额外导航。
- Dirty State guard 拒绝返回时不改变路由；确认离开时只执行一次目标导航。
- `Library`/`Settings` 根路由触发返回不跳转、不产生异常。

完成成果：所有页面标题栏、Alt+Left 和无局部消费者的 Esc 均汇聚到 `NavigateBackAsync`，移除失败后跳转书库的隐式 fallback；编辑页显式确认后以 bypass 方式完成单次目标导航，避免 guard 重复执行。新增局部 Esc 消费端口并从焦点祖先解析，使 Player 的选择/进度/菜单状态、缓存章节选择和帮助抽屉优先于页面返回，根路由保持不动。补充快捷键策略、焦点上下文、Player 临时状态和全量 Presentation/WPF 导航回归测试。

## Phase D：回归、清理与质量门禁

## [ ] T004（P0）：删除旧历史返回依赖并完成导航回归与全量门禁

依赖：T001、T002、T003。

目标：

- 证明应用已经从 Wpf.Ui 历史返回语义完整迁移到显式 AppRoute 导航。
- 清理旧 API、低价值兼容逻辑和只服务旧 back stack 的测试。

实施方向：

1. 全仓审计以下模式：
   - `GoBackAsync`
   - `INavigationService.GoBack()` / `.GoBack()`
   - 只保存 `CurrentRouteId` 后反推参数化页面的逻辑
   - 页面/ViewModel 自己保存“上一页面”
   - 为返回行为引入的 Page Singleton、NavigationCache 或 Route stack
2. 删除不再需要的旧 API、fake、测试 helper 和兼容分支；不要保留同义的 `GoBackAsync` wrapper。
3. 补齐稳定回归矩阵，优先放在 `NovelSpeaker.App.PresentationTests`；只有确实需要 WPF DataContext/页面 activation 证据的场景才进入 `NovelSpeaker.App.WpfTests`：
   - 所有固定 ParentRoute。
   - 三种 Player 来源。
   - 参数化 BookDetails 返回。
   - guard 取消/确认。
   - Shell 一级选中态。
   - Esc 局部消费优先级与无局部消费者时的导航 fallback。
4. 至少保留一条能够直接防止本轮原始缺陷复发的集成回归：`BookDetails(book-A) -> Player(book-A) -> Back -> BookDetails(book-A)`，最终详情页必须能够取得正确 BookId，而不仅断言页面类型。
5. 检查文档、测试名称和注释，不再把应用级返回描述为“pop history/back stack”。
6. 清理本轮所有临时调试产物并执行 `git status --short`。
7. 按固定顺序执行完整质量门禁：

```powershell
dotnet restore --locked-mode -r win-x64
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

完成标准：

- 完整门禁 0 失败；Release build 0 warning / 0 error。
- 生产代码不再依赖 Wpf.Ui `GoBack()` 实现应用返回。
- 不存在新的应用级历史栈、页面缓存补丁或参数反推 workaround。
- BookDetails 参数化返回、Player 三类来源、固定设置层级、guard 与 Esc 行为均有稳定自动回归证据。
- 仓库没有截图、dump、临时脚本、manifest、TestResults 诊断副产物或其它本轮临时文件残留。
