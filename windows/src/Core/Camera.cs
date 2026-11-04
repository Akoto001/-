using System;
using GeoViewer.Core;

namespace GeoViewer.Core
{
    /// <summary>
    /// Turntable camera. Orientation is described by the spherical angles of the eye around
    /// <see cref="Target"/> plus a roll around the view axis. Angles are absolute; the UI shows
    /// offsets relative to the application's initial pose.
    /// </summary>
    public sealed class Camera
    {
        public V3 Target = new(0, 1, 0);
        public double Distance = 6.0;

        /// <summary>Horizontal angle in degrees (0 = looking down -Z, i.e. from the front).</summary>
        public double AzimuthDeg = -30.0;

        /// <summary>Vertical angle in degrees, positive = camera above the target.</summary>
        public double ElevationDeg = 12.0;

        /// <summary>Roll of the image plane in degrees.</summary>
        public double RollDeg = 0.0;

        /// <summary>Vertical field of view in degrees; 50 mm standard lens unless the user changes it.</summary>
        public double FovYDeg = Optics.DefaultFovYDeg;
        public bool Orthographic = false;
        public double OrthoHalfHeight = 2.4;

        public int Width = 960;
        public int Height = 640;

        // Derived state
        public V3 Eye { get; private set; }
        public V3 Forward { get; private set; }
        public V3 Right { get; private set; }
        public V3 Up { get; private set; }

        private double _tanHalf;
        private double _aspect;

        public double Aspect => _aspect;

        public void Update()
        {
            Width = Math.Max(1, Width);
            Height = Math.Max(1, Height);
            _aspect = (double)Width / Height;
            _tanHalf = Math.Tan(FovYDeg * 0.5 * MathUtil.Deg2Rad);

            double el = MathUtil.Clamp(ElevationDeg, -89.0, 89.0) * MathUtil.Deg2Rad;
            double az = AzimuthDeg * MathUtil.Deg2Rad;

            double ce = Math.Cos(el);
            V3 offset = new(ce * Math.Sin(az), Math.Sin(el), ce * Math.Cos(az));
            Eye = Target + offset * Distance;

            Forward = (Target - Eye).Normalized();

            V3 worldUp = V3.UnitY;
            V3 right = V3.Cross(Forward, worldUp);
            if (right.LengthSq < 1e-9)
            {
                right = V3.Cross(Forward, V3.UnitZ);
            }
            Right = right.Normalized();
            Up = V3.Cross(Right, Forward).Normalized();

            if (Math.Abs(RollDeg) > 1e-9)
            {
                double r = RollDeg * MathUtil.Deg2Rad;
                double cs = Math.Cos(r), sn = Math.Sin(r);
                V3 rr = Right * cs + Up * sn;
                V3 uu = Up * cs - Right * sn;
                Right = rr;
                Up = uu;
            }
        }

        /// <summary>Builds a ray for the given pixel coordinate (pixel centre sampling at +0.5).</summary>
        public void RayFor(double px, double py, out V3 origin, out V3 dir)
        {
            double ndcX = 2.0 * px / Width - 1.0;
            double ndcY = 1.0 - 2.0 * py / Height;

            if (Orthographic)
            {
                origin = Eye + Right * (ndcX * OrthoHalfHeight * _aspect) + Up * (ndcY * OrthoHalfHeight);
                dir = Forward;
            }
            else
            {
                origin = Eye;
                dir = (Forward
                       + Right * (ndcX * _tanHalf * _aspect)
                       + Up * (ndcY * _tanHalf)).Normalized();
            }
        }

        /// <summary>Projects a world point into pixel coordinates. Returns false when behind the camera.</summary>
        public bool Project(in V3 p, out double px, out double py, out double depth)
        {
            V3 v = p - Eye;
            double z = V3.Dot(v, Forward);
            depth = Orthographic ? z : v.Length;

            if (Orthographic)
            {
                double nx = V3.Dot(v, Right) / (OrthoHalfHeight * _aspect);
                double ny = V3.Dot(v, Up) / OrthoHalfHeight;
                px = (nx + 1.0) * 0.5 * Width;
                py = (1.0 - ny) * 0.5 * Height;
                return true;
            }

            if (z <= 1e-6) { px = py = 0; return false; }
            double x = V3.Dot(v, Right) / (z * _tanHalf * _aspect);
            double y = V3.Dot(v, Up) / (z * _tanHalf);
            px = (x + 1.0) * 0.5 * Width;
            py = (1.0 - y) * 0.5 * Height;
            return true;
        }

        /// <summary>World size of one pixel at the given view depth (used for pan / line widths).</summary>
        public double WorldPerPixelAt(double depth)
        {
            if (Orthographic) return 2.0 * OrthoHalfHeight / Height;
            return 2.0 * depth * _tanHalf / Height;
        }

        /// <summary>Distance at which a bounding sphere of the given radius fills the frame.</summary>
        public static double FitDistance(double radius, double fovYDeg, double aspect, double margin)
        {
            double vfov = fovYDeg * MathUtil.Deg2Rad;
            double hfov = 2.0 * Math.Atan(Math.Tan(vfov * 0.5) * aspect);
            double f = Math.Min(vfov, hfov);
            return radius / Math.Sin(f * 0.5) * margin;
        }

        public void Zoom(double factor)
        {
            if (Orthographic)
                OrthoHalfHeight = MathUtil.Clamp(OrthoHalfHeight / factor, 0.05, 200.0);
            else
                Distance = MathUtil.Clamp(Distance / factor, 0.4, 200.0);
        }

        public void PanPixels(double dx, double dy)
        {
            double wpp = WorldPerPixelAt(Orthographic ? 0 : Distance);
            Target += Right * (-dx * wpp) + Up * (dy * wpp);
        }

        public void Orbit(double dxPixels, double dyPixels)
        {
            AzimuthDeg -= dxPixels * 0.32;
            ElevationDeg = MathUtil.Clamp(ElevationDeg + dyPixels * 0.32, -89.0, 89.0);
            AzimuthDeg = WrapDegrees(AzimuthDeg);
        }

        public static double WrapDegrees(double d)
        {
            while (d > 180.0) d -= 360.0;
            while (d < -180.0) d += 360.0;
            return d;
        }

        public Camera Clone()
        {
            return new Camera
            {
                Target = Target,
                Distance = Distance,
                AzimuthDeg = AzimuthDeg,
                ElevationDeg = ElevationDeg,
                RollDeg = RollDeg,
                FovYDeg = FovYDeg,
                Orthographic = Orthographic,
                OrthoHalfHeight = OrthoHalfHeight,
                Width = Width,
                Height = Height
            };
        }
    }
}
