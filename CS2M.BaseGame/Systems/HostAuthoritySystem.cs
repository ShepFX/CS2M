using System.Collections.Concurrent;
using System.Collections.Generic;
using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using Game;
using Game.Buildings;
using Game.City;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace CS2M.BaseGame.Systems
{
    /// <summary>
    ///     The host decides what the city's simulation does to buildings and money; joiners copy it.
    ///     On joiners the zone spawner and the upkeep system (level-ups, abandonment) are switched off.
    /// </summary>
    public partial class HostAuthoritySystem : GameSystemBase
    {
        public static readonly ConcurrentQueue<CommandBase> Pending = new ConcurrentQueue<CommandBase>();

        private const float MoneyInterval = 1f;
        private const int WatchFrames = 30;

        private ZoneSpawnSystem _zoneSpawnSystem;
        private BuildingUpkeepSystem _buildingUpkeepSystem;
        private CitySystem _citySystem;
        private PrefabSystem _prefabSystem;
        private EntityArchetype _spawnArchetype;

        private bool _disabledHere;
        private float _lastMoneySend;
        private int _frame;

        private EntityQuery _levelUps;
        private EntityQuery _abandoned;
        private readonly HashSet<Entity> _sentLevelUps = new HashSet<Entity>();
        private readonly HashSet<Entity> _sentAbandoned = new HashSet<Entity>();

        protected override void OnCreate()
        {
            base.OnCreate();
            _zoneSpawnSystem = World.GetOrCreateSystemManaged<ZoneSpawnSystem>();
            _buildingUpkeepSystem = World.GetOrCreateSystemManaged<BuildingUpkeepSystem>();
            _citySystem = World.GetOrCreateSystemManaged<CitySystem>();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            // same archetype as ZoneSpawnSystem's definitions: created Deleted, built this frame
            _spawnArchetype = EntityManager.CreateArchetype(
                ComponentType.ReadWrite<CreationDefinition>(),
                ComponentType.ReadWrite<ObjectDefinition>(),
                ComponentType.ReadWrite<Updated>(),
                ComponentType.ReadWrite<Deleted>());
            _levelUps = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Building>(), ComponentType.ReadOnly<UnderConstruction>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() }
            });
            _abandoned = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Building>(), ComponentType.ReadOnly<Abandoned>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() }
            });
        }

        protected override void OnUpdate()
        {
            MultiplayerRole role = Command.CurrentRole;
            SetSimulationDecisions(role != MultiplayerRole.Client);
            if (role == MultiplayerRole.None)
            {
                _sentLevelUps.Clear();
                _sentAbandoned.Clear();
                return;
            }

            if (role == MultiplayerRole.Server)
            {
                SendMoney();
                if (++_frame % WatchFrames == 0)
                {
                    WatchBuildings();
                }
            }

            while (Pending.TryDequeue(out CommandBase command))
            {
                switch (command)
                {
                    case SpawnCommand spawn when role == MultiplayerRole.Client:
                        ApplySpawn(spawn);
                        break;
                    case LevelUpCommand levelUp when role == MultiplayerRole.Client:
                        ApplyLevelUp(levelUp);
                        break;
                    case AbandonCommand abandon when role == MultiplayerRole.Client:
                        ApplyAbandon(abandon);
                        break;
                    case MoneyCommand money when role == MultiplayerRole.Client:
                        SetMoney(money.Money);
                        break;
                    case ChargeCommand charge when role == MultiplayerRole.Server:
                        SetMoney(GetMoney() - charge.Amount);
                        break;
                }
            }
        }

        private void SetSimulationDecisions(bool enabled)
        {
            if (!enabled && !_disabledHere)
            {
                _zoneSpawnSystem.Enabled = false;
                _buildingUpkeepSystem.Enabled = false;
                _disabledHere = true;
                BaseGameLog.Info("Joined as a client: building spawns and upkeep now come from the host");
            }
            else if (enabled && _disabledHere)
            {
                _zoneSpawnSystem.Enabled = true;
                _buildingUpkeepSystem.Enabled = true;
                _disabledHere = false;
            }
        }

        // ---------- money ----------

        private int GetMoney()
        {
            Entity city = _citySystem.City;
            return city != Entity.Null && EntityManager.HasComponent<PlayerMoney>(city)
                ? EntityManager.GetComponentData<PlayerMoney>(city).money
                : 0;
        }

        private void SetMoney(int amount)
        {
            Entity city = _citySystem.City;
            if (city == Entity.Null || !EntityManager.HasComponent<PlayerMoney>(city))
            {
                return;
            }

            PlayerMoney money = EntityManager.GetComponentData<PlayerMoney>(city);
            int difference = amount - money.money;
            if (difference > 0)
            {
                money.Add(difference);
            }
            else if (difference < 0)
            {
                money.Subtract(-difference);
            }

            EntityManager.SetComponentData(city, money);
        }

        private void SendMoney()
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now - _lastMoneySend < MoneyInterval)
            {
                return;
            }

            _lastMoneySend = now;
            Command.SendToAll?.Invoke(new MoneyCommand { Money = GetMoney() });
        }

        // ---------- level-ups and abandonment (host) ----------

        private void WatchBuildings()
        {
            using (NativeArray<Entity> entities = _levelUps.ToEntityArray(Allocator.Temp))
            {
                var current = new HashSet<Entity>();
                foreach (Entity entity in entities)
                {
                    UnderConstruction construction = EntityManager.GetComponentData<UnderConstruction>(entity);
                    if (construction.m_NewPrefab == Entity.Null)
                    {
                        continue;
                    }

                    current.Add(entity);
                    if (_sentLevelUps.Contains(entity))
                    {
                        continue;
                    }

                    NetRef building = NetRefs.Describe(EntityManager, entity, _prefabSystem);
                    string newPrefab = PrefabLookup.Name(_prefabSystem, construction.m_NewPrefab);
                    if (building.Kind == 3 && newPrefab != null)
                    {
                        Command.SendToAll?.Invoke(new LevelUpCommand
                        {
                            Building = building, NewPrefab = newPrefab,
                            Progress = construction.m_Progress, Speed = construction.m_Speed
                        });
                        BaseGameLog.Info($"Level-up sent: {building.Prefab} -> {newPrefab}");
                    }

                    _sentLevelUps.Add(entity);
                }

                _sentLevelUps.IntersectWith(current);
            }

            using (NativeArray<Entity> entities = _abandoned.ToEntityArray(Allocator.Temp))
            {
                var current = new HashSet<Entity>(entities.ToArray());
                foreach (Entity entity in entities)
                {
                    if (_sentAbandoned.Contains(entity))
                    {
                        continue;
                    }

                    NetRef building = NetRefs.Describe(EntityManager, entity, _prefabSystem);
                    if (building.Kind == 3)
                    {
                        Command.SendToAll?.Invoke(new AbandonCommand
                        {
                            Building = building,
                            AbandonmentTime = EntityManager.GetComponentData<Abandoned>(entity).m_AbandonmentTime
                        });
                    }

                    _sentAbandoned.Add(entity);
                }

                _sentAbandoned.IntersectWith(current);
            }
        }

        // ---------- applying the host's decisions (joiners) ----------

        private void ApplySpawn(SpawnCommand spawn)
        {
            Entity prefab = PrefabLookup.Find(EntityManager, _prefabSystem, spawn.Prefab);
            if (prefab == Entity.Null)
            {
                BaseGameLog.Warn($"Spawn received with an unknown prefab {spawn.Prefab}");
                return;
            }

            NetRefs.Resolve(EntityManager, spawn.Attached, out Entity attached, _prefabSystem);
            var definition = new CreationDefinition
            {
                m_Prefab = prefab,
                m_Attached = attached,
                m_Flags = (CreationFlags)spawn.CreationFlags,
                m_RandomSeed = spawn.RandomSeed
            };
            var objectDefinition = new ObjectDefinition
            {
                m_Position = NetRefs.V3(spawn.Position),
                m_LocalPosition = NetRefs.V3(spawn.LocalPosition),
                m_Scale = NetRefs.V3(spawn.Scale),
                m_Rotation = Q(spawn.Rotation),
                m_LocalRotation = Q(spawn.LocalRotation),
                m_Elevation = spawn.Elevation,
                m_Intensity = spawn.Intensity,
                m_Age = spawn.Age,
                m_IsDecoration = spawn.IsDecoration,
                m_ParentMesh = spawn.ParentMesh,
                m_GroupIndex = spawn.GroupIndex,
                m_Probability = spawn.Probability,
                m_PrefabSubIndex = spawn.PrefabSubIndex
            };

            var buffer = new EntityCommandBuffer(Allocator.Temp);
            Entity entity = buffer.CreateEntity(_spawnArchetype);
            buffer.SetComponent(entity, definition);
            buffer.SetComponent(entity, objectDefinition);
            buffer.Playback(EntityManager);
            buffer.Dispose();
        }

        private void ApplyLevelUp(LevelUpCommand levelUp)
        {
            Entity newPrefab = PrefabLookup.Find(EntityManager, _prefabSystem, levelUp.NewPrefab);
            if (newPrefab == Entity.Null ||
                !NetRefs.Resolve(EntityManager, levelUp.Building, out Entity building, _prefabSystem) ||
                building == Entity.Null)
            {
                BaseGameLog.Warn($"Level-up for a building that isn't here ({levelUp.Building?.Prefab})");
                return;
            }

            var construction = new UnderConstruction
            {
                m_NewPrefab = newPrefab, m_Progress = levelUp.Progress, m_Speed = levelUp.Speed
            };
            if (EntityManager.HasComponent<UnderConstruction>(building))
            {
                EntityManager.SetComponentData(building, construction);
            }
            else
            {
                EntityManager.AddComponentData(building, construction);
            }
        }

        private void ApplyAbandon(AbandonCommand abandon)
        {
            if (!NetRefs.Resolve(EntityManager, abandon.Building, out Entity building, _prefabSystem) ||
                building == Entity.Null || EntityManager.HasComponent<Abandoned>(building))
            {
                return;
            }

            EntityManager.AddComponentData(building, new Abandoned { m_AbandonmentTime = abandon.AbandonmentTime });
            if (!EntityManager.HasComponent<Updated>(building))
            {
                EntityManager.AddComponent<Updated>(building);
            }
        }

        private static quaternion Q(float[] q) =>
            q != null && q.Length >= 4 ? new quaternion(q[0], q[1], q[2], q[3]) : quaternion.identity;
    }
}
