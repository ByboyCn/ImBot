namespace ImBot.OneBot.Api;

/// <summary>
/// OneBot 12 标准接口（完整动作集），由 adapter-onebot 插件提供。
/// 插件开发者 ctx.Require&lt;IOneBotApi&gt; 即可获得类型化的全部标准动作；
/// 外部程序可经 HTTP（POST /）或 WebSocket 调用同名 action。
/// 标注 optional 的动作在当前实现端能力不足时抛 OneBotNotImplementedException。
/// </summary>
public interface IOneBotApi
{
    // ================ Version / Status / Lifecycle ================
    /// <summary>获取实现端版本信息。</summary>
    Task<OneBotVersion> GetVersionAsync();
    /// <summary>获取运行状态。</summary>
    Task<OneBotStatus> GetStatusAsync();
    /// <summary>获取支持的 action 列表。</summary>
    Task<IReadOnlyList<string>> GetSupportedActionsAsync();
    /// <summary>获取机器人自身信息。</summary>
    Task<OneBotSelfInfo> GetSelfInfoAsync();

    // ================ User ================
    /// <summary>获取用户信息（optional）。</summary>
    Task<OneBotUser> GetUserInfoAsync(string userId, string? selfId = null);
    /// <summary>获取好友列表（optional）。</summary>
    Task<IReadOnlyList<OneBotUser>> GetUserListAsync();

    // ================ Friend ================
    /// <summary>发送私聊消息（建议用 SendMessageAsync detail_type=private）。</summary>
    Task<string> SendPrivateMessageAsync(string userId, params OneBotMessageSegment[] message);
    /// <summary>获取好友列表。</summary>
    Task<IReadOnlyList<OneBotFriend>> GetFriendListAsync();

    // ================ Group ================
    /// <summary>获取群信息。</summary>
    Task<OneBotGroup> GetGroupInfoAsync(string groupId, bool noCache = false);
    /// <summary>获取群列表。</summary>
    Task<IReadOnlyList<OneBotGroup>> GetGroupListAsync();
    /// <summary>获取群成员信息。</summary>
    Task<OneBotGroupMember> GetGroupMemberInfoAsync(string groupId, string userId, bool noCache = false);
    /// <summary>获取群成员列表。</summary>
    Task<IReadOnlyList<OneBotGroupMember>> GetGroupMemberListAsync(string groupId);
    /// <summary>设置群名称（optional）。</summary>
    Task SetGroupNameAsync(string groupId, string groupName);
    /// <summary>退出群（optional）。</summary>
    Task LeaveGroupAsync(string groupId);

    // ================ Guild / Channel ================
    /// <summary>获取频道列表（optional）。</summary>
    Task<IReadOnlyList<OneBotGuild>> GetGuildListAsync();
    /// <summary>获取频道信息（optional）。</summary>
    Task<OneBotGuild> GetGuildInfoAsync(string guildId, bool noCache = false);
    /// <summary>获取子频道列表（optional）。</summary>
    Task<IReadOnlyList<OneBotChannel>> GetChannelListAsync(string guildId, bool noCache = false);
    /// <summary>获取子频道信息（optional）。</summary>
    Task<OneBotChannelInfo> GetChannelInfoAsync(string channelId, bool noCache = false);
    /// <summary>发送频道消息（建议用 SendMessageAsync detail_type=channel）。</summary>
    Task<string> SendChannelMessageAsync(string channelId, params OneBotMessageSegment[] message);
    /// <summary>获取频道成员列表（optional）。</summary>
    Task<IReadOnlyList<OneBotUser>> GetGuildMemberListAsync(string guildId, string? nextToken = null);
    /// <summary>获取频道成员信息（optional）。</summary>
    Task<OneBotUser> GetGuildMemberInfoAsync(string guildId, string userId, bool noCache = false);
    /// <summary>禁言频道成员（optional）。</summary>
    Task MuteGuildMemberAsync(string guildId, string userId, long duration);

    // ================ Message ================
    /// <summary>统一消息发送（detail_type: private / group / channel 路由）。返回 message_id。</summary>
    Task<string> SendMessageAsync(OneBotSendMessageRequest request);
    /// <summary>撤回消息（optional）。</summary>
    Task DeleteMessageAsync(string messageId);
    /// <summary>获取消息详情（optional）。</summary>
    Task<object> GetMessageAsync(string messageId);
    /// <summary>获取最新消息列表（optional）。</summary>
    Task<IReadOnlyList<object>> GetLatestMessagesAsync(string detailType, string targetId, int count);

    // ================ Upload / Download ================
    /// <summary>上传文件（data 与 url 二选一）。返回 file_id。</summary>
    Task<OneBotUploadResult> UploadFileAsync(string type, string name, long size, byte[]? data = null, string? url = null);
    /// <summary>分片上传：准备。返回 transfer_id。</summary>
    Task<OneBotTransferHandle> UploadFileFragmentedPrepareAsync(string type, string name, long size);
    /// <summary>分片上传：传输一片。</summary>
    Task UploadFileFragmentedTransferAsync(string transferId, int offset, byte[] data);
    /// <summary>分片上传：完成。返回 file_id。</summary>
    Task<OneBotUploadResult> UploadFileFragmentedFinishAsync(string transferId);
    /// <summary>分片上传：中止。</summary>
    Task UploadFileFragmentedAbortAsync(string transferId);
    /// <summary>下载文件。返回文件字节。</summary>
    Task<byte[]> DownloadFileAsync(string fileId);

    // ================ Like（已废弃，保留兼容）================
    /// <summary>赞好友（optional，已废弃）。</summary>
    Task SendLikeAsync(string userId, int times = 1);
}

/// <summary>实现端不支持的动作（OneBot retcode=10003）。</summary>
public sealed class OneBotNotImplementedException(string action)
    : Exception($"action '{action}' not implemented by this platform adapter");
