using System;
using System.Globalization;
using GeoViewer.Core;
using GeoViewer.Render;

namespace GeoViewer
{
    /// <summary>Very small command line parser used for the offline render mode.</summary>
    public sealed class CliOptions
    {
        public string Shot;
        public string UiShot;
        public int Width = 1280;
        public int Height = 800;
        public int Supersample = 2;
        public ShadingMode Mode = ShadingMode.Opaque;
        public bool Lines = true;
        public bool Persp;
        public string Show = "11111";
        public bool ShowExplicit;
        public string Solid;
        public double? Azimuth;
        public double? Elevation;
        public double? Roll;
        public double? FovYDeg;
        public double? FocalMm;
        public double? Distance;
        public bool NoFit;
        public double? Edge;

        public static CliOptions Parse(string[] args)
        {
            var o = new CliOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string Next() => i + 1 < args.Length ? args[++i] : null;
                switch (a)
                {
                    case "--shot": o.Shot = Next(); break;
                    case "--ui": o.UiShot = Next(); break;
                    case "--w": o.Width = Int(Next(), o.Width); break;
                    case "--h": o.Height = Int(Next(), o.Height); break;
                    case "--ss": o.Supersample = Int(Next(), o.Supersample); break;
                    case "--mode":
                    {
                        string m = Next();
                        o.Mode = m switch
                        {
                            "translucent" => ShadingMode.Translucent,
                            "outline" => ShadingMode.Outline,
                            _ => ShadingMode.Opaque
                        };
                        break;
                    }
                    case "--lines": o.Lines = Int(Next(), 1) != 0; break;
                    case "--struct": o.Lines = Int(Next(), 1) != 0; break;
                    case "--persp": o.Persp = Int(Next(), 1) != 0; break;
                    case "--show": o.Show = Next() ?? o.Show; o.ShowExplicit = true; break;
                    case "--solid": o.Solid = Next(); break;
                    case "--az": o.Azimuth = Dbl(Next()); break;
                    case "--el": o.Elevation = Dbl(Next()); break;
                    case "--roll": o.Roll = Dbl(Next()); break;
                    case "--fov": o.FovYDeg = Dbl(Next()); break;
                    case "--mm": o.FocalMm = Dbl(Next()); break;
                    case "--dist": o.Distance = Dbl(Next()); break;
                    case "--edge": o.Edge = Dbl(Next()); break;
                    case "--nofit": o.NoFit = true; break;
                }
            }
            return o;
        }

        private static int Int(string s, int fallback) =>
            int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

        private static double? Dbl(string s) =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : null;

        /// <summary>Which solid is on stage: --solid wins, otherwise the first flagged entry of --show.</summary>
        public int SelectedSolidIndex
        {
            get
            {
                switch ((Solid ?? string.Empty).Trim().ToLowerInvariant())
                {
                    case "sphere": case "ball": case "球": case "球体": return 0;
                    case "cylinder": case "cyl": case "圆柱": case "圆柱体": return 1;
                    case "cube": case "正方": case "正方体": return 2;
                    case "cuboid": case "box": case "长方": case "长方体": return 3;
                    case "cone": case "圆锥": case "圆锥体": return 4;
                }
                if (ShowExplicit)
                {
                    for (int i = 0; i < Show.Length && i < Scene.Count; i++)
                        if (Show[i] != '0') return i;
                }
                return Scene.DefaultSelected;
            }
        }

        public void Apply(Scene scene)
        {
            scene.Mode = Mode;
            scene.StructureLines = Lines;
        scene.PerspectiveLines = Persp;
            if (Edge.HasValue) scene.SetEdgeLength(Edge.Value);
            scene.Select(SelectedSolidIndex);

            // same semantics as the GUI: --az/--el/--roll are offsets from the initial pose
            if (Azimuth.HasValue || Elevation.HasValue || Roll.HasValue)
                scene.ApplyAxisAngles(Elevation ?? 0.0, Azimuth ?? 0.0, Roll ?? 0.0);
            else
            {
                // no angles given: start from the same pose the window opens with
                scene.Camera.AzimuthDeg = Scene.InitialAzimuth;
                scene.Camera.ElevationDeg = Scene.InitialElevation;
                scene.Camera.RollDeg = 0.0;
            }
            scene.Camera.Update();

            // field of view / focal length: --fov wins over the equivalent --mm
            if (FocalMm.HasValue) scene.SetFocalMm(FocalMm.Value);
            if (FovYDeg.HasValue) scene.SetFovYDeg(FovYDeg.Value);
        }

        public RenderResult Render(Scene scene)
        {
            if (!NoFit) scene.Fit(Width, Height);
            if (Distance.HasValue)
            {
                scene.Camera.Distance = Distance.Value;
                scene.Camera.Update();
            }
            return Renderer.Render(scene, new RenderOptions
            {
                Width = Width,
                Height = Height,
                Supersample = Supersample,
                DrawLines = true
            });
        }
    }
}
