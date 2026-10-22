using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Models;
using ShiroBot.Telegram.Model;

namespace ShiroBot.TelegramAdapter.Services;

public sealed class TelegramInteractionService(TelegramClient client) : IMessageInteractionService
{
    private sealed class Pending(Channel? channel, string? messageId)
    {
        public Channel? Channel { get; } = channel;
        public string? MessageId { get; } = messageId;
        public DateTimeOffset Expires { get; } = DateTimeOffset.UtcNow.AddMinutes(10);
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public bool Acknowledged { get; set; }
    }
    private readonly BoundedCache<Pending> _pending = new();
    internal async Task RegisterAsync(TgCallbackQuery callback)
    {
        _pending.Set(callback.Id, new(callback.Message is { } m ? TelegramMapper.ToChannel(m.Chat, m.MessageThreadId) : null,
            callback.Message?.MessageId.ToString()));
        await AcknowledgeCoreAsync(callback.Id, default);
    }
    public Task AcknowledgeAsync(InteractionEvent interaction, CancellationToken cancellationToken = default) =>
        AcknowledgeCoreAsync(interaction.InteractionId, cancellationToken);
    private Pending Get(string id)
    {
        if (!_pending.TryGet(id, out var pending) || pending.Expires <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("Telegram 交互已过期或不属于当前适配器实例。");
        return pending;
    }
    private async Task AcknowledgeCoreAsync(string id, CancellationToken token)
    {
        var pending = Get(id);
        await pending.Gate.WaitAsync(token);
        try
        {
            if (pending.Acknowledged) return;
            await client.CallAsync("answerCallbackQuery", new { callback_query_id = id }, token);
            pending.Acknowledged = true;
        }
        finally { pending.Gate.Release(); }
    }
    internal async Task<string?> ReplyTargetAsync(InteractionReference reference, CancellationToken token)
    {
        var pending = Get(reference.InteractionId);
        if (pending.Channel is null || pending.Channel.Id != reference.Channel.Id || pending.Channel.Type != reference.Channel.Type)
            throw new NotSupportedException("该交互没有可发送回复的会话，或会话不匹配。");
        await AcknowledgeCoreAsync(reference.InteractionId, token);
        return pending.MessageId;
    }
}
