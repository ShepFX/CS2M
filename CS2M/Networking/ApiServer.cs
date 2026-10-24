using CS2M.Commands.ApiServer;
using CS2M.Util;
using LiteNetLib;
using System;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using Game.City;
using Unity.Entities;

namespace CS2M.Networking
{
    public class ApiServer
    {
        // Often enough to keep the host's router mapping to the API server open, so NAT introductions reach it
        private const int KeepAliveSeconds = 10;

        // Crockford base32 (no I, L, O or U), so a token can be read out and typed
        private const string TokenAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        private const int TokenLength = 10;

        private readonly NetManager _netManager;
        private readonly Stopwatch _sinceKeepAlive = new Stopwatch();
        private ConnectionConfig _connectionConfig;
        private IPEndPoint _endpoint;

        /// <summary>
        ///     The token joiners use to find this server, while it's registered.
        /// </summary>
        public string Token { get; private set; }

        public ApiServer(NetManager netManager)
        {
            _netManager = netManager;
        }

        /// <summary>
        ///     Start registering the hosted server: a keep-alive now and then every few seconds until <see cref="Stop" />.
        /// </summary>
        public void Start(ConnectionConfig connectionConfig)
        {
            _connectionConfig = connectionConfig;
            Token = GenerateToken();
            _endpoint = null;
            Log.Info($"Registering the server with the API server as {Token}");
            SendKeepAlive();
            SendCommand(new PortCheckRequestCommand { Port = connectionConfig.Port });
        }

        /// <summary>
        ///     Stop registering and remove the server from the list.
        /// </summary>
        public void Stop()
        {
            if (Token == null)
            {
                return;
            }

            SendCommand(new ServerUnregisterCommand { Token = Token });
            Token = null;
            _connectionConfig = null;
            _sinceKeepAlive.Reset();
        }

        /// <summary>
        ///     Whether a message came from the API server, so nobody else can send us API commands.
        /// </summary>
        public bool IsApiServer(IPEndPoint endPoint)
        {
            return _endpoint != null && _endpoint.Equals(endPoint);
        }

        /// <summary>
        ///     Send a message to the API server
        /// </summary>
        /// <param name="message"></param>
        public void SendCommand(ApiCommandBase message)
        {
            try
            {
                // Resolved once per server start, as the lookup blocks the game
                _endpoint ??= IPUtil.CreateIP4EndPoint(Mod.Instance.Settings.ApiServer,
                    Mod.Instance.Settings.GetApiServerPort());
                _netManager.SendUnconnectedMessage(ApiCommand.Instance.Serialize(message), _endpoint);
                Log.Debug(
                    $"Sending {message.GetType().Name} to API server at {Mod.Instance.Settings.ApiServer}:{Mod.Instance.Settings.ApiServerPort}");
            }
            catch (Exception e)
            {
                Log.Warn(
                    $"Could not send message to API server at {Mod.Instance.Settings.ApiServer}:{Mod.Instance.Settings.ApiServerPort}: {e}");
            }
        }

        public void KeepAlive()
        {
            if (Token != null && _sinceKeepAlive.Elapsed.TotalSeconds >= KeepAliveSeconds)
            {
                SendKeepAlive();
            }
        }

        private void SendKeepAlive()
        {
            _sinceKeepAlive.Restart();

            string localIp = NetUtils.GetLocalIp(LocalAddrType.IPv4);
            if (string.IsNullOrEmpty(localIp))
            {
                localIp = NetUtils.GetLocalIp(LocalAddrType.IPv6);
            }

            CityConfigurationSystem city =
                World.DefaultGameObjectInjectionWorld?.GetExistingSystemManaged<CityConfigurationSystem>();

            SendCommand(new SessionInfoCommand
            {
                Token = Token,
                LocalIp = localIp,
                LocalPort = _connectionConfig.Port,
                CityName = city?.cityName ?? "",
                HostName = NetworkInterface.Instance.LocalPlayer.Username ?? "",
                PlayerCount = NetworkInterface.Instance.PlayerListConnected.Count,
                MaxPlayers = 0, // no player limit yet
                HasPassword = !string.IsNullOrEmpty(_connectionConfig.Password),
                GameVersion = VersionUtil.GetGameVersion().ToString(),
                ModVersion = VersionUtil.GetModVersion().ToString(),
            });
        }

        private static string GenerateToken()
        {
            byte[] bytes = new byte[TokenLength];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            char[] token = new char[TokenLength];
            for (int i = 0; i < TokenLength; i++)
            {
                // 256 is a multiple of 32, so this stays uniform
                token[i] = TokenAlphabet[bytes[i] % TokenAlphabet.Length];
            }

            return new string(token);
        }
    }
}
