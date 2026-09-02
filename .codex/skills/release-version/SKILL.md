---
name: release-version
description: 为 NovelSpeaker 执行完整版本发布：检查 gh/认证与仓库状态，判断 SemVer 版本并更新 Directory.Build.props，运行质量门禁，按 fast-forward-if-possible 策略处理 main/master 或 PR，打 tag 触发 Release CI，验证发布资产、更新 Release Note，并按分支类型完成发布后收尾。用户明确调用本 Skill 即授权完成该发布所需的版本提交、push、PR 创建/合并、tag 推送和 Release Note 更新；用户当前请求中的特殊限制始终优先。
---

# NovelSpeaker 版本发布

本 Skill 只负责正式版本发布流程。发布包构建、内容校验和 GitHub Release 初始创建以 `.github/workflows/release.yml` 为唯一执行来源，不在 Skill 中复制其文件清单。

用户当前请求中的版本号、分支、合并方式、是否保留分支、Release Note 风格或其它特殊要求优先于本 Skill 的默认值。若用户要求与仓库安全约束冲突，先说明并停止有风险的步骤。

## 授权边界

用户明确调用 `release-version` Skill，即视为授权本次发布所必需的：

- 修改并提交版本配置；
- push 当前发布分支；
- 创建或复用指向主分支的 PR；
- 按本 Skill 规则完成 PR 合并；
- 创建并 push 发布 tag；
- 等待并读取 GitHub Actions / Release 状态；
- 使用 `gh release edit` 更新最终 Release Note；
- 按本 Skill 规则同步、保留或删除本次发布分支。

该授权不包含与发布无关的代码修改、强制改写远端历史、删除失败发布的 tag、绕过失败检查或处理其它仓库。

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
8. 确认 `.github/workflows/release.yml` 存在，并检查其 tag 触发/版本约束仍与本 Skill 假设兼容。
9. 检查当前分支和远端同名分支是否存在异常偏离；记录发布开始时的远端 source SHA，供发布后判断分支是否被别人追加提交。

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
5. 当前分支不是主分支时，版本判断不能只看 source 分支；还要考虑 `origin/<main>` 自 `previous_tag` 以来已经存在、最终也会进入 Release 的提交。

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

并确认远端不存在同名 tag/Release。

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

门禁失败时停止发布，不创建 tag，不绕过检查。本 Skill 不顺带修复与版本发布无关的代码缺陷。

## 6. 创建版本提交

门禁通过后：

1. 再次检查 diff。
2. 创建独立版本提交：

```text
chore(release): prepare vX.Y.Z
```

3. 记录该提交及当前 source HEAD 为 `release_source_sha`。
4. 若当前分支远端存在，push 前确认远端没有从发布开始后新增未知提交。
5. 不使用 force push。

## 7. 当前分支就是 main/master

若 `original_branch == main_branch`：

1. 确认本地 main/master 基于最新 `origin/<main>`，没有远端竞态。
2. push 版本提交到 `origin/<main>`。
3. push 失败或远端已前进时停止，重新 fetch 并重新评估发布范围；不得 force。
4. 进入“最终主分支确认”。

## 8. 当前分支不是 main/master：创建 PR

### 8.1 Push 与 PR

1. push 当前 source 分支；无 upstream 时建立 upstream。
2. 查找 `source -> main` 是否已有 open PR：
   - 有则复用；
   - 没有则创建新的 PR。
3. PR body 应简要说明目标版本、主要变更和已通过的本地门禁。
4. 等待 PR checks 完成：
   - `gh pr checks <number> --watch` 或等价命令；
   - 任一 required/实际质量检查失败则停止；
   - 不绕过失败 checks。

### 8.2 fast-forward-if-possible

始终保留 PR 中的原始 commits：**不 squash、不 rebase**。

在合并前重新 `git fetch origin`，并确认 PR head 仍等于 `release_source_sha`。

判断 `origin/<main>` 是否为 `release_source_sha` 的 ancestor。

#### 可以 fast-forward

如果主分支是 source 的祖先：

1. 首选直接将远端主分支 fast-forward 到 `release_source_sha`。
2. 该 push 必须是普通 non-force fast-forward。
3. 如果仓库规则/权限拒绝直接更新主分支，则退回 `gh pr merge --merge`，允许生成 merge commit。
4. fast-forward 成功后等待并检查 GitHub PR 状态，确认 PR 被识别为 merged。
5. 如果 PR 没有进入可确认的 merged 状态，停止后续 tag 操作并报告，不把 open/closed PR 冒充 merged。

#### 存在分叉

如果 main 与 source 已分叉：

1. 使用 `gh pr merge --merge`。
2. 尽可能使用 head SHA guard，防止 PR 在检查后被追加提交。
3. 不使用 `--squash`。
4. 不使用 `--rebase`。
5. 不在本地重写 source commits 来制造可快进历史。

合并后：

- fetch 最新 `origin/<main>`；
- 确认 `release_source_sha` 已 reachable from 主分支；
- 确认 PR 状态为 merged。

## 9. 最终主分支确认

在打 tag 前重新检查实际将发布的主分支：

1. `git fetch origin --tags`。
2. 记录 `final_main_sha = origin/<main>`。
3. 确认版本提交已包含在 `final_main_sha`。
4. 重新检查：
   - `git log previous_tag..final_main_sha`
   - `git diff previous_tag..final_main_sha`
5. 确认 `Directory.Build.props` 在 `final_main_sha` 中就是 `release_version`。
6. 若在 PR 合并/主分支 push 后又出现本次未分析的新 main 提交：
   - 停止；
   - 不打 tag；
   - 重新评估版本号与 Release 范围。
7. 确认 `release_tag` 仍不存在。

## 10. Tag 与 Release CI

Tag 必须指向最终 `final_main_sha`，不能指向合并前的 feature/dev tip。

默认创建 annotated tag：

```bash
git tag -a vX.Y.Z <final_main_sha> -m "NovelSpeaker vX.Y.Z"
git push origin vX.Y.Z
```

push tag 后：

1. 定位由该 tag 触发的 Release workflow。
2. 等待 workflow 完成。
3. workflow 失败时：
   - 不宣告发布完成；
   - 不自动删除本地/远端 tag；
   - 不自动改版本后重新发同一 tag；
   - 读取失败 job/log，报告明确失败点并停止。
4. workflow 成功后，继续验证 GitHub Release。

## 11. 验证 Release 与资产

使用 `gh release view <release_tag>` 检查：

- Release 已创建；
- tag 与版本号一致；
- Release 不是意外的 draft/prerelease；
- `NovelSpeaker-vX.Y.Z-win-x64.zip` 存在；
- 对应 `.sha256` 资产存在；
- 资产可访问。

如环境允许，可下载 ZIP 与 `.sha256` 到临时目录验证校验和；完成后删除临时文件。具体包内文件合同仍由 `.github/workflows/release.yml` 维护，不在 Skill 中复制。

资产缺失或不可访问时，不宣告发布完成。

## 12. 编写并更新最终 Release Note

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

## 13. 发布后分支收尾

开始发布时记录的 `original_branch` 决定默认行为。任何分支收尾前先 `git fetch origin --prune`。

### 13.1 原分支是 main/master

保持在主分支，更新到 `origin/<main>`，不额外创建分支。

### 13.2 长期开发分支

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
2. `git rebase origin/<main>`
3. 正常 push 更新远端长期分支。

因为本次 source commits 已包含于 main，该过程应收敛到最新主线；不得使用 force push。

### 13.3 已完成的短期分支

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

### 13.4 未识别分支

其它分支默认保留，不自动删除、不自动 rebase。若安全可切换，则回到原分支；用户另有要求时按用户要求处理。

## 14. 最终检查

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
- PR 编号及实际合并方式（direct fast-forward / merge commit / main direct）；
- tag；
- Release workflow 结果；
- Release URL 与资产核对结果；
- Release Note 更新结果；
- 最终所在分支及分支清理/同步结果；
- 任何未完成项或风险。
