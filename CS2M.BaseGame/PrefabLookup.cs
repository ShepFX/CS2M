using System.Collections.Generic;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace CS2M.BaseGame
{
    /// <summary>
    ///     Prefab entities differ between games, so prefabs are sent by type and name.
    ///     (PrefabID also carries a content hash, which isn't needed when both players run the same mods.)
    /// </summary>
    internal static class PrefabLookup
    {
        private static readonly Dictionary<string, Entity> ByName = new Dictionary<string, Entity>();

        public static string Name(PrefabSystem prefabSystem, Entity prefab)
        {
            return prefabSystem.TryGetPrefab(prefab, out PrefabBase prefabBase)
                ? prefabBase.GetType().Name + "/" + prefabBase.name
                : null;
        }

        public static Entity Find(EntityManager entityManager, PrefabSystem prefabSystem, string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return Entity.Null;
            }

            if (ByName.TryGetValue(name, out Entity entity) && entityManager.Exists(entity))
            {
                return entity;
            }

            // Unknown name: rebuild the map (prefabs can load after the first lookup)
            ByName.Clear();
            using (EntityQuery query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PrefabData>()))
            using (NativeArray<Entity> prefabs = query.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity prefab in prefabs)
                {
                    string key = Name(prefabSystem, prefab);
                    if (key != null)
                    {
                        ByName[key] = prefab;
                    }
                }
            }

            return ByName.TryGetValue(name, out entity) ? entity : Entity.Null;
        }
    }
}
