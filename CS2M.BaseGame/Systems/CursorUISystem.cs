using System.Globalization;
using System.Text;
using Colossal.UI.Binding;
using Game.Rendering;
using Game.UI;
using Unity.Mathematics;
using UnityEngine;

namespace CS2M.BaseGame.Systems
{
    /// <summary>
    ///     Gives the UI the screen position of each other player's cursor, for the name labels.
    ///     Positions are percentages of the screen so they work at any resolution and UI scale.
    /// </summary>
    public partial class CursorUISystem : UISystemBase
    {
        private CameraUpdateSystem _cameraUpdateSystem;
        private ValueBinding<string> _cursors;
        private string _last = "[]";

        // Players for the multiplayer HUD: [{"n": name, "c": colour, "a": activity, "me": bool}], empty when not in a session
        private ValueBinding<string> _players;
        private string _lastPlayers = "[]";

        protected override void OnCreate()
        {
            base.OnCreate();
            _cameraUpdateSystem = World.GetOrCreateSystemManaged<CameraUpdateSystem>();
            AddBinding(_cursors = new ValueBinding<string>("CS2M", "Cursors", "[]"));
            AddBinding(_players = new ValueBinding<string>("CS2M", "Players", "[]"));
        }

        protected override void OnUpdate()
        {
            UpdatePlayers();
            string json = "[]";
            if (CursorSystem.Remote.Count != 0 && _cameraUpdateSystem.TryGetViewer(out Viewer viewer))
            {
                var sb = new StringBuilder("[");
                foreach (CursorSystem.RemoteCursor cursor in CursorSystem.Remote.Values)
                {
                    if (!cursor.OnMap)
                    {
                        continue;
                    }

                    Vector3 screen = viewer.camera.WorldToScreenPoint(cursor.Position);
                    if (screen.z <= 0f)
                    {
                        continue;
                    }

                    if (sb.Length > 1)
                    {
                        sb.Append(',');
                    }

                    sb.Append("{\"n\":\"").Append(Escape(cursor.Name))
                        .Append("\",\"a\":\"").Append(Escape(cursor.Activity))
                        .Append("\",\"x\":").Append(Pct(screen.x / Screen.width))
                        .Append(",\"y\":").Append(Pct(1f - screen.y / Screen.height))
                        .Append(",\"c\":\"#").Append(ColorUtility.ToHtmlStringRGB(cursor.Color)).Append("\"}");
                }

                json = sb.Append(']').ToString();
            }

            if (json != _last)
            {
                _cursors.Update(json);
                _last = json;
            }
        }

        private void UpdatePlayers()
        {
            string json = "[]";
            if (CS2M.API.Commands.Command.CurrentRole != CS2M.API.Commands.MultiplayerRole.None)
            {
                string role = CS2M.API.Commands.Command.CurrentRole == CS2M.API.Commands.MultiplayerRole.Server ? " (host)" : "";
                var sb = new StringBuilder("[");
                sb.Append("{\"n\":\"").Append(Escape((CS2M.API.Commands.Command.GetLocalUsername?.Invoke() ?? "You") + role))
                    .Append("\",\"c\":\"#D6FF3F\",\"a\":\"\",\"me\":true}");
                foreach (System.Collections.Generic.KeyValuePair<int, CursorSystem.RemoteCursor> pair in CursorSystem.Remote)
                {
                    CursorSystem.RemoteCursor cursor = pair.Value;
                    sb.Append(",{\"id\":").Append(pair.Key).Append(",\"n\":\"").Append(Escape(cursor.Name))
                        .Append("\",\"c\":\"#").Append(ColorUtility.ToHtmlStringRGB(cursor.Color))
                        .Append("\",\"a\":\"").Append(Escape(cursor.Activity)).Append("\",\"me\":false}");
                }

                json = sb.Append(']').ToString();
            }

            if (json != _lastPlayers)
            {
                _players.Update(json);
                _lastPlayers = json;
            }
        }

        private static string Pct(float value) =>
            (math.clamp(value, -1f, 2f) * 100f).ToString("0.##", CultureInfo.InvariantCulture);

        private static string Escape(string text)
        {
            var sb = new StringBuilder();
            foreach (char c in text ?? "")
            {
                if (c == '"' || c == '\\')
                {
                    sb.Append('\\').Append(c);
                }
                else if (c >= ' ')
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }
    }
}
