using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Models;

namespace ShiroBot.TelegramAdapter.Services;

public sealed class TelegramReactionService(TelegramClient client) : IMessageReactionService
{
    public ReactionCapabilities GetReactionCapabilities(Channel channel) => ReactionCapabilities.Unicode | ReactionCapabilities.PlatformEmoji;
    public Task SetReactionAsync(MessageReference message, ReactionEmoji emoji, bool isAdd = true, CancellationToken cancellationToken = default)
    {
        var (chatId, _) = TelegramMapper.ParseChannelId(message.Channel.Id);
        object reaction = emoji switch
        {
            UnicodeReactionEmoji unicode => new { type = "emoji", emoji = unicode.Value },
            PlatformReactionEmoji custom when string.Equals(custom.InstanceId, message.InstanceId, StringComparison.OrdinalIgnoreCase)
                => new { type = "custom_emoji", custom_emoji_id = custom.Id },
            PlatformReactionEmoji => throw new ArgumentException("平台表情不能跨实例使用。"),
            _ => throw new NotSupportedException("未知 Reaction 类型。")
        };
        // Telegram bots can set one reaction; removing clears this bot's reaction only.
        return client.CallAsync("setMessageReaction", new { chat_id = chatId,
            message_id = TelegramMapper.ParseId(message.MessageId, "messageId"), reaction = isAdd ? new[] { reaction } : [] }, cancellationToken);
    }
}
