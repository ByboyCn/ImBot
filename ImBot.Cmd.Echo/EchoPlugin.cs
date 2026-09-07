using System;
using System.Threading.Tasks;
using ImBot.Core;

namespace ImBot.Plugins;

/// <summary>/echo —— 复读</summary>
[Plugin("cmd-echo", "/echo 命令：复读")]
public sealed class EchoPlugin : IComponent
{
    public string Name => "cmd-echo";
    public Task StartAsync(IContext ctx)
    {
        ctx.Require<ICommandRegistry>(reg =>
            ctx.Track(
                () => reg.Register("echo", "复读: /echo <text>", c =>
                    c.Adapter.SendTextAsync(c.Message.ConversationId,
                        c.Args.Length > 0 ? string.Join(' ', c.Args) : c.Message.Text)),
                () => reg.Unregister("echo")));
        return Task.CompletedTask;
    }
}
