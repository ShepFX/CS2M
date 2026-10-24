using System.Collections.Generic;
using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using Game;
using Game.Common;
using Game.Input;
using Game.Rendering;
using Game.Tools;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace CS2M.BaseGame.Systems
{
    /// <summary>
    ///     Shares the local cursor (where the mouse hits the terrain) and keeps the other players' cursors.
    ///     Runs in the Raycast phase: reads last frame's terrain hit, then queues this frame's ray.
    /// </summary>
    public partial class CursorSystem : GameSystemBase
    {
        public class RemoteCursor
        {
            public string Name;
            public float3 Position;
            public string Activity;
            public bool OnMap;
            public float LastSeen;
            public Color Color;
        }

        /// <summary>
        ///     Other players' cursors by sender id. Only touched on the main thread.
        /// </summary>
        public static readonly Dictionary<int, RemoteCursor> Remote = new Dictionary<int, RemoteCursor>();

        /// <summary>
        ///     A cursor that hasn't been heard from in this many seconds is dropped (player left).
        /// </summary>
        public const float Timeout = 6f;

        private const float SendInterval = 0.1f;
        private const float Heartbeat = 2f;

        private static readonly Color[] Palette =
        {
            new Color(0.25f, 0.75f, 1f), new Color(1f, 0.55f, 0.2f), new Color(0.6f, 1f, 0.3f),
            new Color(1f, 0.35f, 0.65f), new Color(0.75f, 0.5f, 1f), new Color(1f, 0.9f, 0.3f),
            new Color(0.3f, 1f, 0.85f), new Color(1f, 0.4f, 0.35f)
        };

        private RaycastSystem _raycastSystem;
        private CameraUpdateSystem _cameraUpdateSystem;
        private ToolSystem _toolSystem;

        private float3 _lastSentPosition;
        private string _lastSentActivity = "";
        private bool _lastSentOnMap;
        private float _lastSentTime;

        protected override void OnCreate()
        {
            base.OnCreate();
            _raycastSystem = World.GetOrCreateSystemManaged<RaycastSystem>();
            _cameraUpdateSystem = World.GetOrCreateSystemManaged<CameraUpdateSystem>();
            _toolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
        }

        protected override void OnUpdate()
        {
            if (Command.CurrentRole == MultiplayerRole.None)
            {
                Remote.Clear();
                return;
            }

            DropStale();

            NativeArray<RaycastResult> results = _raycastSystem.GetResult(this);
            // no hit leaves the position at zero
            bool onMap = results.Length != 0 && math.lengthsq(results[0].m_Hit.m_HitPosition) > 0f &&
                         InputManager.instance.controlOverWorld;
            float3 hit = onMap ? results[0].m_Hit.m_HitPosition : _lastSentPosition;
            Send(hit, onMap);

            if (_cameraUpdateSystem.TryGetViewer(out Viewer viewer))
            {
                RaycastInput input = default;
                input.m_Line = ToolRaycastSystem.CalculateRaycastLine(viewer.camera);
                input.m_TypeMask = TypeMask.Terrain | TypeMask.Water;
                _raycastSystem.AddInput(this, input);
            }
        }

        private void Send(float3 position, bool onMap)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            string activity = Activity();
            bool changed = onMap != _lastSentOnMap || activity != _lastSentActivity ||
                           (onMap && math.distancesq(position, _lastSentPosition) > 1f);
            if (now - _lastSentTime < SendInterval || (!changed && now - _lastSentTime < Heartbeat))
            {
                return;
            }

            Command.SendToAll?.Invoke(new CursorCommand
            {
                Username = Command.GetLocalUsername?.Invoke() ?? "",
                X = position.x,
                Y = position.y,
                Z = position.z,
                Activity = activity,
                OnMap = onMap
            });
            _lastSentPosition = position;
            _lastSentActivity = activity;
            _lastSentOnMap = onMap;
            _lastSentTime = now;
        }

        private string Activity()
        {
            string tool = _toolSystem.activeTool?.toolID ?? "";
            switch (tool)
            {
                case "Zone Tool": return "zoning";
                case "Net Tool": return "roads";
                case "Bulldoze Tool": return "bulldozing";
                case "Object Tool": return "placing";
                case "Area Tool": return "areas";
                case "Route Tool": return "transit lines";
                case "Terrain Tool": return "terrain";
                case "Upgrade Tool": return "upgrading";
                default: return "";
            }
        }

        public static void OnRemoteCursor(CursorCommand command)
        {
            if (!Remote.TryGetValue(command.SenderId, out RemoteCursor cursor))
            {
                cursor = new RemoteCursor { Color = Palette[(Remote.Count + 1) % Palette.Length] };
                Remote[command.SenderId] = cursor;
            }

            cursor.Name = string.IsNullOrEmpty(command.Username) ? $"Player {command.SenderId}" : command.Username;
            cursor.Position = new float3(command.X, command.Y, command.Z);
            cursor.Activity = command.Activity ?? "";
            cursor.OnMap = command.OnMap;
            cursor.LastSeen = UnityEngine.Time.realtimeSinceStartup;
        }

        private static void DropStale()
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            var stale = new List<int>();
            foreach (KeyValuePair<int, RemoteCursor> pair in Remote)
            {
                if (now - pair.Value.LastSeen > Timeout)
                {
                    stale.Add(pair.Key);
                }
            }

            stale.ForEach(id => Remote.Remove(id));
        }
    }
}
