using System;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ImBot.Plugins;

/// <summary>HTTP 服务器：POST / {"action","params"} 与 POST /{action} 两种调用方式。</summary>
public sealed class OneBotHttpServer(OneBotHub hub, int port)
{
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;

    public Task RunAsync()
    {
        _cts = new CancellationTokenSource();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        Console.WriteLine($"[adapter-onebot] HTTP  : http://127.0.0.1:{port}/");
        _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
        return Task.CompletedTask;
    }

    public void Stop()
    {
        try { _cts?.Cancel(); _listener?.Stop(); _listener?.Close(); } catch { }
        Console.WriteLine("[adapter-onebot] HTTP 已停止");
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener!.GetContextAsync(); }
            catch (Exception) { break; }   // listener stopped
            _ = Task.Run(() => HandleAsync(ctx));
        }
    }

    private async Task HandleAsync(HttpListenerContext http)
    {
        try
        {
            // WS 升级交给 WS 服务器（不同端口时不会到这里；同端口时转发）
            if (http.Request.IsWebSocketRequest)
            {
                http.Response.StatusCode = 400;
                http.Response.Close();
                return;
            }

            string? action = null;
            JsonElement? parameters = null;
            string? echo = null;

            // 路径式：POST /send_message
            var pathAction = http.Request.Url?.AbsolutePath.Trim('/');
            if (!string.IsNullOrEmpty(pathAction)
                && !string.Equals(pathAction, "favicon.ico", StringComparison.OrdinalIgnoreCase))
                action = pathAction;

            if (http.Request.HttpMethod == "POST")
            {
                using var body = await JsonDocument.ParseAsync(http.Request.InputStream);
                var root = body.RootElement;
                if (root.TryGetProperty("action", out var a)) action = a.GetString();
                if (root.TryGetProperty("params", out var p)) parameters = p.Clone();
                if (root.TryGetProperty("echo", out var e)) echo = e.GetString();
            }
            if (action == null)
            {
                await WriteJson(http, 400, """{"status":"failed","retcode":10001,"message":"missing action"}""");
                return;
            }

            var resp = await hub.InvokeAsync(action, parameters);
            var payload = new
            {
                status = resp.Status,
                retcode = resp.Retcode,
                data = resp.Data,
                message = resp.Message,
                echo,
            };
            await WriteJson(http, 200, JsonSerializer.Serialize(payload));
        }
        catch (Exception ex)
        {
            try { await WriteJson(http, 500, JsonSerializer.Serialize(new { status = "failed", retcode = 10001, message = ex.Message })); }
            catch { }
        }
    }

    private static async Task WriteJson(HttpListenerContext http, int code, string json)
    {
        http.Response.StatusCode = code;
        http.Response.ContentType = "application/json";
        var bytes = Encoding.UTF8.GetBytes(json);
        await http.Response.OutputStream.WriteAsync(bytes);
        http.Response.Close();
    }
}

/// <summary>WS 服务器：事件推送 + action 调用（双向）。</summary>
public sealed class OneBotWsServer(OneBotHub hub, int port)
{
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;

    public Task RunAsync()
    {
        _cts = new CancellationTokenSource();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/ws/");
        _listener.Start();
        Console.WriteLine($"[adapter-onebot] WS    : ws://127.0.0.1:{port}/ws");
        _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
        return Task.CompletedTask;
    }

    public void Stop()
    {
        try { _cts?.Cancel(); _listener?.Stop(); _listener?.Close(); } catch { }
        Console.WriteLine("[adapter-onebot] WS 已停止");
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener!.GetContextAsync(); }
            catch { break; }
            if (!ctx.Request.IsWebSocketRequest) { ctx.Response.StatusCode = 400; ctx.Response.Close(); continue; }
            _ = Task.Run(async () =>
            {
                try
                {
                    var ws = (await ctx.AcceptWebSocketAsync(null)).WebSocket;
                    var client = new WsClient(hub, ws);
                    hub.Register(client);
                    Console.WriteLine("[adapter-onebot] WS 客户端接入");
                    await client.PumpAsync();
                }
                catch { /* 客户端断开 */ }
            });
        }
    }
}

/// <summary>单个 WS 客户端连接：读 action 帧 -> 调 hub -> 回包；可被推送事件。</summary>
public sealed class WsClient(OneBotHub hub, System.Net.WebSockets.WebSocket ws)
{
    public Guid Id { get; } = Guid.NewGuid();
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public async Task PumpAsync()
    {
        var buffer = new byte[64 * 1024];
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                string? action = null, echo = null;
                JsonElement? parameters = null;
                try
                {
                    using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(ms.ToArray()));
                    var root = doc.RootElement;
                    if (root.TryGetProperty("action", out var a)) action = a.GetString();
                    if (root.TryGetProperty("params", out var p)) parameters = p.Clone();
                    if (root.TryGetProperty("echo", out var e)) echo = e.GetString();
                }
                catch { }

                if (action != null)
                {
                    var resp = await hub.InvokeAsync(action, parameters);
                    await SendRawAsync(JsonSerializer.Serialize(new
                    {
                        status = resp.Status,
                        retcode = resp.Retcode,
                        data = resp.Data,
                        message = resp.Message,
                        echo,
                    }));
                }
            }
        }
        finally
        {
            hub.Unregister(this);
            Console.WriteLine("[adapter-onebot] WS 客户端断开");
        }
    }

    public async Task SendAsync(string json)
    {
        if (ws.State != WebSocketState.Open) return;
        try
        {
            await _sendLock.WaitAsync();
            await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)),
                WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch { /* 推送失败忽略，客户端下次读时断开 */ }
        finally { _sendLock.Release(); }
    }

    private Task SendRawAsync(string json) => SendAsync(json);
}
