using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AirFoxBT.Models;

namespace AirFoxBT.Services;

/// <summary>
/// Lightweight, zero-dependency embedded HTTP telemetry server.
/// Exposes real-time JSON metrics and Server-Sent Events (SSE) for Home Assistant,
/// browser dashboards, and local scripts at http://localhost:8766.
/// Zero admin rights or UAC prompts required.
/// </summary>
public class LocalApiService : IDisposable
{
    private HttpListener? _listener;
    private readonly Func<IEnumerable<StoredSensorConfig>> _getSensorsFunc;
    private readonly Func<long> _getPacketCountFunc;
    private readonly ConcurrentDictionary<Guid, StreamWriter> _sseClients = new();
    private CancellationTokenSource? _cts;
    private readonly DateTime _startTime = DateTime.UtcNow;
    private bool _isRunning;

    public const int DefaultPort = 8766;
    public int Port { get; }

    public event Action<string>? OnLog;

    public LocalApiService(Func<IEnumerable<StoredSensorConfig>> getSensorsFunc, Func<long> getPacketCountFunc, int port = DefaultPort)
    {
        _getSensorsFunc = getSensorsFunc;
        _getPacketCountFunc = getPacketCountFunc;
        Port = port;
    }

    public void Start(string? targetIp = null)
    {
        if (_isRunning) return;

        try
        {
            _cts = new CancellationTokenSource();
            _listener = new HttpListener();

            bool wildcardSucceeded = false;
            try
            {
                _listener.Prefixes.Add($"http://*:{Port}/");
                _listener.Start();
                wildcardSucceeded = true;
            }
            catch
            {
                _listener.Close();
                _listener = new HttpListener();
            }

            if (!wildcardSucceeded)
            {
                _listener.Prefixes.Add($"http://localhost:{Port}/");
                _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");

                if (!string.IsNullOrEmpty(targetIp) && targetIp != "Auto" && targetIp != "0.0.0.0")
                {
                    try
                    {
                        _listener.Prefixes.Add($"http://{targetIp}:{Port}/");
                    }
                    catch { }
                }

                _listener.Start();
            }

            _isRunning = true;
            _ = AcceptLoopAsync(_cts.Token);
            OnLog?.Invoke($"[API] 🌐 REST & SSE service listening on port {Port}");
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"[API] ⚠️ Failed to bind HTTP listener on port {Port}: {ex.Message}");
        }
    }

    public void RestartWithIp(string targetIp)
    {
        Stop();
        Start(targetIp);
    }

    public void Stop()
    {
        if (!_isRunning) return;
        _isRunning = false;

        try
        {
            _cts?.Cancel();
            foreach (var client in _sseClients.Values)
            {
                try { client.Dispose(); } catch { }
            }
            _sseClients.Clear();

            _listener?.Stop();
            _listener?.Close();
            _listener = null;
        }
        catch { }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener != null && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequestAsync(context), ct);
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    OnLog?.Invoke($"[API] Error in accept loop: {ex.Message}");
                }
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;

        res.Headers.Add("Access-Control-Allow-Origin", "*");
        res.Headers.Add("Access-Control-Allow-Methods", "GET, OPTIONS");
        res.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

        if (req.HttpMethod == "OPTIONS")
        {
            res.StatusCode = 204;
            res.Close();
            return;
        }

        try
        {
            string path = req.Url?.AbsolutePath.ToLowerInvariant() ?? "/";

            switch (path)
            {
                case "/":
                case "/api/v1/summary":
                    await HandleSummaryAsync(res);
                    break;

                case "/api/v1/sensors":
                    await HandleSensorsAsync(res);
                    break;

                case "/api/v1/events":
                    await HandleSseEventsAsync(ctx);
                    break;

                default:
                    res.StatusCode = 404;
                    await WriteJsonAsync(res, new { error = "Not found", path });
                    break;
            }
        }
        catch (Exception ex)
        {
            try
            {
                res.StatusCode = 500;
                await WriteJsonAsync(res, new { error = ex.Message });
            }
            catch { }
        }
        finally
        {
            if (req.Url?.AbsolutePath.ToLowerInvariant() != "/api/v1/events")
            {
                try { res.Close(); } catch { }
            }
        }
    }

    private async Task HandleSummaryAsync(HttpListenerResponse res)
    {
        var sensors = _getSensorsFunc();
        var uptime = DateTime.UtcNow - _startTime;

        var data = new
        {
            service = "AirFoxBT",
            version = "1.0.0",
            status = "online",
            uptime_seconds = (long)uptime.TotalSeconds,
            total_packets_ingested = _getPacketCountFunc(),
            registered_sensors = sensors
        };

        await WriteJsonAsync(res, data);
    }

    private async Task HandleSensorsAsync(HttpListenerResponse res)
    {
        var sensors = _getSensorsFunc();
        await WriteJsonAsync(res, sensors);
    }

    private async Task HandleSseEventsAsync(HttpListenerContext ctx)
    {
        var res = ctx.Response;
        res.ContentType = "text/event-stream";
        res.Headers.Add("Cache-Control", "no-cache");
        res.Headers.Add("Connection", "keep-alive");

        var writer = new StreamWriter(res.OutputStream, Encoding.UTF8);
        var clientId = Guid.NewGuid();
        _sseClients.TryAdd(clientId, writer);

        try
        {
            string connectPayload = JsonSerializer.Serialize(new { @event = "connected", clientId = clientId.ToString() });
            await writer.WriteAsync($"data: {connectPayload}\n\n");
            await writer.FlushAsync();

            while (_isRunning && !_cts!.IsCancellationRequested)
            {
                await Task.Delay(15000, _cts.Token);
                await writer.WriteAsync(": keepalive\n\n");
                await writer.FlushAsync();
            }
        }
        catch
        {
            // Client disconnected
        }
        finally
        {
            _sseClients.TryRemove(clientId, out _);
            try { writer.Dispose(); } catch { }
            try { res.Close(); } catch { }
        }
    }

    public void BroadcastReading(BleReading reading)
    {
        BroadcastTelemetry(reading);
    }

    public void BroadcastTelemetry(object payload)
    {
        if (_sseClients.IsEmpty) return;

        string json = JsonSerializer.Serialize(payload);
        string sseMessage = $"data: {json}\n\n";
        byte[] bytes = Encoding.UTF8.GetBytes(sseMessage);

        foreach (var pair in _sseClients)
        {
            try
            {
                pair.Value.Write(sseMessage);
                pair.Value.Flush();
            }
            catch
            {
                _sseClients.TryRemove(pair.Key, out _);
            }
        }
    }

    private static async Task WriteJsonAsync(HttpListenerResponse res, object data)
    {
        res.ContentType = "application/json";
        string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        byte[] buffer = Encoding.UTF8.GetBytes(json);
        res.ContentLength64 = buffer.Length;
        await res.OutputStream.WriteAsync(buffer);
    }

    public void Dispose()
    {
        Stop();
    }
}
