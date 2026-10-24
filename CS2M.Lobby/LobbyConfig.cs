namespace CS2M.Lobby;

/// <summary>Settings from environment variables; see the README.</summary>
public sealed class LobbyConfig
{
    public int UdpPort { get; init; } = 4242;
    public int HttpPort { get; init; } = 8080;
    public string HttpBind { get; init; } = "+";
    public TimeSpan SessionTtl { get; init; } = TimeSpan.FromSeconds(60);
    public int MaxSessions { get; init; } = 1000;
    public int MaxSessionsPerIp { get; init; } = 5;
    public bool LogIps { get; init; }

    public static LobbyConfig FromEnvironment()
    {
        return new LobbyConfig
        {
            UdpPort = Int("LOBBY_UDP_PORT", 4242, 1, 65535),
            HttpPort = Int("LOBBY_HTTP_PORT", 8080, 1, 65535),
            HttpBind = Environment.GetEnvironmentVariable("LOBBY_HTTP_BIND") is { Length: > 0 } bind ? bind : "+",
            SessionTtl = TimeSpan.FromSeconds(Int("LOBBY_SESSION_TTL", 60, 10, 3600)),
            MaxSessions = Int("LOBBY_MAX_SESSIONS", 1000, 1, 100000),
            MaxSessionsPerIp = Int("LOBBY_MAX_SESSIONS_PER_IP", 5, 1, 1000),
            LogIps = Environment.GetEnvironmentVariable("LOBBY_LOG_IPS") is "1" or "true"
        };
    }

    private static int Int(string name, int fallback, int min, int max)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrEmpty(value))
        {
            return fallback;
        }

        if (int.TryParse(value, out int result) && result >= min && result <= max)
        {
            return result;
        }

        throw new ArgumentException($"{name} must be a number from {min} to {max}");
    }
}
