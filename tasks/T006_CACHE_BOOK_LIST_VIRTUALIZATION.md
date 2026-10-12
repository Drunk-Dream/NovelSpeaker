# T006 — CacheManagement 书目选择器的真正虚拟化

- **类型/优先级：** 实施 / P2 Performance。依赖前期任务完成；只修改 WPF 技术容器、必要的布局/绑定和少量局部展示逻辑。
- **验证基线：** T002 在真实 `CacheManagementPage` 隔离 STA host 上测试 10 / 1,000 / 10,000 个合成书目，分别 realized 10 / 1,000 / 10,000 个项目容器；10,000 项单次布局约 12.9 秒、累计分配约 1.2GB（每档仅一次，不能解释为常见用户延迟或 retained heap）。`CacheManagementPage.xaml` 的 `ScrollViewer + ItemsControl` 是非虚拟化结构；章节列表已有 recycling virtualization。

## 已批准技术方向

- 将 CacheManagement 页面**已缓存书目选择器**改成适当的 WPF 可虚拟化/recycling 列表宿主，保证实际滚动 owner、viewport 测量与 item panel 支持虚拟化；不能仅在非虚拟化 ItemsControl 上增加 `VirtualizingPanel.IsVirtualizing=True` 就宣称修复。
- 保持用户可见的卡片布局、选中/当前/悬停/焦点样式、Light/Dark 主题、单击选择、键盘、滚动和空状态行为，特别是页面在重新加载/重新进入时的正确性。**不借机更改 U1 视觉语言**、书籍/章节数据模型、Active Cache 业务逻辑或选书/章节多选语义。
- 目前 10k 章节的 virtualization/staged projection 继续保留，不能通过关闭/缩小大目录能力换取书目列表的性能改善。不要新增自制 virtualization engine，优先复用 WPF 标准列表与项目现有模板/资源。

## 验证

1. 对比原结构与新结构，使用相同合成书目规模（至少 10、1k、10k）和同等 viewport，在隔离真实 WPF host 测量 realized 容器数量、首次布局、滚动到尾部后的容器数量及创建开销。记录环境、单次/多次样本、测量口径、测试局限；严禁把 `GC.GetTotalAllocatedBytes` 当 retained heap。
2. **必须实际观察到未滚动时 1k/10k 项不会全部 materialize**，并在滚动后正确呈现远端书目、焦点和选中态；若列表改变大小/可见性，还需确认正确重新生成。尽量用数量级/viewport-related 判据，不固定脆弱的容器数量或 FPS 数字。
3. 最小核心自动验证：Cache 页面激活/离开订阅/选择，已缓存书目变动的选择稳定性、Light/Dark 资源与正常章节列表显示。必要时隔离 Desktop WPF smoke；不为每个样式建立永久详细截图断言。人工视觉验收可选，不能阻塞 Codex。
4. 若生成临时截图/对照数据，完成检查后删除源脚本、临时页面/屏幕截图和基准产物。只在 Backlog 留下简洁量化结果与限制，不将私人数据、虚构样本以外的路径加入日志。运行 focused WPF/Presentation tests、Release build、format/diff check。

## 范围之外

- 不修改大型 CacheManagementViewModel 的 `>512` delta/staged replacement、deferred row refresh 和窗口化 decoration；T002 已证明关键行为合理。
- 不更换全局 Design System，不加新的导航状态、业务缓存或页面级后台任务；若默认控件模板导致视觉回退，用现有样式资源适配，而不是另立复杂模板体系。

## 完成

简述方案与受益、关键 WPF 合成数据、回归门禁和限制；标记 T006 `[x]` 并删除临时规格。
