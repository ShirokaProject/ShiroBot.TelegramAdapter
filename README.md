# ShiroBot.TelegramAdapter

使用官方 `ShiroBot.Templates 1.0.1` 和 `ShiroBot.SDK 1.0.1` 的 Telegram Bot API
长轮询适配器，目标框架为 .NET 10，适配器版本为 `0.3.0`。

## 开发与运行

```sh
dotnet build ShiroBot.TelegramAdapter.slnx -c Debug
sh scripts/dev.sh
```

在 Rider 打开 `ShiroBot.TelegramAdapter.slnx`，选择 Debug 配置和 `ShiroBot` 启动项。
Debug 构建自动下载匹配 SDK 的宿主，并将适配器和独立 TestPlugin 安装到 `.shirobot-dev/`。
Windows 使用 `powershell -ExecutionPolicy Bypass -File scripts/dev.ps1`。

访问 `http://127.0.0.1:7002/dashboard/`，登录密钥见开发宿主配置的 `[api].token`。
同一时间只运行一个使用此端口的开发宿主；Discord 与 Telegram 各自保留独立开发目录。
添加 Telegram 适配器实例，填写 BotFather 提供的 **Bot Token** 并连接。
**API Base URL** 留空使用官方服务器，使用自建 Bot API Server 时可填写自定义地址。
配置由宿主管理，修改后重启该实例。每个 Token 只运行一个长轮询实例，使用前需移除已有 webhook。

在 Telegram 私聊机器人发送 `ping`（应回复 `pong`）、`adapter-info` 或 `adapter-echo hello`。
群聊测试需确保机器人有读取相关消息和发送消息的权限；隐私模式会限制机器人收到的群消息。
话题使用 `chatId:threadId` 作为 SDK 频道 ID。

## 验证与发布

```sh
dotnet run --project Tests/Verification/Verification.csproj -c Release
dotnet build ShiroBot.TelegramAdapter.csproj -c Release
```

自动验证使用模拟 HTTP 响应，覆盖 SDK 接口调用、取消、话题与引用回复、媒体发送参数、
能力检查、显式降级、自建 API 下载地址、长轮询事件顺序、身份隔离和配置管理。
真实 Telegram 收发需要有效 Bot Token，通过上面的 TestPlugin 命令验证。

Release 会将私有 DTO 程序集合并到 `bin/Release/net10.0/ShiroBot.TelegramAdapter.dll`，
不打包宿主提供的 SDK/Model。可以直接安装该 DLL。
GitHub Actions 提供构建、回归验证、版本校验和打包；推送匹配版本的 `v0.3.0` 标签时发布产物。
TestPlugin 不进入 Release 适配器包。

## API 与 Model 覆盖

完整方法、Model 和平台限制见 [COVERAGE.md](COVERAGE.md)。可用能力已接入 SDK 的消息、
文件、交互确认和 Reaction 接口；平台没有等价接口的功能明确返回不支持。

文件可来自 HTTP(S)、本地绝对路径、file URI、base64:// 或 base64 data URI，真实使用 multipart 上传。
上传会同时发布文件，并返回当前实例和会话限定的凭据；不会伪造消息 ID。
原生按钮和回调保留 ButtonId/Data，重复交互确认幂等。

共享 `ShiroBot.Model.Telegram.TelegramUser` 可通过 `ITelegramModelService` 查询：

```csharp
var api = Context.GetAdapterExtension<ShiroBot.TelegramAdapter.Services.ITelegramModelService>();
var me = await api!.GetTelegramUserAsync();
```

Markdown 使用 Telegram MarkdownV2 语法。卡片可显式降级；Telegram 没有全员提及、
任意历史消息查询或完整成员列表接口。`GetMembersAsync` 明确不支持，不再只返回管理员。
