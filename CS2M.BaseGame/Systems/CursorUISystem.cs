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

        protected override void OnCreate()
        {
            base.OnCreate();
            _cameraUpdateSystem = World.GetOrCreateSystemManaged<CameraUpdateSystem>();
            AddBinding(_cursors = new ValueBinding<string>("CS2M", "Cursors", "[]"));
        }

        protected override void OnUpdate()
        {
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
