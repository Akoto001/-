using System;
using System.Collections.Generic;

namespace GeoViewer.Core
{
    public enum ShadingMode
    {
        Opaque = 0,
        Translucent = 1,
        Outline = 2
    }

    /// <summary>
    /// The document: the five reference solids, the camera and the presentation settings.
    /// </summary>
    public sealed class Scene
    {
        public const int Count = 5;
        public const int DefaultSelected = 2;

        public const double InitialAzimuth = 0.0;
        public const double InitialElevation = 0.0;

        public readonly Solid[] Solids = new Solid[Count];
        public readonly bool[] Show = new bool[Count];

        public ShadingMode Mode = ShadingMode.Opaque;
        public bool StructureLines = true;
        public bool PerspectiveLines = false;

        public Camera Camera = new Camera();
        public double EdgeLength { get; private set; } = 1.0;
        public int Selected { get; private set; } = DefaultSelected;

        public Scene()
        {
            for (int i = 0; i < Count; i++) Solids[i] = Solid.Create((SolidKind)i, EdgeLength);
            Select(DefaultSelected);
            Layout();
            ResetView(960, 720);
        }

        /// <summary>Show exactly one model; the framing is kept so sizes stay comparable.</summary>
        public void Select(int index)
        {
            Selected = Math.Clamp(index, 0, Count - 1);
            for (int i = 0; i < Count; i++) Show[i] = i == Selected;
        }

        /// <summary>All five models sit on the same spot, bottom face on y = 0.</summary>
        public void Layout()
        {
            for (int i = 0; i < Count; i++)
            {
                Solids[i].Center = new V3(0, 0, 0);
                Solids[i].UpdateBounds();
            }
        }

        public void SetEdgeLength(double a)
        {
            if (!(a > 0.05) || !double.IsFinite(a)) a = 1.0;
            EdgeLength = a;
            for (int i = 0; i < Count; i++)
            {
                Solids[i] = Solid.Create((SolidKind)i, a);
                Solids[i].Center = new V3(0, 0, 0);
                Solids[i].UpdateBounds();
            }
        }

        public IEnumerable<Solid> ActiveSolids
        {
            get
            {
                for (int i = 0; i < Count; i++)
                {
                    if (Show[i]) yield return Solids[i];
                }
            }
        }

        public int ActiveCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Count; i++) if (Show[i]) n++;
                return n;
            }
        }

        public bool AnyVisible => ActiveCount > 0;

        /// <summary>Bounds of the models that are currently shown.</summary>
        public bool TryGetBounds(out V3 min, out V3 max)
        {
            min = new V3(double.MaxValue, double.MaxValue, double.MaxValue);
            max = new V3(double.MinValue, double.MinValue, double.MinValue);
            bool any = false;
            for (int i = 0; i < Count; i++)
            {
                if (!Show[i]) continue;
                Solid s = Solids[i];
                min = V3.Min(min, s.Min);
                max = V3.Max(max, s.Max);
                any = true;
            }
            return any && double.IsFinite(min.X) && double.IsFinite(max.X);
        }

        /// <summary>Bounds of every model, used for framing so switching keeps the same scale.</summary>
        public bool TryGetCanonicalBounds(out V3 min, out V3 max)
        {
            min = new V3(double.MaxValue, double.MaxValue, double.MaxValue);
            max = new V3(double.MinValue, double.MinValue, double.MinValue);
            for (int i = 0; i < Count; i++)
            {
                Solid s = Solids[i];
                min = V3.Min(min, s.Min);
                max = V3.Max(max, s.Max);
            }
            min = new V3(min.X, 0.0, min.Z);
            return double.IsFinite(min.X) && double.IsFinite(max.X);
        }

        public V3 Center
        {
            get
            {
                if (TryGetCanonicalBounds(out V3 mn, out V3 mx)) return (mn + mx) * 0.5;
                return new V3(0, EdgeLength, 0);
            }
        }

        public void ResetView(double width, double height)
        {
            Camera = new Camera();
            Camera.AzimuthDeg = InitialAzimuth;
            Camera.ElevationDeg = InitialElevation;
            Camera.RollDeg = 0.0;
            Camera.FovYDeg = Optics.DefaultFovYDeg;
            Fit(width, height);
        }

        /// <summary>
        /// Frames the union of every model (so switching models keeps the same scale) and keeps the
        /// orbit centre on that box, so a change of azimuth / elevation can never push it off screen.
        /// </summary>
        public void Fit(double width, double height, double margin = 1.16)
        {
            width = Math.Max(8.0, width);
            height = Math.Max(8.0, height);
            Camera.Width = (int)Math.Round(width);
            Camera.Height = (int)Math.Round(height);
            Camera.Update();

            if (!TryGetCanonicalBounds(out V3 mn, out V3 mx))
            {
                Camera.Target = new V3(0, EdgeLength, 0);
                Camera.Distance = 6.0 * EdgeLength;
                Camera.Update();
                return;
            }

            V3 center = (mn + mx) * 0.5;
            double tanY = Math.Tan(Camera.FovYDeg * 0.5 * MathUtil.Deg2Rad);
            double tanX = tanY * Camera.Aspect;
            Camera target = null!;
            Camera.Target = center;
            Camera.Update();

            double need = 0.0;
            for (int i = 0; i < 8; i++)
            {
                V3 p = new V3(
                    (i & 1) == 0 ? mn.X : mx.X,
                    (i & 2) == 0 ? mn.Y : mx.Y,
                    (i & 4) == 0 ? mn.Z : mx.Z);
                V3 v = p - center;
                double dz = V3.Dot(v, Camera.Forward);
                double dx = Math.Abs(V3.Dot(v, Camera.Right));
                double dy = Math.Abs(V3.Dot(v, Camera.Up));
                need = Math.Max(need, dy / tanY - dz);
                need = Math.Max(need, dx / tanX - dz);
            }

            double dist = Math.Max(need * margin, 0.3);
            Camera.Target = center;
            Camera.Distance = dist;
            Camera.OrthoHalfHeight = Math.Max(Math.Abs(mx.Y - mn.Y), Math.Abs(mx.X - mn.X) / Math.Max(0.2, Camera.Aspect)) * 0.5 * margin;
            Camera.Update();
        }

        public void ApplyAxisAngles(double xPitch, double yYaw, double zRoll)
        {
            Camera.ElevationDeg = Camera.WrapDegrees(InitialElevation + xPitch);
            Camera.AzimuthDeg = Camera.WrapDegrees(InitialAzimuth + yYaw);
            Camera.RollDeg = Camera.WrapDegrees(zRoll);
            Camera.Update();
        }

        public double CurrentPitch => Camera.WrapDegrees(Camera.ElevationDeg - InitialElevation);
        public double CurrentYaw => Camera.WrapDegrees(Camera.AzimuthDeg - InitialAzimuth);
        public double CurrentRoll => Camera.WrapDegrees(Camera.RollDeg);

        public double FovYDeg => Camera.FovYDeg;
        public double FocalMm => Optics.FocalFromFovY(Camera.FovYDeg);

        public void SetFovYDeg(double fov)
        {
            Camera.FovYDeg = Math.Clamp(fov, Optics.MinFovYDeg, Optics.MaxFovYDeg);
            Camera.Update();
        }

        public void SetFocalMm(double mm) => SetFovYDeg(Optics.FovYFromFocal(mm));
    }
}
