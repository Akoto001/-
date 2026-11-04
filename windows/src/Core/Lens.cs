using System;

namespace GeoViewer.Core
{
    /// <summary>
    /// Perspective conventions used by painters. Each mode pins the camera axes that
    /// the convention takes for granted, so the remaining controls still work.
    /// </summary>
    public enum LensKind
    {
        /// <summary>No constraint: ordinary free camera.</summary>
        Free = 0,

        /// <summary>One-point: front face parallel to the picture plane (only depth converges).</summary>
        OnePoint = 1,

        /// <summary>Two-point: eye on the horizon, verticals stay vertical.</summary>
        TwoPoint = 2,

        /// <summary>Three-point: only the horizon is kept level; verticals converge as well.</summary>
        ThreePoint = 3,

        /// <summary>Fisheye: equidistant wide-angle lens, all axes free.</summary>
        Fisheye = 4,
    }

    /// <summary>Where the vanishing points of the three world axes are pinned, in half-frame-height units.</summary>
    public struct VpLayout
    {
        public bool HasX, HasY, HasZ;
        public double X, Y, Z;

        public bool Any => HasX || HasY || HasZ;
    }
    public static class Lens
    {
        public const int Count = 5;

        public static string Name(LensKind kind)
        {
            switch (kind)
            {
                case LensKind.OnePoint: return "一点透视";
                case LensKind.TwoPoint: return "两点透视";
                case LensKind.ThreePoint: return "三点透视";
                case LensKind.Fisheye: return "鱼眼透视";
                default: return "自由";
            }
        }

        public static string ShortName(LensKind kind)
        {
            switch (kind)
            {
                case LensKind.OnePoint: return "一点";
                case LensKind.TwoPoint: return "两点";
                case LensKind.ThreePoint: return "三点";
                case LensKind.Fisheye: return "鱼眼";
                default: return "自由";
            }
        }

        /// <summary>Canonical spelling of a mode name (as used by the command line).</summary>
        public static string FromCli(string text)
        {
            switch ((text ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "1p": case "one": case "onepoint": case "one-point": case "一点": case "一点透视": return "1p";
                case "2p": case "two": case "twopoint": case "two-point": case "两点": case "两点透视": return "2p";
                case "3p": case "three": case "threepoint": case "three-point": case "三点": case "三点透视": return "3p";
                case "fish": case "fisheye": case "鱼眼": case "鱼眼透视": return "fish";
                default: return "free";
            }
        }

        public static LensKind Parse(string text, LensKind fallback)
        {
            switch ((text ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "1p": case "one": case "onepoint": case "one-point": case "一点": case "一点透视": return LensKind.OnePoint;
                case "2p": case "two": case "twopoint": case "two-point": case "两点": case "两点透视": return LensKind.TwoPoint;
                case "3p": case "three": case "threepoint": case "three-point": case "三点": case "三点透视": return LensKind.ThreePoint;
                case "fish": case "fisheye": case "鱼眼": case "鱼眼透视": return LensKind.Fisheye;
                case "free": case "0": case "none": return LensKind.Free;
                default: return fallback;
            }
        }

        /// <summary>
        /// Fixed vanishing-point layout of a convention. Coordinates are measured from the frame
        /// centre in half-frame-height units, +x to the right and +y up.
        /// </summary>
        public static VpLayout Layout(LensKind kind)
        {
            switch (kind)
            {
                case LensKind.OnePoint:
                    return new VpLayout { HasZ = true, Z = 0.0 };

                case LensKind.TwoPoint:
                    return new VpLayout { HasX = true, X = -1.05, HasZ = true, Z = 1.05 };

                case LensKind.ThreePoint:
                    return new VpLayout { HasX = true, X = -1.05, HasZ = true, Z = 1.05, HasY = true, Y = 1.85 };

                default:
                    return new VpLayout();
            }
        }

        /// <summary>
        /// Orientation a convention prescribes, plus the focal length it opens with. The focal length
        /// stays editable afterwards: it slides the vanishing points in and out without changing the
        /// perspective family (the directions are what the mode pins).
        /// </summary>
        public static void PresetOrientation(LensKind kind, out double azimuth, out double elevation,
                                             out double roll, out double fovY)
        {
            azimuth = 0.0;
            elevation = 0.0;
            roll = 0.0;
            fovY = Optics.FovYFromFocal(Optics.DefaultFocalMm);

            switch (kind)
            {
                case LensKind.OnePoint:
                    fovY = Optics.FovYFromFocal(50.0);
                    break;

                case LensKind.Fisheye:
                    azimuth = 45.0;
                    elevation = -4.0;
                    fovY = 120.0;
                    break;

                case LensKind.TwoPoint:
                    azimuth = 45.0;
                    fovY = Optics.FovYFromFocal(24.0);
                    break;

                case LensKind.ThreePoint:
                    azimuth = 45.0;
                    elevation = -27.0;
                    fovY = Optics.FovYFromFocal(28.0);
                    break;
            }
        }
        /// <summary>
        /// The axis that stays rotatable inside a convention, because turning it only slides the
        /// vanishing points instead of adding or removing one. 0 = none, 1 = yaw, 2 = pitch.
        /// </summary>
        public static int RotatableAxis(LensKind kind) => kind switch
        {
            LensKind.OnePoint => 3,
            LensKind.TwoPoint => 3,
            LensKind.ThreePoint => 3,
            LensKind.Fisheye => 3,
            _ => 0
        };
        /// <summary>True when the mode prescribes the vanishing points, i.e. orientation and lens are fixed.</summary>
        public static bool IsPrescribed(LensKind kind) =>
            kind == LensKind.OnePoint || kind == LensKind.TwoPoint ||
            kind == LensKind.ThreePoint || kind == LensKind.Fisheye;
        /// <summary>One-point: the box front must face the camera, so all three axes are pinned.</summary>
        public static bool LocksAzimuth(LensKind kind) => kind == LensKind.OnePoint;

        /// <summary>Horizon must stay level in one/two point views, so elevation is pinned to 0.</summary>
        public static bool LocksElevation(LensKind kind) => kind == LensKind.OnePoint || kind == LensKind.TwoPoint;

        /// <summary>Only three-point and fisheye allow a tilted horizon.</summary>
        public static bool LocksRoll(LensKind kind) =>
            kind == LensKind.OnePoint || kind == LensKind.TwoPoint || kind == LensKind.ThreePoint;

        public static bool IsFisheye(LensKind kind) => kind == LensKind.Fisheye;

        /// <summary>Applies the pinned values of a mode to a set of axis offsets.</summary>
        public static void Pin(LensKind kind, ref double pitch, ref double yaw, ref double roll)
        {
            if (LocksAzimuth(kind)) yaw = 0.0;
            if (LocksElevation(kind)) pitch = 0.0;
            if (LocksRoll(kind)) roll = 0.0;
        }

        public static string Describe(LensKind kind)
        {
            switch (kind)
            {
                case LensKind.OnePoint: return "正面对准镜头：只有纵深方向有消失点（方位／俯仰／滚转已锁定）。";
                case LensKind.TwoPoint: return "视平线水平：竖直棱保持竖直，两个水平方向各有一个消失点（俯仰／滚转已锁定，可开「旋转」绕地平面转）。";
                case LensKind.ThreePoint: return "视平线水平但三向收敛：竖直棱向上或向下汇聚（滚转已锁定，可开「旋转」绕地平面转）。";
                case LensKind.Fisheye: return "等距鱼眼：画面半径与视线夹角成正比；可开「旋转」绕地平面转，焦距可拉到 180°。";
                default: return "不锁定任何参量，三轴与焦距自由。";
            }
        }
    }
}
