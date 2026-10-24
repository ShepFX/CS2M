using CS2M.BaseGame.Commands;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace CS2M.BaseGame
{
    /// <summary>
    ///     Converts between road entities and NetRef (position-based) descriptions.
    /// </summary>
    internal static class NetRefs
    {
        // Positions are the same on both games, but allow for float noise
        private const float MatchDistanceSq = 0.25f;

        public static float[] F(float3 v) => new[] { v.x, v.y, v.z };

        public static float3 V3(float[] a) => a != null && a.Length >= 3 ? new float3(a[0], a[1], a[2]) : float3.zero;

        public static NetRef Describe(EntityManager em, Entity entity, PrefabSystem prefabSystem = null)
        {
            if (entity == Entity.Null || !em.Exists(entity))
            {
                return new NetRef { Kind = 0 };
            }

            if (em.HasComponent<Node>(entity))
            {
                return new NetRef { Kind = 1, A = F(em.GetComponentData<Node>(entity).m_Position) };
            }

            if (em.HasComponent<Edge>(entity) && em.HasComponent<Curve>(entity))
            {
                Curve curve = em.GetComponentData<Curve>(entity);
                return new NetRef { Kind = 2, A = F(curve.m_Bezier.a), B = F(curve.m_Bezier.d) };
            }

            if (prefabSystem != null && em.HasComponent<Game.Objects.Transform>(entity) && em.HasComponent<PrefabRef>(entity))
            {
                return new NetRef
                {
                    Kind = 3,
                    A = F(em.GetComponentData<Game.Objects.Transform>(entity).m_Position),
                    Prefab = PrefabLookup.Name(prefabSystem, em.GetComponentData<PrefabRef>(entity).m_Prefab)
                };
            }

            return new NetRef { Kind = 0 };
        }

        /// <summary>
        ///     Finds the local entity for a NetRef. Returns false if it should exist but wasn't found.
        /// </summary>
        public static bool Resolve(EntityManager em, NetRef netRef, out Entity entity, PrefabSystem prefabSystem = null)
        {
            entity = Entity.Null;
            if (netRef == null || netRef.Kind == 0)
            {
                return true;
            }

            float3 a = V3(netRef.A);
            if (netRef.Kind == 3)
            {
                Entity prefab = prefabSystem != null ? PrefabLookup.Find(em, prefabSystem, netRef.Prefab) : Entity.Null;
                if (prefab == Entity.Null)
                {
                    return false;
                }

                using (EntityQuery query = em.CreateEntityQuery(new EntityQueryDesc
                       {
                           All = new[] { ComponentType.ReadOnly<Game.Objects.Transform>(), ComponentType.ReadOnly<PrefabRef>() },
                           None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() }
                       }))
                using (NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp))
                using (NativeArray<Game.Objects.Transform> transforms = query.ToComponentDataArray<Game.Objects.Transform>(Allocator.Temp))
                using (NativeArray<PrefabRef> prefabs = query.ToComponentDataArray<PrefabRef>(Allocator.Temp))
                {
                    for (int i = 0; i < entities.Length; i++)
                    {
                        if (prefabs[i].m_Prefab == prefab && math.distancesq(transforms[i].m_Position, a) < MatchDistanceSq)
                        {
                            entity = entities[i];
                            return true;
                        }
                    }
                }

                return false;
            }

            if (netRef.Kind == 1)
            {
                using (EntityQuery query = em.CreateEntityQuery(new EntityQueryDesc
                       {
                           All = new[] { ComponentType.ReadOnly<Node>() },
                           None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() }
                       }))
                using (NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp))
                using (NativeArray<Node> nodes = query.ToComponentDataArray<Node>(Allocator.Temp))
                {
                    for (int i = 0; i < nodes.Length; i++)
                    {
                        if (math.distancesq(nodes[i].m_Position, a) < MatchDistanceSq)
                        {
                            entity = entities[i];
                            return true;
                        }
                    }
                }

                return false;
            }

            float3 b = V3(netRef.B);
            using (EntityQuery query = em.CreateEntityQuery(new EntityQueryDesc
                   {
                       All = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>() },
                       None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() }
                   }))
            using (NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp))
            using (NativeArray<Curve> curves = query.ToComponentDataArray<Curve>(Allocator.Temp))
            {
                for (int i = 0; i < curves.Length; i++)
                {
                    float3 ca = curves[i].m_Bezier.a;
                    float3 cd = curves[i].m_Bezier.d;
                    if ((math.distancesq(ca, a) < MatchDistanceSq && math.distancesq(cd, b) < MatchDistanceSq) ||
                        (math.distancesq(ca, b) < MatchDistanceSq && math.distancesq(cd, a) < MatchDistanceSq))
                    {
                        entity = entities[i];
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
