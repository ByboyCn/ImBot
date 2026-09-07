using System;
using System.Linq;
using System.Threading.Tasks;
using ImBot.Core;

namespace ImBot.Plugins;

/// <summary>/help —— 列出当前注册的命令（命令随插件装卸动态变化）</summary>
[Plugin("cmd-help", "/help 命令：列出可用命令")]
public sealed class HelpPlugin : IComponent
{
    public string Name => "cmd-help";
    public Task StartAsync(IContext ctx)
    {
        ctx.Require<ICommandRegistry>(reg =>
            ctx.Track(
                () => reg.Register("help", "列出可用命令", async c =>
                {
                    var cmds = reg.Commands.OrderBy(kv => kv.Key);
                    var lines = cmds.Select(kv => $"/{kv.Key,-10} {kv.Value}");
                    await c.Adapter.SendTextAsync(c.Message.ConversationId, string.Join('\n', lines));
                }),
                () => reg.Unregister("help")),
            onRemove: _ => { });
        return Task.CompletedTask;
    }
}
