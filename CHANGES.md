# 变更记录

## 2026-11-23 — v0.3.11：模型自动识别与请求容错

### 目标与边界

- 从实际后端识别模型及模板，避免每次更换模型都修改配置；旧版明确配置继续使用手动模式。
- 处理已经复现的响应、控制符与区域设置错误，补充上游 Issue 的证据分类与排查指引。
- 不替换游戏字体、不修改游戏原始配置和译文缓存，不添加云模型适配，不关闭 TLS 校验或其他安全控制。

### 实现

- 新配置默认 `ModelName=Auto`、`ModelVersion=Auto`。自动模式在翻译前读取同源 `/v1/models`，发送真实模型 ID；模型切换后自动更新模板。明确版本的旧配置不自动改写。
- 匹配既有 Sakura/GalTransl 模板；依据官方模型卡确认 GalTransl v4-4B-2601 使用 v3 模板，为 Sakura Qwen3 v1.5 增加对应系统提示词。未知模板、Base、无版本别名和多个模型明确失败，不猜测回退。
- 校验 Endpoint，补全根地址或 `/v1`，保留反向代理前缀。
- 统一异步请求与超时，协程取消时中止请求；响应头和正文都纳入超时。缺少 usage 不再阻止有效译文提交，退化检测可参考 finish_reason，保持一次重试上限。
- 从 JSON 字符串值读取正文，避免破坏首尾引号或二次反转义控制符；按 invariant culture 写入 JSON 小数。
- 工作流监听真实 `master` 和维护分支，构建 Mono/IL2CPP、运行接口回归、上传区分类型的压缩包及 SHA-256 文件。只有手动授权的发布任务才创建 Release，并校验标签与项目版本。

### 主要文件

- 模型匹配：`SakuraTranslate/Helpers/TranslateModelHelper.cs`
- 请求生命周期：`SakuraTranslate/Helpers/HttpRequestHelper.cs`
- 初始化与翻译：`SakuraTranslate/Modules/Initialize.cs`、`Translate.cs`
- 提示词与 JSON：`SakuraTranslate/Modules/Helpers/RequestHelper.cs`、`Modules/TranslateModels/MakeSakuraPromptV1_0.cs`
- 测试：`Tests/`；逐项核查：`docs/UPSTREAM_ISSUES.md`
- 发布：`.github/workflows/dotnet.yml`、`RELEASE_NOTES.md`

### 已执行验证

- 区域设置 RED：中文环境通过，法语/德语环境均生成无效 JSON；修复后均通过。
- 模型识别与实际插件协程 RED：自动识别未实现、usage 缺失、响应引号及控制符错误均被捕获；未知模型仍发送猜测请求的行为被捕获。
- 边界 RED：异常 JSON 结构暴露空引用；修复后报告明确异常。测试服务的停止竞争单独修正，未放宽生产超时。
- 使用实际插件工程在本机 .NET 6 环境执行 50 项回归，全部通过；依赖使用仓库自带开发 DLL，未为本地打包安装 Python/.NET 软件包。
- 远端 Actions `45053414779`：Mono .NET 3.5 构建、IL2CPP .NET 6 构建及全部接口回归成功，两种压缩包已生成。
- 实际本机 b12633：识别 GalTransl v3.7 并完成日中请求。证据归档在 `E:/DevData/sakura-plugin-inspect/plugin-live-backend.log`；该路径是本地证据，不作为用户安装前提。
- 首次发布构建 `45053682866` 捕获 .NET 原生 HTTP 超时先于协程计时器结束的竞争；已统一两条路径的 TimeoutException，并增加直接请求回归，不放宽超时条件。
- 发布状态以对应手动 Actions 任务为准。尚未替换用户游戏 DLL，尚未进行 Mono/IL2CPP 的新 DLL 游戏内实测。

### 使用与回退

- 升级只替换插件 DLL；旧配置继续有效。启用自动模式只需修改两项一次。
- 自动模式要求后端返回唯一、可识别的模型 ID；自定义别名或不提供模型接口时可手动选择模板。
- 回退时关闭游戏、换回此前 DLL，恢复旧的明确 ModelName/ModelVersion；不会迁移或删除存档及已有译文。
