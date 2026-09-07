using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ImBot.Core;

namespace ImBot.Plugins;

// ==================== Router：提供 ICommandRegistry，桥接 IAdapter =====================

[Plugin("router", "命令路由：桥接适配器与命令")]
public sealed class RouterComponent : IComponent
{
    public const string Prefix = "/";
    public string Name => "router";

    public Task StartAsync(IContext ctx)
    {
        var reg = new CommandRegistry();
        ctx.Provide<ICommandRegistry>(reg);

        // 空格分隔的命令路由表
        // 依赖所有适配器：每个适配器上线 => 挂订阅（生命周期绑定该适配器实例）；
        // 适配器下线 => 摘订阅。router 自己卸载 => Provide 撤销 => 所有命令插件停用。
        ctx.Require<IAdapter>(
            onAdd: adapter =>
            {
                Console.WriteLine($"[router] adapter '{adapter.Platform}' online");
                Func<InboundMessage, Task> h = m => Handle(m, adapter, reg);
                adapter.OnMessage += h;
                _subs[(adapter.Platform, adapter)] = h;
            },
            onRemove: adapter =>
            {
                if (_subs.Remove((adapter.Platform, adapter), out var h))
                {
                    adapter.OnMessage -= h;
                    Console.WriteLine($"[router] adapter '{adapter.Platform}' offline");
                }
            });
        return Task.CompletedTask;
    }

    private readonly Dictionary<(string, IAdapter), Func<InboundMessage, Task>> _subs = new();

    private static async Task Handle(InboundMessage m, IAdapter a, CommandRegistry reg)
    {
        if (!m.Text.StartsWith(Prefix, StringComparison.Ordinal)) return;
        var parts = m.Text[Prefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;
        var cmd = parts[0].ToLowerInvariant();
        if (!reg.Commands.ContainsKey(cmd))
        {
            await a.SendTextAsync(m.ConversationId, $"未知命令 /{cmd}，试试 /help");
            return;
        }
        try { await reg.Dispatch(cmd, new CommandContext(a, m, parts[1..])); }
        catch (Exception ex) { await a.SendTextAsync(m.ConversationId, $"命令执行出错: {ex.Message}"); }
    }
}

internal sealed class CommandRegistry : ICommandRegistry
{
    private readonly ConcurrentDictionary<string, (string help, Func<CommandContext, Task> h)> _cmds = new();

    public IReadOnlyDictionary<string, string> Commands =>
        _cmds.ToDictionary(kv => kv.Key, kv => kv.Value.help);

    public void Register(string name, string help, Func<CommandContext, Task> h) => _cmds[name] = (help, h);
    public void Unregister(string name) => _cmds.TryRemove(name, out _);

    public Task Dispatch(string name, CommandContext ctx)
        => _cmds.TryGetValue(name, out var e) ? e.h(ctx) : Task.CompletedTask;
}
