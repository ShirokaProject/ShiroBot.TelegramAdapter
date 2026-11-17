# ShiroBot.TelegramAdapter

基于官方 `ShiroBot.Templates 1.0.3` 和 `ShiroBot.SDK 1.0.3` 的 Telegram Bot API 长轮询适配器，
目标框架 .NET 11，适配器版本 `0.4.0`。完整覆盖 **Telegram Bot API 10.4**（2026-11-09）：
193 个方法、全部类型和全部 Update 种类。

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
配置由宿主管理，修改后重启该实例。每个 Token 只能有一个长轮询实例，使用前需移除已有 webhook。

在 Telegram 私聊机器人发送 `ping`（应回复 `pong`）、`adapter-info` 或 `adapter-echo hello`。
群聊测试需确保机器人有读取和发送消息的权限；隐私模式会限制机器人收到的群消息。
话题使用 `chatId:threadId` 作为 SDK 频道 ID。

## 完整 Bot API

通用 SDK 接口之外，`ITelegramBotApi` 提供 Bot API 的全部方法，每个方法一个强类型重载，
参数名与官方文档一致（`snake_case` 转为 camelCase），必填参数在前：

```csharp
using ShiroBot.TelegramAdapter.BotApi;
using Api = ShiroBot.Telegram.Model.BotApi;

var telegram = Context.GetAdapterExtension<ITelegramBotApi>()!;

await telegram.SendPollAsync(-1001234567890, "午饭？",
    [new Api.InputPollOption { Text = "面" }, new Api.InputPollOption { Text = "饭" }]);

await telegram.SendMediaGroupAsync(-1001234567890, [
    new Api.InputMediaPhoto { Media = Api.InputFile.FromBytes(bytes, "a.jpg"), Caption = "上传" },
    new Api.InputMediaPhoto { Media = "已有的 file_id 或 https:// 地址" }
]);

if (await telegram.GetChatMemberAsync("@channel", userId) is Api.ChatMemberAdministrator admin) { /* ... */ }

// 每个 Update 的强类型对象，包括没有映射为 SDK 事件的种类。
telegram.UpdateReceived += update => Task.CompletedTask;
```

- `Api.ChatId` 接受数字 ID 或 `@username`；`Api.InputFile` 接受 file_id、HTTP(S) 地址、字节或流。
  含上传时自动改用 multipart，`InputMedia*`、`InputSticker`、`InputProfilePhoto*`、`InputStoryContent*`
  中的上传以 `attach://` 引用。
- 联合类型（`ChatMember`、`ReactionType`、`InputMedia`、`MessageOrigin`、`RichText` 等）是接口，
  按鉴别字段解析为具体类型；本版本未知的取值解析为 `*Unknown` 记录，字段不丢失。
- 所有对象都有 `AdditionalData`，Telegram 新增的字段原样保留，发送时写回。
- 编辑类方法返回 `Message?`：编辑 inline 消息时 Telegram 返回 True，对应 `null`。
- 失败抛出 `TelegramApiException`，包含 `ErrorCode`、`Description`、`RetryAfter`、`MigrateToChatId`。
- 规范之后新增的方法可用 `CallAsync("methodName", parameters)` 调用，返回原始 `JsonElement`。

长轮询由适配器负责：自行调用 `GetUpdatesAsync` 会抢走更新，`SetWebhookAsync` 会让轮询失效，
直到调用 `DeleteWebhookAsync`。轮询订阅规范列出的全部 Update 种类。

插件需要引用本适配器的 DLL 才能使用这些类型。只需要用户信息时，可以继续使用共享 Model
`ShiroBot.Model.Telegram.TelegramUser`：

```csharp
var api = Context.GetAdapterExtension<ShiroBot.TelegramAdapter.Services.ITelegramModelService>();
var me = await api!.GetTelegramUserAsync();
```

### 更新到新的 Bot API

类型和方法由 `scripts/codegen/generate_bot_api.py` 从仓库中固定版本的
[telegram-bot-api-spec](https://github.com/PaulSonOfLars/telegram-bot-api-spec) 生成，不要手工修改 `*.g.cs`：

```sh
curl -fL https://raw.githubusercontent.com/PaulSonOfLars/telegram-bot-api-spec/main/api.json \
    -o scripts/codegen/telegram-bot-api.json
python3 scripts/codegen/generate_bot_api.py
```

CI 会重新生成并检查结果与提交的代码一致。

## SDK 接口覆盖

完整方法、Model 和平台限制见 [COVERAGE.md](COVERAGE.md)。平台有对应能力的功能已接入 SDK 的消息、
文件、交互确认和 Reaction 接口；平台没有等价接口的功能明确返回不支持。

文件可来自 HTTP(S)、本地绝对路径、file URI、base64:// 或 base64 data URI，使用 multipart 上传。
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
Bot API 部分检查规范中的每个方法和类型都已生成，并验证 JSON 与 multipart 请求、`attach://` 上传、
联合类型解析、未知值保留、错误参数和强类型 Update。
真实 Telegram 收发需要有效 Bot Token，通过上面的 TestPlugin 命令验证。

Release 会把私有 DTO 程序集合并进 `bin/Release/net11.0/ShiroBot.TelegramAdapter.dll`，
不打包宿主提供的 SDK/Model，可以直接安装该 DLL。GitHub Actions 负责构建、生成代码检查、
回归验证、版本校验和打包；推送与版本一致的 `v0.4.0` 标签时发布产物。TestPlugin 不进入 Release 包。

## 许可证

[GNU General Public License v3.0](LICENSE)。
