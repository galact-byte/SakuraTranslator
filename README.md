<div align="center">
<h1>
  SakuraTranslator
</h1>
</div>

# 介绍
这是一个基于XUnity.AutoTranslator和Sakura模型的Unity游戏本地翻译器，能够提供高质量离线日文翻译  
建议使用[GalTransl-V3翻译模型](https://huggingface.co/SakuraLLM/Sakura-GalTransl-7B-v3)，当前支持版本为 Sakura v0.9/v0.10/v1.0/v1.5，以及使用 GalTransl v2.6/v3 提示词的模型，可从后端自动识别。

## TODO
- [ ] 添加历史上文（搁置，难以将对话文本与ui文本区分，需要更好的规则）
- [x] 添加退化检测
- [x] 重新整理对不同模型的支持

## 详细部署流程
如果你不了解AutoTraslator，请从头阅读部署教程：详见[本仓库wiki](https://github.com/fkiliver/SakuraTranslator/wiki)  


# 快速开始

## 启动Sakura服务
此处以本地部署为例  
确保Sakura服务器成功启动并监听`http://127.0.0.1:8080` (端口根据你的启动方式而定，但要配置文件与之保持一致)

## 修改配置文件
### 安装SakuraTranslator
从[本 Fork 的 Releases](https://github.com/galact-byte/SakuraTranslator/releases) 下载对应包：普通 Mono 游戏选择 `Mono`，BepInEx IL2CPP 游戏选择 `IL2CPP`，不要混用。解压其中的 `SakuraTranslate.dll`，退出游戏后替换 Translators 文件夹内的旧 DLL。
 - 如果你使用ReiPatcher，你应该放在`{游戏目录}\{游戏名}_Data\Managed\Translators`
 - 如果你使用BepInEx，你应该放在`{游戏目录}\BepInEx\plugins\XUnity.AutoTranslator\Translators`

### 配置SakuraTranslator
启动一次游戏，这时应当会自动生成配置文件  
修改AutoTranslator的配置文件  
 - 如果你使用ReiPatcher，配置文件应该在`{游戏目录}\AutoTranslator\Config.ini`
 - 如果你使用BepInEx，配置文件应该在`{游戏目录}\BepInEx\config\AutoTranslatorConfig.ini`

首先修改配置文件前两个字段为
```
[Service]
Endpoint=SakuraTranslate
FallbackEndpoint=

[General]
Language=zh
FromLanguage=ja
```
如果你在使用中出现了缺字、方块字等情况，请指定外部字体：
```
[Behaviour]
OverrideFont=
OverrideFontTextMeshPro=arialuni_sdf_u2018
```
`OverrideFont` 填系统已安装的字体族名称，用于 UGUI；不确定时先留空。

`OverrideFontTextMeshPro` 不仅能填写 TMP 字体资源包，也能在支持的环境中直接填写已安装的系统字体名称，例如 `lolita`。上游从 XUnity.AutoTranslator v5.6.2 起加入系统字体创建路径，官方说明要求游戏的 TextMeshPro 3.2.0+，实际还需存在对应的 `TMP_FontAsset.CreateFontAsset` 方法。字体名应与系统枚举的名称一致，不是字体文件路径。系统字体路径可绕开特定 TMP 资源包与 Unity 版本不兼容的问题，但不保证所有游戏和加载路径都适用。

```ini
[Behaviour]
OverrideFontTextMeshPro=lolita
```

如果系统字体路径不可用，再使用与目标游戏匹配的 TMP 资源包，例如 `arialuni_sdf_u2018` 或 `arialuni_sdf_u2019`。资源包路径优先于同名系统字体；不要在游戏目录保留同名但不兼容的资源包。依据：[官方实现与说明 PR #854](https://github.com/bbepis/XUnity.AutoTranslator/pull/854)、[v5.6.2 更新记录](https://github.com/bbepis/XUnity.AutoTranslator/releases/tag/v5.6.2)。

其中arialuni_sdf_u201x可以从[字体文件](https://github.com/bbepis/XUnity.AutoTranslator/releases/download/v5.4.5/TMP_Font_AssetBundles.zip)获取，请解压后直接放置在游戏根目录  
部分游戏`OverrideFontTextMeshPro`可能无效，可以尝试使用`FallbackFontTextMeshPro`  
此外，若出现不翻译、翻译缺失格式等问题，可尝试修改`MaxCharactersPerTranslation`和`IgnoreWhitespaceInDialogue`选项

### 完整配置示例
```
[Sakura]
Endpoint=http://127.0.0.1:8080/v1/chat/completions
ModelName=Auto
ModelVersion=Auto
RequestTimeoutSeconds=100
MaxTokensMode=Dynamic
StaticMaxTokens=512
DynamicMaxTokensMultiplier=1.5
DictMode=MatchOriginalText
Dict={"想太":["想太","男主人公"],"ダイヤ":["戴亚","女"]}
FixDegeneration=True
MaxConcurrency=1
EnableShortDelay=True
DisableSpamChecks=False
TemperatureOverride=
TopPOverride=
Debug=False
```

### 支持的模型及对应关系
新配置默认使用自动模式。升级旧版时，只需将下面两项改一次，以后更换已支持的模型便不必再改模板配置：

```ini
[Sakura]
ModelName=Auto
ModelVersion=Auto
```

自动模式在翻译前查询同一服务的 `/v1/models`，使用实际模型 ID 发送请求，并匹配提示词模板。后端换模型后下一次新翻译会重新识别；已有翻译缓存不会自动重译。`ModelName=Auto` 是自动模式开关，自动模式下不使用手填的 `ModelVersion`。

- Sakura v0.9、v0.10、v1.0、Qwen3 v1.5：使用各自模板。
- GalTransl v2.6：使用 v2.6 模板。
- Sakura-GalTransl v3.x、GalTransl v4-4B-2601：使用 v3 模板；文件名版本号不等于配置模板版本。
- Base 模型、未确认模板的 SFT 模型、无明确版本的别名不会被猜测匹配。多个模型同时返回时也不会擅自选第一个。

如果使用 `--alias`，别名需要保留可识别的模型名称与版本。无法识别、没有 `/v1/models` 接口或多模型服务时，请查看日志并手动选择模板。自动识别失败不会悄悄退回 Sakura 1.0。

旧配置中的明确 `ModelName` 保持手动模式，不会被覆盖；手动模式不查询模型列表。对应关系如下，其中 `*` 保留旧版手动配置的默认行为：

| ModelName    | ModelVersion | TranslationModel        |
|--------------|--------------|-------------------------|
| Sakura       | 0.9          | Sakura v0.9             |
| Sakura       | 0.10         | Sakura v0.10            |
| Sakura       | 1.0          | Sakura v1.0             |
| Sakura       | 1.5          | Sakura Qwen3 v1.5       |
| Sakura       | *            | Sakura v1.0 (默认)      |
| GalTransl    | 2.6          | GalTransl v2.6          |
| GalTransl    | 3            | GalTransl v3            |
| GalTransl    | *            | GalTransl v3 (默认)     |
| *            | *            | Sakura v1.0 (默认)      |

`Endpoint` 使用 chat completions 地址，例如 `http://127.0.0.1:8080/v1/chat/completions`。仅填写服务根地址或 `/v1` 时会自动补全，反向代理的已有路径前缀会保留。`RequestTimeoutSeconds` 是单次网络请求的总超时，默认为 100 秒；慢速后端可适当增加。不会禁用 HTTPS 证书校验。

### MaxTokens模式及退化检测配置
- `MaxTokensMode`
  - `Static`（默认）：使用`StaticMaxTokens`的值作为发送给llama.cpp的`max_tokens`
  - `Dynamic`：使用原文的字符数乘以`DynamicMaxTokensMultiplier`向上取整作为发送给llama.cpp的`max_tokens`，最小`max_tokens`为`10`
  - 设置为`Dynamic`有助于在模型退化时减少检测时间
- `StaticMaxTokens`：静态MaxTokens值，默认`512`
- `DynamicMaxTokensMultiplier`：动态MaxTokens乘以原文字符数的倍数，默认`1.5`
- `FixDegeneration`
  - `False`（默认）：关闭退化检测功能
  - `True`：开启退化检测功能，当模型返回的 token 数达到 `max_tokens`，或 `finish_reason=length` 时，将`frequency_penalty`增加至`0.2`重新生成，若仍然失败则使用最后获取的结果

### 字典配置
#### 字典配置项
- `DictMode`
  - `None`（默认）：关闭字典功能（注：Sakura 0.9不支持字典，启用字典功能时也不会传递字典）
  - `Full`：传递整个字典
  - `MatchOriginalText`：传递当前翻译句子包含的字典部分
#### 字典配置（Dict）
- `Dict`默认为空字符串，必须为空或合法的Json格式，解析失败将会视为空
- 字典格式`{"src":["dst","info"]}`，发给sakura的字典为`src->dst #info`
- 其中`info`没有可以写成`{"src":["dst"]}`或者`{"src":"dst"}`，此时发给sakura的字典为`src->dst`
- 示例：`{"たちばな":"橘","橘":["橘"],"あやの":["绫乃","女"],"綾乃":["绫乃","女"]}`

### 并发配置
- `MaxConcurrency`：同时发送的翻译请求数，默认为`1`
- 单卡多线程总体翻译速度一般比单线程高，llama.cpp需要设置`-np`参数（可使用`llama-batched-bench`测试）

### 快速翻译配置 :warning:
- **注意：仅建议在本地部署的情况下使用**
- `EnableShortDelay`
  - `False`（默认）：AutoTranslator默认的翻译延迟
  - `True`：设置翻译延迟为0.1秒（AutoTranslator允许的最小值）
- `DisableSpamChecks`
  - `False`（默认）：开启AutoTranslator的Spam检测
  - `True`：关闭Spam检测

### Temperature和TopP覆盖设置
- `TemperatureOverride`：覆盖模型默认的temperature值（可选，留空使用模型默认值）
  - 不同模型的默认temperature值：
	- Sakura系列：0.1
	- GalTransl v2.6：0.2
	- GalTransl v3：0.3
- `TopPOverride`：覆盖模型默认的top_p值（可选，留空使用模型默认值）
  - 不同模型的默认top_p值：
	- Sakura系列：0.3
	- GalTransl系列：0.8

## 启动游戏与排查
完成配置后重新启动游戏，在尚未缓存的新文本上验证。换模型或升级插件不会清理已有译文。如需重译，请先退出游戏并备份，仅移走自动生成的 `_AutoGeneratedTranslations.txt`，不要删除整个翻译目录或人工译文。

- 后端显示已翻译而游戏没变化：先检查自动生成译文文件。文件中已有中文，通常应继续排查 AutoTranslator 的文本挂钩或字体，而不是反复更换模型。
- 出现方块字：检查外部字体是否真正加载。支持新版系统字体路径时，可将 `OverrideFontTextMeshPro` 设为已安装字体名；使用 TMP 资源包时则检查它是否匹配当前 Unity 版本。改为繁体不能保证补齐字体，不属于插件已修复的显示能力。
- 主菜单或按钮文字画在图片中：本插件不识别和替换图片文字。
- 游戏启动后没有配置文件，或翻译器列表没有 SakuraTranslate：检查 BepInEx/AutoTranslator 是否加载成功、DLL 是否放入正确目录、Mono/IL2CPP 包是否选对。仅手工添加配置段不能修复加载失败。
- 地址错误、超时、未知模板和响应缺少正文会在日志中明确报错。`usage` 统计字段不是有效译文的必需字段，不会因缺失统计而丢弃译文。

逐项上游 Issue 核查记录见 [docs/UPSTREAM_ISSUES.md](docs/UPSTREAM_ISSUES.md)。

![image](https://github.com/fkiliver/SakuraTranslator/assets/48873439/ffba161d-8d0c-4a0e-bd15-71ab95db30ef)

![image](https://github.com/fkiliver/SakuraTranslator/assets/48873439/ffba161d-8d0c-4a0e-bd15-71ab95db30ef)
