using System;

namespace GeoViewer.Core
{
    /// <summary>
    /// Converts between a 35 mm ("full frame") focal length and the vertical field of view used by the
    /// camera. A 36 x 24 mm frame has a 12 mm half height, so fovY = 2 * atan(12 / f) — the window aspect
    /// only changes the horizontal field of view, exactly like cropping a photo.
    /// </summary>
    public static class Optics
    {
        /// <summary>Half of the 24 mm sensor height of a full-frame camera.</summary>
        public const double SensorHalfHeightMm = 12.0;

        /// <summary>Widest / narrowest field of view the tool allows (about 14 mm ... 200 mm).</summary>
        public const double MinFovYDeg = 6.8;
        public const double MaxFovYDeg = 82.0;

        /// <summary>Standard lens: 50 mm, vertical field of view 27 degrees.</summary>
        public const double DefaultFocalMm = 50.0;

        /// <summary>Lenses offered in the drop down, from wide angle to telephoto.</summary>
        public static readonly double[] PresetFocalMm = { 14.0, 18.0, 24.0, 28.0, 35.0, 50.0, 85.0, 135.0, 200.0 };

        public static double DefaultFovYDeg => FovYFromFocal(DefaultFocalMm);

        /// <summary>Vertical field of view in degrees for a focal length, clamped to the allowed range.</summary>
        public static double FovYFromFocal(double focalMm)
        {
            if (!(focalMm > 1e-6)) return MaxFovYDeg;
            double fov = 2.0 * Math.Atan(SensorHalfHeightMm / focalMm) * MathUtil.Rad2Deg;
            return Math.Clamp(fov, MinFovYDeg, MaxFovYDeg);
        }

        /// <summary>Focal length that produces the given vertical field of view.</summary>
        public static double FocalFromFovY(double fovYDeg)
        {
            double half = Math.Clamp(fovYDeg, MinFovYDeg, MaxFovYDeg) * 0.5 * MathUtil.Deg2Rad;
            return SensorHalfHeightMm / Math.Tan(half);
        }
    }
}
