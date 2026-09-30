# T005：实现第一版配置备份与恢复

## 目标

提供本地、私人、完整的配置迁移能力，不把分享型 Provider/Rule 导入导出误当成备份恢复。

## 权威参考

- `docs/05_DATA_AND_COMPATIBILITY.md`
- `docs/00_PRODUCT_AND_SCOPE.md`
- `docs/06_UI_AND_VISUAL_SYSTEM.md`
- `AGENTS.md`

## UI

在 Settings → “缓存与数据”中加入轻量的“配置备份/恢复”入口或操作区；不要新建复杂迁移向导。

备份前明确提示：文件包含 HTTP Provider 的 API Key、Token、Cookie 等敏感凭据，应作为私密文件保存。

恢复前明确提示：会替换当前配置，但不会修改书籍、阅读进度和缓存。

## 备份内容

必须包含：

- App Settings；
- Speech Providers 的完整持久配置、排序和恢复引用所需身份；
- CurrentProvider；
- Chapter Rules；
- Regex Replacement Rules；
- File Name Metadata Rules；
- Text Header Metadata Rules。

不包含：

- TXT/Library/Books/Chapters；
- ReadingProgress；
- Speech Plan / Audio Cache / Active Cache；
- Logs / Telemetry / Diagnostics；
- 临时文件。

## 文件合同

- 使用 NovelSpeaker 自有 versioned backup schema；
- 第一版可以是明文 JSON/单文件格式，不要求加密；
- 不要求使用分享型 Provider/Rule envelope 作为顶层格式；可以内部复用稳定 codec，但必须保留完整恢复所需身份；
- 明显损坏或更高且不兼容 schema 在任何写入前失败。

## 恢复语义

恢复是**替换配置快照**，不是 merge：

1. 完整解析/校验备份；
2. 形成恢复计划；
3. 用户确认覆盖；
4. 协调各正式 store 写入；
5. 失败时通过事务或补偿避免留下半旧半新状态；
6. 成功后通过现有 typed change/invalidation 边界刷新运行态。

不要直接操作 Book/Cache/Diagnostics 数据。

## 持久化约束

优先只读取/写回现有正式 store，不新增备份专用数据库表。如果确实需要新增持久 schema 或持久状态，先按 `AGENTS.md` 单独取得授权。

## 不要做

- 不加入 WebDAV、远程同步、账号系统。
- 不自动上传备份。
- 不默认脱敏或丢弃 Provider Secret。
- 不把 restore 实现成逐条分享导入导致重复配置。

## 核心测试

至少保护：

- round-trip 恢复设置、Provider、CurrentProvider、排序和四类规则；
- Provider Secret 不丢失；
- Books/ReadingProgress/Cache/Diagnostics 不进入备份且恢复不修改它们；
- 损坏/不支持 schema 不发生部分写入；
- 恢复失败保持旧配置一致或完成补偿；
- 成功恢复后运行时 owner 收到正常配置变化。

阶段完成前执行完整质量门禁。
