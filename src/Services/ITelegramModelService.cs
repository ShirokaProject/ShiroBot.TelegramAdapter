using ShiroBot.Model.Telegram;

namespace ShiroBot.TelegramAdapter.Services;

public interface ITelegramModelService
{
    Task<TelegramUser> GetTelegramUserAsync(string? userId = null, CancellationToken cancellationToken = default);
    Task<TelegramUser> GetTelegramMemberAsync(string channelId, string userId, CancellationToken cancellationToken = default);
}
