using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Colossal;
using CS2M.API;
using CS2M.API.Commands;
using CS2M.API.Networking;
using CS2M.Commands;
using CS2M.Commands.ApiServer;
using CS2M.Commands.Data.Internal;
using CS2M.Helpers;
using LiteNetLib;
using Unity.Entities;

namespace CS2M.Networking
{
    public class NetworkInterface
    {
        public delegate void OnPlayerConnected(Player player);

        public delegate void OnPlayerDisconnected(Player player);

        public delegate void OnPlayerJoined(Player player);

        public delegate void OnPlayerLeft(Player player);

        private static NetworkInterface _instance;

        public readonly LocalPlayer LocalPlayer = new();

        /// <summary>
        ///     List of all players, which are connected on network level
        /// </summary>
        public List<Player> PlayerListConnected = new();

        /// <summary>
        ///     List of all players, which are connected on game level
        /// </summary>
        public List<Player> PlayerListJoined = new();

        public NetworkInterface()
        {
            PlayerListConnected.Add(LocalPlayer);
            PlayerListJoined.Add(LocalPlayer);
        }

        public static NetworkInterface Instance => _instance ??= new NetworkInterface();

        private const double ResyncCooldown = 15;

        // players waiting for the city; see SendWorld
        private readonly List<RemotePlayer> _worldQueue = new();
        private bool _sendingWorld;
        private readonly Stopwatch _lastResyncRequest = new();

        /// <summary>
        ///     Event is triggered, when a player is connected on the network level
        /// </summary>
        public event OnPlayerConnected PlayerConnectedEvent;

        /// <summary>
        ///     Event is triggered, when a player disconnects on the network level
        /// </summary>
        public event OnPlayerDisconnected PlayerDisconnectedEvent;

        /// <summary>
        ///     Event is triggered, when a player joins on the game level
        /// </summary>
        public event OnPlayerJoined PlayerJoinedEvent;

        /// <summary>
        ///     Event is triggered, when a player leaves on the game level
        /// </summary>
        public event OnPlayerLeft PlayerLeftEvent;

        public void OnUpdate()
        {
            LocalPlayer.OnUpdate();
        }

        public void Connect(ConnectionConfig connectionConfig)
        {
            LocalPlayer.GetServerInfo(connectionConfig);
        }

        public void UpdateLocalPlayerUsername(string username)
        {
            LocalPlayer.UpdateUsername(username);
        }

        public void StartServer(ConnectionConfig connectionConfig)
        {
            LocalPlayer.Playing(connectionConfig);
        }

        public void StopServer()
        {
            LocalPlayer.Inactive();
        }

        public void SendToAll(CommandBase message)
        {
            LocalPlayer.SendToAll(message);
        }

        public void SendToClient(Player player, CommandBase message)
        {
            if (player is RemotePlayer remotePlayer)
            {
                LocalPlayer.SendToClient(remotePlayer.NetPeer, message);
            }
            else
            {
                Log.Warn("Trying to send packet to non-csm player, ignoring.");
            }
        }

        public void SendToServer(CommandBase message)
        {
            LocalPlayer.SendToServer(message);
        }

        public void SendToApiServer(ApiCommandBase message)
        {
            LocalPlayer.SendToApiServer(message);
        }

        public void SendToClients(CommandBase message)
        {
            LocalPlayer.SendToClients(message);
        }

        public RemotePlayer GetPlayerByPeer(NetPeer peer)
        {
            return PlayerListConnected
                .Where(p => p is RemotePlayer)
                .Cast<RemotePlayer>()
                .FirstOrDefault(p => p.NetPeer.Id == peer.Id);
        }

        public bool IsPeerConnected(NetPeer peer)
        {
            return PlayerListConnected
                .Where(p => p is RemotePlayer)
                .Cast<RemotePlayer>()
                .Any(p => p.NetPeer.Id == peer.Id);
        }

        public bool IsPeerJoined(NetPeer peer)
        {
            return PlayerListJoined
                .Where(p => p is RemotePlayer)
                .Cast<RemotePlayer>()
                .Any(p => p.NetPeer.Id == peer.Id);
        }

        public void PlayerConnected(RemotePlayer player)
        {
            Log.Debug($"RemotePlayer '{player.Username}' connected.");
            PlayerListConnected.Add(player);
            PlayerConnectedEvent?.Invoke(player);

            SendWorld(new List<RemotePlayer> { player });
        }

        /// <summary>
        ///     The "Resync" button. The host sends its current city to every connected player again; a joiner
        ///     asks the host for it. Either way the joiner reloads the city through the normal join transfer.
        /// </summary>
        public void Resync()
        {
            if (LocalPlayer.PlayerStatus != PlayerStatus.PLAYING)
            {
                return;
            }

            if (LocalPlayer.PlayerType == PlayerType.SERVER)
            {
                List<RemotePlayer> players = PlayerListConnected.OfType<RemotePlayer>().Where(IsConnected).ToList();
                if (players.Count == 0)
                {
                    Chat.Instance?.PrintGameMessage("Resync: nobody else is connected.");
                    return;
                }

                Log.Info($"Resync: sending the city to {players.Count} player(s) again");
                Chat.Instance?.PrintGameMessage("Resync: sending the city to everyone again.");
                SendWorld(players);
            }
            else if (LocalPlayer.PlayerType == PlayerType.CLIENT)
            {
                // the host saves the whole city for every request; don't let a double click do it twice
                if (_lastResyncRequest.IsRunning && _lastResyncRequest.Elapsed.TotalSeconds < ResyncCooldown)
                {
                    return;
                }

                _lastResyncRequest.Restart();
                Log.Info("Resync: asking the host for its city");
                Chat.Instance?.PrintGameMessage("Resync: asked the host for a fresh copy of the city.");
                SendToServer(new ResyncRequestCommand { Username = LocalPlayer.Username });
            }
        }

        /// <summary>
        ///     Host: a joiner asked for the city again.
        /// </summary>
        public void ResendWorld(string username)
        {
            RemotePlayer player = PlayerListConnected.OfType<RemotePlayer>()
                .FirstOrDefault(p => p.Username == username && IsConnected(p));
            if (player == null)
            {
                Log.Warn($"Resync requested by '{username}', who isn't connected");
                return;
            }

            Log.Info($"Resync: '{username}' asked for the city again");
            Chat.Instance?.PrintGameMessage($"Resync: sending the city to {username} again.");
            SendWorld(new List<RemotePlayer> { player });
        }

        private static bool IsConnected(RemotePlayer player) =>
            player.NetPeer != null && player.NetPeer.ConnectionState == ConnectionState.Connected;

        /// <summary>
        ///     Saves the city once and sends it to the given players. Requests that arrive while a save is
        ///     running wait for it to finish and are sent together afterwards, so two saves never overlap.
        /// </summary>
        private void SendWorld(List<RemotePlayer> players)
        {
            foreach (RemotePlayer player in players)
            {
                if (!_worldQueue.Contains(player))
                {
                    _worldQueue.Add(player);
                }
            }

            if (_sendingWorld)
            {
                return;
            }

            _sendingWorld = true;
            TaskManager.instance.EnqueueTask("LoadMap", async () =>
            {
                try
                {
                    while (_worldQueue.Count > 0)
                    {
                        List<RemotePlayer> batch = _worldQueue.Where(IsConnected).ToList();
                        _worldQueue.Clear();
                        if (batch.Count == 0)
                        {
                            break;
                        }

                        // Get max packet size from MTU discovery (the smallest, as every player gets the same slices)
                        int maxPacketSize = batch.Min(p => p.NetPeer.GetMaxSinglePacketSize(DeliveryMethod.ReliableOrdered));
                        maxPacketSize -= 25; // Maximum packet overhead as computed and tested in `PacketSizeOverhead` unit test

                        SaveLoadHelper saveLoadHelper =
                            World.DefaultGameObjectInjectionWorld.GetOrCreateSystemManaged<SaveLoadHelper>();
                        SlicedPacketStream stream = await saveLoadHelper.SaveGame(maxPacketSize);

                        var watch = new Stopwatch();
                        watch.Start();

                        Log.Debug($"Sending world with size of {stream.Length} bytes to {batch.Count} player(s). Slice size: {maxPacketSize}");
                        foreach (RemotePlayer player in batch)
                        {
                            int remainingBytes = (int)stream.Length;
                            bool newTransfer = true;
                            foreach (byte[] slice in stream.GetSlices())
                            {
                                remainingBytes -= slice.Length;
                                var cmd = new WorldTransferCommand
                                {
                                    WorldSlice = slice,
                                    RemainingBytes = remainingBytes,
                                    NewTransfer = newTransfer,
                                };

                                CommandInternal.Instance.SendToClient(player, cmd);

                                newTransfer = false;
                            }
                        }

                        Log.Debug($"[SaveGame] Save game packaging took {watch.ElapsedMilliseconds}ms");
                    }
                }
                catch (Exception e)
                {
                    Log.Error("Sending the world failed", e);
                }
                finally
                {
                    _sendingWorld = false;
                }
            });
        }
    }
}
