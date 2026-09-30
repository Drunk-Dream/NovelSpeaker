# T004：增加规则与 Provider 批量交换

## 目标

把现有单项 ContextMenu 导出扩展为一致的桌面多选批量交换，同时让导入格式天然支持同类型多项文档。

## 权威参考

- `docs/06_UI_AND_VISUAL_SYSTEM.md`
- `docs/specs/BOOK_IMPORT.md`
- `docs/specs/REGEX_REPLACEMENT.md`
- `docs/specs/HTTP_TTS.md`
- `docs/05_DATA_AND_COMPATIBILITY.md`
- `AGENTS.md`

## 范围

统一支持：

- ChapterRule；
- RegexReplacementRule；
- FileNameMetadataRule；
- TextHeaderMetadataRule；
- 可分享的 HTTP Speech Provider。

Microsoft Edge 等不可分享 Provider 不进入批量导出选择。

## 选择交互

复用 T001 全局 Selected 视觉，并尽量复用已有缓存管理/主动缓存成熟的 Ctrl/Shift 范围选择逻辑：

- 普通点击：单选并切换 Editor；
- Ctrl：toggle 选择；
- Shift：range 选择；
- Ctrl/Shift 点击不切换右侧 Editor；
- 右键已选中项不破坏选择集；导出动作作用于选择集；
- 不为每个规则页面复制一套独立 selection algorithm；但也不要为了复用建立万能业务列表框架。

## 批量导出

- 单项/多项共用相同版本化 envelope；
- 多项按稳定可见顺序写入一个文档；
- 文件导出只弹一次保存对话框；
- 剪贴板导出只复制一个 JSON 文档；
- 保留 Provider 凭据警告；规则不新增无意义敏感信息提示。

## 批量导入

- 对应导入必须接受同格式一项或多项；
- 逐项校验，单项错误不回滚其它合法项；
- 完全重复跳过；同名不同内容按各类型现有唯一命名/追加语义处理；
- 按文档顺序追加；
- 导入后不切换当前 Editor/CurrentProvider。

如果现有已经支持多项 JSON，优先扩展现有 codec/workspace，不重新发明第二种格式。

## 不要做

- 不把不同规则类型混装成一个万能 envelope。
- 不让 Edge 因批量能力被错误支持导出。
- 不用 WPF ListBox container 自身作为持久选择真值。
- 不增加批量删除等本轮未要求能力。

## 核心测试

至少保护：

- Ctrl toggle / Shift range；
- 修饰键选择不切 Editor；
- 单项和多项导出格式都可重新导入；
- 多项中一个非法项不阻塞其它合法项；
- Provider 导出只包含可分享 HTTP Provider；
- 凭据在 Provider 导出中完整保留，且警告仍出现。

阶段完成前执行完整质量门禁。
