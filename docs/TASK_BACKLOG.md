# NovelSpeaker 当前开发 Backlog

## 1. 阶段定位

当前阶段只处理测试体系收口与文档同步，不新增产品功能，也不重新设计已经完成的 UI。

阶段目标：

- 将当前完整自动测试总数从约 1,400 项收敛到 **800 项以下**，优先删除迁移期、重复覆盖和低价值实现细节测试。
- `<800` 仅是本阶段一次性验收目标，不建立永久测试数量上限；后续新增高价值测试允许总数重新超过 800。
- 所有自动 WPF 测试默认不得在用户当前交互桌面显示任何顶层窗口。
- 普通 Page/UserControl 测试优先使用不显示 Window 的 `WpfControlHost`；只有确实依赖 Window、Popup、Focus、Loaded、PresentationSource 或 HWND 生命周期的测试才进入窗口宿主。
- 必须使用真实 Window 生命周期的测试默认运行在隔离的隐藏 Windows Desktop 中；隔离环境建立失败时必须 fail closed，不得回退到用户当前 Desktop。
- 只有用户在当前任务中明确允许可见窗口时，Codex 才能启用交互式窗口调试；视觉截图生成本身不构成该授权。

稳定测试原则以 `09_TESTING_AND_QUALITY.md` 为准。本文件只描述实施顺序、任务边界和自动验收。

## 2. 状态与优先级

- `[ ]`：未开始。
- `[-]`：进行中。
- `[x]`：已完成并通过自动验收。
- `[!]`：存在阻塞，必须在任务结果中记录可复现原因。
- `P0`：测试安全边界、默认无可见窗口、完整质量门禁。
- `P1`：测试去重、职责收敛和低价值测试清理。

## 3. Codex 执行规则

1. 默认一次只执行一个编号任务；完成后停止，不自动开始下一项。
2. 执行前至少阅读：
   - `AGENTS.md`
   - `docs/09_TESTING_AND_QUALITY.md`
   - 当前任务涉及的生产代码、现有测试和 `tests/TestKit`。
3. 本阶段不通过“把多个独立行为塞进一个 `[Fact]`”或其它只改变测试发现数量的方式凑 `<800`。允许在同一稳定契约下合理合并重复参数场景，但必须保持失败定位清晰。
4. 测试精简按价值排序：先删除迁移期测试、公共视觉合同的页面级重复验证、只验证 getter/setter 或属性转发的测试、旧接口/compat wrapper 测试和重复 helper；缺陷回归、migration、缓存身份、并发/取消、路径安全、脚本沙箱、TTS parser/compiler、损坏数据/音频和关键生命周期测试优先保留。
5. 不为了达到数量目标机械削减 `Application` 或 `Infrastructure` 的高价值行为测试。若 WPF/Presentation 精简后已经 `<800`，立即停止按数量继续删除。
6. 默认测试命令不得设置任何允许可见窗口的环境变量。目标环境变量为 `NOVELSPEAKER_TEST_ALLOW_VISIBLE_WINDOWS=1`；现有 `NOVELSPEAKER_TEST_SHOW_WINDOWS=1` 属于待删除旧入口，同样不得由 Codex 自行设置。
7. 只有用户在当前任务中明确允许测试显示窗口时，Codex 才能设置 `NOVELSPEAKER_TEST_ALLOW_VISIBLE_WINDOWS=1`。授权只对该次明确任务有效，不跨任务继承。
8. `NOVELSPEAKER_GENERATE_VISUAL_ARTIFACTS=1` 只允许生成确定性视觉产物，不代表允许在用户 Desktop 显示窗口。
9. 每个任务都必须更新直接受影响的测试和必要文档，但不得顺手重写无关产品文档。
10. 每个任务完成后将状态改为 `[x]`，在任务末尾追加简短“结果”，记录删除/合并类别、各测试项目数量、实际验证命令和任何剩余风险。
11. 用户要求提交时，仍按可回溯性拆分多个原子提交，不把整个编号任务机械压成一个大提交。

## 4. 测试数量口径

阶段基线以完整 `dotnet test -c Release --no-build` 实际发现并执行的测试为准。当前文档基线约为：

| 测试项目 | 基线 |
| --- | ---: |
| `NovelSpeaker.Domain.UnitTests` | 2 |
| `NovelSpeaker.Application.UnitTests` | 219 |
| `NovelSpeaker.Infrastructure.IntegrationTests` | 348 |
| `NovelSpeaker.App.PresentationTests` | 407 |
| `NovelSpeaker.App.WpfTests` | 443 |
| **总计** | **1,419** |

说明：

- 基线只用于衡量本阶段收敛幅度；实际执行任务前应重新记录一次完整测试结果，若源码已有变化则以重新记录的结果为准。
- 阶段最终要求是完整测试全部通过且总数 `<800`。
- 不新增 CI、架构测试或其它永久机制限制仓库未来测试总数不得超过 800。

## [ ] 1（P0）：加固 WPF Test Host，默认隔离所有可见窗口

目标：

- 将当前“真实 `Window.Show()` + 移到虚拟屏幕外”的默认策略改为隔离 Windows Desktop。
- 普通自动测试即使必须真实执行 `Window.Show()`、Popup、Focus 或 HWND 生命周期，也不得把窗口显示在用户当前交互 Desktop。
- 只有显式用户授权的调试运行才允许可见窗口。

实施：

- 在 `tests/TestKit/Wpf` 建立唯一的 Windows Desktop 隔离边界；共享 STA Dispatcher 在线程进入 WPF 初始化前绑定到专用隐藏 Desktop。
- 默认模式创建独立测试 Desktop，并让 `WpfTestHost`、`WpfWindowHost`、视觉渲染宿主和临时 Popup 都运行在该 Desktop。
- 隔离 Desktop 创建/绑定失败时直接让测试宿主初始化失败；禁止静默退回当前交互 Desktop。
- 将可见调试开关统一为 `NOVELSPEAKER_TEST_ALLOW_VISIBLE_WINDOWS=1`；删除 `NOVELSPEAKER_TEST_SHOW_WINDOWS` 的实现与文档入口，不长期保留双变量兼容。
- 可见调试模式只改变 Desktop/显示策略，不绕过窗口清理、失败诊断、测试串行化和资源释放。
- `WpfControlHost` 继续作为无 Window 的默认页面/控件宿主；对不依赖窗口生命周期的测试不得为了方便改用 `WpfWindowHost`。
- 扩展架构守卫：
  - `NovelSpeaker.App.WpfTests` 不得直接调用 `Window.Show()`、`ShowDialog()`、自行创建 STA Dispatcher/Thread 或直接调用 Win32 Desktop 切换 API。
  - 真实 `Window.Show()` 与 Desktop API 只允许出现在指定 TestKit 宿主实现中。
  - 测试代码不得读取/设置旧的 `NOVELSPEAKER_TEST_SHOW_WINDOWS`。
- 为 TestKit 增加专项测试/可自动验证的契约，至少覆盖默认隔离、显式可见模式分支、fail-closed 初始化和窗口清理边界；测试不得依赖人工观察“有没有弹窗”。

验收：

- 不设置任何可见窗口环境变量时，完整 WPF 测试可运行，并由自动契约证明 WPF Dispatcher 绑定到非交互测试 Desktop。
- TestKit 隔离初始化失败的受控场景能得到明确失败，不会退回当前 Desktop。
- `rg`/架构测试证明普通 WPF 测试没有直接 `Show()`/`ShowDialog()`/Desktop API 绕过路径。
- `NOVELSPEAKER_TEST_SHOW_WINDOWS` 在源码、测试和稳定文档中零引用。
- 定向 WPF/TestKit 测试和完整质量门禁通过。

## [ ] 2（P1）：精简 WPF 测试并收敛 UI 契约所有权

目标：

- 以 `NovelSpeaker.App.WpfTests` 为主要削减对象，删除 UI 统一与迁移阶段留下的重复视觉/结构测试。
- 保留真正依赖 WPF visual tree、资源、布局、主题、Focus、Popup、窗口生命周期和关键交互的契约。

实施：

- 按测试文件和契约族盘点 WPF 测试，标记：保留、合并、删除、迁往 Presentation/TestKit。
- 公共视觉语义只在资源/控件族层验证一次；页面不重复验证公共 Button、Icon、ToggleSwitch、Typography、Surface、Input 等已经由共享契约覆盖的内部属性。
- 页面级 WPF 测试只保留页面自身结构、命令/绑定边界、键盘/选择/滚动、关键几何下限、Automation、主题切换和该页面独有交互。
- 删除 UI 迁移过程中的 Legacy 清零、旧资源替换、逐页面同义样式存在性等历史性测试；最终“无 Legacy/无旧聚合资源”保留一处架构契约即可。
- Style Gallery 和正式页面截图相关测试保留少量 registry/manifest/可重复渲染契约，不把每个展示场景都长期拆成独立回归 case。
- 能使用 `WpfControlHost` 的 Page/UserControl 测试迁离 `WpfWindowHost`；只有真实生命周期需求才保留窗口宿主。
- 删除重复 visual-tree helper/setup，跨测试共用能力回收到 `tests/TestKit/Wpf`，但不得建立新的万能 helper。

验收：

- WPF 测试数量相对任务开始时显著下降，且删除清单能按“迁移期/重复合同/低价值实现细节”解释，而不是按数字随机删除。
- `docs/13_VISUAL_DESIGN_SYSTEM.md` 中的最终视觉边界仍有对应自动守卫，但同一合同没有大量页面级重复测试。
- WPF 定向测试、Presentation 架构测试和完整质量门禁通过。
- 默认运行不设置可见窗口授权变量。

## [ ] 3（P1）：精简 Presentation 测试并去除属性转发型重复覆盖

目标：

- 以 `NovelSpeaker.App.PresentationTests` 为第二主要削减对象，保留用户可观察状态转换和 presentation boundary，删除迁移/重构期累积的低价值细粒度测试。

实施：

- 盘点 ViewModel、Controller、Coordinator、Navigation/Activation 测试，优先保留状态转换、Command 启用、Dirty State、取消/迟到结果、页面生命周期、选择模型、滚动协调和错误投影。
- 删除只验证简单 getter/setter、构造参数赋值、无分支属性转发或与更高层契约完全重复的 case。
- 对 TTS/Chapter/Regex 等共享 Workbench 行为使用共享 contract/factory 覆盖共性，各 Feature 只保留自身差异；不得为减少计数把互不相关行为塞入单一测试。
- 将纯 presentation 行为从 WPF 测试中迁入本项目时，以最终总测试价值为准；如果迁移只会产生重复覆盖则直接删除旧 WPF case，不机械一一重建。
- 清理重复 fake/helper，已有 `tests/TestKit` 能表达的跨项目测试资产不重复实现。

验收：

- Presentation 测试数量相对任务开始时显著下降，同时关键导航、activation、Dirty State、选择、缓存/导出投影和错误处理仍有明确回归保护。
- 不新增 WPF 依赖或真实技术 adapter 依赖。
- Presentation 定向测试、相关 WPF 测试和完整质量门禁通过。

## [ ] 4（P0）：跨项目复查并完成 `<800` 阶段收口

目标：

- 在前两轮主削减后复查完整测试体系，只做达到 `<800` 所必需且有明确重复证据的剩余清理。
- 完成数量、无可见窗口和文档一致性的阶段验收。

实施：

- 重新执行完整测试并记录五个测试项目的实际数量。
- 若已经 `<800`，不得继续为了更低数字删除高价值测试。
- 若仍 `>=800`，优先继续寻找跨 WPF/Presentation 的重复契约、迁移期残留、重复 Theory 数据和低价值实现细节测试；只有有明确重复证据时才触及 Application/Infrastructure。
- 复查 migration、缓存身份/朗读清单、播放/主动缓存/导出状态机、并发/取消、路径安全、脚本沙箱、TTS parser/compiler、损坏数据/音频 fixture 和关键 WPF 生命周期等受保护覆盖没有因精简丢失。
- 复查 `AGENTS.md`、`docs/09_TESTING_AND_QUALITY.md`、`docs/10_ENGINEERING_CONVENTIONS.md`、README 和 TestKit 实现术语一致；若实现过程中发生必要设计调整，只更新直接相关稳定文档。
- 不新增“测试总数 <=800”的永久 CI/架构门禁。

验收：

- 完整质量门禁按固定顺序通过：

```powershell
dotnet restore --locked-mode -r win-x64
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

- 最终完整测试总数 `<800`，并在任务结果中记录各项目数量和总数。
- 默认测试运行没有设置 `NOVELSPEAKER_TEST_ALLOW_VISIBLE_WINDOWS=1`，且自动守卫证明 WPF 测试使用隔离 Desktop/无 Window 控件宿主边界。
- 受保护的关键回归类别仍有自动覆盖。
- 稳定文档与最终实现一致，当前 Backlog 不包含需要人工视觉验收才能关闭的任务。