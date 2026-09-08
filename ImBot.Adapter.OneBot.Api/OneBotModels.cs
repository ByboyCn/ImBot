using System.Text.Json.Serialization;

namespace ImBot.OneBot.Api;

// ==================== OneBot 12 数据模型（snake_case 与标准 JSON 对齐）====================

/// <summary>get_version 返回。</summary>
public record OneBotVersion(
    [property: JsonPropertyName("impl")] string Implementation,
    [property: JsonPropertyName("version")] string VersionNum,
    [property: JsonPropertyName("onebot_version")] string StdVersion);

/// <summary>get_status 返回。</summary>
public record OneBotStatus(
    [property: JsonPropertyName("good")] bool Good,
    [property: JsonPropertyName("online")] bool Online);

/// <summary>get_self_info 返回。</summary>
public record OneBotSelfInfo(
    [property: JsonPropertyName("user_id")] string UserId,
    [property: JsonPropertyName("user_name")] string? UserName,
    [property: JsonPropertyName("user_displayname")] string? UserDisplayName);

public record OneBotUser(
    [property: JsonPropertyName("user_id")] string UserId,
    [property: JsonPropertyName("user_name")] string? UserName,
    [property: JsonPropertyName("user_displayname")] string? UserDisplayName,
    [property: JsonPropertyName("user_remark")] string? UserRemark);

public record OneBotFriend(
    [property: JsonPropertyName("user_id")] string UserId,
    [property: JsonPropertyName("user_name")] string? UserName,
    [property: JsonPropertyName("user_displayname")] string? UserDisplayName,
    [property: JsonPropertyName("user_remark")] string? UserRemark);

public record OneBotGroup(
    [property: JsonPropertyName("group_id")] string GroupId,
    [property: JsonPropertyName("group_name")] string GroupName);

public record OneBotGroupMember(
    [property: JsonPropertyName("user_id")] string UserId,
    [property: JsonPropertyName("user_name")] string? UserName,
    [property: JsonPropertyName("user_displayname")] string? UserDisplayName,
    [property: JsonPropertyName("user_remark")] string? UserRemark,
    [property: JsonPropertyName("group_id")] string GroupId);

public record OneBotGuild(
    [property: JsonPropertyName("guild_id")] string GuildId,
    [property: JsonPropertyName("guild_name")] string GuildName);

public record OneBotChannel(
    [property: JsonPropertyName("channel_id")] string ChannelId,
    [property: JsonPropertyName("channel_name")] string ChannelName,
    [property: JsonPropertyName("guild_id")] string GuildId);

public record OneBotChannelInfo(
    [property: JsonPropertyName("channel_id")] string ChannelId,
    [property: JsonPropertyName("channel_name")] string ChannelName,
    [property: JsonPropertyName("guild_id")] string GuildId,
    [property: JsonPropertyName("owner_guild_id")] string? OwnerGuildId);

/// <summary>消息段。OneBot 12 的 message 为段数组；本实现支持 text / mention / reply / image。</summary>
public record OneBotMessageSegment(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("data")] System.Text.Json.Nodes.JsonObject? Data)
{
    public static OneBotMessageSegment Text(string text) =>
        new("text", new() { ["text"] = text });
    public static OneBotMessageSegment Mention(string userId) =>
        new("mention", new() { ["user_id"] = userId });
    public static OneBotMessageSegment Reply(string messageId) =>
        new("reply", new() { ["message_id"] = messageId });
    public static OneBotMessageSegment Image(string url) =>
        new("image", new() { ["url"] = url });

    /// <summary>提取纯文本（text 段拼接）。</summary>
    public string? ExtractText() => Type == "text" ? Data?["text"]?.GetValue<string>() : null;
}

/// <summary>send_message 请求：detail_type 路由（private/group/channel）。</summary>
public record OneBotSendMessageRequest(
    [property: JsonPropertyName("detail_type")] string DetailType,
    [property: JsonPropertyName("user_id")] string? UserId,
    [property: JsonPropertyName("group_id")] string? GroupId,
    [property: JsonPropertyName("guild_id")] string? GuildId,
    [property: JsonPropertyName("channel_id")] string? ChannelId,
    [property: JsonPropertyName("message")] OneBotMessageSegment[] Message);

/// <summary>文件信息。</summary>
public record OneBotFile(
    [property: JsonPropertyName("file_id")] string FileId,
    [property: JsonPropertyName("file_name")] string FileName,
    [property: JsonPropertyName("file_size")] long FileSize);

/// <summary>上传文件返回。</summary>
public record OneBotUploadResult(
    [property: JsonPropertyName("file_id")] string FileId);

/// <summary>分片上传句柄。</summary>
public record OneBotTransferHandle(
    [property: JsonPropertyName("transfer_id")] string TransferId);
