# T017：限制 HTTP TTS 成功响应落盘大小

## 目标

在 HTTP TTS 成功音频的唯一临时文件写入边界设置有界 copy，防止异常大的 2xx body 在解码前耗尽 Cache 磁盘。

审计依据：`CODEBASE_AUDIT_REPORT.md` F02。相关合同：`docs/specs/HTTP_TTS.md`、`docs/04_CACHE_AND_BACKGROUND_WORK.md`。

## 范围与约束

- 从响应取得到 `TemporaryAudioStore` 写入再到音频校验，追踪所有成功和失败路径。
- 依据当前支持的 Provider 音频格式/最大合成长度确定字节预算；不能凭空选值。若现有产品合同不足以推导边界，记录明确的产品决策点并停止改变该边界。
- 在复制时累计实际字节数；不能只信任 `Content-Length`。超限时停止读取、删除部分临时文件并返回稳定错误。
- 维持取消、现有错误 body 脱敏、音频解码校验和临时文件 owner；不增加完整 body 缓冲或重复预检路径。

## 验收

- 验证小于/等于上限、超过上限、无 `Content-Length`、取消及写入失败的清理行为。
- 保持现有 HTTP provider 行为和隐私合同；不将 body/Header/凭据写入日志。
- 执行 Infrastructure focused tests、format 和 Release build；记录未执行项。

## 交付

删除重复/无效写入路径，更新 Backlog 成果并删除本规格。
