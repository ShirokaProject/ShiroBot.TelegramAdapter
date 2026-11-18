using ShiroBot.SDK.Adapter;
using ShiroBot.Telegram.Model;
using Sdk = ShiroBot.SDK.Models;

namespace ShiroBot.TelegramAdapter.Services;

public sealed class TelegramUserService(TelegramClient client, TelegramMapper? mapper = null) : IUserService
{
    public async Task<Sdk.User> GetSelfAsync(CancellationToken cancellationToken = default)
    {
        var me = await client.CallAsync<TgUser>("getMe", cancellationToken: cancellationToken);
        mapper?.Remember(me);
        return TelegramMapper.ToUser(me);
    }
    public Task<Sdk.User?> GetUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (mapper is not null && mapper.Users.TryGet(userId, out var user)) return Task.FromResult<Sdk.User?>(TelegramMapper.ToUser(user));
        throw new NotSupportedException("Bot API 不提供任意用户查询；该用户未出现在当前实例缓存中，可使用 GetMemberAsync 查询指定会话成员。");
    }
    public Task<IReadOnlyList<Sdk.User>> GetFriendsAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Telegram Bot API 不提供好友列表。");
    public Task AcceptFriendRequestAsync(string token, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Telegram 机器人没有好友申请接口。");
    public Task RejectFriendRequestAsync(string token, string? reason = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Telegram 机器人没有好友申请接口。");
}
