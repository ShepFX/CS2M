using System.Collections.Concurrent;
using System.Collections.Generic;
using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using Game;
using Game.Areas;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace CS2M.BaseGame.Systems
{
    /// <summary>
    ///     District names: the host sends each district's RandomLocalizationIndex when it changes (checked
    ///     every 2 s), joiners copy it. Custom names (renames) are separate and not handled here.
    /// </summary>
    public partial class DistrictNameSyncSystem : GameSystemBase
    {
        public static readonly ConcurrentQueue<DistrictNamesCommand> Pending = new ConcurrentQueue<DistrictNamesCommand>();

        private const float CheckInterval = 2f;

        private PrefabSystem _prefabSystem;
        private EntityQuery _districts;
        private readonly Dictionary<Entity, int> _sent = new Dictionary<Entity, int>();
        private float _lastCheck;

        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            _districts = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<District>(), ComponentType.ReadOnly<RandomLocalizationIndex>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() }
            });
        }

        protected override void OnUpdate()
        {
            MultiplayerRole role = Command.CurrentRole;
            if (role == MultiplayerRole.None)
            {
                _sent.Clear();
                return;
            }

            if (role == MultiplayerRole.Client)
            {
                while (Pending.TryDequeue(out DistrictNamesCommand command))
                {
                    Apply(command);
                }

                return;
            }

            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now - _lastCheck < CheckInterval)
            {
                return;
            }

            _lastCheck = now;
            var keys = new List<AreaKey>();
            var indices = new List<int>();
            using (NativeArray<Entity> entities = _districts.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    DynamicBuffer<RandomLocalizationIndex> buffer = EntityManager.GetBuffer<RandomLocalizationIndex>(entity, true);
                    if (buffer.Length == 0 || (_sent.TryGetValue(entity, out int sent) && sent == buffer[0].m_Index))
                    {
                        continue;
                    }

                    AreaKey key = AreaRefs.Key(EntityManager, _prefabSystem, entity);
                    if (key == null)
                    {
                        continue;
                    }

                    keys.Add(key);
                    indices.Add(buffer[0].m_Index);
                    _sent[entity] = buffer[0].m_Index;
                }
            }

            if (keys.Count > 0)
            {
                Command.SendToAll?.Invoke(new DistrictNamesCommand { Districts = keys.ToArray(), Indices = indices.ToArray() });
            }
        }

        private void Apply(DistrictNamesCommand command)
        {
            for (int i = 0; i < command.Districts.Length && i < command.Indices.Length; i++)
            {
                Entity district = AreaRefs.Resolve(EntityManager, _prefabSystem, command.Districts[i]);
                if (district == Entity.Null || !EntityManager.HasBuffer<RandomLocalizationIndex>(district))
                {
                    continue;
                }

                DynamicBuffer<RandomLocalizationIndex> buffer = EntityManager.GetBuffer<RandomLocalizationIndex>(district);
                if (buffer.Length == 0)
                {
                    buffer.Add(new RandomLocalizationIndex(command.Indices[i]));
                }
                else
                {
                    buffer[0] = new RandomLocalizationIndex(command.Indices[i]);
                }
            }
        }
    }
}
