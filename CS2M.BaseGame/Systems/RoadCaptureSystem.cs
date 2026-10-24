using System.Collections.Generic;
using Colossal.Mathematics;
using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using Game;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace CS2M.BaseGame.Systems
{
    /// <summary>
    ///     Runs in the ApplyTool phase before the game's ApplyNetSystem, so only on the frame a tool is applied.
    ///     Sends the road tool's definitions; the other games rebuild them as permanent definitions.
    /// </summary>
    public partial class RoadCaptureSystem : GameSystemBase
    {
        private EntityQuery _definitions;
        private ToolSystem _toolSystem;
        private PrefabSystem _prefabSystem;

        private string _lastKey;
        private float _lastTime;

        protected override void OnCreate()
        {
            base.OnCreate();
            _toolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            // owned definitions are skipped below, but still logged
            _definitions = GetEntityQuery(ComponentType.ReadOnly<CreationDefinition>(), ComponentType.ReadOnly<NetCourse>());
            RequireForUpdate(_definitions);
        }

        protected override void OnUpdate()
        {
            if (Command.CurrentRole == MultiplayerRole.None || Command.SendToAll == null ||
                !(_toolSystem.activeTool is NetToolSystem))
            {
                if (_toolSystem.activeTool is NetToolSystem)
                {
                    BaseGameLog.Info($"Road capture skipped: role={Command.CurrentRole}, can send={Command.SendToAll != null}");
                }

                return;
            }

            var courses = new List<NetCourseData>();
            using (NativeArray<Entity> entities = _definitions.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    CreationDefinition definition = EntityManager.GetComponentData<CreationDefinition>(entity);
                    LogDefinition(entity, definition);
                    // Permanent = one we created from another player's command. Owned definitions (a
                    // building's sub-roads) come from their owner. m_Original = rebuilding an existing road,
                    // which a junction does to the road it joins: those are sent with the original described.
                    if ((definition.m_Flags & CreationFlags.Permanent) != 0 || EntityManager.HasComponent<OwnerDefinition>(entity))
                    {
                        continue;
                    }

                    NetRef original = NetRefs.Describe(EntityManager, definition.m_Original);
                    if (definition.m_Original != Entity.Null && original.Kind == 0)
                    {
                        continue;
                    }

                    // a rebuilt road may keep its prefab (m_Prefab null): the receiver uses the original's
                    string prefab = definition.m_Prefab != Entity.Null ? PrefabLookup.Name(_prefabSystem, definition.m_Prefab) : "";
                    if (prefab == null)
                    {
                        continue;
                    }

                    NetCourse course = EntityManager.GetComponentData<NetCourse>(entity);
                    Bezier4x3 curve = course.m_Curve;
                    courses.Add(new NetCourseData
                    {
                        Prefab = prefab,
                        CreationFlags = (uint)definition.m_Flags,
                        RandomSeed = definition.m_RandomSeed,
                        Original = original,
                        Curve = new[]
                        {
                            curve.a.x, curve.a.y, curve.a.z, curve.b.x, curve.b.y, curve.b.z,
                            curve.c.x, curve.c.y, curve.c.z, curve.d.x, curve.d.y, curve.d.z
                        },
                        Elevation = new[] { course.m_Elevation.x, course.m_Elevation.y },
                        Length = course.m_Length,
                        FixedIndex = course.m_FixedIndex,
                        Start = Pos(EntityManager, course.m_StartPosition),
                        End = Pos(EntityManager, course.m_EndPosition)
                    });
                }
            }

            BaseGameLog.Info($"Road capture ran: tool={_toolSystem.activeTool?.toolID}, {courses.Count} course(s) to send");
            if (courses.Count == 0)
            {
                return;
            }

            // The apply phase can run on two frames for one click; don't build the road twice
            string key = string.Join(";", courses.ConvertAll(c => c.Prefab + string.Join(",", c.Curve)));
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (key == _lastKey && now - _lastTime < 0.5f)
            {
                return;
            }

            _lastKey = key;
            _lastTime = now;
            // Roads are sent as the applied result by NetDiffCaptureSystem: permanent definitions can't split
            // an existing road (only the preview path does), so replaying definitions breaks junctions.
        }

        // Diagnostics for junctions: every road definition on the apply frame, including skipped ones
        private void LogDefinition(Entity entity, CreationDefinition definition)
        {
            NetCourse course = EntityManager.GetComponentData<NetCourse>(entity);
            NetRef original = NetRefs.Describe(EntityManager, definition.m_Original);
            NetRef start = NetRefs.Describe(EntityManager, course.m_StartPosition.m_Entity);
            NetRef end = NetRefs.Describe(EntityManager, course.m_EndPosition.m_Entity);
            BaseGameLog.Info($"Road def: prefab={PrefabLookup.Name(_prefabSystem, definition.m_Prefab)} flags={definition.m_Flags} " +
                             $"original={original.Kind} start={start.Kind}@{course.m_StartPosition.m_SplitPosition:0.###} " +
                             $"[{course.m_StartPosition.m_Flags}] end={end.Kind}@{course.m_EndPosition.m_SplitPosition:0.###} " +
                             $"[{course.m_EndPosition.m_Flags}] owner={EntityManager.HasComponent<OwnerDefinition>(entity)}");
        }

        public static CoursePosData Pos(EntityManager em, CoursePos pos)
        {
            return new CoursePosData
            {
                Entity = NetRefs.Describe(em, pos.m_Entity),
                Position = NetRefs.F(pos.m_Position),
                Rotation = new[] { pos.m_Rotation.value.x, pos.m_Rotation.value.y, pos.m_Rotation.value.z, pos.m_Rotation.value.w },
                Elevation = new[] { pos.m_Elevation.x, pos.m_Elevation.y },
                CourseDelta = pos.m_CourseDelta,
                SplitPosition = pos.m_SplitPosition,
                Flags = (uint)pos.m_Flags,
                ParentMesh = pos.m_ParentMesh
            };
        }
    }
}
