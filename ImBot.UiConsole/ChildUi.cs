using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TCYM.UI.Core;

namespace ImBot.UI;

/// <summary>
/// UI 子进程（--ui 启动）：通过 stdin/stdout 与宿主通信的后端实现 + TCYM.UI 主循环。
/// </summary>
public static class ChildUi
{
    private sealed record UiStateDto(IReadOnlyList<string> Loaded, IReadOnlyList<string> Catalog,
        IReadOnlyDictionary<string, string> Desc, IReadOnlyList<string> Log);

    private sealed class RemoteBackend : IUiBackend
    {
        private readonly object _lock = new();
        private UiState _state = UiState.Empty;
        private string? _pendingStateHash;

        public UiState State { get { lock (_lock) return _state; } }
        public event Action? StateChanged;

        /// <summary>宿主推来新状态（读线程调用）。hash 变化时投递到 UI 线程回调。</summary>
        public void Apply(UiStateDto dto)
        {
            var state = new UiState(dto.Loaded ?? [], dto.Catalog ?? [], dto.Desc ?? new Dictionary<string, string>(), dto.Log ?? []);
            string hash = $"{string.Join(',', state.Loaded)}|{state.Log.Count}|{state.Log.LastOrDefault()}";
            bool changed;
            lock (_lock)
            {
                changed = hash != _pendingStateHash;
                if (changed) { _state = state; _pendingStateHash = hash; }
            }
            if (changed) UIAppHost.Current?.Post(() => StateChanged?.Invoke());
        }

        public void Load(string name) => WriteOp(new("load", name, null));
        public void Unload(string name) => WriteOp(new("unload", name, null));
        public void Delete(string name) => WriteOp(new("delete", name, null));
        public void Send(string text) => WriteOp(new("send", null, text));

        private sealed record UiOp(string Op, string? Name, string? Text);
        private static void WriteOp(UiOp op) => Console.Out.WriteLine(JsonSerializer.Serialize(op));
    }

    public static void Run()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var backend = new RemoteBackend();

        // 读宿主推送的状态（宿主的 stdout -> 本进程 stdin）
        _ = Task.Run(async () =>
        {
            while (true)
            {
                var line = await Console.In.ReadLineAsync();
                if (line == null) { Environment.Exit(0); return; }   // 宿主管道断开（被卸载）
                UiStateDto? dto = null;
                try { dto = JsonSerializer.Deserialize<UiStateDto>(line); } catch { }
                if (dto != null) backend.Apply(dto);
            }
        });

        // 轮询：驱动宿主回状态（同时也是保活心跳）
        _ = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(300);
                try { Console.Out.WriteLine("{\"op\":\"poll\"}"); Console.Out.Flush(); }
                catch { Environment.Exit(0); return; }
            }
        });

        // UI 主循环（本线程）
        var app = new UiApp(backend);
        app.Run();
        Environment.Exit(0);
    }
}
