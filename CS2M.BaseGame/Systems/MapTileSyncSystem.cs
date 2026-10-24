using System.Collections.Concurrent;
using System.Collections.Generic;
using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using Game;
using Game.Areas;
using Game.City;
using Game.Common;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using AreaNode = Game.Areas.Node;

namespace CS2M.BaseGame.Systems
{
    /// <summary>
    ///     Owned map tiles, decided by the host. A tile is owned when its MapTile entity has no Native tag.
    ///     The host sends the owned set when it changes (and every few seconds); joiners unlock what the host
    ///     owns. A joiner's purchase still happens locally (it's the game's own button), then goes to the host,
    ///     which checks permits and money, charges the city and unlocks; if the host refuses, the joiner's tiles
    ///     are locked again by the next owned set. Tiles are identified by their centre.
    /// </summary>
    public partial class MapTileSyncSystem : GameSystemBase
    {
        public static readonly ConcurrentQueue<CommandBase> Pending = new ConcurrentQueue<CommandBase>();

        private const float CheckInterval = 0.5f;
        private const float HostRepeat = 10f;
        private const float WarmUp = 3f;
        // how long a joiner keeps a tile it bought before the host's answer can lock it again
        private const float RequestGrace = 5f;
        private const float MatchDistance = 50f;
        // new tiles count as a purchase only this soon after the purchase panel had a selection
        private const float SelectionWindow = 2f;

        private MapTilePurchaseSystem _purchaseSystem;
        private CitySystem _citySystem;
        private EntityQuery _tiles;

        private MultiplayerRole _role = MultiplayerRole.None;
        private float _roleSince;
        private float _lastCheck;
        private float _lastHostSend;
        private float _graceUntil;
        private int _lastCost;
        private float _selectionSeenAt = -1f;
        private HashSet<long> _known;

        protected override void OnCreate()
        {
            base.OnCreate();
            _purchaseSystem = World.GetOrCreateSystemManaged<MapTilePurchaseSystem>();
            _citySystem = World.GetOrCreateSystemManaged<CitySystem>();
            _tiles = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<MapTile>(), ComponentType.ReadOnly<Area>(), ComponentType.ReadOnly<AreaNode>()
                },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() }
            });
        }

        protected override void OnUpdate()
        {
            MultiplayerRole role = Command.CurrentRole;
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (role != _role)
            {
                _role = role;
                _roleSince = now;
                _known = null;
                _selectionSeenAt = -1f;
                _lastHostSend = 0f;
                _graceUntil = 0f;
            }

            if (role == MultiplayerRole.None || now - _roleSince < WarmUp)
            {
                return;
            }

            if (role == MultiplayerRole.Client)
            {
                // The purchase cost isn't kept after buying, so remember it while tiles are selected. It's
                // computed by the map tile panel for the current selection, which doesn't change on the click.
                if (_purchaseSystem.selecting && _purchaseSystem.GetSelectedTileCount() > 0)
                {
                    _lastCost = _purchaseSystem.cost;
                    _selectionSeenAt = now;
                }
            }

            bool check = now - _lastCheck >= CheckInterval;
            if (check)
            {
                _lastCheck = now;
                if (role == MultiplayerRole.Client)
                {
                    SendLocalPurchases(now);
                }
            }

            while (Pending.TryDequeue(out CommandBase command))
            {
                switch (command)
                {
                    case MapTilesCommand tiles when role == MultiplayerRole.Client:
                        ApplyOwned(tiles.Owned, now);
                        break;
                    case TilePurchaseRequestCommand request when role == MultiplayerRole.Server:
                        ApplyPurchase(request);
                        break;
                }
            }

            if (role == MultiplayerRole.Server && check)
            {
                SendOwned(now);
            }
        }

        // ---------- host ----------

        private void SendOwned(float now)
        {
            Dictionary<Entity, float2> tiles = Centres();
            var owned = new HashSet<long>();
            var centres = new List<float>();
            foreach (KeyValuePair<Entity, float2> tile in tiles)
            {
                if (!EntityManager.HasComponent<Native>(tile.Key))
                {
                    owned.Add(Key(tile.Value));
                    centres.Add(tile.Value.x);
                    centres.Add(tile.Value.y);
                }
            }

            bool changed = _known == null || !owned.SetEquals(_known);
            if (!changed && now - _lastHostSend < HostRepeat)
            {
                return;
            }

            _known = owned;
            _lastHostSend = now;
            Command.SendToAll?.Invoke(new MapTilesCommand { Owned = centres.ToArray() });
            if (changed)
            {
                BaseGameLog.Info($"Map tiles sent: {owned.Count} owned");
            }
        }

        private void ApplyPurchase(TilePurchaseRequestCommand request)
        {
            Dictionary<Entity, float2> tiles = Centres();
            var locked = new List<Entity>();
            foreach (float2 centre in Pairs(request.Tiles))
            {
                Entity tile = Match(tiles, centre);
                if (tile != Entity.Null && EntityManager.HasComponent<Native>(tile) && !locked.Contains(tile))
                {
                    locked.Add(tile);
                }
            }

            if (locked.Count == 0)
            {
                BaseGameLog.Info("Map tile request received for tiles already owned");
                return;
            }

            // the same checks the purchase button makes here: expansion permits and money
            Entity city = _citySystem.City;
            if (locked.Count > _purchaseSystem.GetAvailableTiles())
            {
                BaseGameLog.Warn($"Map tile request refused: {locked.Count} tiles, not enough permits");
                return;
            }

            if (city != Entity.Null && EntityManager.HasComponent<PlayerMoney>(city))
            {
                PlayerMoney money = EntityManager.GetComponentData<PlayerMoney>(city);
                if (!money.m_Unlimited && money.money < request.Cost)
                {
                    BaseGameLog.Warn($"Map tile request refused: costs {request.Cost}, the city has {money.money}");
                    return;
                }

                money.Subtract(request.Cost);
                EntityManager.SetComponentData(city, money);
            }

            foreach (Entity tile in locked)
            {
                MapTilePurchaseSystem.UnlockTile(EntityManager, tile);
            }

            BaseGameLog.Info($"Map tile request received: {locked.Count} tiles for {request.Cost}");
        }

        // ---------- joiners ----------

        private void SendLocalPurchases(float now)
        {
            HashSet<long> owned = OwnedKeys(out Dictionary<long, float2> centres);
            if (_known != null)
            {
                var bought = new List<float>();
                foreach (long key in owned)
                {
                    if (!_known.Contains(key))
                    {
                        bought.Add(centres[key].x);
                        bought.Add(centres[key].y);
                    }
                }

                // the click clears the selection, so look back a little; a cancelled selection long ago doesn't count
                bool fromPanel = _selectionSeenAt >= 0f && now - _selectionSeenAt <= SelectionWindow;
                if (bought.Count > 0 && !fromPanel)
                {
                    // not from the purchase panel (e.g. a tutorial unlocking tiles); the host's set decides
                    BaseGameLog.Warn($"Map tiles unlocked here without a purchase ({bought.Count / 2}); keeping the host's");
                }
                else if (bought.Count > 0)
                {
                    _selectionSeenAt = -1f;
                    Command.SendToServer?.Invoke(new TilePurchaseRequestCommand { Tiles = bought.ToArray(), Cost = _lastCost });
                    _graceUntil = now + RequestGrace;
                    BaseGameLog.Info($"Map tile purchase sent: {bought.Count / 2} tiles for {_lastCost}");
                }
            }

            _known = owned;
        }

        private void ApplyOwned(float[] owned, float now)
        {
            // a purchase made here since the last check must reach the host before the host's set is applied
            SendLocalPurchases(now);

            Dictionary<Entity, float2> tiles = Centres();
            var hostOwns = new HashSet<Entity>();
            foreach (float2 centre in Pairs(owned))
            {
                Entity tile = Match(tiles, centre);
                if (tile != Entity.Null)
                {
                    hostOwns.Add(tile);
                }
            }

            int unlocked = 0, relocked = 0;
            bool keepOwn = now < _graceUntil;
            foreach (KeyValuePair<Entity, float2> tile in tiles)
            {
                bool ownedHere = !EntityManager.HasComponent<Native>(tile.Key);
                if (hostOwns.Contains(tile.Key) && !ownedHere)
                {
                    MapTilePurchaseSystem.UnlockTile(EntityManager, tile.Key);
                    unlocked++;
                }
                else if (!hostOwns.Contains(tile.Key) && ownedHere && !keepOwn)
                {
                    // the host refused (or never got) this purchase
                    EntityManager.AddComponent<Native>(tile.Key);
                    if (!EntityManager.HasComponent<Updated>(tile.Key))
                    {
                        EntityManager.AddComponent<Updated>(tile.Key);
                    }

                    relocked++;
                }
            }

            if (unlocked > 0 || relocked > 0)
            {
                BaseGameLog.Info($"Map tiles received: {unlocked} unlocked, {relocked} locked again");
            }

            _known = OwnedKeys(out _);
        }

        // ---------- shared ----------

        private Dictionary<Entity, float2> Centres()
        {
            var centres = new Dictionary<Entity, float2>();
            using (NativeArray<Entity> entities = _tiles.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    DynamicBuffer<AreaNode> nodes = EntityManager.GetBuffer<AreaNode>(entity, true);
                    if (nodes.Length == 0)
                    {
                        continue;
                    }

                    float2 sum = float2.zero;
                    foreach (AreaNode node in nodes)
                    {
                        sum += node.m_Position.xz;
                    }

                    centres[entity] = sum / nodes.Length;
                }
            }

            return centres;
        }

        private HashSet<long> OwnedKeys(out Dictionary<long, float2> centres)
        {
            var owned = new HashSet<long>();
            centres = new Dictionary<long, float2>();
            foreach (KeyValuePair<Entity, float2> tile in Centres())
            {
                if (!EntityManager.HasComponent<Native>(tile.Key))
                {
                    long key = Key(tile.Value);
                    owned.Add(key);
                    centres[key] = tile.Value;
                }
            }

            return owned;
        }

        // Both games load the same tiles from the same save, so the centres match; the nearest one within
        // MatchDistance is taken in case of float noise.
        private static Entity Match(Dictionary<Entity, float2> tiles, float2 centre)
        {
            Entity best = Entity.Null;
            float bestDistance = MatchDistance * MatchDistance;
            foreach (KeyValuePair<Entity, float2> tile in tiles)
            {
                float distance = math.distancesq(tile.Value, centre);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = tile.Key;
                }
            }

            return best;
        }

        private static long Key(float2 centre) =>
            ((long)(int)math.round(centre.x) << 32) | (uint)(int)math.round(centre.y);

        private static IEnumerable<float2> Pairs(float[] values)
        {
            if (values == null)
            {
                yield break;
            }

            for (int i = 0; i + 1 < values.Length; i += 2)
            {
                yield return new float2(values[i], values[i + 1]);
            }
        }
    }
}
