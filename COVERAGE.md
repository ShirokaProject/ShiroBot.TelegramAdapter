# Telegram 适配器覆盖清单

版本 `0.5.0`，SDK `1.0.4`（Telegram Model 1.1），Telegram Bot API `10.4`。以下清单同时列出两个平台的语义差异。

## SDK API 清单（1.0.4）

所有方法均由适配器显式声明实现，回归测试使用接口映射检查，防止意外调用 SDK 默认实现。
“不支持”项会抛出带平台原因的 `NotSupportedException`，不会返回伪造成功或不完整列表。
平台权限、账号限制、资源大小和表情可用性仍由平台服务器校验。

| 接口 / 方法 | Discord | Telegram |
| --- | --- | --- |
| IMessageService.GetMessageCapabilities / AssessMessage | 原生能力及平台限制检查 | 原生能力及平台限制检查 |
| SendMessageAsync（两种重载） | 文本、Markdown、Card/Embed、提及、资源、引用、按钮、交互回复 | 文本、MarkdownV2、提及、资源、贴纸、引用、按钮、交互回复 |
| DeleteMessageAsync | REST 删除 | deleteMessage |
| GetMessageAsync | REST 查询 | 不支持：Bot API 无此接口 |
| GetHistoryMessagesAsync | REST 查询，最多 100 条/次 | 不支持：Bot API 无历史查询 |
| GetResourceUrlAsync | 根据附件来源重新查询临时 URL；也接受 HTTP(S) URL | getFile，支持 file_id、tgfile://、当前实例上传句柄 |
| IChannelService.GetChannelsAsync | 分页服务器列表、文字/公告频道及活跃话题；不含归档话题 | 不支持：Bot API 无完整会话枚举 |
| GetChannelAsync | 频道/私聊/话题资料 | getChat；保留话题 ID |
| GetMembersAsync | 分页成员列表；映射 Owner/Admin/Member | 不支持：Bot API 无完整成员枚举，不用管理员列表冒充 |
| GetMemberAsync | 服务器成员与角色 | getChatMember |
| SetChannelNameAsync | PATCH channel | setChatTitle；话题使用 editForumTopic |
| KickMemberAsync | 移除服务器成员 | banChatMember 后 unbanChatMember，允许之后重新加入 |
| MuteMemberAsync | Timeout，最长 28 天 | restrictChatMember，有限时长 30 秒至 366 天 |
| LeaveChannelAsync | 离开所在服务器 | leaveChat |
| IUserService.GetSelfAsync | /users/@me | getMe |
| GetUserAsync | /users/{id} | 当前实例已观察到的用户缓存；未知用户明确不支持 |
| GetFriendsAsync | 不支持：机器人没有好友列表 API | 不支持：Bot API 没有好友列表 API |
| AcceptFriendRequestAsync / RejectFriendRequestAsync | 不支持：机器人没有好友申请 API | 不支持：机器人没有好友申请 API |
| IFileService.GetFileCapabilities / UploadAsync | multipart 发布文件并返回会话限定句柄 | multipart sendDocument 并返回会话限定句柄 |
| IMessageInteractionService.AcknowledgeAsync | 自动 defer；重复确认幂等 | 自动 answerCallbackQuery；重复确认幂等 |
| IMessageReactionService.GetReactionCapabilities / SetReactionAsync | Unicode/平台表情，添加和删除自己的回应 | Unicode/自定义表情，设置一个或清除自己的回应 |

`IFileService`、`IMessageInteractionService`、`IMessageReactionService` 通过当前实例的
`GetExtension<T>()` 或插件的 `Context.GetAdapterExtension<T>()` 获取。平台表情由宿主绑定实例身份。
文件上传会同时发布文件，`UploadPublishes` 和 `IsPublished` 均为 true；返回的 `FileId`
是凭据而不是消息 ID，限定在当前实例、会话和缓存生命周期内。读取资源每次最多 50 MiB。

## SDK Model 清单

| Model | Discord | Telegram |
| --- | --- | --- |
| User / Member / MemberRole | 用户、昵称、头像、机器人标志；成员查询映射角色 | 用户、机器人标志；成员查询映射角色，头像无上游字段时为空 |
| Channel / ChannelType | 私聊、群频道、话题 | 私聊、群/频道、话题 |
| MessageEvent / SentMessage | 消息映射与发送结果 | 消息映射与发送结果 |
| TextSegment | 收发 | 收发 |
| MarkdownSegment | 原生 Discord Markdown | 原生 Telegram MarkdownV2；语法遵循 Telegram |
| CardSegment / CardField | 原生 Embed；标题、说明、图、链接、字段 | 无等价原生卡片；显式允许 CardAsMarkdown/CardAsText 时通过 SDK 降级 |
| ImageSegment / AudioSegment / VideoSegment / FileSegment | 入站映射，出站真实附件上传 | 入站映射，出站真实媒体上传 |
| MentionSegment | 原生 <@id> | 数字 ID 使用 tg://user?id 链接，用户名使用 @username；是否通知受平台规则限制 |
| MentionAllSegment | @everyone/@here 入站；@everyone 出站 | 不支持：Telegram 没有全员提及等价能力 |
| QuoteSegment | 引用回复 | reply_parameters 引用回复 |
| EmojiSegment | Unicode 或自定义表情标记；贴纸原始字段另在 Raw 中保留 | Unicode 或贴纸 file_id；tgfile:// 贴纸优先原生发送 |
| RawSegment | Discord Embed/贴纸可原生发送；其他类型明确拒绝 | 未建模内容入站保留；未知 Raw 发送明确拒绝 |
| OutgoingMessage | 校验并发送完整请求 | 校验并发送完整请求 |
| MessageCapabilities / MessageFeatures / MessageSendAssessment | 声明可用能力，检查实际计划 | 声明可用能力，检查实际计划 |
| MessageFallbackOptions / PreparedMessage / MessageTransformation / MessageTransformationKind | 使用 SDK 显式降级并返回转换记录 | 使用 SDK 显式降级并返回转换记录 |
| MessageButton / MessageButtonRow / MessageButtonLayout | 原生 action rows | 原生 inline keyboard |
| OpenUrlAction / CallbackAction | 原生链接和回调；按钮 ID 与 Data 可往返恢复 | 原生链接和回调；按钮 ID 与 Data 可往返恢复 |
| InteractionEvent / InteractionAcknowledgement | Gateway 交互，自动确认状态 | callback_query，自动确认状态 |
| InteractionReference | 原交互响应编辑；有过期和会话校验 | 确认点击后在来源会话引用回复；inline 无会话回调不能发送会话回复 |
| MessageReactionEvent / ReactionEmoji / UnicodeReactionEmoji / PlatformReactionEmoji / ReactionCapabilities | 类型化添加/删除事件及回应 API | 类型化添加/删除/计数事件及回应 API |
| MessageDeletedEvent | 单条和批量删除 | 普通 Bot API 不发送删除事件；特殊更新保留为 PlatformEvent |
| MemberJoinedEvent / MemberLeftEvent | 服务器成员事件 | 成员 service message |
| BotOfflineEvent | 意外断开和连接错误时发布 | 长轮询失败时发布 |
| FriendRequestEvent / GuildInviteEvent | 无等价机器人事件，不伪造 | 无等价事件；入群申请属于用户申请，保留为 chat_join_request 平台事件 |
| PlatformEvent / BotEvent.Raw | 未覆盖 Gateway 事件保留克隆 JSON | 其他 Update 保留原始 JSON；已订阅当前文档列出的所有 Update 类型 |
| UserReference / ChannelReference / MessageReference | 由 SDK/宿主做实例路由；不混用平台 ID | 由 SDK/宿主做实例路由；不混用平台 ID |
| FileCapabilities / FileUploadRequest / FileUploadResult | 支持上传、发布语义、范围限定凭据 | 支持上传、发布语义、范围限定凭据 |

接收到的协议 DTO 使用 `JsonExtensionData` 保存未建模字段，事件的 `Raw` 保留完整已接收数据。
SDK 映射层只覆盖 SDK 1.0.4 合约；Telegram 特有能力由共享 Model 提供，其余方法通过原始调用使用。SDK/宿主负责写入 `InstanceId`、构造路由引用和包装发送结果。

按钮回调将 ID 与 Data 一起编码，长度校验按编码后的实际字节计算。不会截断回调数据。
交互令牌和上传句柄缓存有容量限制，实例停止后释放；不能跨实例复用。

## Telegram Model 1.1 清单

| 接口 / 契约 | 实现 |
| --- | --- |
| ITelegramClientApi | AccountKind = Bot、Transport = BotApi；上传上限 50 MB / 下载上限 20 MB（自建 Bot API Server 均为 2000 MB）；getMe、已见用户缓存 |
| ITelegramChatApi | getChat、getChatMemberCount、getChatMember、getChatAdministrators、ban/unban/restrict/promoteChatMember、setChatAdministratorCustomTitle、setChatPermissions、setChatTitle/Description/Photo、deleteChatPhoto、pin/unpin/unpinAll（话题用 unpinAllForumTopicMessages）、export/create/edit/revokeChatInviteLink、approve/declineChatJoinRequest、leaveChat、create/edit/close/reopen/deleteForumTopic |
| ITelegramMessageApi | sendMessage/Poll/Location/Venue/Contact/Dice/Sticker/Invoice/MediaGroup（本地内容以 attach:// 上传）、editMessageText/Caption/ReplyMarkup（含 inline 消息）、forward/copyMessage、deleteMessages、sendChatAction、stopPoll、带提示的 answerCallbackQuery |
| ITelegramInlineApi | answerInlineQuery：文章、图片（地址或 file_id）、贴纸 |
| ITelegramPaymentApi | answerPreCheckoutQuery、answerShippingQuery、refundStarPayment、getMyStarBalance |
| ITelegramBotProfileApi | set/get/deleteMyCommands（全部范围）、setMyName/Description/ShortDescription |
| ITelegramBotApi | 任意方法 + multipart 上传，原始 Update 事件 |
| MessageEvent.Raw | `TelegramMessage`：发送者会话、编辑时间、相册 ID、转发来源、话题、inline 机器人、签名、企业连接、原始 JSON |
| RawSegment | venue（同时带 location 时只报地点）、location、contact、dice、poll、successful_payment、formatted_text；其他未建模字段保持 `JsonElement` |
| PlatformEvent | message_edited、chat_member_updated、my_chat_member_updated、chat_join_request、inline_query、chosen_inline_result、poll、poll_answer、pre_checkout_query、shipping_query、chat_boost、removed_chat_boost 使用 Model 负载；其他 Update 为 `JsonElement` |

已转换为 `TelegramMessage` 资料的元数据字段（如 media_group_id、forward_origin、edit_date）不再重复作为 RawSegment 上报。

## Telegram Bot API 10.4

共享 Model 没有单独封装的方法通过 `ITelegramBotApi.CallAsync` 调用，参数为 JSON，支持 multipart 上传与 `attach://` 引用；
`UpdateReceived` 提供全部 Update 的原始 JSON。长轮询订阅 Bot API 10.4 的全部 29 种 Update。
失败抛出 `TelegramApiException`（适配器内部类型），消息包含错误码与说明，并解析 `retry_after` 与 `migrate_to_chat_id`。

Bot API 自身的限制不变：没有按 ID 读取任意消息、历史消息、完整成员列表和会话枚举的方法；
调用 `getUpdates` 或 `setWebhook` 会与适配器的长轮询冲突。

## 验证

```sh
dotnet run --project Tests/Verification/Verification.csproj -c Release
dotnet build -c Release
```

回归测试覆盖所有 SDK API 是否显式实现、核心 REST 请求参数、原生按钮数据往返、交互确认幂等性、
Reaction、上传内容及会话隔离、共享用户 Model、未知字段保留、事件类型及平台不支持项；
Bot API 部分覆盖规范完整性、JSON/multipart/`attach://` 请求、联合类型与未知值、错误参数和强类型 Update。
测试使用模拟 HTTP，不访问真实机器人账号。真实账号权限、网络连接和平台服务端行为需配置 Token 后联调。

协议依据：[Discord Message](https://docs.discord.com/developers/resources/message)、
[Discord Interactions](https://docs.discord.com/developers/interactions/receiving-and-responding)、
[Telegram Bot API](https://core.telegram.org/bots/api)。
