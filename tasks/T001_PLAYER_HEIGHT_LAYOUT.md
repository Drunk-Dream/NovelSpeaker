# T001 — 修复播放页垂直缩放时的全局滚动与控制栏遮挡

**优先级：P0 / 实现任务。** 依赖：无。目标是解决真实可复现的布局缺陷，不对播放页视觉风格进行整体重设计。

## 1. 已知现象与调查线索

用户反馈：在 Player 页，窗口沿垂直方向拉大时中间目录/正文可扩展；拉小时中间区域不能持续收缩，出现全局纵向滚动、底部媒体控制区被挤走或遮挡。该页面本应是独立内部滚动、没有全局页面滚动的三段布局。

当前静态线索（开工先以本地实际代码复核）：
- `src/NovelSpeaker.App/Features/Playback/Components/PlayerView.xaml` 外层 `Grid` 的行是 `Auto / * / Auto`，内容区与内侧 ListBox 已有 `MinHeight=0`、滚动/虚拟化设置。
- `src/NovelSpeaker.App/Features/Playback/PlayerPage.xaml` 的 `PlayerView.Height` 当前显式绑定 Page `ActualHeight`。
- Shell `MainWindow` 含 Wpf.Ui `NavigationView`；其内容宿主的 Measure/Arrange 和全局 ScrollViewer 边界需要调查。窗口当前 `MinHeight=640`；不通过提高它掩盖问题。
- Header 有工具按钮、可变章节管理状态文本；正文右侧有错误状态；Footer 包含媒体按钮/段落 Slider 和可能出现的无 Provider 状态。特别检查这些 Auto 区域在低窗口高度时的实际占用。

以上只是嫌疑点，不是已确定的根因。严禁只删去外部滚动条的视觉样式而让控件仍在屏幕外。

## 2. 目标行为

1. 播放页采用 `顶部必要高度 + 中间剩余高度 + 底部必要高度`。窗口变大/变小时，**中间目录与正文同步双向伸缩**；左右列表仅在自身可用区域滚动。
2. 在现有允许的窗口最小高度及宽度、实际导航栏状态下，Header、必要错误提示及媒体控制的关键操作都可到达；纵向缩小时不得因为一层无界 Measure 使内容撑高全局页面，也不得将 Footer 裁掉。
3. Header/Footer 不能随正文滚动。正文自动居中、用户手动滚动、章节跳转和段落滚动条继续工作；10k 目录虚拟化不受破坏。
4. 其它需要全局滚动的页面（如设置）保持原状；全局导航和页面切换不回归。
5. 如果极端瞬时状态使高度预算真正不足，应由产品约定的窗口最小尺寸和局部内部布局合理处理，不能出现不可操作的底栏。不要随意隐藏必要按钮。

## 3. 实施步骤及技术边界

- 先通过 WPF Layout 实际观察或可控隔离 WPF harness，记录 `MainWindow` 内容 viewport、NavigationView 宿主、`PlayerPage`、`PlayerView`、顶/中/底三区的 `ActualHeight / DesiredSize` 及局部/外部 ScrollViewer 的 viewport/extent。在测试中执行 **高 → 低 → 高 → 低**，定位第一处出现无界高度或意外 MinHeight 的父节点。
- 先考虑消除 Page→View 的强制 Height 绑定或在明确有限高度的祖先中采用正常 Stretch/`*` 测量；是否需要修改共享宿主，必须由真实证据决定。若只涉及 Player，可在 Player 侧局部修正；避免创建全局布局 hack。
- 确保中间左右 `ListBox` 的 `ScrollViewer` 能获得有限高度；控件的可视性、按钮点击与键盘导航保持正常。不要关掉内部滚动条来使测试通过。
- 对错误状态、无 Provider 状态、章节管理状态文案、长书名和定时停止显示等动态 UI 做必要的极端检查，但只修复会造成溢出/遮挡的具体问题。
- 使用既有控件及状态源；不要为页面高度引入 ViewModel 属性、业务层状态或定时器。

## 4. 最小自动验收

- 使用真实隔离 WPF 布局/窗口宿主（如仓库 `tests/TestKit/Wpf`，无用户 Desktop）验证默认与最小允许尺寸，以及高/低反复调整：底部媒体控制实际边界在 Player viewport 内；顶部可见，中间高度发生适当变化，页面无整体纵向可滚动溢出。
- 左右列表仍能分别滚动，章节选择/段落选择与当前定位没有明显回归；至少覆盖一个可能引起额外高度的动态状态。
- 与修改范围匹配的 focused tests、Release build、`git diff --check`、LF；测试不能仅以 `Visibility` 或 `ClipToBounds` 断言代替可用 viewport 实证。屏幕截图可以作为临时视觉辅助，任务完成清理。
- 完成后在 Backlog 标记 T001 完成并记录根因、最小修复与验证。删除本 task spec。无需等待人工视觉验收。
