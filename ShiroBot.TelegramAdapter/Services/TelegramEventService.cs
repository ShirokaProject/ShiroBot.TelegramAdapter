using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Models;
using ShiroBot.Telegram.Model;

namespace ShiroBot.TelegramAdapter.Services;

public sealed class TelegramEventService : IEventService
{
    public event Func<BotEvent, Task>? EventReceived;

    public void Attach(TelegramClient client)
    {
        client.UpdateReceived += OnUpdateAsync;
    }

    private async Task OnUpdateAsync(TgUpdate update)
    {
        if (EventReceived is null)
        {
            return;
        }

        foreach (var botEvent in Map(update))
        {
            await EventReceived(botEvent);
        }
    }

    private static IEnumerable<BotEvent> Map(TgUpdate update)
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
                        SelfId = TelegramMapper.SelfId,
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
                    SelfId = TelegramMapper.SelfId,
                    Raw = message,
                    Channel = TelegramMapper.ToChannel(message.Chat),
                    UserId = left.Id.ToString(),
                    OperatorId = message.From?.Id.ToString()
                };
            }
            else
            {
                yield return TelegramMapper.ToMessageEvent(message);
            }
        }

        if (update.ChannelPost is { } post)
        {
            yield return TelegramMapper.ToMessageEvent(post);
        }

        if (update.EditedMessage is { } edited)
        {
            yield return new PlatformEvent
            {
                Platform = TelegramMapper.PlatformId,
                SelfId = TelegramMapper.SelfId,
                Raw = edited,
                Kind = "message_edited",
                Channel = TelegramMapper.ToChannel(edited.Chat, edited.MessageThreadId)
            };
        }

        if (update.CallbackQuery is { } callback)
        {
            yield return new PlatformEvent
            {
                Platform = TelegramMapper.PlatformId,
                SelfId = TelegramMapper.SelfId,
                Raw = callback,
                Kind = "callback_query",
                Channel = callback.Message is null ? null : TelegramMapper.ToChannel(callback.Message.Chat)
            };
        }

        if (update.ChatMember is { } memberUpdate)
        {
            yield return new PlatformEvent
            {
                Platform = TelegramMapper.PlatformId,
                SelfId = TelegramMapper.SelfId,
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
                SelfId = TelegramMapper.SelfId,
                Raw = myUpdate,
                Kind = "my_chat_member_updated",
                Channel = TelegramMapper.ToChannel(myUpdate.Chat)
            };
        }
    }
}
