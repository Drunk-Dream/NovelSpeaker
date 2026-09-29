# T001：收拢 Button Style 语义与命名

## 目标

在修复具体 Provider Popup 崩溃前，先把当前 Button 样式体系的职责边界整理清楚，使后续页面只选择已有稳定语义，不再根据“看起来要透明/像某页面按钮”临时发明全局 Style Key。

## 权威参考

- `docs/06_UI_AND_VISUAL_SYSTEM.md`
- `docs/08_QUALITY_AND_TESTING.md`
- `AGENTS.md`

## 必须完成

1. 审计 `Shared/Theming/Resources/Styles/Buttons.xaml`、相关 `App.Media.*` / `App.Navigation.*` Button 变体及其真实调用方。
2. 按稳定交互职责分类现有 Style：主要/次要/弱化/图标/危险动作、只承担命令与命中区域的 interaction host，以及确实属于独立控件族的 Media/Navigation 变体。
3. 对职责重复、命名只描述偶然外观、或仅属于单一控件族却位于通用 `App.Button.*` 的样式进行合并、重命名或迁移；具体命名根据真实调用范围决定。
4. `App.Button.Transparent` 不是需要补齐的正式语义，不得为了当前崩溃新增同名兼容 Style。Provider Popup 的最终用法由 T002 按本任务确定的 interaction-host 语义修复。
5. Interaction-host 类 Button 自身不拥有 Hover/Pressed Surface；当内部 Selection/Card 已表达 Hover/Selected/Current 时，保持单一视觉状态 owner。
6. 如果共享 Style Key 被重命名，直接迁移全部生产调用方、Style Gallery 和必要测试，删除旧 Key；不保留 Old/New/V2/compat alias。
7. 保持 Provider Style Bridge 作为 Wpf.Ui 模板边界，不复制标准 Button 完整模板，除非现有 interaction-host 的无视觉模板确有长期必要且已有行为证据支持。

## 非目标

- 不重做整体视觉设计。
- 不统一所有页面局部 Button 几何。
- 不为每个 Style 新增永久视觉测试。
- 不在本任务修复 Player Provider Popup 的具体失效调用点；T002 专门负责该崩溃和回归测试。

## 自动验收

- 所有共享 Button Style 都能用明确交互职责解释，未新增按页面或“透明”等纯外观命名的全局 Style。
- 搜索确认没有为内部资源重命名留下兼容 alias。
- 受影响页面/Style Gallery 能构建；运行与 Button 语义相关的 focused tests。
- 若使用临时视觉验证，任务结束前删除全部截图、脚本和临时测试。
