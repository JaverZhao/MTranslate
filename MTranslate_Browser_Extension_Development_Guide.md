# MTranslate 浏览器沉浸式翻译插件开发说明

> 桌面端配套浏览器扩展  
> 面向 AI 编程模型 / Agent  
> 目标浏览器：Chrome、Edge；后续 Firefox  
> Manifest：V3  
> 文档版本：v1.0  
> 日期：2026-08-27

* * *

# 1\. 项目目标

开发一款调用 Hy-MT2 桌面翻译器本地 API 的浏览器扩展。

插件本身：

**不包含 Hy-MT2 模型。**

插件本身：

**不运行 GGUF。**

模型统一由桌面端运行。

架构：

```text
Web Page
↓
Browser Extension
↓
http://127.0.0.1:17891/api/v1
↓
Hy-MT Desktop Translator
↓
Translation Queue
↓
llama.cpp
↓
Hy-MT2
```

这样可以：

-   Chrome / Edge 共用桌面模型
-   OCR 软件共用模型
-   避免浏览器加载 600MB~1.1GB GGUF
-   避免 WebGPU/WebAssembly 兼容问题
-   避免插件内存过高
-   模型切换由桌面端统一管理
-   翻译缓存由桌面端统一管理

* * *

# 2\. 第一版功能

MVP：

1.  当前页面翻译
2.  双语页面
3.  仅显示译文
4.  选择文本翻译
5.  右键菜单翻译
6.  自动检测源语言
7.  选择目标语言
8.  Fast / Standard 模式
9.  与桌面端配对
10.  显示桌面服务状态
11.  SPA 动态内容翻译
12.  页面新增内容增量翻译
13.  翻译进度
14.  停止翻译
15.  恢复原文
16.  当前网站自动翻译设置

* * *

# 3\. 技术栈

建议：

-   TypeScript
-   Vite
-   Manifest V3
-   Preact（仅 popup / options，可选）
-   WebExtension APIs
-   ESLint
-   Prettier
-   Vitest
-   Playwright

不建议：

-   在扩展里运行 llama.cpp WASM
-   在扩展里下载 GGUF
-   在 content script 中直接请求模型
-   React 大型组件体系
-   将网页 HTML 整页发送给翻译 API

* * *

# 4\. 支持浏览器

Phase 1：

```text
Google Chrome
Microsoft Edge
```

共同使用 Chromium Manifest V3。

Phase 2：

```text
Firefox
```

通过 WebExtensions 兼容层。

代码设计时不要把：

```text
chrome.*
```

散落整个项目。

建议封装：

```text
BrowserAdapter
StorageAdapter
PermissionAdapter
MessagingAdapter
```

Firefox 时可替换。

* * *

# 5\. 项目结构

```text
hy-mt-browser-extension/

src/
├─ background/
│  ├─ service-worker.ts
│  ├─ api-client.ts
│  ├─ pairing.ts
│  └─ translation-controller.ts
│
├─ content/
│  ├─ content-script.ts
│  ├─ dom-scanner.ts
│  ├─ block-extractor.ts
│  ├─ translation-renderer.ts
│  ├─ mutation-observer.ts
│  └─ styles.css
│
├─ popup/
│  ├─ Popup.tsx
│  └─ popup.ts
│
├─ options/
│  ├─ Options.tsx
│  └─ options.ts
│
├─ shared/
│  ├─ types.ts
│  ├─ messages.ts
│  ├─ languages.ts
│  └─ constants.ts
│
├─ adapters/
│  ├─ browser.ts
│  ├─ storage.ts
│  └─ permissions.ts
│
└─ manifest.json

tests/
├─ unit/
└─ e2e/
```

* * *

# 6\. Manifest V3

原则：

只申请真正需要的权限。

推荐：

```json
{
  "manifest_version": 3,
  "name": "Hy-MT Local Translator",
  "version": "1.0.0",
  "permissions": [
    "storage",
    "activeTab",
    "scripting",
    "contextMenus"
  ],
  "host_permissions": [
    "http://127.0.0.1/*",
    "http://localhost/*"
  ],
  "optional_host_permissions": [
    "https://*/*",
    "http://*/*"
  ],
  "background": {
    "service_worker": "service-worker.js",
    "type": "module"
  },
  "action": {
    "default_popup": "popup.html"
  }
}
```

说明：

`host_permissions` 用于：

从 Extension Service Worker 调用桌面本地 API。

不要默认使用：

```text
<all_urls>
```

作为永久页面权限。

网站自动翻译通过：

`optional_host_permissions`

按需申请。

* * *

# 7\. 为什么 API 请求必须由 Service Worker 发起

不要：

```text
Content Script
→ localhost API
```

推荐：

```text
Content Script
↓ chrome.runtime.sendMessage
Service Worker
↓ fetch
127.0.0.1:17891
```

优点：

-   网络调用统一管理
-   API Token 不暴露在页面上下文
-   更容易实现重试
-   更容易做请求队列
-   权限更清晰
-   不让网页 JS 接触本地服务 Token

* * *

# 8\. Service Worker 职责

只负责：

-   Local API Client
-   Token
-   Pairing
-   Translation Request
-   Context Menu
-   Tab State
-   Site Permissions
-   Background Translation Queue
-   Extension Message Routing

不要把 DOM 操作放在 Service Worker。

* * *

# 9\. Content Script 职责

负责：

-   扫描页面
-   提取文本
-   生成 segment
-   页面可见区域判断
-   注入翻译结果
-   恢复原文
-   MutationObserver
-   双语样式
-   页面翻译状态

不要在 Content Script 保存 API Token。

* * *

# 10\. Popup

Popup：

```text
┌───────────────────────────────┐
│ Hy-MT Local Translator        │
│ ● Desktop connected           │
│                               │
│ Source: Auto                  │
│ Target: 简体中文              │
│ Mode: Standard                │
│                               │
│ [ Translate This Page ]       │
│ [ Stop ]                      │
│                               │
│ Display                       │
│ ○ Bilingual                   │
│ ○ Translation only            │
│                               │
│ Auto translate this site [ ]  │
└───────────────────────────────┘
```

状态：

```text
Connected
Connecting
Desktop app not running
Model loading
Model not installed
Unauthorized
```

* * *

# 11\. Options 页面

配置：

```text
Desktop Connection
Target Language
Default Model Mode
Display Mode
Font Size
Translation Position
Auto Translate Sites
Never Translate Sites
Ignore Selectors
Shortcut
Cache
Debug
```

不要加入大量非翻译功能。

* * *

# 12\. 桌面连接

默认 URL：

```text
http://127.0.0.1:17891/api/v1
```

备用：

```text
17893
17895
17897
17899
```

启动检测：

```text
GET /health
```

每个端口超时：

```text
300~500ms
```

找到第一个：

```text
status=ok
```

保存。

* * *

# 13\. Pairing

如果：

```http
401 Unauthorized
```

Popup 显示：

> Connect to desktop translator

用户在桌面软件：

```text
Settings
→ Local API
→ Pair Browser
```

得到：

```text
6 digit code
```

插件提交：

```http
POST /api/v1/pair
```

成功后获得 Token。

Token：

```text
chrome.storage.local
```

不要使用：

```text
localStorage
```

* * *

# 14\. Token 安全

Token：

只允许存在：

-   Service Worker
-   extension storage

Content Script 不应该拿到 Token。

Content Script：

```text
translate request
↓
runtime.sendMessage
↓
Service Worker
↓
API
```

* * *

# 15\. 页面翻译核心原则

**不要把 document.body.innerHTML 发给模型。**

禁止：

```text
整个 HTML
↓
Hy-MT2
↓
替换 innerHTML
```

这会导致：

-   DOM 破坏
-   JavaScript event listener 丢失
-   表单状态丢失
-   React/Vue hydration 问题
-   链接损坏
-   格式损坏
-   Token 浪费

* * *

# 16\. 推荐翻译单位

优先：

```text
Block-Level Semantic Segment
```

包括：

```text
p
li
blockquote
h1
h2
h3
h4
td
th
figcaption
article text blocks
```

提取：

```text
textContent
```

但是原 DOM 不删除。

* * *

# 17\. 默认双语模式

强烈建议 MVP 默认：

```text
Original Block
Translation Block
```

例如：

```text
<p>
  This is an example.
</p>

<div class="hymt-translation">
  这是一个示例。
</div>
```

优点：

-   不破坏原页面
-   不需要重建 inline DOM
-   可一键恢复
-   翻译失败不会损坏网页
-   开发复杂度低
-   与沉浸式翻译使用体验相近

* * *

# 18\. Translation Only 模式

不要直接删除原 DOM。

使用：

```text
original.style.display = "none"
translation.style.display = ""
```

恢复：

```text
original.style.display = ""
translation.remove()
```

如果隐藏会影响 layout，则使用：

class 标记。

* * *

# 19\. DOM 扫描

使用：

```text
TreeWalker
```

或 block scanner。

跳过：

```text
script
style
noscript
code
pre
textarea
input
select
option
canvas
svg
math
video
audio
```

默认跳过：

```text
contenteditable=true
```

避免翻译在线编辑器正在输入的文本。

* * *

# 20\. 可翻译判断

文本必须：

```text
trim().length > 1
```

过滤：

-   纯数字
-   单 URL
-   hash
-   CSS
-   JSON-like code
-   文件路径
-   极短符号
-   emoji only

不要把：

```text
2026
```

送模型。

* * *

# 21\. 唯一 Segment ID

每个 block：

```text
crypto.randomUUID()
```

保存：

```ts
interface PageSegment {
  id: string;
  sourceText: string;
  translatedText?: string;
  element: Element;
  status: SegmentStatus;
}
```

状态：

```text
pending
queued
translating
translated
failed
skipped
```

* * *

# 22\. 文本标准化

送 API 前：

-   trim 边缘异常空格
-   保留内部换行
-   合并 HTML 渲染造成的连续空格
-   不修改标点
-   不 lowercase
-   不删除 emoji

Cache key 必须基于规范化文本。

* * *

# 23\. Batch

不要：

```text
200 DOM blocks
= 200 HTTP requests
```

Content Script：

```text
segments
↓
group
↓
Service Worker
↓
/translate/batch
```

建议：

```text
5~20 segments/batch
```

并限制：

```text
总 token / 总字符
```

桌面 Gateway 再统一调度。

* * *

# 24\. 页面优先级

优先翻译：

```text
Viewport
```

然后：

```text
Viewport 附近
```

最后：

```text
页面其它区域
```

推荐：

`IntersectionObserver`

给 segment 打优先级：

```text
High
Normal
Low
```

用户看到的内容最先完成。

* * *

# 25\. 长页面

不要等待扫描整个页面完成才开始。

流程：

```text
Scan visible
↓
Translate
↓
Render
↓
Scan next
↓
Translate
```

形成渐进式体验。

* * *

# 26\. 动态网页

必须使用：

```text
MutationObserver
```

处理：

-   X / Twitter
-   Reddit
-   YouTube comments
-   Facebook
-   无限滚动
-   SPA
-   新闻网站动态推荐

Observer：

只处理新加入节点。

不要每次 Mutation 都重新扫描整页。

* * *

# 27\. Mutation Debounce

动态页面可能每秒产生大量 DOM 变化。

使用：

```text
100~300ms debounce
```

之后：

```text
collect added nodes
↓
deduplicate
↓
extract
↓
translate
```

* * *

# 28\. 防止翻译自己的节点

所有插件插入节点：

```html
data-hymt-owned="true"
```

DOM Scanner：

发现：

```text
[data-hymt-owned]
```

立即跳过。

否则会出现：

```text
译文
↓
再次被检测
↓
翻译译文
↓
无限循环
```

* * *

# 29\. 页面缓存

Page Cache：

```text
sourceText
targetLanguage
modelMode
translatedText
```

优先：

1.  Content session cache
2.  Extension cache
3.  Desktop Translation Cache
4.  Inference

对于大型网页：

桌面 SQLite Cache 是主缓存。

插件缓存只保存：

-   页面状态
-   少量最近结果

* * *

# 30\. 选择文本翻译

用户选中文字。

入口：

-   Popup
-   右键
-   快捷键

流程：

```text
window.getSelection()
↓
runtime.sendMessage
↓
Service Worker
↓
/translate
↓
floating bubble
```

浮窗：

```text
原文
译文
Copy
```

不要阻塞网页操作。

* * *

# 31\. 右键菜单

Manifest：

```text
contextMenus
```

菜单：

```text
Translate selection
Translate page
```

如果没有 selected text：

禁用 selection item。

* * *

# 32\. 快捷键

建议：

```text
Alt + Shift + T
```

功能：

当前页翻译 / 停止。

选中文字时：

优先翻译选择内容。

快捷键允许用户修改。

* * *

# 33\. 语言策略

Popup：

```text
Source
Auto

Target
Chinese
English
Japanese
Korean
Spanish
French
German
Turkish
Arabic
...
```

内部：

BCP-47。

不要把 UI 文本直接作为 API language id。

* * *

# 34\. 模型模式

插件只传：

```json
{
  "mode": "fast"
}
```

或：

```json
{
  "mode": "standard"
}
```

插件不要知道具体 GGUF 文件路径。

桌面端负责：

```text
fast → 2-Bit
standard → Q4
```

这样未来模型升级不需要更新扩展。

* * *

# 35\. API Client

统一类：

```ts
class LocalTranslationClient {
  health()
  pair()
  translate()
  translateBatch()
  translateStream()
  getModels()
}
```

所有 fetch：

只允许这个模块发起。

不要在 Popup、Content Script 到处写 fetch。

* * *

# 36\. Message Protocol

Content Script：

```ts
{
  type: "TRANSLATE_BATCH",
  payload: {...}
}
```

Service Worker：

```ts
{
  type: "TRANSLATE_BATCH_RESULT",
  payload: {...}
}
```

消息 type 必须集中定义。

不要使用散落字符串。

推荐：

```ts
enum ExtensionMessageType
```

或 discriminated union。

* * *

# 37\. 停止翻译

用户点击：

`Stop`

发送：

```text
CANCEL_TAB_TRANSLATION
```

每个 Tab：

拥有：

```text
AbortController
```

Service Worker 同样取消未发送和等待中的 API 请求。

桌面 API 支持 Cancellation 时应同步取消。

* * *

# 38\. 标签页隔离

状态：

```ts
Map<tabId, TranslationSession>
```

每个 tab：

-   source
-   target
-   mode
-   display
-   status
-   controller
-   progress

不能使用一个 global 状态覆盖多个网页。

* * *

# 39\. 页面恢复

点击：

`Restore Original`

执行：

1.  取消任务
2.  删除 `data-hymt-owned`
3.  恢复被隐藏原节点
4.  清理 session
5.  停止 MutationObserver

不得 reload 页面。

* * *

# 40\. SPA URL 变化

监听：

-   `webNavigation`
-   History API 变化
-   content side URL observation

页面 route 改变：

```text
old translation session
↓
cancel
↓
cleanup
↓
new route
```

如果用户启用自动翻译：

重新开始。

* * *

# 41\. 自动翻译网站

例如用户打开：

```text
https://example.com
```

选择：

```text
Always translate this site
```

这时再请求：

`optional_host_permissions`

例如：

```text
https://example.com/*
```

成功后：

保存：

```text
SiteRule
```

* * *

# 42\. 权限原则

不要一安装就要求：

```text
Read and change all your data on all websites
```

除非产品必须。

MVP 更推荐：

```text
activeTab
```

手动翻译当前页面。

只有用户主动打开：

`Always translate this site`

才请求 site permission。

* * *

# 43\. 网站规则

```ts
interface SiteRule {
  host: string;
  autoTranslate: boolean;
  targetLanguage?: string;
  displayMode?: "bilingual" | "translation";
  disabled?: boolean;
}
```

* * *

# 44\. Ignore Selector

高级设置：

用户可以添加：

```text
.code-editor
.comment-input
.custom-widget
```

插件不翻译。

内置：

```text
pre
code
textarea
input
[contenteditable]
```

* * *

# 45\. 页面注入 CSS

所有 CSS class 使用 namespace：

```text
hymt-
```

例如：

```text
.hymt-translation
.hymt-loading
.hymt-error
.hymt-selection-popup
```

避免：

```text
.translation
.container
.active
```

这种通用名称污染网站。

* * *

# 46\. Shadow DOM

V1：

普通 DOM 优先。

对于 open ShadowRoot：

可以递归扫描。

Closed ShadowRoot：

无法可靠支持，直接忽略。

不要为了覆盖少数网站把 MVP 复杂化。

* * *

# 47\. iframe

同源 iframe：

可在有权限时处理。

跨域 iframe：

需要对应 host permission。

MVP：

默认只翻译 top frame。

Phase 2：

支持 iframe。

* * *

# 48\. 翻译进度

Popup：

```text
Translating 34 / 128
```

页面右下角可选轻量状态：

```text
Hy-MT
34 / 128
```

不要做大面积覆盖 UI。

* * *

# 49\. Loading

不要所有段落插入：

`Translating...`

否则页面视觉很乱。

推荐：

保持原文。

翻译完成后再插入译文。

* * *

# 50\. 错误处理

单个 block 翻译失败：

-   保留原文
-   标记 failed
-   可以重试

不要因为：

1 个 paragraph

失败，就取消整个页面。

* * *

# 51\. Desktop App 未启动

Popup：

```text
Desktop translator not detected.
Open Hy-MT Translator to use local translation.
```

提供：

`Retry`

不要自动尝试联网翻译。

本项目默认：

Local-only。

* * *

# 52\. 模型正在加载

API：

```text
MODEL_LOADING
```

插件：

```text
Model is loading...
```

自动：

1 秒后重试 health。

最多：

30 秒。

不要无限 retry。

* * *

# 53\. 模型未安装

显示：

```text
Translation model is not installed.
Open the desktop app to download a model.
```

不要让插件下载模型。

* * *

# 54\. API 版本

Service Worker 启动：

检查：

```text
apiVersion
```

如果桌面：

```text
2.x
```

插件只支持：

```text
1.x
```

则：

提示：

```text
Desktop translator version is incompatible.
```

禁止静默失败。

* * *

# 55\. 浏览器本地化

插件 UI 最低：

```text
English
Simplified Chinese
```

Phase 2：

-   Traditional Chinese
-   Japanese
-   Korean

使用：

Chrome i18n。

不要在 JSX 写死全部 UI 文案。

* * *

# 56\. 数据存储

`chrome.storage.local`：

```text
settings
apiEndpoint
apiToken
siteRules
recentTargetLanguages
```

不要存：

大量网页全文。

* * *

# 57\. 隐私

扩展商店隐私说明：

需要明确：

-   网页文本仅发送到用户本机翻译服务
-   不上传开发者服务器
-   不出售网页数据
-   不记录浏览历史到云端
-   API 默认是 localhost

如果未来加入 telemetry：

必须单独 opt-in。

* * *

# 58\. CSP

Manifest V3：

不要：

-   eval
-   new Function
-   远程加载 JS
-   CDN script
-   远程执行代码

所有运行代码必须：

打包进扩展。

* * *

# 59\. 日志

Production：

默认关闭 debug。

Debug：

```text
[HyMT][Content]
[HyMT][Worker]
[HyMT][API]
```

禁止 log：

-   API Token
-   大段网页原文
-   完整译文

* * *

# 60\. 网络请求

插件正常运行时只能主动访问：

```text
127.0.0.1
localhost
```

除非未来明确加入：

-   Update metadata
-   Cloud service

MVP 没有必要。

* * *

# 61\. API Timeout

Health：

```text
500ms~1s
```

Single Translation：

```text
30s
```

Batch：

```text
60s
```

Streaming：

使用 idle timeout。

超时：

可重试一次。

* * *

# 62\. Retry

只重试：

```text
NETWORK_ERROR
MODEL_LOADING
TEMPORARY_ENGINE_ERROR
```

不要重试：

```text
401
400
413
UNSUPPORTED_LANGUAGE
```

指数退避：

```text
500ms
1000ms
2000ms
```

最多：

3 次。

* * *

# 63\. 网页 Batch 恢复策略

如果：

```text
batch items = 10
```

只失败：

```text
2
```

不要重跑 10。

只 retry：

失败 item。

* * *

# 64\. 翻译去重

页面常出现重复：

```text
Sign in
Sign in
Sign in
```

Batch 前：

```text
Map<normalizedText, segmentIds>
```

同文本：

只请求一次。

结果：

分发到多个 DOM 节点。

这对网页翻译性能非常重要。

* * *

# 65\. 元素变化检测

如果翻译期间：

```text
source element text changed
```

旧结果不能直接插入。

发送请求时保存：

```text
sourceSnapshot
```

响应时：

```text
if currentText != sourceSnapshot:
    discard result
```

避免 React/Vue 更新后插错译文。

* * *

# 66\. Translation Renderer

统一接口：

```ts
interface TranslationRenderer {
  render(segment, translation): void;
  remove(segment): void;
  restoreAll(): void;
}
```

不要在 DOM Scanner 里直接修改页面。

* * *

# 67\. 译文样式

默认继承：

```text
font-family
font-size
line-height
text-align
```

译文可轻微区分：

-   opacity
-   margin-top

但不要改变网站主色。

用户可关闭插件样式。

* * *

# 68\. 右到左语言

必须考虑：

```text
Arabic
Hebrew
Persian
Urdu
```

Translation block：

根据目标语言设置：

```html
dir="rtl"
```

不要全局修改网页 direction。

* * *

# 69\. 选择浮窗

Selection Translation：

建议使用 Shadow DOM 创建自身 UI。

原因：

避免被页面 CSS 污染。

结构：

```text
Shadow Host
↓
Shadow Root
↓
Floating Card
```

* * *

# 70\. Accessibility

按钮：

必须有：

```text
aria-label
```

支持：

-   Keyboard
-   Focus
-   Esc close
-   High contrast

Popup 不依赖 hover 才能操作。

* * *

# 71\. E2E 测试页面

建立：

```text
tests/pages/
```

包含：

```text
article.html
dynamic.html
spa.html
table.html
rtl.html
code.html
long-page.html
contenteditable.html
```

Playwright 自动测试。

* * *

# 72\. E2E 必测

### 普通文章

-   paragraph 被翻译
-   link 可点击
-   图片不变

### 动态内容

-   新段落自动翻译

### Code

-   pre 不翻译
-   code 不翻译

### Form

-   textarea 不翻译
-   input value 不被改

### Restore

-   原页面恢复

### Duplicate

-   同文本只请求一次

* * *

# 73\. Fake Local API

开发插件时不要每次启动真实模型。

提供：

```text
Mock Translation Server
```

模拟：

```text
/health
/pair
/translate
/batch
```

这样：

DOM / UI / Permission

开发不依赖模型性能。

* * *

# 74\. API Contract Tests

插件和桌面项目共享：

```text
openapi.json
```

或：

```text
api-contract.schema.json
```

CI：

验证：

-   Request schema
-   Response schema
-   Error schema

桌面 API 改动如果破坏 contract：

测试失败。

* * *

# 75\. 打包

输出：

```text
dist/chrome/
dist/edge/
```

不要为 Chrome / Edge 维护两个完全独立代码库。

* * *

# 76\. Chrome Store

发布前检查：

-   最小权限
-   Privacy Policy
-   Manifest V3
-   无 remote code
-   无动态下载 JS
-   图标
-   Screenshots
-   Description
-   Permission rationale

* * *

# 77\. Firefox

迁移阶段：

-   检查 MV3 差异
-   `browser.*` API
-   host permission UX
-   service worker 支持
-   optional permissions
-   store packaging

优先通过 Adapter 层处理，不 fork 整个项目。

* * *

# 78\. Phase 1：Extension Skeleton

先完成：

```text
Manifest
Service Worker
Popup
Content Script
Messaging
```

没有翻译也可以运行。

* * *

# 79\. Phase 2：Desktop Connectivity

实现：

```text
Port discovery
Health
Pair
Token
Translate
```

用 Mock API 测试。

* * *

# 80\. Phase 3：Selection

先做：

```text
Selection Translation
```

这是最小可验证闭环：

```text
网页
↓
选中文字
↓
Extension
↓
Desktop
↓
Hy-MT2
↓
结果
```

* * *

# 81\. Phase 4：Page Translation

实现：

```text
Block Scanner
Batch
Bilingual Render
Restore
Progress
Cancel
```

* * *

# 82\. Phase 5：Dynamic Page

实现：

```text
MutationObserver
Deduplication
Viewport Priority
SPA Route
```

* * *

# 83\. Phase 6：Permissions

实现：

```text
activeTab
optional_host_permissions
Always translate this site
Never translate this site
```

* * *

# 84\. Phase 7：Quality

完成：

-   Playwright
-   Chrome
-   Edge
-   long page
-   dynamic page
-   memory profiling
-   API error testing

* * *

# 85\. AI 开发规则

AI 编程模型必须：

1.  先实现接口，再写复杂逻辑。
2.  Content Script 不得持有 API Token。
3.  页面代码不得拿到 API Token。
4.  所有 localhost 请求统一走 Service Worker。
5.  不得翻译 HTML 字符串并覆盖 `innerHTML`。
6.  不得破坏原 DOM。
7.  默认双语插入，不修改网站源节点内容。
8.  DOM Scanner 与 Renderer 分离。
9.  API Client 单例化。
10.  所有请求必须可 Cancel。
11.  所有页面 Session 按 tabId 隔离。
12.  MutationObserver 必须 debounce。
13.  必须避免翻译插件自己插入的译文。
14.  必须做重复文本去重。
15.  必须处理页面节点在请求期间发生变化。
16.  不申请不必要权限。
17.  不远程加载可执行 JavaScript。
18.  不默认上传任何网页数据到云端。

* * *

# 86\. Definition of Done

MVP 完成标准：

### Connection

-   Chrome 可发现桌面端
-   Edge 可发现桌面端
-   配对成功
-   Token 可保存
-   Token 可吊销
-   桌面关闭时插件正确提示

### Translation

-   选择文本翻译
-   当前页翻译
-   Batch
-   双语
-   Translation Only
-   Restore
-   Stop

### DOM Safety

-   Links 不损坏
-   Forms 不损坏
-   Code 不翻译
-   Script 不翻译
-   页面事件不丢失
-   React/Vue 页面不因翻译被重建

### Dynamic

-   MutationObserver 正常
-   无限滚动可增量翻译
-   不会无限翻译译文

### Permissions

-   手动页面翻译使用 activeTab
-   自动站点翻译按需申请 host permission
-   不默认申请无意义权限

### Privacy

-   API Token 不进入网页环境
-   网页文本不发送第三方服务器
-   日志不保存全文
-   Localhost only

* * *

# 87\. 推荐最终用户体验

用户安装：

```text
Hy-MT Desktop
```

下载：

```text
Standard Model
```

安装：

```text
Browser Extension
```

第一次：

```text
Extension
↓
Detect Desktop
↓
Pair
↓
Ready
```

之后访问网页：

```text
Click Translate
↓
可见区域 1~2 秒开始出现译文
↓
向下滚动
↓
后续内容继续翻译
```

用户不需要理解：

-   GGUF
-   llama.cpp
-   端口
-   Q2
-   Q4
-   Token
-   CORS

这些全部属于内部实现细节。

* * *

# 88\. 最终架构原则

浏览器插件只解决三件事：

```text
发现网页文本
↓
请求本地翻译
↓
安全地呈现译文
```

不要把：

-   模型管理
-   Prompt
-   Cache Engine
-   llama.cpp
-   文档翻译
-   Translation Memory

复制进插件。

这些全部属于桌面服务。

保持：

```text
Browser Extension = Thin Client
Desktop Translator = Translation Platform
Hy-MT2 = Inference Engine
```

这是该项目后续能够同时服务：

-   浏览器
-   OCR
-   字幕软件
-   编辑器
-   自动化脚本

的关键。