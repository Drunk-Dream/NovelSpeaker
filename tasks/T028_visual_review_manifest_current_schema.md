# T028：移除 Visual Review manifest 的过期 fallback

## 目标

让 `tools/Generate-VisualReviewManifest.ps1` 按当前 Gallery generator 的 manifest 结构工作，删除未使用历史 schema fallback。

审计依据：S09。相关材料：`tools/README*`（如存在）、脚本自身及 `GalleryScreenshotGenerator` 输出合同。

## 范围与约束

- 对照当前 `GalleryManifest.Scenes` / `GalleryManifestEntry.Scene` 序列化结果与脚本输入读取方式。
- 删除旧 `Scenarios` 属性 fallback 和缺失 `scene` 时的默认 scenario 路径。
- 保留 ArtifactId 等仅大小写不同的 PowerShell 属性读取（PowerShell 属性查找大小写不敏感，不能当作独立 schema）；保留文件存在、hash 校验和 root index 生成。
- 不破坏当前生成器、README 使用方式或审核产物安全校验。

## 验收

- 使用当前 generator 结构验证正常输出；旧结构和缺失必需 scene 明确失败并给出可定位错误。
- 运行 PowerShell 静态/脚本验证（若仓库有对应入口）；同时静态核对文件/hash/root-index 路径。
- 不运行无关 build/tests。

## 交付

清理过期分支与专用 fixture，更新 Backlog 并删除本规格。
