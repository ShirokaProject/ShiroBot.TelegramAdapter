using System.Text.Json;
using ShiroBot.Model.Telegram;
using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Models;
using ShiroBot.Telegram.Model;
using Api = ShiroBot.Telegram.Model.BotApi;
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
        var typed = json.Deserialize<Api.Update>(Api.BotApiJson.Options)!;
        var interactions = getInteractions?.Invoke();
        if (update.CallbackQuery is { } callback && interactions is not null)
            await interactions.RegisterAsync(callback);
        foreach (var botEvent in Map(update, typed, json, interactions)) await PublishAsync(botEvent);
    }

    private PlatformEvent Platform(string kind, object raw, Channel? channel = null) => new()
    {
        Platform = TelegramMapper.PlatformId, SelfId = mapper.SelfId, Kind = kind, Raw = raw, Channel = channel
    };

    private static JsonElement Field(JsonElement update, string name) =>
        update.TryGetProperty(name, out var value) ? value.Clone() : default;

    private IEnumerable<BotEvent> Map(TgUpdate update, Api.Update typed, JsonElement json, TelegramInteractionService? interactions)
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
                yield return mapper.ToMessageEvent(message, typed.Message, Field(json, "message"));
            }
        }

        if (update.ChannelPost is { } post)
            yield return mapper.ToMessageEvent(post, typed.ChannelPost, Field(json, "channel_post"));

        if ((update.EditedMessage ?? update.EditedChannelPost) is { } edited)
        {
            var isChannel = update.EditedMessage is null;
            var editedTyped = (isChannel ? typed.EditedChannelPost : typed.EditedMessage)!;
            var editedJson = Field(json, isChannel ? "edited_channel_post" : "edited_message");
            var channel = TelegramMapper.ToChannel(edited.Chat, edited.MessageThreadId);
            yield return Platform(TelegramEventKinds.MessageEdited, new TelegramMessageEdited
            {
                UpdateId = updateId, Time = Time(editedTyped.EditDate ?? editedTyped.Date), Json = editedJson,
                Channel = channel, MessageId = edited.MessageId.ToString(), IsChannelPost = isChannel,
                Message = TelegramModelConverter.Message(editedTyped, editedJson),
                Segments = [.. TelegramMapper.ToSegments(edited), .. NativeSegments(editedTyped)]
            }, channel);
        }

        if (update.CallbackQuery is { } callback)
        {
            mapper.Users.Set(callback.From.Id.ToString(), callback.From);
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
            if (reaction.User is { } reactor) mapper.Users.Set(reactor.Id.ToString(), reactor);
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
        if (typed.ChatJoinRequest is { } join)
        {
            mapper.Users.Set(join.From.Id.ToString(), update.ChatJoinRequest!.From);
            yield return Platform(TelegramEventKinds.ChatJoinRequest, new TelegramChatJoinRequest
            {
                UpdateId = updateId, Time = Time(join.Date), Json = Field(json, "chat_join_request"),
                Chat = Chat(join.Chat), User = User(join.From), UserChatId = join.UserChatId, Bio = join.Bio,
                InviteLink = join.InviteLink is { } link ? InviteLink(link) : null
            }, TelegramMapper.ToChannel(update.ChatJoinRequest.Chat));
        }

        foreach (var (kind, source, field) in new[]
                 {
                     (TelegramEventKinds.ChatMemberUpdated, typed.ChatMember, "chat_member"),
                     (TelegramEventKinds.MyChatMemberUpdated, typed.MyChatMember, "my_chat_member")
                 })
        {
            if (source is null) continue;
            yield return Platform(kind, new TelegramChatMemberUpdated
            {
                UpdateId = updateId, Time = Time(source.Date), Json = Field(json, field), Chat = Chat(source.Chat),
                Actor = User(source.From), OldMember = Member(source.OldChatMember), NewMember = Member(source.NewChatMember),
                InviteLink = source.InviteLink is { } link ? InviteLink(link) : null,
                ViaJoinRequest = source.ViaJoinRequest == true
            }, Channel(source.Chat, null));
        }

        if (update.AdditionalData is not { } extra) yield break;
        foreach (var (kind, value) in extra)
        {
            var raw = value.Clone();
            object payload = kind switch
            {
                TelegramEventKinds.InlineQuery when typed.InlineQuery is { } query => new TelegramInlineQuery
                {
                    UpdateId = updateId, Time = DateTimeOffset.UtcNow, Json = raw, QueryId = query.Id, From = User(query.From),
                    Query = query.Query, Offset = query.Offset,
                    ChatType = query.ChatType is { } type && type != "sender" ? ChatType(type) : null,
                    Location = query.Location is { } location ? Location(location) : null
                },
                TelegramEventKinds.ChosenInlineResult when typed.ChosenInlineResult is { } chosen => new TelegramChosenInlineResult
                {
                    UpdateId = updateId, Time = DateTimeOffset.UtcNow, Json = raw, ResultId = chosen.ResultId,
                    From = User(chosen.From), Query = chosen.Query, InlineMessageId = chosen.InlineMessageId
                },
                TelegramEventKinds.Poll when typed.Poll is { } poll => new TelegramPollUpdated
                    { UpdateId = updateId, Time = DateTimeOffset.UtcNow, Json = raw, Poll = Poll(poll) },
                TelegramEventKinds.PollAnswer when typed.PollAnswer is { } answer => new TelegramPollAnswer
                {
                    UpdateId = updateId, Time = DateTimeOffset.UtcNow, Json = raw, PollId = answer.PollId,
                    User = answer.User is { } voter ? User(voter) : null,
                    VoterChat = answer.VoterChat is { } chat ? Chat(chat) : null,
                    OptionIds = answer.OptionIds.Select(id => (int)id).ToArray()
                },
                TelegramEventKinds.PreCheckoutQuery when typed.PreCheckoutQuery is { } checkout => new TelegramPreCheckoutQuery
                {
                    UpdateId = updateId, Time = DateTimeOffset.UtcNow, Json = raw, QueryId = checkout.Id,
                    From = User(checkout.From), Currency = checkout.Currency, TotalAmount = checkout.TotalAmount,
                    InvoicePayload = checkout.InvoicePayload, ShippingOptionId = checkout.ShippingOptionId
                },
                TelegramEventKinds.ShippingQuery when typed.ShippingQuery is { } shipping => new TelegramShippingQuery
                {
                    UpdateId = updateId, Time = DateTimeOffset.UtcNow, Json = raw, QueryId = shipping.Id,
                    From = User(shipping.From), InvoicePayload = shipping.InvoicePayload,
                    ShippingAddress = new TelegramShippingAddress(shipping.ShippingAddress.CountryCode,
                        shipping.ShippingAddress.State, shipping.ShippingAddress.City, shipping.ShippingAddress.StreetLine1,
                        shipping.ShippingAddress.StreetLine2, shipping.ShippingAddress.PostCode)
                },
                TelegramEventKinds.ChatBoost when typed.ChatBoost is { } boost => new TelegramChatBoost
                {
                    UpdateId = updateId, Time = Time(boost.Boost.AddDate), Json = raw, Chat = Chat(boost.Chat),
                    BoostId = boost.Boost.BoostId, Source = BoostSource(boost.Boost.Source).Name,
                    User = BoostSource(boost.Boost.Source).User, ExpirationDate = Time(boost.Boost.ExpirationDate)
                },
                TelegramEventKinds.ChatBoostRemoved when typed.RemovedChatBoost is { } removed => new TelegramChatBoost
                {
                    UpdateId = updateId, Time = Time(removed.RemoveDate), Json = raw, Chat = Chat(removed.Chat),
                    BoostId = removed.BoostId, Source = BoostSource(removed.Source).Name,
                    User = BoostSource(removed.Source).User, IsRemoved = true
                },
                _ => raw
            };
            yield return Platform(kind, payload);
        }
    }

    private static (string Name, TelegramUser? User) BoostSource(Api.ChatBoostSource source) => source switch
    {
        Api.ChatBoostSourcePremium premium => ("premium", User(premium.User)),
        Api.ChatBoostSourceGiftCode gift => ("gift_code", User(gift.User)),
        Api.ChatBoostSourceGiveaway giveaway => ("giveaway", giveaway.User is { } user ? User(user) : null),
        _ => (source.Source, null)
    };
}
