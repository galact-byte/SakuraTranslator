# 上游 Issue 核查与处理

核查日期：2026-11-23。来源：[fkiliver/SakuraTranslator 开放 Issue](https://github.com/fkiliver/SakuraTranslator/issues)。不代原作者关闭 Issue；相似现象不等于已证实同一根因。

| Issue | 核查结果 | 本次处理 |
|---|---|---|
| #32 后端翻译但游戏不显示 | 原报告缺少日志，作者要求检查自动生成译文文件，尚无法证明是插件故障 | 增加日志和分层排查说明；另行复现并修复缺少 usage 时无法提交译文及字符串解码错误，但不宣称已解决该用户的原问题 |
| #30 希望繁体输出 | 属于输出语言请求，涉及部分游戏字体覆盖不足；不能把繁体当成通用字体修复 | 明确字体和输出模板的边界。没有加入未经验证的繁简转换依赖，也没有承诺已支持繁体输出 |
| #28 外部 API 与代理方案分享 | 不是本插件可复现缺陷，列出的是独立项目和代理实现 | 不把其他云服务接口强行合并到 Sakura 模板插件；兼容后端可手动选择模板 |
| #27 字体名称如何填写 | 旧版与新版加载能力不同，不能把 TMP 字段限制为资源包 | 已核对上游 PR #854 与 v5.6.2 更新记录：支持环境可直接填写系统字体名，官方要求 TMP 3.2.0+ 及对应创建方法。README 解释系统字体和资源包两种路径、版本条件、同名文件优先级，并移除会被误粘贴为配置值的行内说明 |
| #26 简体显示方块，转繁体部分改善 | 报告与评论指向字体资源没有正确替换或缺少字形，外部转换只能缓解部分情况 | 增加字体排查与限制说明；没有冒充已修好游戏字体或实现繁体强转 |
| #19 Invalid URI / Invalid port | 报告使用旧插件类型，当前代码依然缺少初始化地址校验 | 新增 HTTP/HTTPS 地址校验与根地址补全，错误配置在初始化时报告，避免延迟到翻译阶段抛难懂错误 |
| #17 无 AutoTranslator 配置目录 | 安装、引擎及 IL2CPP 加载问题，不能靠手工补配置修复 | 增加加载检查说明，构建与交付 Mono/IL2CPP 两种包；尚未进行 IL2CPP 游戏内实测，不承诺解决原游戏的注入兼容问题 |
| #15 代理 /completion ReceiveFailure | 原报告采用旧配置，代理的真实协议与网络原因未确认 | 说明当前使用 chat completions API；增加 HTTP 错误与超时诊断，手动模式不依赖模型列表；不伪装支持任意旧 /completion 协议 |
| #14 配置没有 Sakura 段、面板无插件 | 作者回复与现象指向 DLL 没有加载或安装包不匹配 | README 补充正确包与目录、加载失败的排查顺序；不要求盲目复制配置 |

## 模型识别依据

- [Sakura-GalTransl-7B-v3.7](https://huggingface.co/SakuraLLM/Sakura-GalTransl-7B-v3.7)：使用 v3 提示词。
- [GalTransl-v4-4B-2601](https://huggingface.co/SakuraLLM/GalTransl-v4-4B-2601)：模型说明明确提示词格式同 v3.7。
- [Sakura-14B-Qwen3-v1.5-GGUF](https://huggingface.co/SakuraLLM/Sakura-14B-Qwen3-v1.5-GGUF)：使用模型说明中的新版系统提示词。
- Sakura v0.9/v0.10/v1.0、GalTransl v2.6/v3 沿用仓库既有模板。没有明确依据的 v1.1 SFT 与 Base 模型不会猜测匹配。

## 验证方式

`Tests` 为不依赖第三方测试框架的 .NET 6 控制台回归测试，引用实际插件工程及仓库内的开发 DLL。它通过本机 HTTP 测试服务驱动插件真实的初始化和翻译协程，覆盖默认自动模式、后端换模型、旧手动模式、未知/多个模型、统计缺失、控制符、异常响应、超时和协程取消。不是 Python 仿写协议测试。

命令：

```text
dotnet run --project Tests/Tests.csproj -c IL2CPPRelease
```

另用实际运行的 llama.cpp b12633 服务验证，插件识别到 `Sakura-Galtransl-7B-v3.7-IQ4_XS.gguf` 并完成「こんにちは」→「你好」。这不等于在所有 Unity 游戏内实测。Mono .NET 3.5 与 IL2CPP .NET 6 构建均由远端工作流核验，发布必须以构建、测试成功为前提。
