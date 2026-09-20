---
name: release-version
description: 为 NovelSpeaker 执行完整版本发布：检查 gh/认证与仓库状态，判断 SemVer 版本并更新 Directory.Build.props，运行质量门禁，通过标准 PR 流程合并到 main/master，打 tag 触发 Release CI，并在可判定为测试问题时修复后继续，验证发布资产、更新 Release Note，并按分支类型完成发布后收尾。用户明确调用本 Skill 即授权完成该发布所需的版本提交、push、PR 创建/合并、tag 推送和 Release Note 更新；用户当前请求中的特殊限制始终优先。
---

# NovelSpeaker 版本发布

本 Skill 只负责正式版本发布流程。发布包构建、内容校验和 GitHub Release 的创建/更新以 `.github/workflows/release.yml` 为唯一执行来源，不在 Skill 中复制其文件清单。

用户当前请求中的版本号、分支、合并方式、是否保留分支、Release Note 风格或其它特殊要求优先于本 Skill 的默认值。若用户要求与仓库安全约束冲突，先说明并停止有风险的步骤。

## 授权边界

用户明确调用 `release-version` Skill，即视为授权本次发布所必需的：

- 修改并提交版本配置；
- 修复本次发布直接暴露的、可确认属于测试或测试基础设施的问题，并为此更新发布分支/PR；
- 修复本次发布直接暴露的、局部且低风险的源码问题，并为此更新发布分支/PR；
- push 当前发布分支；
- 创建或复用指向主分支的 PR；
- 按本 Skill 规则完成 PR 合并；不直接 push main/master；
- 创建并 push 发布 tag，或在本次发布失败恢复中按本 Skill 规则安全移动该 tag；
- 等待并读取 GitHub Actions / Release 状态；
- 使用 `gh release edit` 更新最终 Release Note，必要时使用 workflow 更新已有 Release 的资产；
- 按本 Skill 规则同步、保留或删除本次发布分支。

该授权不包含与发布无关的代码修改、主分支强制改写、删除 tag/Release、绕过失败检查或处理其它仓库。唯一例外是第 9 节为修复本次发布问题而对本次 release tag 执行带远端旧 SHA 保护的强制更新；不得使用无条件 `--force`，不得删除后重建远端 tag。

## 1. 前置检查

任何文件修改前依次执行：

1. 检查 `gh` 是否存在。
   - Linux/macOS/WSL 可使用 `command -v gh`。
   - PowerShell 可使用 `Get-Command gh`。
   - **没有 `gh`：立即结束，不修改文件，不尝试用其它 API/工具替代。**
2. 执行 `gh auth status`。
   - 未认证、Token 失效或当前账号无仓库访问能力：立即结束，不修改文件。
3. 确认当前目录位于 Git 仓库，且不是 detached HEAD。
4. 记录：
   - 当前分支 `original_branch`；
   - 当前 HEAD；
   - `git status --porcelain`；
   - remote URL；
   - remote 默认分支。
5. 工作区必须干净。
   - 若存在任务开始前的未提交修改，默认停止。
   - 不自动 stash、不自动提交、不覆盖这些修改。
   - 只有用户明确要求把这些修改纳入本次发布时才继续。
6. 执行 `git fetch origin --prune --tags`。
7. 通过远端默认分支确定发布主分支：
   - 默认分支为 `main` 或 `master` 时使用它；
   - 其它名称只有在用户明确指定时使用，否则停止并询问。
8. 当前分支不得是发布主分支。
   - 发布提交必须从非主分支通过 PR 进入 `main/master`；
   - 如果当前分支是 `main/master`，立即停止并要求用户切换到发布源分支；不得先修改版本文件，也不得隐式创建临时分支。
9. 确认 `.github/workflows/release.yml` 存在，并检查其 tag 触发/版本约束以及“已有 Release 时更新资产”的恢复路径仍与本 Skill 假设兼容。
10. 检查当前分支和远端同名分支是否存在异常偏离；记录发布开始时的远端 source SHA，供发布后判断分支是否被别人追加提交。

任何前置检查失败都不得创建版本提交、PR 或 tag。

## 2. 确定上一版本与发布范围

1. 使用 `gh release` 查询最新**正式、非 draft、非 prerelease** Release，并得到 `previous_tag`。
2. 读取 `Directory.Build.props`：
   - `Version`
   - `AssemblyVersion`
   - `FileVersion`
   - `InformationalVersion`
3. 正常情况下，当前 `Version` 应与 `previous_tag` 去掉 `v` 后一致。
   - 不一致时先调查原因；
   - 不得在版本来源冲突时直接继续递增。
4. 分析从 `previous_tag` 到待发布内容的真实变更：
   - `git log`
   - `git diff`
   - 用户可见行为、兼容性和迁移变化。
5. 版本判断不能只看 source 分支；还要考虑 `origin/<main>` 自 `previous_tag` 以来已经存在、最终也会进入 Release 的提交。

若 `previous_tag..待发布内容` 没有任何提交，默认停止，不创建空版本；用户明确要求仍发布时除外。

## 3. 确定 SemVer

若用户明确指定 major、minor、patch 或完整版本号，优先使用用户要求。

用户没有指定时，按实际变更决定：

- **major**：存在不兼容用户行为、数据/配置格式破坏性变化、必须迁移的公开接口或明确 `BREAKING CHANGE`。
- **minor**：新增向后兼容的用户功能、重要可用能力或新的公开行为。
- **patch**：Bug 修复、视觉/交互优化、性能/兼容性修正、内部质量改进、文档/工程整理等不需要 minor/major 的发布。
- Conventional Commit 类型和 `BREAKING CHANGE` 是判断信号，不代替对真实 diff 的检查。

若 major/minor 边界存在实质歧义且无法从代码、测试和文档确认，停止并询问用户，不自行猜测。

得到：

- `release_version = X.Y.Z`
- `release_tag = vX.Y.Z`

并确认远端不存在同名 tag/Release。若当前执行明确是在恢复本次已记录的失败发布，则可以存在同名 tag/Release，但必须记录其当前 tag commit，并确认它属于本次发布尝试；不能把其它历史发布的同名 tag 当作恢复目标。

## 4. 更新版本配置

只修改 `Directory.Build.props` 中的版本字段：

- `<Version>X.Y.Z</Version>`
- `<AssemblyVersion>X.Y.Z.0</AssemblyVersion>`
- `<FileVersion>X.Y.Z.0</FileVersion>`
- `<InformationalVersion>X.Y.Z</InformationalVersion>`

要求：

- 四个字段必须一致；
- 不顺带升级依赖；
- 不修改 `release.yml` 来迁就本次版本；
- 使用项目规定的文本修改方式；
- 修改后检查 diff，确认只有预期版本变化。

## 5. 发布前质量门禁

版本配置更新后、任何 push/PR/tag 前执行完整门禁：

```powershell
dotnet restore --locked-mode -r win-x64
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

要求：

- 0 test failure；
- Release build 0 warning / 0 error；
- 不设置 `NOVELSPEAKER_TEST_ALLOW_VISIBLE_WINDOWS=1`；
- 若门禁修改了非预期文件，先调查并重新验证。

### 5.1 CI 失败分类与恢复

本地门禁或 GitHub Actions 失败时，不默认立即终止。先读取失败 job、测试名称、日志和必要的重跑结果，判断失败属于哪一类：

1. **测试或测试基础设施问题**：例如测试之间共享临时目录/端口/文件、并发清理竞态、固定延时导致的时序失败、fixture 隔离错误、测试断言与已确认的产品契约不一致、WPF 测试宿主清理问题或测试 workflow 配置问题。确认不是生产行为错误后，修复测试、fixture 或测试基础设施，不能仅通过删除测试、放宽断言、关闭并发或屏蔽失败来“修绿”。
2. **明确且局部的源码问题**：如果证据表明是源码功能/逻辑问题，但修复范围小、行为明确、风险低且可以由现有测试验证，可以在发布源分支修复，重新执行完整门禁并继续。
3. **复杂、影响范围不清或需要产品取舍的源码问题**：停止发布，保留现场，向用户报告失败证据和可选方案，等待用户决策。
4. **无法分类或疑似外部 runner/网络问题**：先进行有限次数的针对性重跑；若仍无法证明是仓库内测试问题，则停止并报告，不自行修改产品或绕过检查。

每次获准修复后都必须：

- 先运行针对失败的 focused check，确认假设成立；
- 提交修复并更新 PR/source 分支；
- 重新执行完整发布前门禁和相关 GitHub checks；
- 将最新、已验证的 source tip 更新为新的 `release_source_sha`；
- 只有所有 required checks 通过后才继续合并或发布。

门禁失败不允许被静默忽略。测试问题修复完成后继续流程；复杂或不确定的源码问题仍然停止。本 Skill 不修复与本次发布无关的缺陷。

## 6. 创建版本提交

门禁通过后：

1. 再次检查 diff。
2. 创建独立版本提交：

```text
chore(release): prepare vX.Y.Z
```

3. 记录该提交及当前 source HEAD 为 `release_source_sha`。若后续按第 5.1 节提交了获准修复，则以最后一个已通过完整门禁的 source tip 更新该值。
4. 若当前分支远端存在，push 前确认远端没有从发布开始后新增未知提交。
5. 不使用 force push。

## 7. 通过标准 PR 流程合并

### 7.1 Push 与 PR

1. push 当前 source 分支；无 upstream 时建立 upstream。
2. 查找 `source -> main` 是否已有 open PR：
   - 有则复用；
   - 没有则创建新的 PR。
3. PR body 应简要说明目标版本、主要变更和已通过的本地门禁。
4. 等待 PR checks 完成：
   - `gh pr checks <number> --watch` 或等价命令；
   - 任一 required/实际质量检查失败都按第 5.1 节分类处理；
   - 测试问题修复后 push 并重新等待 checks；
   - 不绕过失败 checks，也不在 checks 仍失败时合并。

### 7.2 PR 合并

始终保留 PR 中的原始 commits，使用普通 merge commit：**不直接 push main/master，不 squash，不 rebase**。

在合并前重新 `git fetch origin`，并确认 PR head 等于最新已验证的 `release_source_sha`。如果 PR head 发生变化，先确认变化只来自按第 5.1 节获准并已通过门禁的修复；否则停止并重新评估，不自动接受未知追加提交。

使用：

```bash
gh pr merge <number> --merge --match-head-commit <release_source_sha>
```

合并后：

- fetch 最新 `origin/<main>`；
- 确认 `release_source_sha` 已 reachable from 主分支；
- 确认 PR 状态为 merged。

如果 PR 合并失败、检查未通过或无法确认 merged 状态，停止后续 tag 操作并报告，不把 open/closed PR 冒充 merged。

## 8. 最终主分支确认

在打 tag 前重新检查实际将发布的主分支：

1. `git fetch origin --tags`。
2. 记录 `final_main_sha = origin/<main>`。
3. 确认版本提交已包含在 `final_main_sha`。
4. 重新检查：
   - `git log previous_tag..final_main_sha`
   - `git diff previous_tag..final_main_sha`
5. 确认 `Directory.Build.props` 在 `final_main_sha` 中就是 `release_version`。
6. 若在 PR 合并后又出现本次未分析的新 main 提交：
   - 停止；
   - 不打 tag；
   - 重新评估版本号与 Release 范围。
7. 新发布必须确认 `release_tag` 仍不存在；失败发布恢复必须确认已有 `release_tag` 仍指向已记录的旧发布提交，并且没有被其它操作改动。

## 9. Tag 与 Release CI

Tag 必须指向最终 `final_main_sha`，不能指向合并前的 feature/dev tip。

新发布默认创建 annotated tag：

```bash
git tag -a vX.Y.Z <final_main_sha> -m "NovelSpeaker vX.Y.Z"
git push origin vX.Y.Z
```

push tag 后：

1. 定位由该 tag 触发的 Release workflow。
2. 等待 workflow 完成。
3. workflow 失败时：
   - 读取失败 job/log，按第 5.1 节分类；
   - 如果只是可重试的外部 runner/网络故障，有限次数重跑同一 workflow，不移动 tag；
   - 如果是测试问题，或是已确认的局部低风险源码问题，先通过新的标准 PR 修复并合并到主分支；
   - 如果是复杂或不确定的源码问题，停止并等待用户决策，不移动 tag；
   - 修复合并后重新 fetch，确认版本配置仍为 `release_version`，并记录新的 `final_main_sha`；
   - 确认远端 tag 当前仍指向修复前的旧 `final_main_sha`，然后使用带旧 SHA 保护的 tag 更新：

```bash
git fetch origin --tags
git tag -f -a vX.Y.Z <new_final_main_sha> -m "NovelSpeaker vX.Y.Z"
git push --force-with-lease=refs/tags/vX.Y.Z:<old_tag_sha> origin refs/tags/vX.Y.Z
```

   - 不使用无条件 `git push --force`，不删除后重建远端 tag；
   - 移动 tag 后重新等待该 tag workflow，并重新验证 Release 和资产。
4. workflow 成功后，继续验证 GitHub Release。

如果同名 GitHub Release 已经存在，Release workflow 必须更新并覆盖本次生成的同名资产，而不是再次执行只允许创建新 Release 的操作；不得删除已有 Release 来规避冲突。

## 10. 验证 Release 与资产

使用 `gh release view <release_tag>` 检查：

- Release 已创建；
- tag 与版本号一致；
- Release 不是意外的 draft/prerelease；
- `NovelSpeaker-vX.Y.Z-win-x64.zip` 存在；
- 对应 `.sha256` 资产存在；
- 资产可访问。

如环境允许，可下载 ZIP 与 `.sha256` 到临时目录验证校验和；完成后删除临时文件。具体包内文件合同仍由 `.github/workflows/release.yml` 维护，不在 Skill 中复制。

资产缺失或不可访问时，不宣告发布完成。

## 11. 编写并更新最终 Release Note

Release workflow 创建的自动 notes 只是初始内容。最终 Release Note 必须根据真实发布范围重新整理。

依据：

```text
git log <previous_tag>..<release_tag>
git diff <previous_tag>..<release_tag>
```

要求：

- 面向用户描述实际变化；
- 可以合并同一功能族的多个 commits；
- 不只罗列 commit title、文件名或 backlog 编号；
- 不把 fast-forward/merge、版本递增、CI 成功、测试数量本身当作主要发布内容；
- 只写已有代码、测试、文档或发布资产支持的事实；
- Bug 修复描述用户可见问题与影响场景，不无证据推断根因；
- 保留 `<previous_tag>...<release_tag>` 的完整 compare link。

按真实内容选择分组，没有内容的分组省略：

- `功能更新`
- `Bug 修复`
- `性能/兼容性`
- `破坏性变更/迁移`
- `测试与质量`

将 notes 写到仓库外临时文件，执行：

```bash
gh release edit <release_tag> --notes-file <file>
```

然后重新读取远端 Release body，确认版本、实际 diff、资产与正文一致。验证成功后删除临时 notes 文件。

## 12. 发布后分支收尾

开始发布时记录的 `original_branch` 决定默认行为。任何分支收尾前先 `git fetch origin --prune`。

### 12.1 长期开发分支

默认长期分支名称：

- `dev`
- `develop`
- `development`

处理前先检查 `origin/<branch>` 是否仍等于本次已发布的 source SHA。

若远端长期分支在 PR 创建后又出现新提交：

- 不 rebase；
- 不覆盖远端；
- 切回该分支并报告存在后续工作。

若没有新增提交：

1. `git switch <branch>`
2. `git merge --ff-only origin/<main>`
3. 正常 push 更新远端长期分支。

因为本次 source commits 已包含于 main，该过程应通过 fast-forward 收敛到最新主线；不得使用 force push。

### 12.2 已完成的短期分支

默认可清理前缀：

- `feature/`
- `feat/`
- `bugfix/`
- `fix/`
- `hotfix/`

只有同时满足以下条件才删除：

- 对应 PR 已 merged；
- branch tip 已 reachable from `origin/<main>`；
- 远端 branch tip 自发布开始后没有新增提交；
- 当前没有用户要求保留该分支。

满足后：

1. 切换并停留在主分支；
2. 删除远端短期分支；
3. 删除本地短期分支。

任何条件不满足都保留分支并报告原因。

### 12.3 未识别分支

其它分支默认保留，不自动删除、不自动 rebase。若安全可切换，则回到原分支；用户另有要求时按用户要求处理。

## 13. 最终检查

发布完成前必须确认：

- `release_version` 与 `release_tag` 一致；
- tag 指向最终主分支发布提交；
- Release workflow 成功；
- Release ZIP 和 SHA-256 资产存在；
- 最终 Release Note 已通过 `gh release edit` 更新并回读确认；
- 分支收尾符合原分支类型；
- 没有意外的临时 notes、下载资产或其它发布临时文件留在仓库；
- `git status --short` 可解释且没有本 Skill 遗留的未提交修改。

最终汇报：

- 上一版本 → 新版本；
- major/minor/patch 的判断依据（若为自动判断）；
- 版本提交 SHA；
- PR 编号及实际合并方式（标准 PR merge commit）；
- tag；
- Release workflow 结果；
- Release URL 与资产核对结果；
- Release Note 更新结果；
- 如发生失败恢复，记录失败原因、修复提交以及 tag 移动前后的 commit SHA；
- 最终所在分支及分支清理/同步结果；
- 任何未完成项或风险。
