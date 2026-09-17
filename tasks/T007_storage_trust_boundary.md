# T007 — 统一应用存储信任边界

## 目标

解决当前 reparse-point 规则在 AppStoragePathResolver、AppDataDirectoryProvider、Diagnostic Session 等位置不一致的问题。最终选定的数据根是可信锚点，允许数据根自身及其祖先通过 Junction/Symlink 映射；数据根内部仍必须拒绝未经应用管理的 reparse-point 逃逸。

## 必读

- `AGENTS.md`
- `TASK_BACKLOG.md`
- `docs/05_DATA_AND_COMPATIBILITY.md`
- `docs/07_OBSERVABILITY_AND_DIAGNOSTICS.md`
- `docs/08_QUALITY_AND_TESTING.md`
- 当前 `AppDataRootResolver`、`AppDataDirectoryProvider`、`AppStoragePathResolver`、`ReparsePointPathGuard`
- Diagnostics/Books/Cache/Persistence 中所有路径归属与 reparse-point 调用点

## 必须完成

1. 建立单一的“数据根信任边界”实现；不要继续通过不同调用点组合 `includeRoot` 等布尔参数表达不同安全语义。
2. 明确区分：
   - 逻辑 data root；
   - data root 自身及祖先；
   - data root 内部路径段。
3. Data root 自身是 Junction/Symlink 时，Books、Cache、Database、Logs、Telemetry、Diagnostics 均能正常工作。
4. data root 内部任意子目录链接到根外时必须拒绝。
5. 删除 Diagnostics 中私有/重复 reparse-point 实现，统一复用基础设施。
6. 不通过 resolve real path 后重新定义“数据根”为目标物理路径来规避边界；逻辑 data root 仍是应用合同。
7. 不降低路径 containment 校验，不允许 `..`、绝对 legacy path 或链接逃逸绕过数据根。

## 自动验收

至少新增/调整以下集成测试：

- 普通 data root。
- `current -> version`，Data 为普通目录。
- `current\Data -> persist\novelspeaker\Data`，Data root 本身为 Junction/Symlink。
- Data root 内部 `Diagnostics` / `Books` / `Cache` 任一子路径为指向根外链接时拒绝。
- AppStoragePathResolver 对不存在的后续文件仍保持 containment。
- SqliteDiagnosticSessionStore start/recover 在 Data-root Junction 下正常。

链接创建因平台/权限不可用时允许显式 skip，但 Windows CI/开发环境可创建时必须真正运行。

完成后执行相关 Infrastructure tests、Architecture tests、Release build。
