# 正则替换流水线

## 1. 定位

正则替换用于动态分段后的文本变换，不修改导入后的内部正文，也不重新划分章节。

每个播放段维护：

```text
SourceText
  ├─ Display pipeline → DisplayText
  └─ Speech pipeline  → SpeechText
```

## 2. 规则模型

每条规则至少包含：

- 稳定 Id。
- 名称。
- Pattern。
- Replacement。
- 作用目标：Display、Speech 或 Both。
- Enabled。
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
- SpeechText 为空：该段不请求 TTS，并由播放状态机安全跳过。
- 两者都为空：段落仍保留来源位置用于进度/定位，但不形成可见或可听内容。

## 5. 错误与资源限制

- 无效正则在编辑保存前校验。
- 运行时仍使用超时/长度限制防止灾难性回溯。
- 单条规则失败不能让播放进程崩溃。
- 错误记录只包含规则 Id/安全摘要，不写入整段小说正文。
- 可恢复错误在 UI 的规则页或播放错误状态中提供可操作提示。

## 6. 与朗读清单和缓存键关系

影响 SpeechText 的规则有效字段按稳定顺序进入版本化 `TextProfileFingerprint`。该指纹只用于判断每章当前朗读清单是否需要重新计算，不直接进入 `AudioCacheKey`。

重新计算后：

- 段来源身份和最终 `SpeechText` 都未变化：更新计划头中的文本配置指纹，继续复用原音频。
- 只有部分段的最终 `SpeechText` 变化：只有这些段形成新的音频缓存身份。
- 分段边界变化：受影响来源范围对应的段形成新身份，未变化来源范围仍可复用。
- 仅影响 DisplayText 的规则变化不重建正文朗读清单，也不生成新音频。

完整度读取会携带当前 `TextProfileFingerprint`。发现已有清单指纹过期时，本次先返回更新中状态并异步重建该章；普通目录中的清单缺失不触发首次建立，缓存管理页中的有缓存章节若异常缺失清单则异步补建。

主动缓存批次创建时冻结所需规则快照和稳定段清单；运行中编辑规则不改变该批次。MP3 导出只认当前朗读清单和当前合成配置对应的缓存。

## 7. 与播放状态关系

规则变化后重新投影当前章节：

- 如果 SpeechText 变化，当前音频 session 按播放规则刷新。
- 如果只有 DisplayText 变化，尽量保持音频连续与播放位置。
- 重建后依据来源位置解析最接近的可播放段，不简单使用旧列表索引。

## 8. 规则工作台

正则替换三级页使用统一规则工作台：

- 左侧：名称、Pattern 摘要、启用状态、拖动排序、`⋮`。
- `⋮`：删除、上移、下移。
- 右侧：名称、Pattern、Replacement、作用目标、帮助、取消、保存。
- 新建规则先形成编辑副本，保存后才进入正式列表。
- 未修改时取消/保存禁用。
- 切换规则、返回或退出时复用统一 dirty-state 导航保护。

不提供复杂预览器、批量导入/导出或脚本替换语言。

## 9. Application 边界

- Repository 只负责规则持久化。
- Workspace/Editor service 负责列表、草稿、校验、排序和保存语义。
- Replacement pipeline 只负责执行规则。
- Player 只消费处理后的段落，不自行读取正则 repository。

## 10. 自动测试

至少覆盖：

- Display/Speech/Both。
- 顺序和禁用规则。
- 空输出。
- Unicode/中文文本。
- 无效表达式、超时和错误隔离。
- 修改 Speech 规则但未改变实际输出时缓存键保持不变。
- SpeechText 变化只使对应段缓存键变化。
- 正则配置反复修改后每章仍只保存一份当前朗读清单。
- 只改 DisplayText 不产生不必要的 TTS 请求。
- 编辑 dirty state、排序和导航保护。