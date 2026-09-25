# T008：重构“语音服务”管理 UI 并统一插入槽拖拽排序

## 目标

把“设置 → TTS 规则”升级为 Provider 管理页，同时修复所有现有规则页拖拽排序中同一位置由两个卡片边界重复表示的问题。

## 语音服务页

保持桌面双栏布局：

```text
Provider 列表 | Provider Editor Host
```

要求：

- 设置入口和页面标题改为“语音服务”。
- Provider 不按类型分组。
- 左侧按统一 SortOrder 展示。
- 点击列表项只改变右侧编辑对象，不改变 CurrentProvider。
- 左侧 Selection 表示“正在编辑”。
- CurrentProvider 用独立轻量状态图标/标识表示，不使用“当前”文字，图标不可点击。
- 移除旧 Enabled Toggle。
- HTTP Provider：支持编辑、复制、单项导出、删除、排序。
- Microsoft Edge 若已经存在：允许选择配置和排序，但不显示复制/导出/删除等无意义动作。
- 页首保留“新建”“导入”；目前新建直接创建 HTTP Draft，不显示 Provider Type 选择器。
- HTTP 模板帮助只属于 HTTP Editor。
- 保留现有 Draft/Dirty/Save/Cancel/Test 导航保护生命周期。
- 删除 CurrentProvider 时清空 CurrentProvider，不自动 fallback。

## HTTP Editor

按长期规范展示：

- 名称
- 请求 URL / 模板
- GET / POST
- Header 键值列表
- POST Body
- 请求频率限制：最多 N 次 / M 毫秒
- 试听 / 取消 / 保存

不再显示：

- Enabled
- 单独 Content-Type 字段
- `2/1000` 文本格式“并发限制”
- Legado 兼容说明

## 统一拖拽排序

把共享拖拽反馈改成“插入槽位”：

```text
slot
Item A
slot
Item B
slot
```

要求：

- N 个可见项只有 N+1 个可见 slot。
- 插入线位于相邻卡片之间的 gap。
- A.After 与 B.Before 不得继续表示两个等价候选。
- 顶部、底部各有唯一 slot。
- 保留已有边缘自动滚动。
- Provider、章节规则、正则替换规则全部使用相同交互原则。
- 不为此建立万能业务列表抽象；只共享真正稳定的拖拽/slot primitive。

## 隐藏 Provider 的排序映射

可见列表只是完整排序投影。

采用以下语义：

- 隐藏 item 不因为隐藏而改变 SortOrder。
- 插入两个可见项之间 → 插到“后一张可见项”之前。
- 插入可见列表末尾 → 放到最后可见项之后、其后隐藏项之前。
- 插入可见列表顶部 → 放到第一张可见项之前；如果完整列表更前方已有隐藏项，则那些隐藏项仍保持在它之前。
- 显式拖动可见 Provider 可以跨过隐藏 Provider，因此双方相对顺序允许变化。
- 隐藏项不产生不可见命中区域或横线。

## 自动验收

这是 UI/交互为主的任务，不为横线像素、Margin、颜色或 Visual Tree 形状增加永久测试。

如果把 reorder mapping 提取成稳定纯逻辑，可保留极少量长期行为测试：

- 首/尾插入；
- 相邻项只有一个逻辑 slot；
- 隐藏 Provider 的完整排序映射。

视觉/XAML 验证可以使用临时测试、Style Gallery 或截图；任务完成前必须删除截图、脚本和临时测试。

运行受影响 focused tests、Release build、format。

## 完成要求

更新 Backlog、清理临时资产、删除本任务文件，不等待人工验收。
