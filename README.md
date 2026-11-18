# ShiroBot.TelegramAdapter

基于官方 `ShiroBot.Templates 1.0.4` 和 `ShiroBot.SDK 1.0.4` 的 Telegram Bot API 长轮询适配器，
目标框架 .NET 11，适配器版本 `0.5.0`。实现宿主共享 Model `ShiroBot.Model.Telegram` 1.1，
并通过原始调用覆盖 **Telegram Bot API 10.4**（2026-11-09）的全部方法与 Update 种类。

> SDK 1.0.4 尚未发布。发布前在本地打包宿主仓库的 SDK 再构建：
> `dotnet pack ../ShiroBot/SDK/Packaging/ShiroBot.SDK.Package.csproj -c Release -o /tmp/shirobot-feed`，
> 然后 `dotnet build -c Release -p:RestoreAdditionalProjectSources=/tmp/shirobot-feed`。
> Debug 构建会下载 v1.0.4 宿主，发布前需按宿主文档放入本地构建的宿主。

## 开发与运行

```sh
dotnet build ShiroBot.TelegramAdapter.slnx -c Debug
sh scripts/dev.sh
```

在 Rider 打开 `ShiroBot.TelegramAdapter.slnx`，构建配置选 Debug，在右上角选择 `ShiroBot`
（Windows 选 `ShiroBot (Windows)`），Run 与 Debug 共用这一配置，保留“启动前构建”。
Debug 构建会自动下载与 SDK 版本一致的宿主，并把适配器和独立的 TestPlugin 安装到 `.shirobot-dev/`。
Windows 命令行使用 `powershell -ExecutionPolicy Bypass -File scripts/dev.ps1`；
只准备宿主不启动时加 `--prepare`。

访问 `http://127.0.0.1:7002/dashboard/`，登录密钥见开发宿主 `config.toml` 的 `[api].token`。
同一时间只运行一个使用此端口的开发宿主。添加 Telegram 适配器实例，填写 BotFather 提供的
**Bot Token** 并连接。**API Base URL** 留空使用官方服务器，使用自建 Bot API Server 时填写其地址。
网络需要代理时填写 **代理**（如 `http://127.0.0.1:7897` 或 `socks5://127.0.0.1:7891`），留空使用系统代理设置。
配置由宿主管理，修改后重启该实例。每个 Token 只能有一个长轮询实例，使用前需移除已有 webhook。

在 Telegram 私聊机器人发送 `ping`（应回复 `pong`）、`adapter-info` 或 `adapter-echo hello`。
群聊测试需确保机器人有读取和发送消息的权限；隐私模式会限制机器人收到的群消息。
话题使用 `chatId:threadId` 作为 SDK 频道 ID。

## Telegram Model（插件推荐）

适配器实现宿主共享 Model `ShiroBot.Model.Telegram` 1.1 的全部扩展接口。插件只依赖宿主提供的 Model，
不需要引用本适配器的 DLL：

| 接口 | 内容 |
| --- | --- |
| `ITelegramClientApi` | 账号类型（机器人）、传输方式（Bot API）、上传与下载上限、自身资料、已见用户 |
| `ITelegramChatApi` | 会话资料、成员与管理员、封禁/限制/提升、默认权限、标题/简介/头像、置顶、邀请链接、入群审批、话题 |
| `ITelegramMessageApi` | 投票、位置、地点、联系人、骰子、贴纸、相册、发票；内联键盘、自定义键盘、强制回复；编辑、转发、复制、批量删除、输入状态、结束投票、带提示回答回调 |
| `ITelegramInlineApi` | 回答 inline 查询 |
| `ITelegramPaymentApi` | 付款确认、收货方式、Stars 退款与余额 |
| `ITelegramBotProfileApi` | 命令菜单、名称与介绍 |
| `ITelegramBotApi` | 任意 Bot API 方法、文件上传和原始 Update，只使用 System.Text.Json 类型 |

入站消息的 `MessageEvent.Raw` 为 `TelegramMessage`（相册 ID、转发来源、话题、原始 JSON）；位置、地点、联系人、
骰子、投票、付款和格式实体以 `RawSegment` 上报，`Payload` 为对应 Model 记录。消息编辑、成员与机器人状态、
入群申请、inline 查询、投票、付款、收货和助力以 `PlatformEvent` 上报，`Raw` 为 `TelegramEventPayload`。
完整说明见宿主文档的 Telegram Model 页面。

```csharp
using ShiroBot.Model.Telegram;

var telegram = Context.GetAdapterExtension<ITelegramMessageApi>()!;
await telegram.SendAsync(message.Channel, new TelegramPollContent("午饭？", ["面", "饭"]));
```

按钮回调默认自动确认。需要提示或弹窗时，在适配器配置中关闭 **自动确认按钮回调**，再调用
`ITelegramMessageApi.AnswerCallbackAsync`。

## 全部 Bot API

共享 Model 没有单独封装的方法，通过 `ITelegramBotApi` 直接调用，参数使用 Bot API 的 snake_case 名称，
只依赖 System.Text.Json，插件同样不需要引用本适配器：

```csharp
using System.Text.Json.Nodes;
using ShiroBot.Model.Telegram;

var raw = Context.GetAdapterExtension<ITelegramBotApi>()!;
var result = await raw.CallAsync("setChatMenuButton", new JsonObject
{
    ["menu_button"] = new JsonObject { ["type"] = "commands" }
});

await using var file = File.OpenRead("cover.jpg");
await raw.CallAsync("sendPhoto", new JsonObject { ["chat_id"] = 123456 }, [new TelegramUploadFile("photo", "cover.jpg", file)]);
```

- 值为 null 的参数不会发送；有文件时自动改用 multipart，嵌套对象中的文件以 `attach://名称` 引用。
- `UpdateReceived` 提供每个 Update 的原始 JSON，包括没有映射为 SDK 事件的种类。
- 失败抛出异常，消息包含 Telegram 错误码与说明；限流时带有需要等待的秒数。
- 长轮询由适配器负责，订阅 Bot API 10.4 的全部 Update 种类：自行调用 `getUpdates` 会抢走更新，
  `setWebhook` 会让轮询失效，直到调用 `deleteWebhook`。

## SDK 接口覆盖

完整方法、Model 和平台限制见 [COVERAGE.md](COVERAGE.md)。平台有对应能力的功能已接入 SDK 的消息、
文件、交互确认和 Reaction 接口；平台没有等价接口的功能明确返回不支持。

文件可来自 HTTP(S)、本地绝对路径、file URI、base64:// 或 base64 data URI，使用 multipart 上传。
官方服务器限制机器人上传的文件最大 50 MB；需要发送更大的文件（例如长视频）时，自建
[Telegram Bot API Server](https://github.com/tdlib/telegram-bot-api) 并以 `--local` 模式运行，上限为 2000 MB，
然后在 **API Base URL** 中填写它的地址。超过 50 MB 的文件从磁盘流式上传，远程文件先写入临时文件，上传后删除。
上传会同时发布文件，并返回限定于当前实例和会话的凭据，不会伪造消息 ID。
原生按钮和回调保留 ButtonId/Data，重复的交互确认是幂等的。

Markdown 使用 Telegram MarkdownV2 语法。卡片可显式降级；Telegram 没有全员提及、
任意历史消息查询或完整成员列表接口，`GetMembersAsync` 明确不支持，不用管理员列表冒充全体成员。

适配器实现 `ILifetimeAwareAdapter`：轮询任务登记在宿主注入的生命周期上，停止和配置热重载时
宿主会等待它结束再卸载程序集。

## 验证与发布

```sh
dotnet run --project Tests/Verification/Verification.csproj -c Release
dotnet build ShiroBot.TelegramAdapter.csproj -c Release
```

自动验证使用模拟 HTTP 响应，覆盖 SDK 接口调用、取消、话题与引用回复、媒体发送参数、
能力检查、显式降级、自建 API 下载地址、长轮询事件顺序、身份隔离和配置管理。
共享 Model 部分验证各接口的请求参数、multipart 与 `attach://` 上传、原生内容段、特有事件负载和回调提示；
客户端部分验证真实格式的 Token、代理、显式 null 参数、错误参数和长轮询订阅。
真实 Telegram 收发需要有效 Bot Token，通过上面的 TestPlugin 命令验证。

Release 会把私有 DTO 程序集合并进 `bin/Release/net11.0/ShiroBot.TelegramAdapter.dll`，
不打包宿主提供的 SDK/Model，可以直接安装该 DLL。GitHub Actions 负责构建、
回归验证、版本校验和打包；推送与版本一致的 `v0.5.0` 标签时发布产物。TestPlugin 不进入 Release 包。

## 许可证

[GNU General Public License v3.0](LICENSE)。
