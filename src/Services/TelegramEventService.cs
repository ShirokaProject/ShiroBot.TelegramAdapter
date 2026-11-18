using System.Text.Json;
using ShiroBot.Model.Telegram;
using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Models;
using ShiroBot.Telegram.Model;
using static ShiroBot.TelegramAdapter.TelegramModelConverter;

namespace ShiroBot.TelegramAdapter.Services;

public sealed class TelegramEventService(TelegramMapper mapper, Func<TelegramInteractionService?>? getInteractions = null) : IEventService
{
    private static readonly JsonSerializerOptions DtoOptions = new(JsonSerializerDefaults.Web);

    public event Func<BotEvent, Task>? EventReceived;

    public void Attach(TelegramClient client) => client.RawUpdateReceived += OnRawUpdateAsync;

    public void Detach(TelegramClient client) => client.RawUpdateReceived -= OnRawUpdateAsync;

    public async Task PublishAsync(BotEvent botEvent)
    {
        if (EventReceived is not { } handlers) return;
        foreach (var handler in handlers.GetInvocationList().Cast<Func<BotEvent, Task>>()) await handler(botEvent);
    }

    private async Task OnRawUpdateAsync(JsonElement json)
    {
        var update = json.Deserialize<TgUpdate>(DtoOptions)!;
        var interactions = getInteractions?.Invoke();
        if (update.CallbackQuery is { } callback && interactions is not null)
            await interactions.RegisterAsync(callback);
        foreach (var botEvent in Map(update, json, interactions)) await PublishAsync(botEvent);
    }

    private PlatformEvent Platform(string kind, object raw, Channel? channel = null) => new()
    {
        Platform = TelegramMapper.PlatformId, SelfId = mapper.SelfId, Kind = kind, Raw = raw, Channel = channel
    };

    private static JsonElement Field(JsonElement update, string name) =>
        update.TryGetProperty(name, out var value) ? value.Clone() : default;

    private IEnumerable<BotEvent> Map(TgUpdate update, JsonElement json, TelegramInteractionService? interactions)
    {
        var updateId = update.UpdateId;

        if (update.Message is { } message)
        {
            // 成员加入/离开在 Telegram 里是 service message
            if (message.NewChatMembers is { Count: > 0 } joined)
            {
                foreach (var user in joined)
                {
                    yield return new MemberJoinedEvent
                    {
                        Platform = TelegramMapper.PlatformId, SelfId = mapper.SelfId, Raw = message,
                        Channel = TelegramMapper.ToChannel(message.Chat), UserId = user.Id.ToString(),
                        OperatorId = message.From?.Id.ToString()
                    };
                }
            }
            else if (message.LeftChatMember is { } left)
            {
                yield return new MemberLeftEvent
                {
                    Platform = TelegramMapper.PlatformId, SelfId = mapper.SelfId, Raw = message,
                    Channel = TelegramMapper.ToChannel(message.Chat), UserId = left.Id.ToString(),
                    OperatorId = message.From?.Id.ToString()
                };
            }
            else
            {
                yield return mapper.ToMessageEvent(message, Field(json, "message"));
            }
        }

        if (update.ChannelPost is { } post)
            yield return mapper.ToMessageEvent(post, Field(json, "channel_post"));

        if ((update.EditedMessage ?? update.EditedChannelPost) is { } edited)
        {
            var isChannel = update.EditedMessage is null;
            var editedJson = Field(json, isChannel ? "edited_channel_post" : "edited_message");
            var channel = TelegramMapper.ToChannel(edited.Chat, edited.MessageThreadId);
            yield return Platform(TelegramEventKinds.MessageEdited, new TelegramMessageEdited
            {
                UpdateId = updateId, Time = Time(Long(editedJson, "edit_date") ?? Long(editedJson, "date") ?? 0), Json = editedJson,
                Channel = channel, MessageId = edited.MessageId.ToString(), IsChannelPost = isChannel,
                Message = TelegramModelConverter.Message(editedJson),
                Segments = [.. mapper.ResolveMentions(TelegramMapper.ToSegments(edited)), .. NativeSegments(editedJson)]
            }, channel);
        }

        if (update.CallbackQuery is { } callback)
        {
            mapper.Remember(callback.From);
            var decoded = callback.Data is null ? ((string?)null, (string?)null) : ButtonCodec.Decode(callback.Data);
            var automatic = interactions?.AutoAnswer == true;
            yield return new InteractionEvent
            {
                Platform = TelegramMapper.PlatformId, SelfId = mapper.SelfId, Raw = callback, Kind = "callback_query",
                Channel = callback.Message is null ? null : TelegramMapper.ToChannel(callback.Message.Chat, callback.Message.MessageThreadId),
                InteractionId = callback.Id, User = TelegramMapper.ToUser(callback.From), ButtonId = decoded.Item1,
                Data = decoded.Item2, MessageId = callback.Message?.MessageId.ToString(),
                Acknowledgement = automatic ? InteractionAcknowledgement.Acknowledged : InteractionAcknowledgement.Pending,
                IsAutomaticallyAcknowledged = automatic, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
            };
        }
        if (update.MessageReaction is { } reaction)
        {
            if (reaction.User is { } reactor) mapper.Remember(reactor);
            if (reaction.OldReaction.Concat(reaction.NewReaction).Any(e => e.Type is not ("emoji" or "custom_emoji")))
                yield return Platform("message_reaction", reaction, TelegramMapper.ToChannel(reaction.Chat));
            foreach (var (items, other, added) in new[]
            {
                (reaction.NewReaction, reaction.OldReaction, true), (reaction.OldReaction, reaction.NewReaction, false)
            })
            foreach (var emoji in items.Where(e => !other.Any(o => o.Type == e.Type && o.Emoji == e.Emoji && o.CustomEmojiId == e.CustomEmojiId)))
            {
                if (emoji.Type is not ("emoji" or "custom_emoji")) continue;
                yield return new MessageReactionEvent
                {
                    Platform = TelegramMapper.PlatformId, SelfId = mapper.SelfId, Raw = reaction,
                    Kind = added ? "reaction_add" : "reaction_remove", Channel = TelegramMapper.ToChannel(reaction.Chat),
                    MessageId = reaction.MessageId.ToString(), IsAdded = added,
                    User = reaction.User is null ? null : TelegramMapper.ToUser(reaction.User),
                    Emoji = emoji.Type == "emoji" ? new UnicodeReactionEmoji(emoji.Emoji!) : new PlatformReactionEmoji(emoji.CustomEmojiId!, "")
                };
            }
        }
        if (update.MessageReactionCount is { } count)
        {
            if (count.Reactions.Count == 0 || count.Reactions.Any(e => e.Type.Type is not ("emoji" or "custom_emoji")))
                yield return Platform("message_reaction_count", count, TelegramMapper.ToChannel(count.Chat));
            foreach (var entry in count.Reactions)
            {
                if (entry.Type.Type is not ("emoji" or "custom_emoji")) continue;
                yield return new MessageReactionEvent
                {
                    Platform = TelegramMapper.PlatformId, SelfId = mapper.SelfId, Raw = count, Kind = "reaction_count",
                    Channel = TelegramMapper.ToChannel(count.Chat), MessageId = count.MessageId.ToString(), Count = entry.TotalCount,
                    IsAdded = entry.TotalCount > 0,
                    Emoji = entry.Type.Type == "emoji" ? new UnicodeReactionEmoji(entry.Type.Emoji!)
                        : new PlatformReactionEmoji(entry.Type.CustomEmojiId!, "")
                };
            }
        }
        if (update.ChatJoinRequest is { } joinRequest)
        {
            var join = Field(json, "chat_join_request");
            mapper.Remember(joinRequest.From);
            yield return Platform(TelegramEventKinds.ChatJoinRequest, new TelegramChatJoinRequest
            {
                UpdateId = updateId, Time = Time(Long(join, "date") ?? 0), Json = join, Chat = Chat(Prop(join, "chat")),
                User = User(Prop(join, "from")), UserChatId = Long(join, "user_chat_id") ?? 0, Bio = Str(join, "bio"),
                InviteLink = Has(join, "invite_link", out var link) ? InviteLink(link) : null
            }, TelegramMapper.ToChannel(joinRequest.Chat));
        }

        foreach (var (kind, field) in new[]
                 {
                     (TelegramEventKinds.ChatMemberUpdated, "chat_member"), (TelegramEventKinds.MyChatMemberUpdated, "my_chat_member")
                 })
        {
            if (!Has(json, field, out var source)) continue;
            yield return Platform(kind, new TelegramChatMemberUpdated
            {
                UpdateId = updateId, Time = Time(Long(source, "date") ?? 0), Json = source.Clone(), Chat = Chat(Prop(source, "chat")),
                Actor = User(Prop(source, "from")), OldMember = Member(Prop(source, "old_chat_member")),
                NewMember = Member(Prop(source, "new_chat_member")),
                InviteLink = Has(source, "invite_link", out var link) ? InviteLink(link) : null,
                ViaJoinRequest = Bool(source, "via_join_request")
            }, Channel(Prop(source, "chat"), null));
        }

        if (update.AdditionalData is not { } extra) yield break;
        foreach (var (kind, value) in extra)
        {
            var raw = value.Clone();
            var now = DateTimeOffset.UtcNow;
            object payload = kind switch
            {
                TelegramEventKinds.InlineQuery => new TelegramInlineQuery
                {
                    UpdateId = updateId, Time = now, Json = raw, QueryId = Str(raw, "id") ?? "", From = User(Prop(raw, "from")),
                    Query = Str(raw, "query") ?? "", Offset = Str(raw, "offset") ?? "",
                    ChatType = Str(raw, "chat_type") is { } type && type != "sender" ? ChatType(type) : null,
                    Location = Has(raw, "location", out var location) ? Location(location) : null
                },
                TelegramEventKinds.ChosenInlineResult => new TelegramChosenInlineResult
                {
                    UpdateId = updateId, Time = now, Json = raw, ResultId = Str(raw, "result_id") ?? "", From = User(Prop(raw, "from")),
                    Query = Str(raw, "query") ?? "", InlineMessageId = Str(raw, "inline_message_id")
                },
                TelegramEventKinds.Poll => new TelegramPollUpdated { UpdateId = updateId, Time = now, Json = raw, Poll = Poll(raw) },
                TelegramEventKinds.PollAnswer => new TelegramPollAnswer
                {
                    UpdateId = updateId, Time = now, Json = raw, PollId = Str(raw, "poll_id") ?? "",
                    User = Has(raw, "user", out var voter) ? User(voter) : null,
                    VoterChat = Has(raw, "voter_chat", out var voterChat) ? Chat(voterChat) : null,
                    OptionIds = Items(raw, "option_ids").Select(id => id.GetInt32()).ToArray()
                },
                TelegramEventKinds.PreCheckoutQuery => new TelegramPreCheckoutQuery
                {
                    UpdateId = updateId, Time = now, Json = raw, QueryId = Str(raw, "id") ?? "", From = User(Prop(raw, "from")),
                    Currency = Str(raw, "currency") ?? "", TotalAmount = Long(raw, "total_amount") ?? 0,
                    InvoicePayload = Str(raw, "invoice_payload") ?? "", ShippingOptionId = Str(raw, "shipping_option_id")
                },
                TelegramEventKinds.ShippingQuery => new TelegramShippingQuery
                {
                    UpdateId = updateId, Time = now, Json = raw, QueryId = Str(raw, "id") ?? "", From = User(Prop(raw, "from")),
                    InvoicePayload = Str(raw, "invoice_payload") ?? "",
                    ShippingAddress = Address(Prop(raw, "shipping_address"))
                },
                TelegramEventKinds.ChatBoost => new TelegramChatBoost
                {
                    UpdateId = updateId, Time = Time(Long(Prop(raw, "boost"), "add_date") ?? 0), Json = raw, Chat = Chat(Prop(raw, "chat")),
                    BoostId = Str(Prop(raw, "boost"), "boost_id") ?? "", Source = Str(Prop(Prop(raw, "boost"), "source"), "source") ?? "",
                    User = BoostUser(Prop(Prop(raw, "boost"), "source")),
                    ExpirationDate = Time(Long(Prop(raw, "boost"), "expiration_date") ?? 0)
                },
                TelegramEventKinds.ChatBoostRemoved => new TelegramChatBoost
                {
                    UpdateId = updateId, Time = Time(Long(raw, "remove_date") ?? 0), Json = raw, Chat = Chat(Prop(raw, "chat")),
                    BoostId = Str(raw, "boost_id") ?? "", Source = Str(Prop(raw, "source"), "source") ?? "",
                    User = BoostUser(Prop(raw, "source")), IsRemoved = true
                },
                _ => raw
            };
            yield return Platform(kind, payload);
        }
    }

    private static TelegramUser? BoostUser(JsonElement source) => Has(source, "user", out var user) ? User(user) : null;

    private static TelegramShippingAddress Address(JsonElement address) => new(Str(address, "country_code") ?? "",
        Str(address, "state") ?? "", Str(address, "city") ?? "", Str(address, "street_line1") ?? "",
        Str(address, "street_line2") ?? "", Str(address, "post_code") ?? "");
}
