# 正则替换流水线

## 1. 定位

正则替换用于动态分段后的文本变换，不修改导入后的内部正文，也不重新划分章节。

```text
SourceText
├─ Display pipeline → DisplayText
└─ Speech pipeline  → SpeechText
```

## 2. 规则模型

每条规则至少包含：

- 稳定 Id；
- 名称；
- Pattern；
- Replacement；
- 作用目标：Display、Speech 或 Both；
- Enabled；
- SortOrder。

规则按稳定排序执行；同排序值按稳定次序兜底。

## 3. 执行顺序

```text
normalized chapter text
→ segment
→ SourceText
→ enabled regex rules in order
→ DisplayText / SpeechText
```

正则替换不会改变章节 offset、段落来源范围或阅读位置的事实坐标。

## 4. 空结果

- DisplayText 为空：该段不显示正文，但仍可有 SpeechText。
- SpeechText 为空，或只包含空白、标点、分隔符和装饰符号而没有 Unicode 字母/数字：该段不请求 TTS，并由播放状态机安全跳过；这类段不计入朗读清单、主动缓存或缓存完整度分母。
- 两者都为空：段落仍保留来源位置用于进度/定位，但不形成可见或可听内容。

## 5. 错误与资源限制

- 无效正则在编辑保存前校验。
- 运行时仍使用超时/长度限制防止灾难性回溯。
- 单条规则失败不能让播放进程崩溃。
- 错误记录只包含规则 Id/安全摘要，不写入小说正文。
- 可恢复错误在规则页或播放错误状态提供可操作提示。

## 6. 与 Speech Plan 和缓存身份关系

影响 SpeechText 的规则有效字段按稳定顺序进入版本化 `TextProfileFingerprint`。该指纹用于判断每章当前 Speech Plan 是否需要重新计算，不直接等价于 `AudioCacheKey`。

重新计算后：

- 来源身份和最终 SpeechText 都未变化：更新计划头文本配置指纹并复用原音频。
- 只有部分段最终 SpeechText 变化：只有这些段形成新的音频缓存身份。
- 分段边界变化：受影响来源范围形成新身份，未变化来源范围仍可复用。
- 仅影响 DisplayText 的规则变化不重建 Speech Plan，也不生成新音频。

Coverage 读取携带当前 TextProfileFingerprint。已有 Plan 指纹过期时返回更新中/过期状态，并由明确 orchestration 异步补建；普通目录不会为从未建立计划的普通章节无条件建立新计划。

Active Cache batch 创建时冻结规则快照和稳定段计划；运行中编辑规则不改变该批次。MP3 Export 只认当前 Plan 与当前合成配置对应的缓存。

## 7. 与播放状态关系

规则变化后重新投影当前章节：

- SpeechText 变化时按播放规则刷新当前 audio session。
- 仅 DisplayText 变化时尽量保持音频连续与播放位置。
- 重建后依据来源位置解析最接近可播放段，不简单复用旧列表 index。

## 8. 规则工作台

- `AppPageHeader.Actions` 提供新建、从文件导入、从剪切板导入和帮助，不提供页面级导出。
- 左侧卡片显示名称/Pattern 摘要和 ToggleSwitch；启用状态即时持久化，不属于编辑草稿。
- 单项导出、复制、上移、下移、删除使用 ContextMenu，右键不切换当前编辑对象。
- 排序以整卡长按后拖动为主，使用轻量反馈和插入线，并保留上移/下移备用排序。
- 右侧编辑器提供名称、Pattern、Replacement、作用目标、帮助、取消、保存。
- 页面进入时不自动选择规则；新建先形成编辑副本，保存后才进入正式列表。
- 切换规则、返回或退出复用统一 dirty-state 导航保护。
- 文件/剪切板导入采用合并策略：完全重复跳过，同名不同内容作为新规则按源顺序追加；导入后不自动打开规则。
- 不提供复杂预览器、页面级批量导出或脚本替换语言。

## 9. Application 边界

- Repository 只负责规则持久化。
- Workspace/Editor service 负责列表、草稿、校验、排序和保存语义。
- Replacement pipeline 只负责执行规则。
- Player 只消费处理后的段落，不自行读取 Regex repository。
- 规则 mutation 只发布自身 typed semantic change，不直接协调 Cache invalidation。

## 10. 自动测试

至少保护：

- Display/Speech/Both；
- 排序与 disabled；
- 空输出；
- Unicode/中文文本；
- 无效表达式、超时与错误隔离；
- Speech 输出未变化时缓存身份可复用；
- SpeechText 变化只影响对应段；
- Display-only 变化不产生不必要 TTS；
- 编辑 dirty state、Cancel、启用状态与草稿解耦；
- 合并导入、重复跳过、同名不同内容新增；
- 拖动/备用排序与持久化。
