using System;
using System.Threading;
using System.Threading.Tasks;
using ImBot.Core;

namespace ImBot.Plugins;

// ==================== 适配器基类 =====================

/// <summary>
/// 适配器插件模板：连接/断开由派生类实现。
/// Provide<IAdapter> 是可撤销效果：卸载插件 => DisconnectAsync 自动执行，
/// 所有命令插件随之停用（不报错）；重新加载 => 自动恢复。
/// </summary>
public abstract class AdapterComponent : IComponent
{
    public abstract string Name { get; }
    protected abstract Task ConnectAsync(IAdapter adapter, CancellationToken ct);
    protected abstract IAdapter CreateAdapter();

    public async Task StartAsync(IContext ctx)
    {
        var adapter = CreateAdapter();
        var cts = new CancellationTokenSource();

        ctx.Track(
            () => { },
            () =>
            {
                cts.Cancel();   // 停止连接/轮询循环
                Console.WriteLine($"[{Name}] disconnected");
            });

        ctx.Provide<IAdapter>(adapter);   // revert 自动通知依赖者 deactivating
        await ConnectAsync(adapter, cts.Token);
    }
}

// ==================== Console 适配器：stdin 模拟 IM，开箱即用 =====================

public sealed class ConsoleAdapter : IAdapter
{
    public string Platform => "console";
    public bool Connected { get; private set; } = true;
    public event Func<InboundMessage, Task>? OnMessage;

    /// <summary>回复镜像钩子（可选）：UI 控制台用来把机器人回复显示到界面上。</summary>
    public Action<string>? ReplySink { get; set; }

    public Task SendTextAsync(string conversationId, string text)
    {
        Console.WriteLine($"  [bot -> {conversationId}] {text}");
        ReplySink?.Invoke(text);
        return Task.CompletedTask;
    }

    public Task RaiseAsync(string sender, string text)
    {
        if (OnMessage is { } h) return h(new InboundMessage("console", "main", sender, text));
        // 范式上"无人订阅=静默等待"，但对用户太不友好：明确提示缺什么
        Console.WriteLine("  [console] 消息无人处理：router 未加载或未订阅本适配器（:load router / :load *）");
        return Task.CompletedTask;
    }

    public Task RaiseUserMessageAsync(string senderId, string text) => RaiseAsync(senderId, text);
}

[Plugin("adapter-console", "控制台适配器（模拟 IM 平台）")]
public sealed class ConsoleAdapterComponent : AdapterComponent
{
    public override string Name => "adapter-console";

    public ConsoleAdapterComponent() { }   // 扫描装配要求无参构造
    public ConsoleAdapter Adapter { get; private set; } = null!;

    protected override IAdapter CreateAdapter() => Adapter = new ConsoleAdapter();

    protected override Task ConnectAsync(IAdapter adapter, CancellationToken ct)
    {
        // 连接阶段仅完成初始化；stdin 的读取由宿主控制通道负责分发
        //（见 Program.Main：':' 前缀行走宿主管理，其余转发给适配器）
        Console.WriteLine("[adapter-console] online（输入 /help 开始，:load/:unload 走宿主管理通道）");
        return Task.CompletedTask;
    }
}
