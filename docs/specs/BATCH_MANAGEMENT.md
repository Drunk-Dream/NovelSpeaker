# 页面级批量管理与选择规范

## 1. 定位

本规范定义 NovelSpeaker 普通业务页面的统一批量管理语义。

目标不是把所有页面都变成文件管理器，而是明确区分：

```text
Normal Mode
= 使用对象

Management Mode
= 管理对象
```

适用页面：

- Library；
- Playback 的章节目录；
- Speech Provider 管理页；
- Chapter Rules；
- Regex Replacement Rules；
- File Name Metadata Rules；
- Text Header Metadata Rules；
- 后续具有同类对象批量操作需求的普通页面。

明确例外：

- `CacheManagement` 继续保持现有文件管理器式 Extended Selection，不强制进入本规范的页面级 Management Mode。

## 2. Selection scope

Selection scope 必须是页面级、同类对象级。

规则：

- 不跨页面保存选择。
- 离开页面时退出 Management Mode 并清空 selection。
- 不同对象类型不得混在一个 selection scope。
- Provider 与 Rules 分属不同 scope。
- 不同 Rule workspace 也各自拥有独立 scope。
- selection 事实由稳定 item key 保存，不依赖 WPF container 生命周期。

## 3. 进入 Management Mode

至少支持两类入口：

1. 显式、可发现的“选择 / 批量管理”入口；
2. 正常模式下 Ctrl/Shift 点击项目。

进入模式后：

- 保留当前页面上下文；
- 不因为进入模式自动执行批量动作；
- 不因为进入模式自动清空页面已有业务 current 状态；
- 如果页面存在 Dirty Draft，必须复用原有保存 / 放弃 / 取消保护，不允许隐式丢弃。

是否在进入时自动选中触发项：

- Ctrl/Shift 点击进入：按照该手势产生的 selection 结果进入。
- 显式管理入口进入：可以以空 selection 开始，不要求默认选中当前项。

## 4. Management Mode 中的点击

进入 Management Mode 后：

- 普通左键点击：toggle 当前项目 selected/unselected。
- Ctrl/Shift 可以继续用于桌面式 toggle/range selection。
- 普通点击不得触发该项目在 Normal Mode 下的主动作。
- 双击不得绕过 Management Mode 打开/激活对象。
- 点击页面空白处不得自动退出模式。
- SelectedCount 变为 0 不自动退出模式。

退出方式：

- Page Header 的明确 Cancel/退出；
- 页面导航离开；
- 页面本身被销毁。

## 5. Normal Mode 中的对象动作

退出 Management Mode 后恢复页面原行为，例如：

- Library card → 打开 Book；
- Playback chapter → 跳转章节；
- Provider → 切换正在编辑的 Provider；
- Rule → 切换正在编辑的 Rule。

Current 与 Selected 是不同状态：

- Current 表示系统正在使用/播放的对象；
- Selected 表示当前管理选择集；
- 两者允许叠加，视觉遵守 `docs/06_UI_AND_VISUAL_SYSTEM.md`。

## 6. Select All

Select All 只作用于：

- 当前页面；
- 当前 selection scope；
- 当前可见；
- 当前可管理的对象。

不跨：

- 页面；
- 分页/未加载的远端集合；
- 隐藏 filter result；
- 不属于当前 scope 的对象。

如果当前页面没有可管理项，Select All 保持空 selection，但 Management Mode 不因此退出。

## 7. Filter / Search / Refresh

selection 必须只包含当前可见且仍可确认是同一对象的项目。

### 可以保留

- 排序变化；
- projection rebuild 后 stable key 相同且仍可见；
- same-page refresh 后对象 stable identity 明确未变。

### 必须移除

- search/filter 让条目不可见；
- 数据变化让条目不再属于当前集合；
- stable identity 无法确认；
- 对象被删除。

Selection controller 应在 visible/manageable item set 更新时主动 reconciliation，不让隐藏 selected item 继续参与批量动作。

## 8. Right Click / Context Menu

Management Mode：

- 右键已 selected item → Context Menu 作用于整个当前 selection。
- 右键未 selected item → selection 先切换为只选该 item，再显示单项 Context Menu。
- Context Menu 不放“退出管理模式”；退出属于页面级动作。

Normal Mode：

- 保持页面原有右键语义。
- 如果普通右键动作需要共享 batch command，可以由同一业务 command owner 提供，但 selection 行为不得偷偷切换到跨页面状态。

## 9. Page Header

进入 Management Mode 后，Page Header 临时投影管理状态。

至少表达：

- selected count；
- 当前页面支持的 batch actions；
- Select All；
- Cancel。

Page Header 与 Context Menu 应调用同一业务动作，但不要求完全相同的按钮排列。

退出 Management Mode 后恢复普通 Page Header。

## 10. Batch execution

批量操作按 item 独立处理，不因为一个 item 失败而终止剩余项。

结果分类：

```text
Succeeded
Skipped
Failed
```

最终给出 batch-level aggregate result。

规则：

- destructive action：整个 batch 只确认一次；
- non-destructive action：默认不确认；
- 不为每个 item 重复弹 dialog；
- 支持项正常执行；
- 不支持项跳过，并在结果中说明；
- 单项内部需要事务/原子性的操作仍应保持单项原子；
- 不要求整个 batch 跨所有 item 形成一个巨大事务。

## 11. Batch Export

一次用户动作只产生一个 batch-level export result。

允许内部结构：

```text
chosen directory/archive
├─ item A
├─ item B
└─ item C
```

禁止：

- 为每个 selected item 连续弹独立 Save dialog；
- 失败一个 item 后丢弃所有已成功 item，除非具体格式本身要求整体原子。

当前 Library 对 Local Source 的 Book export 可以输出每 Book 一个 UTF-8 TXT 到同一批次目标目录。未来非 Local Source 若无法提供完整正文，应由能力判断跳过，而不是强迫整个 selection 不可执行。

## 12. Delete

删除：

- batch 级一次确认；
- 确认后执行真实删除；
- 当前不做 Undo；
- 当前不做 recycle bin；
- partial failure 继续处理其它 selected items；
- 删除完成后 selection reconciliation。

Provider / Rule 的删除不为了维持旧运行态增加隐藏保护：

- 删除 CurrentProvider 被允许，删除后 CurrentProvider=None；
- 删除当前正在编辑/匹配的 Rule 被允许，后续由正常状态逻辑处理；
- 不自动选择另一个对象作为业务 fallback，只为了让旧状态“看起来不断”。

## 13. Page-specific contracts

### Library

Management actions：

- Select All；
- Export；
- Delete；
- Cancel。

Normal click 打开 Book。

### Playback Chapter Catalog

Management Mode 是“章节管理”，不是“主动缓存模式”。

当前 actions：

- Select All；
- Cache；
- Cancel。

未来新增章节批量动作复用同一模式。

Cache action 幂等：

- fully cached → skip；
- partially cached → fill missing；
- uncached → cache normally。

Current chapter 仍用 Current rail；selected chapter 使用 Selected surface；可叠加。

### Speech Provider

Management actions：

- Select All；
- Export；
- Delete；
- Cancel。

不支持 Export/Delete 的内置 Provider 可以被选中；执行动作时 skip/report。

进入 Management Mode 不能隐式丢弃 Provider Editor Dirty Draft。

### Rules

每个 Rule workspace 独立 scope。

Management actions：

- Select All；
- Export；
- Delete；
- Cancel。

进入 Management Mode 不能隐式丢弃当前 Rule Editor Dirty Draft。

### CacheManagement

保持例外：

- 继续使用文件管理器式 Extended Selection；
- 不要求显式 Management Mode；
- 不因本规范重写既有缓存页 selection interaction。

## 14. Shared implementation boundary

可以复用：

- stable-key selection engine；
- Management Mode lifecycle；
- Select All；
- visible-set reconciliation；
- right-click selection semantics；
- selected count projection。

不要建立：

- 包含所有页面业务动作的万能 BatchManager；
- 跨页面全局 SelectionService；
- 让 Shared 依赖 Provider/Book/Rule feature model 的泛型业务容器。

共享层只拥有稳定交互 primitive，业务 action 由对应 Feature/Application owner 实现。

## 15. 测试合同

永久测试只保护核心交互边界：

- mode lifecycle；
- hidden item 不继续参与 selection；
- zero selected 不自动退出；
- Normal vs Management Mode 的主动作隔离；
- destructive batch 单次确认与 partial success；
- CacheManagement 例外不被统一模型误改。

不为：

- 精确按钮排列；
- 文案；
- pixel；
- Visual Tree；
- 具体 shared controller 私有结构

建立永久测试。

可使用临时 WPF/截图验证视觉，完成后删除。
