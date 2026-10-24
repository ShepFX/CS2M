using System.Net;
using System.Net.Sockets;
using LiteNetLib;

namespace CS2M.Lobby;

/// <summary>
///     The UDP side: the mod's API messages (unconnected LiteNetLib messages) and NAT introductions
///     (LiteNetLib's NatPunchModule). Everything runs on one thread, see <see cref="Run" />.
/// </summary>
public sealed class UdpService : INatPunchListener
{
    // Port checks connect with a key the game never accepts: a rejection proves the port is open.
    private const string PortCheckKey = "CS2M-LOBBY-PORTCHECK";
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(5);

    private readonly LobbyConfig _config;
    private readonly SessionRegistry _registry;
    private readonly NetManager _net;

    // A second socket on a random port, so a port check is a fresh inbound packet for the host's router
    // rather than a reply on the flow the host opened to us.
    private readonly NetManager _probe;
    private readonly Dictionary<IPEndPoint, List<IPEndPoint>> _portChecks = new();

    private readonly RateLimiter _messageLimit = new(40, TimeSpan.FromSeconds(10));
    private readonly RateLimiter _introduceLimit = new(10, TimeSpan.FromSeconds(10));
    private readonly RateLimiter _portCheckLimit = new(2, TimeSpan.FromSeconds(30));

    public UdpService(LobbyConfig config, SessionRegistry registry)
    {
        _config = config;
        _registry = registry;

        var listener = new EventBasedNetListener();
        listener.ConnectionRequestEvent += request => request.Reject();
        listener.NetworkReceiveUnconnectedEvent += OnUnconnectedMessage;
        listener.NetworkErrorEvent += (endPoint, error) => Log.Warn($"Socket error {error} ({Mask(endPoint)})");
        _net = new NetManager(listener)
        {
            UnconnectedMessagesEnabled = true,
            NatPunchEnabled = true,
            IPv6Enabled = false,
            AutoRecycle = true
        };
        _net.NatPunchModule.Init(this);

        var probeListener = new EventBasedNetListener();
        probeListener.PeerConnectedEvent += peer =>
        {
            // Only happens if the key is ever accepted; still proves the port is reachable
            FinishPortCheck(peer, Protocol.PortCheckState.Reachable);
            peer.Disconnect();
        };
        probeListener.PeerDisconnectedEvent += OnProbeDisconnected;
        _probe = new NetManager(probeListener)
        {
            IPv6Enabled = false,
            AutoRecycle = true,
            DisconnectTimeout = 5000
        };
    }

    public void Run(CancellationToken cancel)
    {
        if (!_net.Start(IPAddress.Any, IPAddress.IPv6Any, _config.UdpPort))
        {
            throw new InvalidOperationException($"Could not bind UDP port {_config.UdpPort}");
        }

        if (!_probe.Start())
        {
            throw new InvalidOperationException("Could not bind the port check socket");
        }

        Log.Info($"UDP listening on {_config.UdpPort}");

        DateTime nextSweep = DateTime.UtcNow + SweepInterval;
        while (!cancel.IsCancellationRequested)
        {
            _net.PollEvents();
            _net.NatPunchModule.PollEvents();
            _probe.PollEvents();

            if (DateTime.UtcNow >= nextSweep)
            {
                nextSweep = DateTime.UtcNow + SweepInterval;
                int expired = _registry.RemoveExpired();
                if (expired > 0)
                {
                    Log.Info($"{expired} session(s) expired, {_registry.Count} live");
                }

                _messageLimit.Cleanup();
                _introduceLimit.Cleanup();
                _portCheckLimit.Cleanup();
            }

            Thread.Sleep(10);
        }

        _probe.Stop();
        _net.Stop();
    }

    private void OnUnconnectedMessage(IPEndPoint remote, NetPacketReader reader, UnconnectedMessageType type)
    {
        if (type != UnconnectedMessageType.BasicMessage || !_messageLimit.Allow(remote.Address))
        {
            return;
        }

        Protocol.Message? message;
        try
        {
            message = Protocol.Decode(reader.GetRemainingBytes());
        }
        catch (Exception e)
        {
            Log.Warn($"Undecodable message from {Mask(remote)}: {e.GetType().Name}");
            return;
        }

        switch (message)
        {
            case Protocol.SessionInfoMessage info:
                Registered(remote, info.Token,
                    _registry.Register(remote, info.Token, info.LocalIp, info.LocalPort, info));
                break;
            case Protocol.ServerRegistrationMessage registration:
                Registered(remote, registration.Token,
                    _registry.Register(remote, registration.Token, registration.LocalIp, registration.LocalPort, null));
                break;
            case Protocol.ServerUnregisterMessage unregister:
                if (_registry.Unregister(remote, unregister.Token))
                {
                    Log.Info($"Session {unregister.Token} closed by host, {_registry.Count} live");
                }

                break;
            case Protocol.PortCheckRequestMessage check:
                StartPortCheck(remote, check.Port);
                break;
            case null:
                Log.Warn($"Unknown message type from {Mask(remote)}");
                break;
        }
    }

    private void Registered(IPEndPoint remote, string? token, RegisterResult result)
    {
        switch (result)
        {
            case RegisterResult.Added:
                Log.Info($"Session {token} registered by {Mask(remote)}, {_registry.Count} live");
                break;
            case RegisterResult.Invalid:
                Log.Warn($"Registration with an invalid token from {Mask(remote)}");
                break;
            case RegisterResult.TokenTaken:
                Log.Warn($"Session {token}: registration from another address ({Mask(remote)}) ignored");
                break;
            case RegisterResult.Full:
                Log.Warn($"Registration from {Mask(remote)} refused: session limit reached");
                break;
        }
    }

    public void OnNatIntroductionRequest(IPEndPoint localEndPoint, IPEndPoint remoteEndPoint, string token)
    {
        if (!_introduceLimit.Allow(remoteEndPoint.Address))
        {
            return;
        }

        (IPEndPoint External, IPEndPoint? Internal, string Token)? host = null;
        if (token.StartsWith("token:", StringComparison.Ordinal))
        {
            string sessionToken = token.Substring(6);
            if (SessionRegistry.IsValidToken(sessionToken))
            {
                host = _registry.FindByToken(sessionToken);
            }
        }
        else if (token.StartsWith("ip:", StringComparison.Ordinal) &&
                 IPEndPoint.TryParse(token.Substring(3), out IPEndPoint? target))
        {
            host = _registry.FindByAddress(target);
        }

        if (host == null)
        {
            Log.Info($"NAT introduction for an unknown session from {Mask(remoteEndPoint)}");
            return;
        }

        // Each side gets the other's LAN and public endpoint and punches both
        IPEndPoint hostInternal = host.Value.Internal ?? host.Value.External;
        IPEndPoint clientInternal = IsUsable(localEndPoint) ? localEndPoint : remoteEndPoint;
        _net.NatPunchModule.NatIntroduce(hostInternal, host.Value.External, clientInternal, remoteEndPoint,
            host.Value.Token);
        Log.Info($"Introduced {Mask(remoteEndPoint)} to session {host.Value.Token}");
    }

    public void OnNatIntroductionSuccess(IPEndPoint targetEndPoint, NatAddressType type, string token)
    {
        // Only the two players punch; this server never does
    }

    private void StartPortCheck(IPEndPoint requester, int port)
    {
        if (port is <= 0 or > 65535)
        {
            Reply(requester, Protocol.PortCheckState.Error, "Invalid port");
            return;
        }

        if (!_portCheckLimit.Allow(requester.Address))
        {
            return;
        }

        var target = new IPEndPoint(requester.Address, port);
        if (_portChecks.TryGetValue(target, out List<IPEndPoint>? waiting))
        {
            if (!waiting.Contains(requester))
            {
                waiting.Add(requester);
            }

            return;
        }

        _portChecks[target] = new List<IPEndPoint> { requester };
        _probe.Connect(target, PortCheckKey);
    }

    private void OnProbeDisconnected(NetPeer peer, DisconnectInfo info)
    {
        Protocol.PortCheckState state = info.Reason switch
        {
            DisconnectReason.ConnectionRejected or DisconnectReason.RemoteConnectionClose
                or DisconnectReason.InvalidProtocol or DisconnectReason.DisconnectPeerCalled
                => Protocol.PortCheckState.Reachable,
            DisconnectReason.ConnectionFailed or DisconnectReason.Timeout or DisconnectReason.HostUnreachable
                or DisconnectReason.NetworkUnreachable
                => Protocol.PortCheckState.Unreachable,
            _ => Protocol.PortCheckState.Error
        };
        FinishPortCheck(peer, state, state == Protocol.PortCheckState.Error ? info.Reason.ToString() : null);
    }

    private void FinishPortCheck(NetPeer peer, Protocol.PortCheckState state, string? message = null)
    {
        var target = new IPEndPoint(peer.Address, peer.Port);
        if (!_portChecks.Remove(target, out List<IPEndPoint>? requesters))
        {
            return;
        }

        foreach (IPEndPoint requester in requesters)
        {
            Reply(requester, state, message);
        }

        Log.Info($"Port check {Mask(target)}: {state}");
    }

    private void Reply(IPEndPoint requester, Protocol.PortCheckState state, string? message)
    {
        _net.SendUnconnectedMessage(Protocol.EncodePortCheckResult(state, message), requester);
    }

    private static bool IsUsable(IPEndPoint? endPoint)
    {
        return endPoint != null && endPoint.Port > 0 && endPoint.AddressFamily == AddressFamily.InterNetwork &&
               !endPoint.Address.Equals(IPAddress.Any);
    }

    /// <summary>Players' addresses stay out of the logs unless LOBBY_LOG_IPS is set.</summary>
    private string Mask(IPEndPoint? endPoint)
    {
        if (endPoint == null)
        {
            return "?";
        }

        if (_config.LogIps)
        {
            return endPoint.ToString();
        }

        byte[] bytes = endPoint.Address.MapToIPv4().GetAddressBytes();
        return $"{bytes[0]}.{bytes[1]}.x.x";
    }

    /// <summary>A fixed window counter per address.</summary>
    private sealed class RateLimiter
    {
        private readonly int _limit;
        private readonly TimeSpan _window;
        private readonly Dictionary<IPAddress, (DateTime Start, int Count)> _counters = new();

        public RateLimiter(int limit, TimeSpan window)
        {
            _limit = limit;
            _window = window;
        }

        public bool Allow(IPAddress address)
        {
            DateTime now = DateTime.UtcNow;
            if (!_counters.TryGetValue(address, out (DateTime Start, int Count) entry) || now - entry.Start > _window)
            {
                entry = (now, 0);
            }

            entry.Count++;
            _counters[address] = entry;
            return entry.Count <= _limit;
        }

        public void Cleanup()
        {
            DateTime now = DateTime.UtcNow;
            foreach (IPAddress address in _counters.Where(kv => now - kv.Value.Start > _window).Select(kv => kv.Key)
                         .ToList())
            {
                _counters.Remove(address);
            }
        }
    }
}
