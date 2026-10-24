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
            _definitions = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<CreationDefinition>(), ComponentType.ReadOnly<NetCourse>() },
                None = new[] { ComponentType.ReadOnly<OwnerDefinition>() }
            });
            RequireForUpdate(_definitions);
        }

        protected override void OnUpdate()
        {
            if (Command.CurrentRole == MultiplayerRole.None || Command.SendToAll == null ||
                !(_toolSystem.activeTool is NetToolSystem))
            {
                return;
            }

            var courses = new List<NetCourseData>();
            using (NativeArray<Entity> entities = _definitions.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    CreationDefinition definition = EntityManager.GetComponentData<CreationDefinition>(entity);
                    // Permanent = one we created from another player's command; m_Original = editing an
                    // existing road (replace/upgrade), not synced yet
                    if ((definition.m_Flags & (CreationFlags.Permanent | CreationFlags.Delete)) != 0 ||
                        definition.m_Original != Entity.Null)
                    {
                        continue;
                    }

                    string prefab = PrefabLookup.Name(_prefabSystem, definition.m_Prefab);
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
                        Curve = new[]
                        {
                            curve.a.x, curve.a.y, curve.a.z, curve.b.x, curve.b.y, curve.b.z,
                            curve.c.x, curve.c.y, curve.c.z, curve.d.x, curve.d.y, curve.d.z
                        },
                        Elevation = new[] { course.m_Elevation.x, course.m_Elevation.y },
                        Length = course.m_Length,
                        FixedIndex = course.m_FixedIndex,
                        Start = Pos(course.m_StartPosition),
                        End = Pos(course.m_EndPosition)
                    });
                }
            }

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
            Command.SendToAll(new RoadCommand { Courses = courses.ToArray() });
            BaseGameLog.Info($"Road sent: {courses.Count} course(s), first {courses[0].Prefab}");
        }

        private CoursePosData Pos(CoursePos pos)
        {
            return new CoursePosData
            {
                Entity = NetRefs.Describe(EntityManager, pos.m_Entity),
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
