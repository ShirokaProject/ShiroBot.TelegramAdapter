using ShiroBot.SDK.Adapter;
using ShiroBot.Telegram.Model;
using Sdk = ShiroBot.SDK.Models;

namespace ShiroBot.TelegramAdapter.Services;

public sealed class TelegramUserService(TelegramClient client) : IUserService
{
    public async Task<Sdk.User> GetSelfAsync()
    {
        var me = await client.CallAsync<TgUser>("getMe");
        return TelegramMapper.ToUser(me);
    }
}
