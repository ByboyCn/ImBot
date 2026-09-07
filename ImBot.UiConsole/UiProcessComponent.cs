using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ImBot.Core;
using ImBot.Plugins;

namespace ImBot.UI;

/// <summary>
/// UI 控制台插件（宿主侧）：启动一个独立的 UI 子进程，经 stdin/stdout JSON 行协议通信。
/// 卸载（revert）= 终止子进程 —— 进程级时间可组合性：UI 卡死/崩溃/卸载都不影响宿主。
/// </summary>
[Plugin("ui-console", "TCYM.UI 桌面控制台（独立进程，卸载=关闭窗口）")]
public sealed class UiProcessComponent : IComponent, IHostInjectedPlugin
{
    private sealed record UiOp(string Op, string? Name, string? Text);
    private sealed record UiStateDto(IReadOnlyList<string> Loaded, IReadOnlyList<string> Catalog,
        IReadOnlyDictionary<string, string> Desc, IReadOnlyList<string> Log);

    private BotAdminApi? _api;
    private Func<IAdapter?>? _adapterLookup;
    private IReadOnlyDictionary<string, string>? _descriptions;

    // ---- IHostInjectedPlugin：宿主扫描装配时注入（无静态引用，dll 可回收）----
    IAdminApi? IHostInjectedPlugin.AdminApi { get => _api; set => _api = value as BotAdminApi; }
    IReadOnlyDictionary<string, string>? IHostInjectedPlugin.Descriptions { get => _descriptions; set => _descriptions = value; }
    Func<IAdapter?>? IHostInjectedPlugin.AdapterLookup { get => _adapterLookup; set => _adapterLookup = value; }

    private readonly object _logLock = new();
    private readonly List<string> _log = new();
    private volatile bool _selfUnloaded;

    public string Name => "ui-console";

    internal void AppendLog(string line)
    {
        lock (_logLock)
        {
            _log.Add(line);
            if (_log.Count > 200) _log.RemoveAt(0);
        }
    }

    public Task StartAsync(IContext ctx)
    {
        if (_api == null)
            throw new InvalidOperationException("ui-console 缺少宿主注入（Api/Descriptions/LiveAdapterLookup）");

        var exe = Environment.ProcessPath!;
        var psi = new ProcessStartInfo(exe, "--ui")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = false,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        var proc = Process.Start(psi)!;

        // 看门狗：UI 窗口被用户点 X 关闭（子进程自行退出）时，自动卸载本组件，
        // 消除"窗口已关但宿主仍显示已加载"的僵尸状态；之后 :load 可重新拉起
        _ = Task.Run(async () =>
        {
            try { proc.WaitForExit(); } catch { return; }
            if (_selfUnloaded) return;   // 正常卸载路径（revert 杀进程）已处理，勿重复
            Console.WriteLine("[ui-console] ui process exited (window closed), auto-unloading...");
            try { await _api!.UnloadAsync(Name); } catch (Exception ex) { Console.WriteLine($"[ui-console] auto-unload 失败: {ex.Message}"); }
        });

        // 子进程消息循环：执行 op，回写状态
        _ = Task.Run(async () =>
        {
            try
            {
                while (!proc.HasExited)
                {
                    var line = await proc.StandardOutput.ReadLineAsync();
                    if (line == null) break;
                    UiOp? op = null;
                    try { op = JsonSerializer.Deserialize<UiOp>(line); } catch { }
                    if (op == null) continue;
                    switch (op.Op)
                    {
                        case "load" when op.Name != null:
                            AppendLog($"[ui] load {op.Name}");
                            await _api.LoadAsync(op.Name);
                            break;
                        case "unload" when op.Name != null:
                            AppendLog($"[ui] unload {op.Name}");
                            await _api.UnloadAsync(op.Name);
                            break;
                        case "delete" when op.Name != null:
                            AppendLog($"[ui] delete {op.Name}");
                            var delOk = await _api.DeleteAsync(op.Name);
                            AppendLog(delOk ? $"[ui] {op.Name} 已删除（含 dll 文件）" : $"[ui] 删除 {op.Name} 失败");
                            break;
                        case "send" when op.Text != null:
                            AppendLog($"> 我: {op.Text}");
                            var adapter = _adapterLookup?.Invoke();
                            if (adapter == null)
                            {
                                AppendLog("< 系统: 无 console 适配器在线，消息丢弃");
                            }
                            else
                            {
                                if (adapter is ConsoleAdapter ca) ca.ReplySink = s => AppendLog($"< bot: {s}");
                                await adapter.RaiseUserMessageAsync("me", op.Text);
                            }
                            break;
                        case "poll": break;
                    }
                    // 回状态（子进程自己被卸载时这里会随进程一起消失，无需善后）
                    var state = new UiStateDto(_api.Loaded, _api.Catalog(), _descriptions ?? new Dictionary<string, string>(), SnapshotLog());
                    await proc.StandardInput.WriteLineAsync(JsonSerializer.Serialize(state));
                    await proc.StandardInput.FlushAsync();
                }
            }
            catch (ObjectDisposedException) { }   // 宿主卸载本插件时管道关闭
            catch (IOException) { }
        });

        // 卸载 = 杀子进程（时间可组合性）
        ctx.Track(
            () => { },
            () =>
            {
                Console.WriteLine("[ui-console] terminating ui process...");
                _selfUnloaded = true;   // 告诉看门狗：这是正常卸载，不要重复处理
                try
                {
                    if (!proc.HasExited) { proc.Kill(entireProcessTree: true); proc.WaitForExit(3000); }
                }
                catch { /* 已退出 */ }
                Console.WriteLine("[ui-console] ui process terminated, plugin unloaded");
            });
        return Task.CompletedTask;
    }

    private IReadOnlyList<string> SnapshotLog()
    {
        lock (_logLock) return _log.TakeLast(100).ToList();
    }
}
