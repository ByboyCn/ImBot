using System;
using System.Linq;
using System.Threading.Tasks;
using ImBot.Core;

namespace ImBot.Plugins;

/// <summary>/plugins —— 聊天内热装卸插件（依赖宿主级 IAdminApi）</summary>
[Plugin("admin", "聊天内插件管理命令 /plugins")]
public sealed class AdminPlugin : IComponent
{
    public string Name => "admin";
    public Task StartAsync(IContext ctx)
    {
        ctx.Require<IAdminApi>(admin =>
            ctx.Require<ICommandRegistry>(reg =>
                ctx.Track(
                    () => reg.Register("plugins", "插件管理: /plugins list|load <name>|unload <name>", async c =>
                    {
                        var args = c.Args;
                        if (args.Length == 0 || args[0] == "list")
                        {
                            var loaded = string.Join(", ", admin.Loaded);
                            var catalog = string.Join(", ", admin.Catalog().Where(n => !admin.Loaded.Contains(n)));
                            await c.Adapter.SendTextAsync(c.Message.ConversationId,
                                $"已加载: {loaded}\n可加载: {(catalog.Length > 0 ? catalog : "(无)")}");
                            return;
                        }
                        switch (args)
                        {
                            case ["load", var name]:
                            {
                                var ok = await admin.LoadAsync(name);
                                await c.Adapter.SendTextAsync(c.Message.ConversationId, ok ? $"已加载 {name}" : $"加载失败：{name} 不在目录中");
                                return;
                            }
                            case ["unload", var name]:
                            {
                                var ok = await admin.UnloadAsync(name);
                                await c.Adapter.SendTextAsync(c.Message.ConversationId, ok ? $"已卸载 {name}，其效果已回滚" : $"卸载失败：{name} 未加载");
                                return;
                            }
                            default:
                                await c.Adapter.SendTextAsync(c.Message.ConversationId, "用法: /plugins [list|load <name>|unload <name>]");
                                return;
                        }
                    }),
                    () => reg.Unregister("plugins"))));
        return Task.CompletedTask;
    }
}
