using System;
using System.Collections.Generic;
using System.Linq;

namespace ImBot.Core;

// ==================== 领域词汇（框架层）====================

/// <summary>入站消息：平台无关。</summary>
public sealed record InboundMessage(string Platform, string ConversationId, string SenderId, string Text);

/// <summary>
/// 平台适配器。由各平台适配器插件提供（同一类型可多实例：QQ、企微、Telegram…）。
/// 实现方约定：Connected 变化、掉线重连都应通过 Provide/撤销 Provide 表达，
/// 这样依赖者会自动停用/恢复。
/// </summary>
public interface IAdapter
{
    string Platform { get; }
    bool Connected { get; }
    event Func<InboundMessage, Task>? OnMessage;
    Task SendTextAsync(string conversationId, string text);

    /// <summary>向适配器注入一条入站消息（宿主控制台/UI 测试用），如同来自真实用户。</summary>
    Task RaiseUserMessageAsync(string senderId, string text);
}

/// <summary>命令路由服务：由 Router 插件提供，命令插件消费。</summary>
public interface ICommandRegistry
{
    void Register(string name, string help, Func<CommandContext, Task> handler);
    void Unregister(string name);
    IReadOnlyDictionary<string, string> Commands { get; }
}

public sealed record CommandContext(IAdapter Adapter, InboundMessage Message, string[] Args);

/// <summary>运行时管理服务：由宿主提供，Admin 插件消费（装卸插件）。</summary>
public interface IAdminApi
{
    IReadOnlyList<string> Loaded { get; }
    Task<bool> LoadAsync(string name);
    Task<bool> UnloadAsync(string name);
    IReadOnlyList<string> Catalog();
}

// ==================== 组件模型（论文第 4 章）====================

/// <summary>组件（插件）：StartAsync 中产生的效果与依赖由 ctx 追踪，卸载自动回滚。</summary>
public interface IComponent
{
    string Name { get; }
    Task StartAsync(IContext ctx);
}

/// <summary>
/// 统一上下文（论文 3.3 context paradigm）：
/// - Track       → 可撤销效果（时间可组合性）
/// - Provide     → 提供服务（本身是可撤销效果）
/// - Require     → 声明依赖（空间可组合性），服务增/减时回调
/// </summary>
public interface IContext
{
    void Track(Action apply, Action revert);
    void Track(Action apply, Func<Action> revertFactory); // 先执行 apply，再取逆操作

    T? Resolve<T>() where T : class;
    IReadOnlyList<T> ResolveAll<T>() where T : class;

    void Provide<T>(T instance) where T : class;
    void Require<T>(Action<T> onAdd, Action<T>? onRemove = null) where T : class;
}

/// <summary>插件自描述特性：宿主扫描 plugins 目录时按此注册名称与描述。</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class PluginAttribute(string name, string description) : Attribute
{
    public string Name { get; } = name;
    public string Description { get; } = description;
}

/// <summary>
/// 宿主注入接口：经 plugins 目录扫描装配的插件若需要宿主服务，实现此接口，
/// 宿主在 Load 前注入（避免宿主静态引用插件类型，保证 dll 可回收）。
/// </summary>
public interface IHostInjectedPlugin
{
    IAdminApi? AdminApi { get; set; }
    IReadOnlyDictionary<string, string>? Descriptions { get; set; }
    Func<IAdapter?>? AdapterLookup { get; set; }
}

