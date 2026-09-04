# NovelSpeaker 文档索引

本目录只保存长期有效的产品、架构和工程合同。任务过程、诊断过程、临时测量和迁移步骤不进入编号文档；当前执行计划统一写在 `TASK_BACKLOG.md`，历史由 Git 保存。

## 阅读顺序

常规开发先阅读：

1. `00_PRODUCT_AND_SCOPE.md`：产品边界、已支持能力和非目标。
2. `01_ARCHITECTURE.md`：四层依赖、Feature 边界、状态所有权、查询与大列表架构。
3. 按任务选择专项文档。
4. `08_TESTING_AND_QUALITY.md`、`09_ENGINEERING_CONVENTIONS.md`：测试与工程约束。
5. `10_DECISIONS.md`：已经确认、不得在实现中自行改变的决策。
6. `TASK_BACKLOG.md`：当前唯一任务计划。

## 文档职责

| 文件 | 唯一职责 |
|---|---|
| `00_PRODUCT_AND_SCOPE.md` | 产品定义、功能范围、非目标、用户可观察行为 |
| `01_ARCHITECTURE.md` | 项目分层、Feature、DI、状态 owner、read model、大列表和架构约束 |
| `02_RUNTIME_AND_STATE.md` | 启动、Page activation、Playback session、后台任务、关闭与线程生命周期 |
| `03_DATA_AND_PERSISTENCE.md` | SQLite、书籍/规则/进度/缓存/文件、迁移和数据兼容 |
| `04_UI_NAVIGATION_AND_PERFORMANCE.md` | 页面信息架构、导航、页面交互、大列表、staged loading 与 UI 性能合同 |
| `05_HTTP_TTS_COMPATIBILITY.md` | HTTP TTS 规则格式、编译、脚本安全、限流和请求兼容 |
| `06_REGEX_REPLACEMENT_PIPELINE.md` | 正则替换模型、流水线、错误、缓存关系和编辑器语义 |
| `07_VISUAL_DESIGN_SYSTEM.md` | 主题、资源所有权、Token、Surface、共享控件和视觉禁止项 |
| `08_TESTING_AND_QUALITY.md` | 测试分层、WPF 隔离、Architecture Fitness Tests 和质量门禁 |
| `09_ENGINEERING_CONVENTIONS.md` | 代码、异步、命名、依赖、文档和 Git 工程规范 |
| `10_DECISIONS.md` | 已确认架构/产品决策和仍需证据驱动处理的风险 |
| `TASK_BACKLOG.md` | 当前任务、依赖、实施方向、验收和完成成果 |

## 专项规范

`05_HTTP_TTS_COMPATIBILITY.md` 与 `06_REGEX_REPLACEMENT_PIPELINE.md` 是协议/流水线专项规范，允许比其它编号文档更详细。`07_VISUAL_DESIGN_SYSTEM.md` 只保留长期视觉合同，不保存逐任务截图、临时页面调整过程或 Gallery 调试过程。

## 文档维护规则

- 一条稳定规则只在一个编号文档中定义，其它文档使用链接或简短引用，不复制整段规则。
- 数字编号文档描述目标终态，不记录“本轮”“Txxx”“此前诊断”等任务过程。
- `TASK_BACKLOG.md` 在新的规划阶段可以直接重写；已完成历史依赖 Git，不建立任务归档文档。
- 诊断报告、性能 trace、视觉验收截图和一次性规划文件不是长期文档，任务结束后按需要删除或留在工作区，不加入索引。
- 行为与文档冲突时先核对代码和自动测试；确认目标行为后修正文档，不再新增第二套解释。
