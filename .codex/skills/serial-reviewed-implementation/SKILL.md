---
name: serial-reviewed-implementation
description: 严格串行完成代码任务切片：主 agent 负责实现、验证、修复和提交；每轮创建一个短生命周期 review-runner subagent，以提权方式启动 Codex 内置 Review，将大量 Review 过程输出隔离在 subagent 和临时文件中，只把最终审查结论及 actionable findings 汇报给主 agent。若有问题则修复、重新验证并使用新的 runner 复审最新完整 diff；仅在最终 Review 通过后进行原子提交。适用于需要“实现—验证—独立审查—修复—复审—提交”流程的 Git 代码项目；不适用于纯代码审查或明确禁止提交的任务。用户明确要求使用本 skill 时，即视为已授权本 skill 所执行任务切片的原子提交。
---

# 串行实现、Codex Review 与原子提交

严格按本 Skill 执行代码修改任务。

本 Skill 面向 Codex 工作流设计，但不绑定具体项目、编程语言、构建系统、操作系统或测试框架。

## 1. 核心流程

每个任务切片必须严格串行执行：

```text
主 agent
  ├─ 理解任务和项目约束
  ├─ 实现
  ├─ 验证
  ├─ 确认 Review 范围
  ├─ 创建 review-runner subagent
  │    └─ 提权启动 Codex built-in Review
  │         ├─ 最终结果 → 独立文件
  │         └─ stdout/stderr → 日志文件
  ├─ runner 汇报精炼结果并关闭
  ├─ CHANGES_REQUIRED → 修复 → 验证 → 新 runner → 完整复审
  └─ PASS → 原子提交
```

review-runner 的主要目的不是再次提供一个 reviewer，而是作为 Codex Review 的**执行与上下文隔离层**：

- 真正执行代码审查的是 Codex 内置 Review；
- runner 承担 Review CLI 的启动、等待、结果收集和汇报；
- Review 的大量过程输出不得进入主 agent 上下文；
- runner 不修改代码、不修复 finding、不执行实现线程验证、不提交。

## 2. 核心约束

始终满足：

1. 任意时刻只处理一个任务切片。
2. 当前切片完成实现、验证、Review 和提交前，不开始下一切片。
3. 每轮 Review 必须创建一个新的短生命周期 review-runner。
4. 主 agent 不得绕过 runner 直接运行 Codex Review。
5. 任意时刻最多运行一个 Review。
6. 当前 runner 完成汇报并关闭后，才能启动下一轮 Review。
7. 每次复审都必须重新检查当前切片的最新完整 diff，不得只检查上一轮 findings。
8. Review 通过后的行为性修改会使原 `PASS` 失效。
9. 只有最终完整 diff 获得有效 `PASS` 后才能提交。
10. 不得覆盖、清理、暂存或提交任务开始前已经存在的无关修改。

## 3. 前置检查

开始前：

1. 确认当前目录位于 Git 仓库中。
2. 读取适用的 `AGENTS.md`、`CONTRIBUTING`、项目文档、开发规范、CI 配置和任务要求。
3. 确认当前环境支持：
   - 创建、等待和关闭 subagent；
   - runner 使用提权或 sandbox 外 shell；
   - `codex exec review`；
   - 将 Review 输出写入工作区之外的临时文件。
4. 确认 Codex CLI 可用，并记录实际 CLI 路径和版本。
5. 记录初始 `git status`，识别已有 staged、unstaged 和 untracked 修改。
6. 将任务拆分为可独立实现、验证、Review 和提交的任务切片。

如果任务足够小，可以只使用一个切片。

如果强制 runner、提权 Review 或可靠结果收集不可用，则报告环境阻塞；不得静默降级为主 agent 直接 Review。

## 4. 提交授权

用户明确要求使用本 Skill，即视为已授权本 Skill 所产生任务切片的原子提交，提交前无需再次确认。

该授权不包括：

- push；
- tag；
- Release；
- 创建或合并 PR；
- 修改远端分支；
- 其他远端写操作。

## 5. 定义任务切片

修改前明确：

- 当前切片目标；
- 范围和不包含内容；
- 验收标准；
- 预计涉及的文件或模块；
- 适用验证方式；
- Review 基准；
- 预期 diff 范围；
- 任务开始前已有且不得混入的修改。

切片应足够小，使其 diff 可以独立理解、验证、Review 和提交。

实施过程中如果必须扩大范围，先重新界定当前切片，再继续修改。

## 6. 实现

主 agent 完成当前切片的全部代码修改。

要求：

- 遵循项目现有架构、接口、风格和约定；
- 只修改完成当前切片所必需的内容；
- 补充或调整必要测试；
- 不混入无关重构、格式变化、清理或顺手修改；
- 持续检查工作区，避免影响已有无关修改；
- 项目已有成熟库、框架或公共组件时优先复用，不重复造轮子。

## 7. 验证

主 agent 根据项目实际情况执行与改动合理相关的验证，例如：

- build；
- format；
- lint；
- typecheck；
- unit / integration test；
- benchmark；
- static analysis；
- 项目规定的其他检查。

不得机械执行所有检查；以项目规范和改动风险为准。

记录关键验证命令和结果。发现问题时先修复并重新验证。

进入 Review 前必须：

- 实现满足验收标准；
- 必要验证已经完成；
- 检查 `git status`；
- 检查当前切片完整 diff；
- 确认没有混入无关修改；
- 确认属于当前切片的 untracked 新文件已纳入 Review；
- 确认 Review 基准和范围。

## 8. 准备精确 Review 范围

Review 必须只针对当前任务切片。

### 8.1 工作区仅包含当前切片

如果当前 staged、unstaged 和相关 untracked 修改全部属于当前切片，可以直接使用当前工作区。

Review 前记录一个 scope manifest，至少包含：

- `HEAD` / Review 基准；
- staged 路径；
- unstaged 路径；
- untracked 路径；
- 必要时的 diff 摘要或内容 hash。

### 8.2 工作区混有无关修改

不得直接 Review 整个工作区。

创建隔离 Review workspace，使：

> 相对于 Review 基准的全部变化恰好等于当前切片。

Git 项目优先使用临时 worktree；宿主提供等价隔离 workspace 时也可以使用。

要求：

1. 以正确基准创建隔离 workspace。
2. 只应用当前切片修改和属于当前切片的新文件。
3. 不复制无关既有修改。
4. 检查 `git status` 和完整 diff。
5. 生成 scope manifest。
6. Review 完成后由主 agent 清理隔离 workspace。
7. 清理不得改变原始工作区。

无法可靠构造精确 Review 范围时，停止 Review / 提交流程并报告阻塞。

## 9. 创建 review-runner

每轮 Review 创建一个新的 runner。

只向 runner 提供必要上下文：

- Review workspace；
- Review 基准；
- scope manifest；
- 当前切片目标的简要说明；
- 必要设计约束；
- 已完成验证的摘要；
- Review 模式；
- 临时输出目录；
- 汇报格式。

如果宿主支持，应避免继承主 agent 的完整会话历史。

不得主动向 runner 或 Review prompt 复制与审查无关的：

- Token、API Key、认证信息或密码；
- 个人数据；
- 专有业务数据；
- 大段用户提供内容；
- 完整请求 / 响应正文；
- 其他不必要敏感信息。

runner 只承担本轮 Review，不得复用。

## 10. 提权启动 Codex Review

runner 必须通过宿主环境提供的**提权 / sandbox 外 shell**启动 `codex exec review`。

权限关系是：

```text
提权 / sandbox 外 shell
  └─ codex exec --sandbox read-only review ...
       └─ Codex Review agent 保持 read-only
```

`--sandbox read-only` 约束嵌套 Review agent，不代表启动 Codex CLI 的外层 shell 可以留在受限沙箱中。

如果提权被拒绝、无法获得 sandbox 外执行环境，或无法确认 Review 以要求的方式启动，则本轮不得继续；报告环境阻塞或判为 `REVIEW_INVALID`。

不得静默降级。

## 11. Review 输出隔离与防截断

Codex Review 可能产生大量控制台输出。

**不得依赖终端回显、终端缓冲或 subagent 捕获完整控制台 transcript 来取得最终 Review 结果。**

每轮创建工作区之外的唯一临时目录，至少保存：

```text
final-review.md
stdout.log
stderr.log
exit-status
```

优先使用 Codex CLI 的最终消息输出选项，把最后一条 Review message 单独写入文件：

```bash
review_dir="<unique-temp-dir>"
review_output="$review_dir/final-review.md"
review_stdout="$review_dir/stdout.log"
review_stderr="$review_dir/stderr.log"
review_status="$review_dir/exit-status"

cd "<review-workspace>"

codex exec   --sandbox read-only   --ephemeral   -o "$review_output"   review --uncommitted   >"$review_stdout"   2>"$review_stderr"

review_exit_status=$?
printf '%s\n' "$review_exit_status" > "$review_status"
```

上面的命令必须通过第 10 节规定的提权 / sandbox 外执行路径启动。

当前 CLI 版本参数不同可以调整命令形式，但必须保持以下不变量：

1. 最终 Review message 单独持久化；
2. stdout 和 stderr 写入文件；
3. CLI 真实退出状态单独保存；
4. 大量过程输出不直接进入 runner 或主 agent 上下文；
5. runner 正常情况下只读取最终 Review message；
6. 完整日志仅在诊断异常时按需读取。

### 正常路径

如果：

- CLI 正常结束；
- `final-review.md` 存在且非空；
- scope 可确认；
- 最终结果完整；

runner：

1. 只读取 `final-review.md`；
2. 不读取完整 stdout / stderr；
3. 提取整体结论和所有 actionable findings；
4. 向主 agent 返回精炼汇报。

### 异常路径

只有在：

- CLI 失败；
- 最终文件缺失或为空；
- 最终结果明显不完整；
- 无法确认 Review 是否启动或为何失败；

时才读取日志。

优先读取 stderr 尾部或最相关错误片段，必要时再扩大范围；不得默认把整个大日志读入上下文，更不得把整份日志转发给主 agent。

如果无法可靠恢复完整最终结果，本轮为 `REVIEW_INVALID`。

## 12. Review 等待规则

Review 可能耗时很长。

### 等待 timeout 只是轮询 timeout

subagent 等待接口的一次 timeout 只表示本次等待窗口结束，不表示：

- runner 失败；
- Review 失败；
- Review 卡死；
- Review 应被终止。

等待 timeout 后：

1. 查询 runner 状态；
2. runner 仍正常运行则继续等待；
3. 可以反复使用新的等待窗口轮询；
4. 不得仅因为等待次数多或耗时长而强制终止。

### 默认没有固定总体 deadline

本 Skill 默认不设置任意固定的 30 分钟、60 分钟或其他 Review 总体 deadline。

只要 runner / Review 可确认仍正常运行，就继续等待。

### 只有明确失败才终止

主动终止只适用于：

- 用户明确取消；
- 宿主任务存在外部强制 hard deadline；
- runner 或 Review 明确失败；
- 宿主报告不可恢复错误；
- 有明确证据证明本轮无法正常完成。

“运行很久”本身不是失败证据。

如果状态无法确认，不得仅凭猜测把一个可能仍正常运行的 Review 杀死。

## 13. Review 生命周期和工作区稳定性

runner 应同步、前台运行 Review，不得故意把 Review 进程 detach 到后台后提前结束。

Review 运行期间：

- 主 agent 不修改 Review workspace；
- 其他 agent 不修改 Review workspace；
- 不执行会改变目标 diff 的格式化、生成、修复或提交。

runner 完成后，主 agent：

1. 接收精炼汇报；
2. 确认 Review 进程已结束；
3. 关闭 runner；
4. 再次检查 scope manifest；
5. 确认 Review 期间目标 diff 没有变化；
6. 再清理 Review 临时资源和隔离 workspace。

等待接口 timeout 不得触发清理。

如果 runner 已进入失败终态但无法确认其 Review 进程是否结束，保留现场并判为 `REVIEW_INVALID`；在状态恢复可确认前不得启动下一轮或提交。

## 14. Review 模式

### Native scoped Review

当 Review workspace 的全部未提交修改都属于当前切片时，优先：

```bash
codex exec   --sandbox read-only   --ephemeral   -o "$review_output"   review --uncommitted
```

适合：

- 当前工作区仅包含本切片；
- 隔离 worktree 仅包含本切片；
- 不需要额外 Review prompt。

### Custom Review

只有确实需要向 Review 注入仓库本身无法提供的必要上下文时才使用，例如：

- 特殊设计约束；
- 非显然的 diff 基准；
- 上一轮未采纳 finding 的具体依据。

Custom Review 不得假设自动等价于 `--uncommitted`。

必须：

- 在 instructions 中明确 Review 范围和查看完整 diff 的方式；
- 明确 staged / unstaged / untracked 范围；
- 使用 scope manifest 核验实际 Review 范围。

如果当前 CLI 不允许显式 target 与自定义 prompt 同时使用，不得通过错误参数组合规避限制。

Native scoped Review 足够时优先使用 Native scoped Review。

## 15. Review 审查重点

Review 重点检查：

- correctness；
- regression；
- 边界条件；
- 错误处理；
- 状态和生命周期一致性；
- 并发问题；
- 接口契约；
- 数据格式兼容；
- 安全问题；
- 明显性能风险；
- 资源泄漏；
- 测试缺口；
- 与项目既有架构和约定的冲突。

只报告由当前切片引入、具体、可定位、可操作的问题。

build、lint、test 等实现验证仍由主 agent 负责，不把它们转移给 Review。

## 16. Runner 汇报

runner 不得把完整 Review transcript 或大量日志传给主 agent。

推荐格式：

```text
Review result: PASS | CHANGES_REQUIRED | REVIEW_INVALID

Scope:
- baseline: ...
- reviewed paths: ...
- manifest: matched | mismatched

CLI:
- exit status: ...
- final output: complete | incomplete

Overall verdict:
<忠实摘要>

Findings:
1. [priority/severity if available] path:line-or-function
   Problem: ...
   Affected scenario / impact: ...
   Suggested remediation: ...

Notes:
<仅影响有效性判断的必要信息>
```

要求：

- 保留所有 actionable findings；
- 不得因为 runner 自己认为“不重要”而省略；
- 不得自行把 Review 的不通过结论改为通过；
- finding 至少应能定位问题并说明问题本身及其场景或影响；
- priority、severity、remediation 如果原 Review 提供则保留；
- 缺少非关键格式字段本身不使整个 Review 无效。

## 17. 归一化结论

主 agent 将结果归一化为：

### `PASS`

同时满足：

- Review 完整有效；
- scope 正确；
- Review 对应当前切片最新完整 diff；
- Review 期间 diff 未变化；
- 没有未解决 actionable finding；
- 若 Review 提供整体 verdict / correctness，其结论不得认为 patch 不正确。

### `CHANGES_REQUIRED`

包括：

- 存在一个或多个成立的 actionable finding；
- Review 认为存在 correctness、regression 或其他应在提交前修复的问题；
- 关键测试缺口使当前 patch 不适合提交。

### `REVIEW_INVALID`

包括：

- Review CLI 明确失败；
- 最终 Review message 缺失、截断或明显不完整；
- scope 或基准无法确认；
- 混入无关修改；
- Review 期间 diff 变化；
- runner 汇报不足且无法从最终结果恢复；
- 无法确认 Review 是否真正完成；
- 其他使结论不可信的情况。

**等待接口 timeout 本身不是 `REVIEW_INVALID`。**

## 18. 处理 findings 和复审

如果为 `CHANGES_REQUIRED`：

1. 主 agent 逐项验证 finding 是否成立。
2. 修复所有成立问题。
3. 对未采纳 finding，记录具体代码、接口、测试或设计依据。
4. 重新执行受影响的必要验证。
5. 检查最新完整 diff 和 `git status`。
6. 重新生成 scope manifest。
7. 创建一个新的 review-runner。
8. 重新 Review 最新完整 diff。
9. 不复用上一轮 runner。
10. 不把 Review 范围缩小到上一轮 findings。
11. 重复直到获得有效 `PASS`。

不得由主 agent 自行绕过成立的 finding 后宣布通过。

## 19. `PASS` 后修改

`PASS` 只对当时 Review 的完整 diff 有效。

如果之后发生可能影响以下内容的修改：

- 代码行为；
- 接口；
- 配置；
- 数据格式；
- 依赖关系；
- 构建逻辑；
- 测试语义；
- 生成结果；

原 `PASS` 立即失效。

重新：

1. 验证；
2. 生成 scope；
3. 创建新 runner；
4. 完整 Review；
5. 获得新 `PASS`。

纯注释、文档或格式调整是否需要重新 Review，根据其是否可能影响行为、生成结果、API 契约、配置含义或测试输入保守判断。

## 20. 原子提交

最终完整 diff 获得有效 `PASS` 后：

1. 再次检查 `git status` 和完整 diff。
2. 确认没有混入任务开始前的无关修改。
3. 确认提交内容与最后一次通过 Review 的内容一致。
4. 按逻辑组织一个或多个原子 commit。

要求：

- 每个 commit 一个明确目的；
- 每个 commit 逻辑完整；
- 不同性质改动尽量分开；
- commit message 简洁准确；
- 不用一个大提交掩盖多个无关修改。

如果 staging、commit hook、formatter、codegen 或其他提交准备步骤改变了文件，则原 `PASS` 失效，必须重新验证和完整 Review。

## 21. 完成条件

当前切片只有在以下条件全部满足后才完成：

- 实现满足验收标准；
- 必要验证通过；
- 最终完整 diff 获得有效 `PASS`；
- 所有成立 findings 已处理；
- 所有 runner 已关闭；
- 修改已原子提交；
- commit 内容与最后一次 Review 内容一致；
- 不存在属于当前切片的未提交修改；
- 任务开始前的无关修改保持原状。

然后才能开始下一切片。

全部切片完成后汇总：

- 完成的任务切片；
- 关键验证；
- 最终 Review 结果；
- 主要 findings 及处理结果；
- commits 及其目的；
- 仍存在但不属于本任务范围的既有修改或风险。

不得在任何切片尚未获得有效 `PASS` 或尚未提交时宣告整个任务完成。
