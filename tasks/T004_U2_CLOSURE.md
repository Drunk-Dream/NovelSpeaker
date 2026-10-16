# T004 — U2 布局、阅读交互与质量收口

**优先级：P1 / Closure。** 依赖：T001–T003。目标是确认 U2 真的解决窗口问题，且没有为体验改善引入架构复杂度和多余测试。

## 1. 差异审查

- 对照 T001–T003 全部源代码、测试和文档 diff，检查 WPF layout owner、绑定和测量边界是否清晰，Splitter 是否只维护最少状态；没有重复控制同一列宽的 ViewModel、code-behind 和 settings 多套 owner。
- 检查消除死代码/多余 setter、只为兼容旧 XAML 保留的 wrapper、不必要 timer、Async fire-and-forget、事件未解除、布局重新进入时 handler 重复注册与意外 Dispatcher 无界工作。
- 确认没有隐藏关键控件、提高 `MinHeight` 或大面积 Clip 作为掩盖溢出的手段；其它页面的全局滚动与 Page navigation 保持不变。
- T003 发现的新问题仅对已获授权且事实明确的低风险修复纳入；超范围建议保留为候选，不在阶段结尾顺手扩张。

## 2. 跨场景验证

至少覆盖：
- 窗口最小高度/默认高度/更高，反复垂直拉大与缩小；底部按钮、段落进度始终在 viewport 内；无播放页全局纵向滚动；两个内侧 ListBox 可分别滚动。
- 最小宽度/默认宽度/横向拉宽，导航栏展开和收起，分隔条拖动/键盘调节；目录临时压缩、重新扩大恢复偏好；切页/重启恢复配置。
- 默认、超长标题/正文、错误/准备/无 Provider/空章节、多选与主动缓存状态；暗色/浅色，focus/hover，自动定位和用户手动浏览。
- 章节与段落定位、播放/暂停/恢复、语速/Provider、Slider、设置页与书库页面的正常滚动；10k catalog 虚拟化和没有因大小变化重计算/持久化 Speech Plan。

优先使用项目现有的隔离 WPF Desktop/TestKit，测试窗口不可显示在用户真实 Desktop。视觉审查截图或用于尺寸比对的一次性 harness 用后清理。只留核心可用性/偏好行为测试；所有细碎布局诊断/采样必须移除。

## 3. 文档与质量门禁

- 将 U2 **最终稳定规则** 精简写入 `docs/06_UI_AND_VISUAL_SYSTEM.md` 的 Player 一节：三段高度模型、无播放页全局滚动、目录/正文内部滚动、轻量可拖动分隔条、可用最小宽度、首选目录固定宽度及可恢复宽度偏好。仅实际存在语义改变时再检查 `docs/02_RUNTIME_AND_NAVIGATION.md`，不要将任务过程和诊断细节写进长期文档。
- 运行仓库完整的 locked restore、format verify、Release build、保留核心的全量 test、`git diff --check`、LF 与临时产物检查；若环境无法完成 GUI/全量门禁，严格区分“未执行”和“通过”，记录阻碍和已执行的替代验证，不凭空断言质量门禁全绿。
- `TASK_BACKLOG.md` 四项任务最终标 `[x]` 并简要记录证据、结果、限制；删除 `tasks/T004_U2_CLOSURE.md` 及前序已完成 task specs。`U2_PLAYER_UX_AUDIT.md` 等用户手动管理的**未提交临时报告**可按用户既有管理规则保留在仓库根目录，但不得暂存/提交；其它临时截图、fixture、harness、生成数据全部清理。
- 不等待人工验收，不擅自推送 GitHub。
