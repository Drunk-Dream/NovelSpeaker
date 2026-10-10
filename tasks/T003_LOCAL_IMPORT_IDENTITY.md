# T003 — 重接 Local TXT 导入、身份确认与 CurrentCatalog 提交

## 目标

让 Local TXT 导入完整运行在新的 Book Identity + Local Binding + CurrentCatalog 模型上，并实现“仅自动识别不完整时才确认 Title/Author”的轻量交互。

本任务是 staged breaking migration window 的最后一个允许中间破坏态的任务。

## 必读

- `docs/specs/BOOK_IMPORT.md`
- `docs/specs/BOOK_DATA_MODEL.md`
- `docs/05_DATA_AND_COMPATIBILITY.md`
- `docs/06_UI_AND_VISUAL_SYSTEM.md`（仅与确认面板/反馈相关部分）

## 导入流程

必须形成：

```text
select TXT
→ encoding/text analysis
→ metadata rule extraction
→ metadata identity completeness check
→ optional Title/Author confirmation
→ normalized identity lookup
→ prepare Local content + Catalog snapshot
→ atomic commit
```

### Metadata confirmation

只有当 Title 或 Author 至少一个**没有被元数据规则明确识别**时才弹出确认面板。

- 两者都被规则识别：跳过确认。
- Title 未识别：面板默认使用文件名 stem。
- Author 未识别：默认空，允许用户直接确认。
- 已识别字段预填，用户在确认前可以修正。
- Title 最终不得为空；Author 可以为空。
- Description 不阻塞身份确认。
- 编码低置信度流程先完成，获得可靠文本后再做 metadata 确认；不要做大型多步骤 wizard。

实现应通过明确 result/status 或等价 continuation contract 支持 UI 恢复导入，不把 View/Dialog 引入 Application。

## Book resolution

使用最终确认后的 normalized Title + Author：

- 无 Book → create Book；
- 已有唯一 Book → create/update 该 Book 的 Local Binding；
- 正常 schema 不再存在“多个候选让用户选择”的业务路径；删除旧 RequiresBookSelection / CreateNewBook 等仅为身份歧义服务的复杂度，除非仍有其它真实调用理由。

不要允许强行创建第二本相同 normalized identity 的 Book。

## Local Binding / CurrentCatalog commit

### New Book

- create Book；
- create Local Binding；
- Local Binding becomes Active；
- commit full CurrentCatalog；
- persist Local content pointer/ranges atomically with SQLite coordination。

### Existing Book

- update/create its sole Local Binding；
- 若 Local Binding 是 Active：原子替换 CurrentCatalog；
- 若未来 Local Binding 非 Active：只更新 Local persistent content，不持久保存第二套 Local Catalog；切回 Local 时再解析。

当前只有 Local Source，也不能重新把“Local 永远 Active”写成数据库不可扩展特例。

## Failure / recovery

- staged/final local file 与 SQLite pointer 切换继续遵守现有 journal/recovery 的安全原则；
- 失败保持旧可用内容和旧 CurrentCatalog；
- cancellation 不留下数据库指向半成品文件；
- 不新增重型一次性 recovery framework。

## UI

建立最小 metadata confirmation surface：

- 与现有视觉系统一致；
- 不增加与身份确认无关的设置；
- 不显示冗余状态文案；
- 成功/失败继续复用统一反馈；
- 不新增入库后的 Title/Author edit 入口。

## 测试

核心保护：

- Title+Author 都规则命中 → 无 confirmation；
- 仅 Title 或仅 Author 缺失 → confirmation，默认值正确；
- 两者都缺失 → Title filename fallback + blank Author，可直接确认；
- user correction 后按最终 identity 入库；
- same identity re-import → 同 BookId / sole Local Binding；
- atomic Local content + CurrentCatalog failure preservation；
- no duplicate normalized Book。

不为每一种 Regex 排列增加永久 UI 测试；规则工作台已有测试继续承担规则本身风险。

## 任务结束允许状态

本地导入链路应基本可工作，但 Library/Details/Playback/Delete 的旧 read model 允许等待 T004 最终接回。
