# MTranslate 本地桌面翻译器开发规格

> 面向 AI 编程模型 / Agent 的实现文档  
> 目标平台：Windows + macOS  
> 文档版本：v1.0  
> 日期：2026-08-27

* * *

## 0\. 文档目的

本项目目标是开发一款**轻量、完全本地优先、可扩展的桌面翻译软件**。软件核心使用 Hy-MT2-1.8B 小型机器翻译模型，通过 GGUF + llama.cpp 在用户电脑端运行。

产品同时承担两个角色：

1.  **桌面翻译软件**
    
    -   单段文本翻译
    -   文件翻译
    -   TXT / SRT / VTT / Markdown 等纯文本类文档翻译
    -   翻译历史
    -   双语查看
    -   模型管理
    -   术语表和基础翻译设置
2.  **本地翻译服务**
    
    -   向浏览器插件提供翻译 API
    -   向 OCR 软件提供翻译 API
    -   向字幕工具、脚本、编辑器、自动化工具等第三方本地软件提供 API
    -   默认只允许本机访问，不开放公网

本项目第一阶段不追求完整复刻 DeepL、Google Translate、有道翻译等成熟商业产品，而是优先完成：

> 小体积、本地运行、低门槛、稳定、可调用、可扩展。

* * *

# 1\. 核心技术决策

## 1.1 桌面技术栈

建议：

-   UI：Avalonia UI
-   Runtime：.NET 10 LTS
-   语言：C#
-   架构：MVVM
-   DI：Microsoft.Extensions.DependencyInjection
-   配置：Microsoft.Extensions.Configuration
-   日志：Serilog
-   本地数据库：SQLite
-   JSON：System.Text.Json
-   HTTP API：[ASP.NET](http://ASP.NET) Core Minimal API
-   推理后端：llama.cpp / llama-server
-   模型格式：GGUF
-   打包：
    -   Windows：MSIX 或 NSIS/Inno Setup
    -   macOS：.app + DMG
-   单元测试：xUnit
-   集成测试：xUnit + TestServer / 独立 API 测试

不建议第一版使用：

-   Python 作为主运行环境
-   PyTorch
-   Transformers
-   Electron
-   浏览器 WebView 作为主 UI

原因：

-   会显著增加安装体积和依赖复杂度
-   Python/PyTorch 对普通桌面用户不够友好
-   Electron 内存占用没有必要
-   GGUF + llama.cpp 更适合该项目的端侧部署目标

* * *

# 2\. 模型方案

软件必须支持两种 Hy-MT2-1.8B 模型，并允许用户随时切换。

## 2.1 极速模型

名称：

`Hy-MT2 1.8B 2-Bit`

建议模型：

`AngelSlim/Hy-MT2-1.8B-2Bit-GGUF`

用途：

-   低配置电脑
-   CPU 推理
-   划词翻译
-   网页翻译
-   短文本
-   字幕
-   高频快速翻译

特点：

-   GGUF 文件约 601 MB
-   内存占用低
-   启动速度快
-   普通翻译质量优秀
-   复杂格式、复杂指令遵循能力弱于 Q4

重要兼容要求：

该版本使用 Q2\_0c 量化格式。

必须：

1.  使用明确支持 Q2\_0c 的 llama.cpp 版本。
2.  项目锁定 llama.cpp 的测试通过版本。
3.  不允许开发阶段“自动跟随 latest”。
4.  升级 llama.cpp 前必须执行完整回归测试。

* * *

## 2.2 标准模型

名称：

`Hy-MT2 1.8B Q4`

首选：

`Hy-MT2-1.8B-Q4_K_M.gguf`

参考仓库：

`tencent/Hy-MT2-1.8B-GGUF`

用途：

-   默认模型
-   文件翻译
-   网页长文本
-   Markdown
-   SRT/VTT
-   更高质量翻译
-   更好的格式保持
-   更复杂语言对

模型大小约：

`1.13 GB`

软件首次启动时建议默认推荐此模型。

* * *

## 2.3 模型模式

UI 中不要直接向普通用户显示过多量化技术名词。

显示：

### 极速

说明：

> 占用更低，适合日常短文本和低配置电脑。

对应：

`2-Bit`

### 标准

说明：

> 翻译质量和稳定性更好，推荐日常使用。

对应：

`Q4_K_M`

高级设置中再显示：

-   模型文件
-   GGUF quant type
-   SHA256
-   文件大小
-   llama.cpp runtime 版本

* * *

# 3\. 模型下载与管理

模型不能强制内置在安装包中。

建议：

```text
安装程序
≈ 100~300 MB

首次启动
↓
选择模型
↓
下载 GGUF
```

模型管理页需要提供：

-   极速模型
-   标准模型
-   当前模型
-   下载状态
-   下载速度
-   下载进度
-   暂停
-   继续
-   删除
-   校验
-   切换
-   模型存储目录
-   磁盘占用

必须支持：

### 断点续传

使用 HTTP Range。

临时文件：

```text
model.gguf.part
```

下载结束：

1.  校验 SHA256
2.  校验通过
3.  原子 rename
4.  更新数据库

禁止：

下载一半时把文件识别成可用模型。

* * *

# 4\. llama.cpp Runtime 管理

不要要求用户自行安装 llama.cpp。

软件应内置经过测试的 llama.cpp runtime。

目录示例：

```text
/runtime
    /win-x64
        llama-server.exe
        *.dll

    /osx-arm64
        llama-server
        *.dylib

    /osx-x64
        llama-server
        *.dylib
```

软件启动时：

```text
App
↓
RuntimeManager
↓
检测 CPU / GPU / OS
↓
选择 runtime
↓
启动 llama-server
```

* * *

# 5\. 推理架构

推荐采用：

```text
┌──────────────────────────────┐
│        Avalonia Desktop      │
└──────────────┬───────────────┘
               │
               ▼
┌──────────────────────────────┐
│     Translation Service      │
│                              │
│ Prompt Builder               │
│ Chunk Manager                │
│ Translation Cache            │
│ Glossary                     │
│ File Parser                  │
│ Job Queue                    │
└──────────────┬───────────────┘
               │
               ▼
┌──────────────────────────────┐
│ Internal Inference Client    │
└──────────────┬───────────────┘
               │ HTTP
               ▼
┌──────────────────────────────┐
│ llama-server                 │
│ 127.0.0.1:17892              │
└──────────────┬───────────────┘
               │
               ▼
          Hy-MT2 GGUF
```

注意：

**第三方软件绝对不要直接调用 17892。**

17892 是内部端口。

外部调用统一经过应用自己的 API Gateway。

* * *

# 6\. 端口设计

避免常见开发端口：

-   3000
-   5000
-   5173
-   8000
-   8080
-   8888
-   11434

建议：

## 外部 API

```text
127.0.0.1:17891
```

## llama.cpp 内部 API

```text
127.0.0.1:17892
```

## 备用端口池

如果端口被占用：

```text
17891
17893
17895
17897
17899
```

内部：

```text
17892
17894
17896
17898
17900
```

程序启动时必须进行端口检测。

对外 API 的实际端口必须显示在：

`设置 → 本地 API`

浏览器插件默认尝试：

```text
17891
17893
17895
17897
17899
```

用户也可以手动指定。

* * *

# 7\. 网络安全原则

默认监听：

```text
127.0.0.1
```

禁止默认监听：

```text
0.0.0.0
```

默认：

-   不允许局域网访问
-   不允许公网访问
-   不允许匿名执行翻译 API
-   `/health` 可匿名
-   `/pair` 仅允许一次性配对
-   其它接口需要 Token

* * *

# 8\. API Gateway

桌面应用内部启动：

[ASP.NET](http://ASP.NET) Core Minimal API。

Base URL：

```text
http://127.0.0.1:17891/api/v1
```

* * *

# 9\. API 鉴权

采用：

```http
Authorization: Bearer <TOKEN>
```

Token：

-   256 bit 随机值
-   Base64Url 编码
-   每个客户端独立 Token
-   支持吊销
-   支持重新配对

数据库记录：

```text
ApiClient
---------
Id
Name
TokenHash
CreatedAt
LastUsedAt
Permissions
Revoked
```

数据库只保存：

`SHA256(token)`

禁止明文保存 Token。

* * *

# 10\. 浏览器插件配对

第一次连接：

插件：

```http
GET /api/v1/health
```

确认桌面软件存在。

桌面软件生成：

```text
6 位一次性配对码
```

例如：

```text
482731
```

有效期：

`5 分钟`

插件：

```http
POST /api/v1/pair
```

Request：

```json
{
  "code": "482731",
  "clientName": "HyMT Browser Extension",
  "clientType": "browser-extension"
}
```

Response：

```json
{
  "success": true,
  "token": "...",
  "apiVersion": "1.0"
}
```

插件保存 Token 到：

```text
chrome.storage.local
```

* * *

# 11\. CORS 与本地安全

API 必须检查：

-   Host
-   Origin
-   Authorization

允许 Host：

```text
127.0.0.1
localhost
```

浏览器插件允许 Origin：

```text
chrome-extension://*
moz-extension://*
```

不要简单地对所有网站返回：

```http
Access-Control-Allow-Origin: *
```

普通网页：

```text
https://xxx.com
```

不应该直接调用本地翻译 API。

第三方桌面软件通常没有 Origin Header，可以凭 API Token 调用。

* * *

# 12\. API 设计

## 12.1 健康检查

```http
GET /api/v1/health
```

Response：

```json
{
  "status": "ok",
  "version": "1.0.0",
  "apiVersion": "1.0",
  "engine": "llama.cpp",
  "modelLoaded": true,
  "model": "hy-mt2-1.8b-q4"
}
```

* * *

## 12.2 当前服务信息

```http
GET /api/v1/info
```

需要鉴权。

返回：

```json
{
  "appVersion": "1.0.0",
  "apiVersion": "1.0",
  "activeModel": "standard",
  "supportedLanguages": [],
  "streaming": true,
  "batch": true
}
```

* * *

## 12.3 模型信息

```http
GET /api/v1/models
```

Response：

```json
{
  "active": "hy-mt2-1.8b-q4",
  "models": [
    {
      "id": "hy-mt2-1.8b-2bit",
      "name": "Hy-MT2 1.8B Fast",
      "installed": true
    },
    {
      "id": "hy-mt2-1.8b-q4",
      "name": "Hy-MT2 1.8B Standard",
      "installed": true
    }
  ]
}
```

* * *

# 13\. 单文本翻译 API

```http
POST /api/v1/translate
```

Request：

```json
{
  "text": "Hello world.",
  "sourceLanguage": "auto",
  "targetLanguage": "zh-CN",
  "mode": "standard",
  "context": null,
  "options": {
    "preserveLineBreaks": true,
    "useCache": true
  }
}
```

Response：

```json
{
  "requestId": "uuid",
  "translatedText": "你好，世界。",
  "detectedLanguage": "en",
  "targetLanguage": "zh-CN",
  "model": "hy-mt2-1.8b-q4",
  "cached": false,
  "elapsedMs": 382
}
```

* * *

# 14\. 批量翻译 API

```http
POST /api/v1/translate/batch
```

Request：

```json
{
  "items": [
    {
      "id": "1",
      "text": "Hello"
    },
    {
      "id": "2",
      "text": "Good morning"
    }
  ],
  "sourceLanguage": "auto",
  "targetLanguage": "zh-CN",
  "mode": "fast"
}
```

Response：

```json
{
  "items": [
    {
      "id": "1",
      "translatedText": "你好"
    },
    {
      "id": "2",
      "translatedText": "早上好"
    }
  ]
}
```

建议最大：

```text
50 items/request
```

并限制总 Token。

* * *

# 15\. Streaming API

用于：

-   长文本
-   OCR
-   网页翻译
-   UI 实时显示

可以采用：

Server-Sent Events。

Endpoint：

```http
POST /api/v1/translate/stream
```

事件：

```text
start
delta
complete
error
```

不要让浏览器插件直接使用 llama.cpp streaming 格式。

统一由 Gateway 转换成稳定协议。

* * *

# 16\. OpenAI-compatible API

第一版可以不开放。

V1.1 可选提供：

```text
/api/openai/v1/chat/completions
```

但必须明确：

这只是兼容层。

外部翻译客户端应该优先使用：

```text
/api/v1/translate
```

原因：

翻译 API 更稳定、更容易版本管理。

* * *

# 17\. 翻译 Prompt

Hy-MT2 没有必要加入复杂 system prompt。

Prompt 必须保持短、明确。

标准模板：

```text
Translate the following segment into {TargetLanguage}, without additional explanation:

{Text}
```

如果 source 明确：

```text
Translate the following {SourceLanguage} segment into {TargetLanguage}, without additional explanation:

{Text}
```

禁止默认添加：

```text
You are a helpful AI assistant...
```

不要把软件 UI 逻辑、JSON 结构说明等大量信息塞进 prompt。

* * *

# 18\. 上下文翻译

API 允许：

```json
{
  "context": "Previous paragraph..."
}
```

Prompt：

```text
Use the previous context only to understand meaning and terminology.
Translate only the segment marked CURRENT into Chinese.
Do not translate the context.

CONTEXT:
...

CURRENT:
...
```

注意：

2-Bit 模型的复杂指令遵循能力较弱。

因此：

-   Fast 模式尽量减少 context prompt 复杂度
-   Standard 模式允许上下文
-   上下文不能无限增长

推荐滑动窗口：

```text
上一段
+
当前段
```

或：

```text
最近 500~1000 tokens 上下文
```

* * *

# 19\. 推理参数

初版优先从 Hy-MT2 官方推荐参数开始测试：

```json
{
  "temperature": 0.7,
  "top_p": 0.6,
  "top_k": 20,
  "repetition_penalty": 1.05,
  "max_tokens": 4096
}
```

但是不要将其永久硬编码。

配置：

```text
TranslationProfile
```

允许内部 A/B 测试：

-   temperature
-   top\_p
-   top\_k
-   repetition penalty
-   output limit

最终以实际翻译 QA 结果决定生产参数。

* * *

# 20\. llama-server 启动

内部命令示意：

```bash
llama-server \
  -m "<model-path>" \
  --host 127.0.0.1 \
  --port 17892 \
  -c 8192 \
  -np 2
```

实际参数必须由：

`InferenceRuntimeConfiguration`

统一生成。

禁止散落在 UI 代码中。

* * *

# 21\. 模型加载状态

状态机：

```text
NotInstalled
↓
Downloading
↓
Installed
↓
Loading
↓
Ready
↓
Unloading
```

异常：

```text
DownloadFailed
ChecksumFailed
LoadFailed
RuntimeCrashed
```

UI 必须明确展示：

```text
正在下载
正在加载
已就绪
加载失败
```

不能出现用户点击翻译后无响应。

* * *

# 22\. 模型切换

切换模型：

```text
Fast → Standard
```

流程：

1.  暂停新任务
2.  等待当前任务结束，或用户选择取消
3.  停止 llama-server
4.  清理旧进程
5.  启动新模型
6.  调用 health
7.  Ready
8.  恢复任务队列

不能同时加载两个模型作为第一版默认行为。

避免浪费 RAM。

* * *

# 23\. 崩溃恢复

RuntimeManager 必须监控：

```text
llama-server process
```

若意外退出：

```text
ProcessExited
↓
记录日志
↓
当前任务标记 interrupted
↓
自动重启一次
↓
Health Check
```

若连续：

```text
3 次 / 5 分钟
```

停止自动重启。

显示：

> 翻译引擎启动失败，请检查模型或运行环境。

* * *

# 24\. 翻译任务队列

所有翻译都必须进入统一队列。

来源：

```text
Desktop Text
File
Browser
OCR API
Other API
```

定义：

```csharp
TranslationJob
{
    Id
    Source
    Priority
    CreatedAt
    CancellationToken
}
```

优先级建议：

```text
High
- 划词
- OCR
- 单文本 API

Normal
- 网页可视区域
- 普通输入框

Low
- 文件
- 网页后台区域
```

这样用户进行一个大型 SRT 翻译时，仍然可以快速执行一次划词翻译。

* * *

# 25\. 并发策略

不要默认让小模型无限并发。

初版：

```text
Inference parallel slots = 1~2
```

API 可以接收多个请求，但：

```text
Gateway
↓
Queue
↓
Inference Scheduler
```

统一调度。

网页批量翻译必须尽量 Batch。

* * *

# 26\. Chunk Manager

禁止把任意长度文本一次性丢给模型。

统一：

```text
Text
↓
Normalize
↓
Segment
↓
Chunk
↓
Translate
↓
Merge
```

推荐初值：

普通文本：

```text
Input target:
800~1800 tokens/chunk
```

不要仅按字符切割。

优先：

1.  段落
2.  句子
3.  Token

* * *

# 27\. 长文本切分

切分优先级：

```text
\n\n
↓
\n
↓
句号 / 问号 / 感叹号
↓
语言对应句末符号
↓
Token hard limit
```

禁止：

把一句话从单词中间切开。

* * *

# 28\. 翻译缓存

使用 SQLite。

表：

```text
TranslationCache
----------------
Hash
SourceLanguage
TargetLanguage
Model
SourceText
TranslatedText
CreatedAt
LastUsedAt
HitCount
```

Hash：

```text
SHA256(
 normalized_text
 + source
 + target
 + model/profile
 + glossary_version
)
```

缓存默认开启。

上限设置：

```text
500 MB
```

用户可：

-   清空
-   禁用
-   修改上限

* * *

# 29\. 术语表

V1 支持简单术语表：

```text
Source
Target
CaseSensitive
ExactMatch
```

例如：

```text
BIGO LIVE → BIGO LIVE
LLM → 大语言模型
```

应用前：

```text
Source Text
↓
Glossary Protector
↓
Hy-MT2
↓
Glossary Restorer
```

不要完全依赖 prompt 保持术语。

可以将关键术语替换为稳定占位符。

* * *

# 30\. 桌面端首页

建议布局：

```text
┌─────────────────────────────────────────┐
│ Source: Auto        Target: Chinese     │
│ Model: Standard                         │
├───────────────────┬─────────────────────┤
│ 原文              │ 译文                │
│                   │                     │
│                   │                     │
├───────────────────┴─────────────────────┤
│ Translate  Copy  Swap  Clear            │
└─────────────────────────────────────────┘
```

支持：

-   自动语言
-   目标语言
-   模型模式
-   粘贴
-   清空
-   复制
-   取消翻译
-   翻译耗时

第一版不需要：

-   富文本编辑器
-   WYSIWYG
-   Office 风格 Ribbon

* * *

# 31\. 文件翻译页面

支持拖拽。

```text
拖入文件
↓
识别类型
↓
解析
↓
显示信息
↓
选择目标语言
↓
选择输出目录
↓
翻译
```

任务列表：

```text
文件名
类型
源语言
目标语言
进度
状态
耗时
输出
```

支持：

-   多文件排队
-   暂停
-   取消
-   重试
-   打开输出目录

* * *

# 32\. 第一版文件格式

P0：

```text
.txt
.srt
.vtt
```

P1：

```text
.md
.markdown
.ass
```

暂不把以下格式放进第一版核心：

```text
.docx
.xlsx
.pptx
.pdf
```

原因：

这些不是纯文本容器，需要复杂格式重建。

后续应通过独立 Parser Plugin 实现。

* * *

# 33\. TXT 翻译

流程：

```text
读取文件
↓
检测 BOM / Encoding
↓
Normalize
↓
按段落 Chunk
↓
翻译
↓
Merge
↓
写入新文件
```

默认：

不覆盖源文件。

输出：

```text
example.zh-CN.txt
```

或：

```text
example.translated.zh-CN.txt
```

* * *

# 34\. SRT 翻译

SRT 必须使用 parser。

禁止：

正则全文件直接替换。

结构：

```text
Cue
- Index
- Start
- End
- Lines
```

只翻译：

`Lines`

绝对不能修改：

-   序号
-   时间码

例如：

```text
12
00:00:18,100 --> 00:00:20,300
Hello!
```

输出：

```text
12
00:00:18,100 --> 00:00:20,300
你好！
```

* * *

# 35\. SRT 批处理策略

不要每条字幕单独启动一次完整推理请求。

建议：

```text
10~30 cues
↓
一个 Batch
```

内部为每条 Cue 分配 ID。

如果批量返回发生：

-   缺失
-   ID 错乱
-   条数不一致

则：

```text
自动对失败 Cue 单条重试
```

这样兼顾速度和稳定性。

* * *

# 36\. 双语字幕

支持：

```text
仅译文
```

以及：

```text
原文
译文
```

例：

```text
Hello, everyone.
大家好。
```

UI 设置：

```text
Subtitle Output
- Translation only
- Original + Translation
- Translation + Original
```

* * *

# 37\. VTT

必须保留：

```text
WEBVTT
timestamps
cue settings
NOTE
STYLE
REGION
```

只翻译 Cue 文本。

* * *

# 38\. Markdown

必须保护：

-   fenced code block
-   inline code
-   URL
-   link destination
-   image URL
-   HTML tag
-   front matter key
-   markdown syntax

例如：

```markdown
[OpenAI](https://openai.com)
```

可以翻译：

`OpenAI`

但禁止修改 URL。

Code block 默认不翻译。

* * *

# 39\. 文件进度

进度不能只按照：

```text
已完成 chunk 数 / 总 chunk 数
```

因为 chunk 长度不同。

建议按：

```text
已完成 source token / 总 source token
```

计算。

* * *

# 40\. 文件任务恢复

大型文件翻译保存 checkpoint：

```text
JobId
FileHash
TargetLanguage
Model
CompletedSegments
OutputTempFile
```

应用异常退出后：

提示：

> 检测到未完成的翻译任务，是否继续？

* * *

# 41\. 输出安全

永远写：

```text
*.tmp
```

结束后：

```text
flush
↓
validate
↓
atomic rename
```

防止程序中途崩溃得到损坏的正式输出文件。

* * *

# 42\. Language Code

内部统一使用 BCP-47：

```text
en
zh-CN
zh-TW
ja
ko
de
fr
es
pt-BR
tr
ar
vi
th
id
```

不要在代码里混用：

```text
Chinese
CN
zh_CN
zh
```

显示名和内部 code 分开。

* * *

# 43\. 自动语言识别

V1 可以使用两层策略：

```text
短文本
↓
轻量 Language Detector

无法可靠判断
↓
Hy-MT2 Auto
```

Language Detector 必须做成接口：

```csharp
ILanguageDetector
```

未来可以替换为：

-   fastText language ID
-   CLD 类模型
-   其它小型本地 detector

不要让检测实现耦合 UI。

* * *

# 44\. 历史记录

SQLite：

```text
TranslationHistory
------------------
Id
SourceText
TranslatedText
SourceLanguage
TargetLanguage
Model
CreatedAt
SourceType
```

设置：

```text
History:
On / Off
```

隐私模式：

```text
Do not save history
```

* * *

# 45\. API 客户端管理

设置页：

```text
Local API
```

显示：

-   API 状态
-   端口
-   当前模型
-   已配对客户端
-   最近访问
-   删除 Token
-   新建 API Token
-   重启 API

例如：

```text
Chrome Extension
Last used: 1 min ago

OCR Tool
Last used: Yesterday
```

* * *

# 46\. 第三方 OCR 软件接入示例

OCR 软件：

```http
POST http://127.0.0.1:17891/api/v1/translate
Authorization: Bearer TOKEN
Content-Type: application/json
```

Body：

```json
{
  "text": "recognized OCR text",
  "sourceLanguage": "auto",
  "targetLanguage": "zh-CN",
  "mode": "fast"
}
```

* * *

# 47\. Rate Limit

本地 API 同样需要保护。

默认：

```text
120 requests/min/client
```

批量 API：

```text
30 requests/min/client
```

超过返回：

```http
429 Too Many Requests
```

主要目的不是商业限流，而是防止：

-   插件 BUG 无限调用
-   OCR 软件循环
-   恶意页面间接触发

* * *

# 48\. 请求限制

单个普通请求：

```text
max input:
约 32 KB 文本
```

超长内容：

返回：

```http
413 Payload Too Large
```

提示客户端：

使用：

```text
batch
```

或：

```text
jobs
```

接口。

* * *

# 49\. 错误协议

统一：

```json
{
  "error": {
    "code": "MODEL_NOT_READY",
    "message": "Translation model is not ready.",
    "requestId": "uuid"
  }
}
```

错误码：

```text
INVALID_REQUEST
UNAUTHORIZED
MODEL_NOT_INSTALLED
MODEL_LOADING
MODEL_NOT_READY
MODEL_LOAD_FAILED
QUEUE_FULL
REQUEST_TOO_LARGE
TRANSLATION_FAILED
RATE_LIMITED
UNSUPPORTED_LANGUAGE
INTERNAL_ERROR
```

* * *

# 50\. API Versioning

必须从第一版就使用：

```text
/api/v1/
```

未来：

```text
/api/v2/
```

不要直接：

```text
/translate
```

否则后续很难兼容插件旧版本。

* * *

# 51\. 翻译服务抽象

核心接口：

```csharp
public interface ITranslationService
{
    Task<TranslationResult> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken);

    IAsyncEnumerable<TranslationDelta> TranslateStreamAsync(
        TranslationRequest request,
        CancellationToken cancellationToken);
}
```

文件模块：

```csharp
public interface IDocumentTranslator
{
    Task<DocumentTranslationResult> TranslateAsync(
        DocumentTranslationRequest request,
        IProgress<TranslationProgress> progress,
        CancellationToken cancellationToken);
}
```

* * *

# 52\. Parser 架构

```csharp
public interface IDocumentParser
{
    bool CanHandle(string extension);

    Task<ParsedDocument> ParseAsync(...);

    Task WriteAsync(...);
}
```

实现：

```text
TxtDocumentParser
SrtDocumentParser
VttDocumentParser
MarkdownDocumentParser
AssDocumentParser
```

以后扩展：

```text
DocxDocumentParser
PdfDocumentParser
```

不需要修改 TranslationService。

* * *

# 53\. 推荐 Solution 结构

```text
HyMTTranslator.sln

src/
├─ HyMT.Desktop/
│  ├─ Views/
│  ├─ ViewModels/
│  └─ App.axaml
│
├─ HyMT.Application/
│  ├─ Translation/
│  ├─ Jobs/
│  ├─ Documents/
│  └─ Models/
│
├─ HyMT.Domain/
│  ├─ Entities/
│  ├─ ValueObjects/
│  └─ Interfaces/
│
├─ HyMT.Infrastructure/
│  ├─ Database/
│  ├─ Cache/
│  ├─ Files/
│  └─ Settings/
│
├─ HyMT.Inference/
│  ├─ LlamaCpp/
│  ├─ Runtime/
│  ├─ Prompting/
│  └─ ModelManager/
│
├─ HyMT.Api/
│  ├─ Endpoints/
│  ├─ Auth/
│  ├─ Middleware/
│  └─ Contracts/
│
└─ HyMT.DocumentFormats/
   ├─ Txt/
   ├─ Srt/
   ├─ Vtt/
   └─ Markdown/

tests/
├─ HyMT.UnitTests/
├─ HyMT.IntegrationTests/
└─ HyMT.TranslationTests/
```

* * *

# 54\. 配置文件

```json
{
  "Api": {
    "Enabled": true,
    "Port": 17891,
    "BindAddress": "127.0.0.1"
  },
  "Inference": {
    "InternalPort": 17892,
    "ContextSize": 8192,
    "Parallel": 2
  },
  "Translation": {
    "DefaultModel": "standard",
    "DefaultTargetLanguage": "zh-CN",
    "CacheEnabled": true
  }
}
```

敏感 Token：

不要写进普通 `settings.json`。

* * *

# 55\. 本地数据库

建议 SQLite：

```text
app.db
```

表：

```text
Settings
Models
TranslationHistory
TranslationCache
TranslationJobs
ApiClients
Glossaries
GlossaryEntries
```

数据库迁移必须版本化。

* * *

# 56\. 本地目录

Windows：

```text
%LOCALAPPDATA%/HyMTTranslator/
```

macOS：

```text
~/Library/Application Support/HyMTTranslator/
```

结构：

```text
models/
runtime/
cache/
database/
logs/
downloads/
temp/
```

* * *

# 57\. 日志

Serilog。

等级：

```text
Information
Warning
Error
Debug
```

生产环境日志禁止默认保存：

-   完整原文
-   完整译文
-   API Token
-   配对码

允许：

```text
RequestId
字符数量
Token 数
模型
耗时
错误
```

* * *

# 58\. 隐私设计

核心原则：

> Local First.

默认：

-   翻译内容不上传服务器
-   无登录
-   无云端依赖
-   无遥测或遥测默认关闭
-   不保存 API 原文日志

UI 明确显示：

```text
Local translation
```

* * *

# 59\. CPU / GPU

第一版必须保证：

```text
CPU-only 可运行
```

GPU 是加速项。

检测：

-   AVX2
-   ARM64
-   Apple Silicon
-   Metal
-   可用 GPU Backend

不要因为存在 GPU 就假设一定更快。

提供：

```text
Acceleration
- Auto
- CPU
- GPU
```

默认：

`Auto`

* * *

# 60\. 首次启动流程

```text
Welcome
↓
选择界面语言
↓
选择模型

Recommended:
Standard (~1.13 GB)

Lightweight:
Fast (~0.60 GB)

↓
下载
↓
校验
↓
加载
↓
测试翻译
↓
Home
```

允许：

`稍后下载`

但翻译按钮必须提示模型未安装。

* * *

# 61\. 性能指标

最低目标：

### 启动

应用 UI：

```text
< 2 秒
```

不要求模型同步加载完成。

模型后台加载。

### API

`/health`

```text
< 50 ms
```

### 翻译

性能指标使用：

```text
tokens/s
TTFT
total latency
RAM
CPU
```

不要只记录“秒”。

* * *

# 62\. Benchmark 页面

高级设置可加入：

```text
Run Benchmark
```

测试：

```text
100 tokens
500 tokens
1000 tokens
```

输出：

```text
Model
CPU
GPU backend
Prompt tokens/s
Generation tokens/s
Peak RAM
```

用于用户判断 Fast / Standard。

* * *

# 63\. 翻译质量测试集

项目必须建立自己的 regression dataset。

目录：

```text
tests/data/translation/
```

语言对至少：

```text
EN → ZH
ZH → EN
JA → ZH
KO → ZH
DE → ZH
FR → ZH
ES → ZH
TR → ZH
AR → ZH
VI → ZH
```

内容类型：

```text
日常句子
长句
口语
网页
技术文档
字幕
数字
日期
URL
人名
产品名
emoji
Markdown
```

每次升级：

-   llama.cpp
-   GGUF
-   prompt
-   chunker

都跑回归。

* * *

# 64\. SRT 自动测试

必须检查：

```text
输入 cue 数 == 输出 cue 数
```

以及：

```text
输入时间码 == 输出时间码
```

任何失败：

该任务不允许标记为成功。

* * *

# 65\. Markdown 自动测试

必须验证：

```text
code block hash before == after
URL before == after
front matter keys before == after
```

* * *

# 66\. 模型兼容测试

尤其 2-Bit：

测试：

```text
runtime can load Q2_0c
health returns ready
simple translation succeeds
1000 token translation succeeds
streaming succeeds
cancel succeeds
```

升级 llama.cpp 时必须全部通过。

* * *

# 67\. 安装包策略

不要把两个模型塞进安装包。

推荐：

```text
App Installer
+
Runtime
```

模型：

首次启动下载。

优点：

-   安装包小
-   用户只下载需要的模型
-   模型可以独立升级
-   可删除未使用模型

* * *

# 68\. 自动更新

App 更新和模型更新分离。

```text
Application Update
Runtime Update
Model Update
```

不要把三者绑定为一个版本。

例如：

```text
App: 1.2.0
Runtime: llama.cpp-2026xxxx
Model Fast: 1.0
Model Standard: 1.0
```

* * *

# 69\. MVP 范围

第一阶段必须完成：

-   Avalonia Windows/macOS UI
-   Hy-MT2 Q4
-   Hy-MT2 2-Bit
-   模型下载
-   模型切换
-   单文本翻译
-   TXT
-   SRT
-   VTT
-   翻译任务队列
-   SQLite Cache
-   本地 API
-   API Token
-   Browser Extension Pairing
-   流式翻译基础支持
-   日志
-   自动错误恢复

* * *

# 70\. MVP 暂不实现

不要第一阶段过度开发：

-   PDF
-   Word
-   Excel
-   PPT
-   云端账号
-   多设备同步
-   团队协作
-   大模型聊天
-   AI 写作
-   OCR
-   TTS
-   ASR
-   图片翻译
-   同时加载多个模型
-   在线搜索

OCR 由外部 OCR 软件通过 API 接入即可。

* * *

# 71\. Phase 1：Inference POC

先不要做完整 UI。

目标：

```text
GGUF
↓
llama.cpp
↓
C#
↓
成功翻译
```

完成：

1.  下载两个模型
2.  固定 llama.cpp runtime
3.  启动 llama-server
4.  C# Client
5.  EN → ZH
6.  ZH → EN
7.  Streaming
8.  Cancel
9.  Benchmark

通过后再开始桌面 UI。

* * *

# 72\. Phase 2：Core

实现：

```text
TranslationService
PromptBuilder
ChunkManager
Cache
JobQueue
ModelManager
RuntimeManager
```

全部有单测。

* * *

# 73\. Phase 3：Desktop UI

实现：

```text
Home
Files
History
Models
API
Settings
```

* * *

# 74\. Phase 4：Documents

顺序：

```text
TXT
↓
SRT
↓
VTT
↓
Markdown
↓
ASS
```

每增加一种格式：

必须有：

```text
Parser Test
Round-trip Test
Translation Test
```

* * *

# 75\. Phase 5：Local API

实现：

```text
Health
Auth
Pair
Translate
Batch
Stream
Models
Rate Limit
CORS
```

* * *

# 76\. Phase 6：Browser Integration

按照独立文档：

`HyMT2_Browser_Extension_Development_Guide.md`

开发 Chrome / Edge Manifest V3 插件。

* * *

# 77\. Phase 7：Release

完成：

-   Windows x64
-   Windows arm64（可后续）
-   macOS arm64
-   macOS x64
-   installer
-   model downloader
-   license
-   crash log
-   update
-   regression test

* * *

# 78\. AI 开发执行原则

AI 编程模型必须遵循：

1.  不一次性生成整个项目。
2.  按 Phase 实施。
3.  每个模块先定义接口再实现。
4.  推理、API、UI 三层不得直接互相耦合。
5.  UI 不允许直接调用 llama-server。
6.  浏览器插件不允许直接调用 llama-server。
7.  文件 parser 不允许包含模型推理逻辑。
8.  API Contract 必须有版本。
9.  所有长任务必须支持 CancellationToken。
10.  所有 IO 必须 async。
11.  禁止吞异常。
12.  所有外部进程必须有生命周期管理。
13.  所有文件写入必须避免覆盖源文件。
14.  任何模型更新必须通过 translation regression。
15.  不得在日志中泄漏 API Token 和用户文本。

* * *

# 79\. Definition of Done

MVP 被视为完成，必须满足：

### Desktop

-   Windows 可以安装并运行
-   macOS 可以安装并运行
-   两个模型都可以下载
-   两个模型都可以加载
-   可以切换 Fast / Standard
-   文本翻译正常
-   TXT 翻译正常
-   SRT 时间码完全保留
-   VTT cue 正常
-   可取消大型任务

### API

-   `/health` 可用
-   Token 鉴权可用
-   Pairing 可用
-   `/translate` 可用
-   `/batch` 可用
-   Streaming 可用
-   限流可用
-   只监听 loopback

### Stability

-   llama.cpp crash 可以恢复
-   端口冲突可检测
-   模型损坏可检测
-   下载中断可恢复
-   App 异常退出后文件任务可恢复

### Privacy

-   无必要云请求
-   不上传翻译内容
-   日志不记录全文
-   Token 不明文落库

* * *

# 80\. 后续版本方向

V1.1：

-   Markdown
-   ASS
-   glossary
-   OpenAI-compatible local API
-   Firefox
-   GPU 优化
-   自动模型推荐

V1.2：

-   DOCX
-   EPUB
-   HTML 文件
-   更完整 Translation Memory

V2：

-   OCR 模块
-   PDF
-   Office
-   双语文档重建
-   可选云端高质量翻译 fallback

* * *

# 81\. 参考实现与技术依据

开发时应优先核对以下上游项目的最新文档：

-   Tencent `Hy-MT2-1.8B-GGUF`
-   AngelSlim `Hy-MT2-1.8B-2Bit-GGUF`
-   AngelSlim `Hy-MT2-1.8B-Q4-GGUF`
-   `ggml-org/llama.cpp`
-   llama.cpp `tools/server`
-   Avalonia UI
-   [ASP.NET](http://ASP.NET) Core Minimal API
-   Chrome Extensions Manifest V3

当前确认事项：

-   Tencent 官方 GGUF 仓库提供 `Hy-MT2-1.8B-Q4_K_M.gguf`，大小约 1.13 GB。
-   AngelSlim 2-Bit GGUF 使用 Q2\_0c，需要兼容该 kernel 的 llama.cpp。
-   llama-server 可以提供 OpenAI-compatible HTTP API、Chat Completion 和流式输出。
-   Hy-MT2 1.8B 的官方模型卡建议不要依赖默认 system prompt，翻译指令应在请求中明确给出。

* * *

# 82\. 最终产品原则

本项目不要变成一个“大而全 AI 助手”。

核心定位始终保持：

> **一个运行在用户电脑上的轻量、高质量、多语言翻译引擎，以及围绕它构建的桌面翻译工具和本地翻译 API。**

评价一个功能是否应该加入时，优先问：

```text
它是否明显改善翻译？
它是否改善本地使用体验？
它是否帮助其它软件调用翻译能力？
```

如果答案都是否，则不进入 MVP。