using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Models;
using ShiroBot.Telegram.Model;

namespace ShiroBot.TelegramAdapter.Services;

public sealed class TelegramEventService(TelegramMapper mapper, Func<TelegramInteractionService?>? getInteractions = null) : IEventService
{
    public event Func<BotEvent, Task>? EventReceived;

    public void Attach(TelegramClient client)
    {
        client.UpdateReceived += OnUpdateAsync;
    }

    public void Detach(TelegramClient client) => client.UpdateReceived -= OnUpdateAsync;

    public async Task PublishAsync(BotEvent botEvent)
    {
        if (EventReceived is not { } handlers) return;
        foreach (var handler in handlers.GetInvocationList().Cast<Func<BotEvent, Task>>()) await handler(botEvent);
    }

    private async Task OnUpdateAsync(TgUpdate update)
    {
        if (update.CallbackQuery is { } callback && getInteractions?.Invoke() is { } interactions)
            await interactions.RegisterAsync(callback);
        foreach (var botEvent in Map(update)) await PublishAsync(botEvent);
    }

    private IEnumerable<BotEvent> Map(TgUpdate update)
    {
        if (update.Message is { } message)
        {
            // 成员加入/离开在 Telegram 里是 service message
            if (message.NewChatMembers is { Count: > 0 } joined)
            {
                foreach (var user in joined)
                {
                    yield return new MemberJoinedEvent
                    {
                        Platform = TelegramMapper.PlatformId,
                        SelfId = mapper.SelfId,
                        Raw = message,
                        Channel = TelegramMapper.ToChannel(message.Chat),
                        UserId = user.Id.ToString(),
                        OperatorId = message.From?.Id.ToString()
                    };
                }
            }
            else if (message.LeftChatMember is { } left)
            {
                yield return new MemberLeftEvent
                {
                    Platform = TelegramMapper.PlatformId,
                    SelfId = mapper.SelfId,
                    Raw = message,
                    Channel = TelegramMapper.ToChannel(message.Chat),
                    UserId = left.Id.ToString(),
                    OperatorId = message.From?.Id.ToString()
                };
            }
            else
            {
                yield return mapper.ToMessageEvent(message);
            }
        }

        if (update.ChannelPost is { } post)
        {
            yield return mapper.ToMessageEvent(post);
        }

        if ((update.EditedMessage ?? update.EditedChannelPost) is { } edited)
        {
            yield return new PlatformEvent
            {
                Platform = TelegramMapper.PlatformId,
                SelfId = mapper.SelfId,
                Raw = edited,
                Kind = "message_edited",
                Channel = TelegramMapper.ToChannel(edited.Chat, edited.MessageThreadId)
            };
        }

        if (update.CallbackQuery is { } callback)
        {
            mapper.Users.Set(callback.From.Id.ToString(), callback.From);
            var decoded = callback.Data is null ? ((string?)null, (string?)null) : ButtonCodec.Decode(callback.Data);
            yield return new InteractionEvent
            {
                Platform = TelegramMapper.PlatformId, SelfId = mapper.SelfId, Raw = callback, Kind = "callback_query",
                Channel = callback.Message is null ? null : TelegramMapper.ToChannel(callback.Message.Chat, callback.Message.MessageThreadId),
                InteractionId = callback.Id, User = TelegramMapper.ToUser(callback.From), ButtonId = decoded.Item1,
                Data = decoded.Item2, MessageId = callback.Message?.MessageId.ToString(),
                Acknowledgement = getInteractions?.Invoke() is null ? InteractionAcknowledgement.Pending : InteractionAcknowledgement.Acknowledged,
                IsAutomaticallyAcknowledged = getInteractions?.Invoke() is not null, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
            };
        }
        if (update.MessageReaction is { } reaction)
        {
            if (reaction.User is { } reactor) mapper.Users.Set(reactor.Id.ToString(), reactor);
            if (reaction.OldReaction.Concat(reaction.NewReaction).Any(e => e.Type is not ("emoji" or "custom_emoji")))
                yield return new PlatformEvent { Platform = TelegramMapper.PlatformId, SelfId = mapper.SelfId,
                    Kind = "message_reaction", Channel = TelegramMapper.ToChannel(reaction.Chat), Raw = reaction };
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
                yield return new PlatformEvent { Platform = TelegramMapper.PlatformId, SelfId = mapper.SelfId,
                    Kind = "message_reaction_count", Channel = TelegramMapper.ToChannel(count.Chat), Raw = count };
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
        if (update.ChatJoinRequest is { } join)
        {
            mapper.Users.Set(join.From.Id.ToString(), join.From);
            yield return new PlatformEvent { Platform = TelegramMapper.PlatformId, SelfId = mapper.SelfId, Raw = join,
                Kind = "chat_join_request", Channel = TelegramMapper.ToChannel(join.Chat) };
        }
        if (update.AdditionalData is { } unknown)
            foreach (var (kind, value) in unknown)
                yield return new PlatformEvent { Platform = TelegramMapper.PlatformId, SelfId = mapper.SelfId,
                    Kind = kind, Raw = value.Clone() };

        if (update.ChatMember is { } memberUpdate)
        {
            yield return new PlatformEvent
            {
                Platform = TelegramMapper.PlatformId,
                SelfId = mapper.SelfId,
                Raw = memberUpdate,
                Kind = "chat_member_updated",
                Channel = TelegramMapper.ToChannel(memberUpdate.Chat)
            };
        }

        if (update.MyChatMember is { } myUpdate)
        {
            yield return new PlatformEvent
            {
                Platform = TelegramMapper.PlatformId,
                SelfId = mapper.SelfId,
                Raw = myUpdate,
                Kind = "my_chat_member_updated",
                Channel = TelegramMapper.ToChannel(myUpdate.Chat)
            };
        }
    }
}
