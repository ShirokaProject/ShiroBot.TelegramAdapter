using ShiroBot.SDK.Adapter;
using ShiroBot.Telegram.Model;
using Sdk = ShiroBot.SDK.Models;

namespace ShiroBot.TelegramAdapter.Services;

public sealed class TelegramUserService(TelegramClient client, TelegramMapper? mapper = null) : IUserService, ITelegramModelService
{
    public async Task<Sdk.User> GetSelfAsync(CancellationToken cancellationToken = default)
    {
        var me = await client.CallAsync<TgUser>("getMe", cancellationToken: cancellationToken);
        mapper?.Users.Set(me.Id.ToString(), me);
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
    public async Task<ShiroBot.Model.Telegram.TelegramUser> GetTelegramUserAsync(string? userId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (userId is null)
        {
            var self = await client.CallAsync<TgUser>("getMe", cancellationToken: cancellationToken);
            mapper?.Users.Set(self.Id.ToString(), self);
            return TelegramMapper.ToPlatformUser(self);
        }
        if (mapper is not null && mapper.Users.TryGet(userId, out var known)) return TelegramMapper.ToPlatformUser(known);
        throw new NotSupportedException("Bot API 不提供任意用户查询；请使用指定会话的成员查询。");
    }
    public async Task<ShiroBot.Model.Telegram.TelegramUser> GetTelegramMemberAsync(string channelId, string userId, CancellationToken cancellationToken = default)
    {
        var (chatId, _) = TelegramMapper.ParseChannelId(channelId);
        var member = await client.CallAsync<TgChatMember>("getChatMember", new { chat_id = chatId,
            user_id = TelegramMapper.ParseId(userId, "userId") }, cancellationToken);
        mapper?.Users.Set(userId, member.User);
        return TelegramMapper.ToPlatformUser(member.User);
    }
}
