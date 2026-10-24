using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CS2M.Lobby;

/// <summary>
///     Read-only JSON API for the in-game server browser: GET /sessions and GET /health.
///     Plain HTTP; TLS is done by nginx and Cloudflare in front of it.
/// </summary>
public sealed class HttpApi
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private readonly LobbyConfig _config;
    private readonly SessionRegistry _registry;
    private readonly HttpListener _listener = new();
    private readonly DateTime _started = DateTime.UtcNow;

    public HttpApi(LobbyConfig config, SessionRegistry registry)
    {
        _config = config;
        _registry = registry;
        _listener.Prefixes.Add($"http://{config.HttpBind}:{config.HttpPort}/");
    }

    public async Task Run(CancellationToken cancel)
    {
        _listener.Start();
        Log.Info($"HTTP listening on {_config.HttpPort}");
        await using CancellationTokenRegistration stopOnCancel = cancel.Register(() => _listener.Stop());

        while (!cancel.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception) when (cancel.IsCancellationRequested)
            {
                break;
            }
            catch (HttpListenerException e)
            {
                Log.Warn($"HTTP listener: {e.Message}");
                continue;
            }

            _ = Task.Run(() => Handle(context), CancellationToken.None);
        }
    }

    private void Handle(HttpListenerContext context)
    {
        HttpListenerResponse response = context.Response;
        try
        {
            HttpListenerRequest request = context.Request;
            string path = request.Url?.AbsolutePath.TrimEnd('/') ?? "";
            if (request.HttpMethod != "GET" && request.HttpMethod != "HEAD")
            {
                Write(response, 405, new { error = "method not allowed" }, "no-store");
            }
            else if (path == "/sessions")
            {
                Write(response, 200, _registry.Snapshot(), "public, max-age=5");
            }
            else if (path == "/health")
            {
                Write(response, 200, new
                {
                    status = "ok",
                    sessions = _registry.Count,
                    uptimeSeconds = (long)(DateTime.UtcNow - _started).TotalSeconds
                }, "no-store");
            }
            else
            {
                Write(response, 404, new { error = "not found" }, "no-store");
            }
        }
        catch (Exception e)
        {
            Log.Warn($"HTTP request failed: {e.GetType().Name}");
            try
            {
                response.Abort();
            }
            catch
            {
                // already gone
            }
        }
    }

    private static void Write(HttpListenerResponse response, int status, object body, string cacheControl)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(body, Json);
        response.StatusCode = status;
        response.ContentType = "application/json; charset=utf-8";
        response.Headers["Cache-Control"] = cacheControl;
        response.Headers["Access-Control-Allow-Origin"] = "*";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes);
        response.Close();
    }
}
