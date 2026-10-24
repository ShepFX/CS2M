using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace CS2M.Lobby;

public sealed class Session
{
    public required string Token { get; init; }

    /// <summary>The host's game socket as this server sees it. Never leaves the server except in a NAT introduction.</summary>
    public required IPEndPoint External { get; set; }

    /// <summary>The host's LAN address, for joiners behind the same router.</summary>
    public IPEndPoint? Internal { get; set; }

    public bool HasDetails { get; set; }
    public string? CityName { get; set; }
    public string? HostName { get; set; }
    public int PlayerCount { get; set; }
    public int MaxPlayers { get; set; }
    public bool HasPassword { get; set; }
    public string? GameVersion { get; set; }
    public string? ModVersion { get; set; }
    public DateTime Created { get; init; }
    public DateTime LastSeen { get; set; }

    /// <summary>From SessionDetailsCommand; null until the host sends one (older mods never do).</summary>
    public SessionDetailsView? Details { get; set; }
}

public sealed record PlayerView(string Name, string? Color);

public sealed record ModView(string Id, string? Name, string? Version, bool ClientSide);

/// <summary>
///     The extra details a host sends. Month is 0-11. ModCount can be larger than Mods when the host's list was cut
///     short to fit a packet; it lists required mods first.
/// </summary>
public sealed record SessionDetailsView(
    int Population, int Milestone, int Year, int Month, IReadOnlyList<PlayerView> PlayerList,
    IReadOnlyList<ModView> Mods, int ModCount, DateTime Updated);

/// <summary>Public view of a session for the HTTP API. Has no addresses on purpose.</summary>
public sealed record SessionView(
    string Token, string? Name, string? Host, int Players, int MaxPlayers, bool HasPassword,
    string? GameVersion, string? ModVersion, DateTime LastSeen, SessionDetailsView? Details);

public enum RegisterResult
{
    Added,
    Refreshed,
    Invalid,
    TokenTaken,
    Full
}

/// <summary>Live sessions by token. Thread safe: the UDP loop writes, the HTTP API reads.</summary>
public sealed partial class SessionRegistry
{
    private readonly LobbyConfig _config;
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public SessionRegistry(LobbyConfig config)
    {
        _config = config;
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _sessions.Count;
            }
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{4,64}$")]
    private static partial Regex TokenPattern();

    public static bool IsValidToken(string? token)
    {
        return token != null && TokenPattern().IsMatch(token);
    }

    public RegisterResult Register(IPEndPoint external, string? token, string? localIp, int localPort,
        Protocol.SessionInfoMessage? info)
    {
        if (!IsValidToken(token))
        {
            return RegisterResult.Invalid;
        }

        DateTime now = DateTime.UtcNow;
        lock (_lock)
        {
            RegisterResult result;
            if (_sessions.TryGetValue(token!, out Session? session) && !IsExpired(session, now))
            {
                // A token belongs to the host that registered it. The port may change if the host's router
                // remaps it, the address may not: otherwise anyone could take over a listed session.
                if (!session.External.Address.Equals(external.Address))
                {
                    return RegisterResult.TokenTaken;
                }

                session.External = external;
                result = RegisterResult.Refreshed;
            }
            else
            {
                if (_sessions.Count >= _config.MaxSessions ||
                    _sessions.Values.Count(s => s.External.Address.Equals(external.Address) && !IsExpired(s, now)) >=
                    _config.MaxSessionsPerIp)
                {
                    return RegisterResult.Full;
                }

                session = new Session { Token = token!, External = external, Created = now };
                _sessions[token!] = session;
                result = RegisterResult.Added;
            }

            session.LastSeen = now;
            session.Internal = ParseInternal(localIp, localPort);
            if (info != null)
            {
                session.HasDetails = true;
                session.CityName = Clean(info.CityName, 64);
                session.HostName = Clean(info.HostName, 32);
                session.PlayerCount = Math.Clamp(info.PlayerCount, 0, 10000);
                session.MaxPlayers = Math.Clamp(info.MaxPlayers, 0, 10000);
                session.HasPassword = info.HasPassword;
                session.GameVersion = Clean(info.GameVersion, 32);
                session.ModVersion = Clean(info.ModVersion, 32);
            }

            return result;
        }
    }

    /// <summary>
    ///     Stores a host's SessionDetails. Only for a live session, from the address that registered it.
    /// </summary>
    public bool UpdateDetails(IPEndPoint external, Protocol.SessionDetailsMessage details)
    {
        if (!IsValidToken(details.Token))
        {
            return false;
        }

        DateTime now = DateTime.UtcNow;
        lock (_lock)
        {
            if (!_sessions.TryGetValue(details.Token!, out Session? session) || IsExpired(session, now) ||
                !session.External.Address.Equals(external.Address))
            {
                return false;
            }

            List<ModView> mods = details.Mods
                .Select(m => new ModView(Clean(m.Id, 48) ?? "?", Clean(m.Name, 40), Clean(m.Version, 20),
                    m.ClientSide))
                .ToList();
            if (details.ModOffset > 0)
            {
                // The rest of a long mod list: only right after the part before it, so pages never mix
                if (session.Details == null || session.Details.Mods.Count != details.ModOffset)
                {
                    return false;
                }

                session.Details = session.Details with
                {
                    Mods = session.Details.Mods.Concat(mods).Take(Protocol.MaxListedMods).ToList(),
                    Updated = now
                };
                return true;
            }

            session.Details = new SessionDetailsView(
                Math.Clamp(details.Population, 0, 100_000_000),
                Math.Clamp(details.Milestone, 0, 1000),
                Math.Clamp(details.Year, 0, 100_000),
                Math.Clamp(details.Month, 0, 11),
                details.Players.Take(Protocol.MaxListedPlayers)
                    .Select(p => new PlayerView(Clean(p.Name, 32) ?? "?", CleanColor(p.Color)))
                    .ToList(),
                mods.Take(Protocol.MaxListedMods).ToList(),
                Math.Clamp(details.ModCount, 0, 10000),
                now);
            return true;
        }
    }

    public bool Unregister(IPEndPoint external, string? token)
    {
        if (!IsValidToken(token))
        {
            return false;
        }

        lock (_lock)
        {
            if (_sessions.TryGetValue(token!, out Session? session) && session.External.Address.Equals(external.Address))
            {
                return _sessions.Remove(token!);
            }

            return false;
        }
    }

    /// <summary>The host's endpoints for a NAT introduction, or null.</summary>
    public (IPEndPoint External, IPEndPoint? Internal, string Token)? FindByToken(string token)
    {
        lock (_lock)
        {
            if (_sessions.TryGetValue(token, out Session? s) && !IsExpired(s, DateTime.UtcNow))
            {
                return (s.External, s.Internal, s.Token);
            }

            return null;
        }
    }

    /// <summary>For "ip:" introductions: the host at that public address (and port, if given).</summary>
    public (IPEndPoint External, IPEndPoint? Internal, string Token)? FindByAddress(IPEndPoint target)
    {
        DateTime now = DateTime.UtcNow;
        lock (_lock)
        {
            Session? match = _sessions.Values
                .Where(s => !IsExpired(s, now) && s.External.Address.Equals(target.Address))
                .Where(s => target.Port == 0 || s.External.Port == target.Port || s.Internal?.Port == target.Port)
                .OrderByDescending(s => s.LastSeen)
                .FirstOrDefault();
            return match == null ? null : (match.External, match.Internal, match.Token);
        }
    }

    public int RemoveExpired()
    {
        DateTime now = DateTime.UtcNow;
        lock (_lock)
        {
            List<string> expired = _sessions.Values.Where(s => IsExpired(s, now)).Select(s => s.Token).ToList();
            foreach (string token in expired)
            {
                _sessions.Remove(token);
            }

            return expired.Count;
        }
    }

    public List<SessionView> Snapshot()
    {
        DateTime now = DateTime.UtcNow;
        lock (_lock)
        {
            return _sessions.Values
                .Where(s => !IsExpired(s, now))
                .OrderByDescending(s => s.PlayerCount)
                .ThenBy(s => s.CityName ?? "￿", StringComparer.OrdinalIgnoreCase)
                .Select(s => new SessionView(s.Token, s.CityName, s.HostName, s.PlayerCount, s.MaxPlayers,
                    s.HasPassword, s.GameVersion, s.ModVersion, s.LastSeen, s.Details))
                .ToList();
        }
    }

    private bool IsExpired(Session session, DateTime now)
    {
        return now - session.LastSeen > _config.SessionTtl;
    }

    private static IPEndPoint? ParseInternal(string? localIp, int localPort)
    {
        if (localPort is <= 0 or > 65535 || !IPAddress.TryParse(localIp, out IPAddress? address))
        {
            return null;
        }

        return new IPEndPoint(address, localPort);
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex ColorPattern();

    private static string? CleanColor(string? color)
    {
        return color != null && ColorPattern().IsMatch(color) ? color.ToUpperInvariant() : null;
    }

    /// <summary>Trims, drops control characters and caps the length of a name shown to other players.</summary>
    private static string? Clean(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var builder = new StringBuilder(Math.Min(value.Length, maxLength));
        foreach (char c in value.Trim())
        {
            if (!char.IsControl(c))
            {
                builder.Append(c);
            }

            if (builder.Length >= maxLength)
            {
                break;
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }
}
