using System;

namespace GeoViewer.Core
{
    /// <summary>Immutable 3D vector (double precision).</summary>
    public readonly struct V3
    {
        public readonly double X, Y, Z;

        public V3(double x, double y, double z) { X = x; Y = y; Z = z; }

        public static readonly V3 Zero = new(0, 0, 0);
        public static readonly V3 One = new(1, 1, 1);
        public static readonly V3 UnitX = new(1, 0, 0);
        public static readonly V3 UnitY = new(0, 1, 0);
        public static readonly V3 UnitZ = new(0, 0, 1);

        public static V3 operator +(in V3 a, in V3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static V3 operator -(in V3 a, in V3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static V3 operator -(in V3 a) => new(-a.X, -a.Y, -a.Z);
        public static V3 operator *(in V3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
        /// <summary>Component-wise product (light colour × albedo).</summary>
        public static V3 operator *(in V3 a, in V3 b) => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
        public static V3 operator *(double s, in V3 a) => new(a.X * s, a.Y * s, a.Z * s);
        public static V3 operator /(in V3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);

        public static double Dot(in V3 a, in V3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public static V3 Cross(in V3 a, in V3 b) =>
            new(a.Y * b.Z - a.Z * b.Y,
                a.Z * b.X - a.X * b.Z,
                a.X * b.Y - a.Y * b.X);

        public static V3 Min(in V3 a, in V3 b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
        public static V3 Max(in V3 a, in V3 b) => new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));

        public double LengthSq => X * X + Y * Y + Z * Z;
        public double Length => Math.Sqrt(LengthSq);

        public V3 Normalized()
        {
            double l = Length;
            return l > 1e-12 ? new V3(X / l, Y / l, Z / l) : Zero;
        }

        public V3 Lerp(in V3 b, double t) =>
            new(X + (b.X - X) * t, Y + (b.Y - Y) * t, Z + (b.Z - Z) * t);

        public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###})";
    }

    public static class MathUtil
    {
        public static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);
        public static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);
        public static double Sat(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);
        public static double SmoothStep(double edge0, double edge1, double x)
        {
            double t = Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3 - 2 * t);
        }
        public const double Deg2Rad = Math.PI / 180.0;
        public const double Rad2Deg = 180.0 / Math.PI;
    }

    /// <summary>sRGB / linear conversion helpers.</summary>
    public static class ColorUtil
    {
        public static double SrgbToLinear(double c) =>
            c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

        public static double LinearToSrgb(double c) =>
            c <= 0.0031308 ? c * 12.92 : 1.055 * Math.Pow(c, 1.0 / 2.4) - 0.055;

        public static V3 FromSrgb(int r, int g, int b) =>
            new(SrgbToLinear(r / 255.0), SrgbToLinear(g / 255.0), SrgbToLinear(b / 255.0));

        public static V3 FromSrgb(V3 srgb01) =>
            new(SrgbToLinear(srgb01.X), SrgbToLinear(srgb01.Y), SrgbToLinear(srgb01.Z));
    }
}
