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
            double back = 0.0;
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
                back = Math.Max(back, -dz);
            }

            // wide lenses would otherwise put the eye inside the bounding box: keep it behind the rear face
            double dist = Math.Max(Math.Max(need * margin, back + 0.30 * EdgeLength), 0.3);
            Camera.Target = center;
            Camera.Distance = dist;
            Camera.OrthoHalfHeight = Math.Max(Math.Abs(mx.Y - mn.Y), Math.Abs(mx.X - mn.X) / Math.Max(0.2, Camera.Aspect)) * 0.5 * margin;
            Camera.Update();
        }

        /// <summary>Active perspective convention; pinned axes are enforced here as well.</summary>
        public LensKind LensMode { get; private set; } = LensKind.Free;

        /// <summary>Lets the user spin the view inside a convention (2-point: yaw, 3-point: pitch).</summary>
        public bool LensRotate { get; private set; }

        public void SetLensRotate(bool on)
        {
            LensRotate = on && Lens.RotatableAxis(LensMode) != 0;
            if (!LensRotate) RestoreLensPose();
        }

        private void RestoreLensPose()
        {
            if (!Lens.IsPrescribed(LensMode)) return;
            Lens.PresetOrientation(LensMode, out double az, out double el, out double roll, out double fov);
            Camera.AzimuthDeg = Camera.WrapDegrees(InitialAzimuth + az);
            Camera.ElevationDeg = Camera.WrapDegrees(InitialElevation + el);
            Camera.RollDeg = roll;
            Camera.FovYDeg = Math.Clamp(fov, Optics.MinFovYDeg, Optics.MaxFovYDeg);
            Camera.Fisheye = Lens.IsFisheye(LensMode);
            Camera.Update();
        }

        /// <summary>Drag-to-rotate that respects what the perspective convention pins.</summary>
        public void TryOrbit(double dxPixels, double dyPixels)
        {
            if (!Lens.IsPrescribed(LensMode)) { Camera.Orbit(dxPixels, dyPixels); return; }
            int axis = Lens.RotatableAxis(LensMode);
            if (axis == 0 || !LensRotate) return;

            if (axis == 3) SetLensSweep(Camera.RollDeg + dxPixels * 0.4);
            else if (axis == 1) Camera.AzimuthDeg = Camera.WrapDegrees(Camera.AzimuthDeg - dxPixels * 0.32);
            else Camera.ElevationDeg = MathUtil.Clamp(Camera.ElevationDeg + dyPixels * 0.32, -89.0, 89.0);
            Camera.Update();
        }

        public void ApplyAxisAngles(double xPitch, double yYaw, double zRoll)
        {
            if (Lens.IsPrescribed(LensMode))
            {
                if (!LensRotate) return;
                int ax = Lens.RotatableAxis(LensMode);
                if (ax == 3) SetLensSweep(zRoll);
                else if (ax == 2) Camera.ElevationDeg = Camera.WrapDegrees(InitialElevation + xPitch);
                else Camera.AzimuthDeg = Camera.WrapDegrees(InitialAzimuth + yYaw);
                Camera.Update();
                return;
            }

            Lens.Pin(LensMode, ref xPitch, ref yYaw, ref zRoll);
            Camera.ElevationDeg = Camera.WrapDegrees(InitialElevation + xPitch);
            Camera.AzimuthDeg = Camera.WrapDegrees(InitialAzimuth + yYaw);
            Camera.RollDeg = Camera.WrapDegrees(zRoll);
            Camera.Update();
        }

        /// <summary>
        /// Finds the camera orientation and focal length that put the three axis vanishing points
        /// exactly where the convention wants them. Solved with the renderer's own projection, so
        /// the markers drawn in the viewport land on the requested spots.
        /// </summary>
        private void SolveVanishingPoints(VpLayout layout)
        {
            Camera.Fisheye = false;
            Camera.RollDeg = 0.0;

            const double probe = 20000.0;
            double w2 = Camera.Width * 0.5, h2 = Camera.Height * 0.5;

            // screen position of the vanishing point of an axis pair (+axis or -axis, whichever is ahead)
            bool Measure(V3 axis, double az, double el, double fov, out double sx, out double sy)
            {
                Camera.AzimuthDeg = az;
                Camera.ElevationDeg = el;
                Camera.FovYDeg = fov;
                Camera.Update();

                for (int s = 1; s >= -1; s -= 2)
                {
                    V3 d = axis * s;
                    if (V3.Dot(d, Camera.Forward) <= 0.08) continue;
                    if (!Camera.Project(Camera.Eye + d * probe, out sx, out sy, out _)) continue;
                    if (!double.IsFinite(sx) || !double.IsFinite(sy)) continue;
                    return true;
                }

                sx = sy = double.NaN;
                return false;
            }

            // 1-D solve by sampling then bisecting the first sign change
            double Find(Func<double, double> err, double lo, double hi, double step, double fallback)
            {
                double a = lo, ea = err(a);
                bool haveA = double.IsFinite(ea);
                for (double b = lo + step; b <= hi + 1e-9; b += step)
                {
                    double eb = err(b);
                    if (!double.IsFinite(eb)) { a = b; ea = eb; haveA = false; continue; }
                    if (haveA && Math.Sign(ea) != Math.Sign(eb))
                    {
                        double left = a, right = b;
                        for (int i = 0; i < 40; i++)
                        {
                            double m = 0.5 * (left + right);
                            double em = err(m);
                            if (!double.IsFinite(em)) break;
                            if (Math.Sign(em) == Math.Sign(err(left))) left = m; else right = m;
                        }
                        return 0.5 * (left + right);
                    }
                    a = b; ea = eb; haveA = true;
                }
                return fallback;
            }

            double targetZx = w2 + layout.Z * h2;
            double targetXx = w2 + layout.X * h2;
            double targetYy = h2 - layout.Y * h2;

            double az = 25.0, el = 0.0, fov = 45.0;

            if (layout.HasX && layout.HasZ)
            {
                // the two horizontal vanishing points are mirror images in the focal length
                double g = Math.Sqrt(Math.Max(0.08, -(layout.X * layout.Z)));
                fov = 2.0 * Math.Atan(1.0 / g) * MathUtil.Rad2Deg;
            }

            for (int pass = 0; pass < 3; pass++)
            {
                if (layout.HasZ)
                {
                    az = Find(a => Measure(new V3(0, 0, 1), a, el, fov, out double sx, out _) ? sx - targetZx : double.NaN,
                                -88.0, 88.0, 2.0, az);
                }

                if (layout.HasY)
                {
                    el = Find(e => Measure(new V3(0, 1, 0), az, e, fov, out _, out double sy) ? sy - targetYy : double.NaN,
                                -80.0, 80.0, 2.0, el);
                }
                else
                {
                    el = 0.0;
                }

                // fine-tune the lens so the horizontal points land exactly
                if (layout.HasX && layout.HasZ)
                {
                    fov = Find(f => Measure(new V3(1, 0, 0), az, el, f, out double sx, out _) ? sx - targetXx : double.NaN,
                                14.0, 132.0, 1.0, fov);
                }
            }

            Camera.AzimuthDeg = Camera.WrapDegrees(az);
            Camera.ElevationDeg = MathUtil.Clamp(el, -89.0, 89.0);
            Camera.RollDeg = 0.0;
            Camera.FovYDeg = MathUtil.Clamp(fov, Optics.MinFovYDeg, Optics.MaxFovYDeg);
            Camera.Fisheye = false;
            Camera.Update();
        }
        /// <summary>Spin of the model around its own vertical axis. The camera, ground and vanishing points stay put.</summary>
        public double ModelYawDeg { get; private set; }

        /// <summary>Roll of the model about the world Z axis (locked modes keep the ground and vanishing points fixed).</summary>
        public double ModelRollDeg { get; private set; }

        public void SetModelYaw(double degrees)
        {
            ModelYawDeg = Camera.WrapDegrees(degrees);
            for (int i = 0; i < Count; i++) Solids[i].YawDeg = ModelYawDeg;
        }
        /// <summary>
        /// Vertical sweep inside a locked perspective: swings the camera up or down (0 = level, where the
        /// vertical vanishing point runs off to infinity and a three-point view reads as two-point).
        /// The ground plane itself never rotates, so this is the "vertical two-point" slider.
        /// </summary>
        public void SetLensSweep(double degrees)
        {
            Camera.RollDeg = Camera.WrapDegrees(degrees);
            Camera.Update();
        }
        public void SetModelRoll(double degrees)
        {
            ModelRollDeg = MathUtil.Clamp(degrees, -180.0, 180.0);
            double lift = Math.Abs(Math.Sin(ModelRollDeg * MathUtil.Deg2Rad));
            for (int i = 0; i < Count; i++)
            {
                Solids[i].RollDeg = ModelRollDeg;
                double halfWidth = Solids[i].Kind == SolidKind.Cube || Solids[i].Kind == SolidKind.Cuboid
                    ? Solids[i].HalfWidth
                    : Solids[i].Radius;
                Solids[i].RollLift = 0.0 * halfWidth * lift;
            }
        }

        /// <summary>Switches the perspective convention, forcing the axes that mode pins.</summary>
        public void SetLens(LensKind kind)
        {
            double pitch = CurrentPitch;
            double yaw = CurrentYaw;
            double roll = CurrentRoll;
            Lens.Pin(kind, ref pitch, ref yaw, ref roll);

            LensMode = kind;

            // prescribed conventions pin the vanishing points: solve orientation and lens from them
            if (Lens.IsPrescribed(kind))
            {
                Lens.PresetOrientation(kind, out double pAz, out double pEl, out double pRoll, out double pFov);
                Camera.AzimuthDeg = Camera.WrapDegrees(InitialAzimuth + pAz);
                Camera.ElevationDeg = Camera.WrapDegrees(InitialElevation + pEl);
                Camera.RollDeg = pRoll;
                Camera.FovYDeg = Math.Clamp(pFov, Optics.MinFovYDeg, Optics.MaxFovYDeg);
                Camera.Fisheye = Lens.IsFisheye(kind);
                Camera.Update();
                PerspectiveLines = true;   // show the guides into the vanishing points
                return;
            }
            Camera.Fisheye = Lens.IsFisheye(kind);

            if (Camera.Fisheye)
            {
                Camera.FovYDeg = Math.Clamp(Camera.FovYDeg, Optics.MinFovYDeg, Optics.MaxFisheyeFovYDeg);
            }
            else
            {
                Camera.FovYDeg = Math.Clamp(Camera.FovYDeg, Optics.MinFovYDeg, Optics.MaxFovYDeg);
            }

            ApplyAxisAngles(pitch, yaw, roll);
        }

        public double CurrentPitch => Camera.WrapDegrees(Camera.ElevationDeg - InitialElevation);
        public double CurrentYaw => Camera.WrapDegrees(Camera.AzimuthDeg - InitialAzimuth);
        public double CurrentRoll => Camera.WrapDegrees(Camera.RollDeg);

        public double FovYDeg => Camera.FovYDeg;
        public double FocalMm => Optics.FocalFromFovY(Camera.FovYDeg);

        public void SetFovYDeg(double fov)
        {
            double max = Camera.Fisheye ? Optics.MaxFisheyeFovYDeg : Optics.MaxFovYDeg;
            Camera.FovYDeg = Math.Clamp(fov, Optics.MinFovYDeg, max);
            Camera.Update();
        }

        public void SetFocalMm(double mm) => SetFovYDeg(Optics.FovYFromFocal(mm));
    }
}
