# T003 — HTTP Provider 完全禁止自动重定向

- **类型/优先级：** 实施 / P1 Security，第一项执行。
- **已批准产品决策：** **方案 A，禁止所有自动重定向**。任何相同或不同 origin 的 301/302/303/307/308（及其他 redirect 响应）都**不自动跟随**。用户应把 Provider 的 URL 配置为最终有效地址。
- **证据来源：** E1-A T002 真实 `HttpTtsClient` + loopback 302，`.NET 10.0.12`：自动 redirect 后 Authorization 消失，但虚构 `X-Api-Key`、手工 Cookie 和普通自定义头仍发送到另一端口 origin。该报告只作为已批准决策的技术背景，不需 Codex 访问用户临时报告。

## 范围与执行依据

1. 阅读 `AGENTS.md`、`docs/specs/HTTP_TTS.md`、`docs/08_QUALITY_AND_TESTING.md` 与当前 `HttpTtsClient`、HTTP Provider 请求编译/运行/状态处理及相关 focused tests。启动前复核本地真实 HEAD 与工作区。
2. 在生产路径为 HTTP TTS 共享 transport 的 `SocketsHttpHandler` 禁用自动重定向；不能只在 UI/配置验证层拦截、不能只拦跨源、不能依赖 `Authorization` 被框架自动剥离。HTTP 301/302/303/307/308 等应作为非成功结果交给既有响应校验/错误路径，禁止尝试读取重定向内容并视为音频成功。
3. 用户可理解地呈现“请求返回重定向，需要配置最终 URL”这一语义，利用现有 Speech Provider 失败反馈途径，不增设面向 HTTP 的公共状态机、自动跳转菜单或复杂的 Location 处理。状态码可用于错误摘要，但不可暴露包含凭据的 Location/Query、请求 Header 或敏感 URL；保留日志/诊断脱敏合同。不将包含 `Location` 的异常消息不加过滤地写日志或 UI。
4. 更新 `docs/specs/HTTP_TTS.md` 中 HTTP 执行/响应章节，正式持久化“所有 HTTP Provider 请求都不跟随重定向；依赖 redirect 的旧配置需自行设置最终地址；不提供开关、同源例外或迁移”合同。仅更新相关小节，不重排其他 Provider 文档。
5. 保留现有超时、请求取消、POST/GET、header/body 模板、限流、重试及传输失败边界；不同端点的正常非重定向行为不得改变。**不新增 Provider 配置字段或数据库/JSON migration。**

## 最小验证

- 用 loopback 双 origin 和**合成凭据**对生产 handler 真实 Send 路径测试 302，确认 Location 端点**没有被请求**（即不传任何 Header/Body），包括同源 redirect；增加至少一个 307/308 + POST/body 场景，保证拒绝自动 redirect 与请求方法无关。不能仅以 mock handler 模拟行为后宣布完成。
- 验证直达 2xx 仍按原工作流生成/读取音频，3xx 产生可理解但不含凭据的失败反馈，取消/超时映射保持稳定；已有自定义 Header 直达最终用户配置端点的用例继续通过。
- 永久测试限于真正保护 redirect 不泄漏与 3xx 处理的**少量关键用例**。若真实 handler loopback 测试可以做成稳定、隔离的高价值测试，优先保留；否则用一次性外部 harness 验证并记录，清理其产物。禁止向互联网/真实 Provider 发凭据。
- 运行相关 Infrastructure / Application/Speech focused tests、format/Release build（若工程环境允许）与 `git diff --check`。记下实测命令、结果、未执行项、保留/移除测试的理由。

## 结束要求

更新 Backlog T003 完成成果并删除本临时任务文件；不提交临时 HTTP 服务、伪凭据文件、trace。不得顺带重构 Provider 接口、改写任意 Provider 规则或影响 Edge Provider。
