using System;
using System.Collections.Generic;
using GeoViewer.Core;

namespace GeoViewer.Render
{
    /// <summary>
    /// Rasterises world-space line segments on top of the shaded frame, using the surface depth
    /// buffer for hidden line removal. Hidden construction lines are kept visible but faint, which
    /// is what makes them useful as drawing guides.
    /// </summary>
    internal static class LineCanvas
    {
        private struct Style
        {
            public V3 Color;
            public double Alpha;
            public double HiddenAlpha;
            public double Width;
        }

        public static void Draw(Scene scene, Camera cam, List<Segment> segments, int w, int h,
                                float[] rgb, float[] depth, int ss)
        {
            V3 eye = cam.Eye;
            V3 fwd = cam.Forward, right = cam.Right, up = cam.Up;
            double tanHalf = Math.Tan(cam.FovYDeg * 0.5 * MathUtil.Deg2Rad);
            double aspect = (double)w / h;
            double tanX = tanHalf * aspect, tanY = tanHalf;
            const double nz = 0.04;

            // world size of one pixel of the (supersampled) frame, per unit of depth
            double wppPerDepth = 2.0 * tanY / h;
            double epsPixels = 2.6;

            // Two passes: the faint hidden line work first, the visible line work on top of it.
            // Otherwise a hidden line drawn later would wash out a visible one at the same pixel
            // (a cylinder's back generatrix lies exactly on its front generatrix, for instance).
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (Segment seg in segments)
                {
                Style st = StyleFor(seg.Kind, scene.Mode);
                double alpha = st.Alpha * seg.Scale;
                double hiddenAlpha = st.HiddenAlpha * seg.Scale;
                if (alpha <= 0.002 && hiddenAlpha <= 0.002) continue;

                V3 ra = seg.A - eye, rb = seg.B - eye;
                double ax = V3.Dot(ra, right), ay = V3.Dot(ra, up), az = V3.Dot(ra, fwd);
                double bx = V3.Dot(rb, right), by = V3.Dot(rb, up), bz = V3.Dot(rb, fwd);

                if (az < nz && bz < nz) continue;

                double da = ra.Length, db = rb.Length;

                if (az < nz || bz < nz)
                {
                    double t = (nz - az) / (bz - az);
                    double cx = ax + (bx - ax) * t, cy = ay + (by - ay) * t;
                    double cd = da + (db - da) * t;
                    if (az < nz) { ax = cx; ay = cy; az = nz; da = cd; }
                    else { bx = cx; by = cy; bz = nz; db = cd; }
                }

                // construction lines lie exactly on the surface they belong to, so the depth
                // buffer rejects them; give them a small relative bias instead of dropping them
                double bias = seg.Kind switch
                {
                    LineKind.Structure => 0.028,
                    LineKind.Mid => 0.028,
                    LineKind.Axis => 0.026,
                    _ => 0.0
                };

                V3 color = seg.HasTint ? seg.Tint : st.Color;
                if (seg.HasTint) bias = 0.02;
                bool through = seg.Through;

                if (through && pass == 0) continue;

                // the fisheye projection bends straight lines, so split them before projecting
                if (cam.Fisheye)
                {
                    const int steps = 20;
                    double px0 = 0, py0 = 0, pd0 = 0;
                    for (int k = 0; k <= steps; k++)
                    {
                        double t = (double)k / steps;
                        double cx = ax + (bx - ax) * t;
                        double cy = ay + (by - ay) * t;
                        double czz = az + (bz - az) * t;
                        double cd = da + (db - da) * t;
                        FisheyePoint(cx, cy, czz, aspect, tanHalf, w, h, out double px, out double py);
                        if (k > 0)
                        {
                            DrawSegment(rgb, depth, w, h, ss,
                                        px0, py0, pd0, px, py, cd,
                                        color, alpha, hiddenAlpha, st.Width, wppPerDepth, epsPixels, bias, pass, through);
                        }
                        px0 = px; py0 = py; pd0 = cd;
                    }
                    continue;
                }

                double sx0 = (ax / (az * tanX) + 1.0) * 0.5 * w;
                double sy0 = (1.0 - ay / (az * tanY)) * 0.5 * h;
                double sx1 = (bx / (bz * tanX) + 1.0) * 0.5 * w;
                double sy1 = (1.0 - by / (bz * tanY)) * 0.5 * h;

                if (double.IsNaN(sx0) || double.IsNaN(sy0) || double.IsNaN(sx1) || double.IsNaN(sy1)) continue;

                DrawSegment(rgb, depth, w, h, ss,
                            sx0, sy0, da, sx1, sy1, db,
                            color, alpha, hiddenAlpha, st.Width, wppPerDepth, epsPixels, bias, pass, through);
            }
            }
        }

        /// <summary>Camera-space point to pixel, matching Camera's equidistant fisheye mapping.</summary>
        private static void FisheyePoint(double x, double y, double z, double aspect, double tanHalf,
                                         int w, int h, out double px, out double py)
        {
            double half = Math.Atan(tanHalf);
            double len = Math.Sqrt(x * x + y * y + z * z);
            double cos = len > 1e-9 ? z / len : 1.0;
            double theta = Math.Acos(Math.Clamp(cos, -1.0, 1.0));
            double rl = Math.Sqrt(x * x + y * y);
            double ux = 0.0, uy = 0.0;
            if (rl > 1e-9) { ux = x / rl; uy = y / rl; }
            double k = theta / Math.Max(1e-6, half);
            px = (ux * k / Math.Max(1e-6, aspect) + 1.0) * 0.5 * w;
            py = (1.0 - uy * k) * 0.5 * h;
        }

        private static void DrawSegment(float[] rgb, float[] depth, int w, int h, int ss,
                                        double x0, double y0, double d0,
                                        double x1, double y1, double d1,
                                        V3 color, double alpha, double hiddenAlpha, double widthPx,
                                        double wppPerDepth, double epsPixels, double bias, int pass,
                                        bool through = false)
        {
            double halfW = Math.Max(0.45, widthPx * 0.5 * ss);
            double pad = halfW + 1.0;
            double invD0 = d0 > 1e-6 ? 1.0 / d0 : 0.0;
            double invD1 = d1 > 1e-6 ? 1.0 / d1 : 0.0;

            int minX = (int)Math.Floor(Math.Min(x0, x1) - pad);
            int maxX = (int)Math.Ceiling(Math.Max(x0, x1) + pad);
            int minY = (int)Math.Floor(Math.Min(y0, y1) - pad);
            int maxY = (int)Math.Ceiling(Math.Max(y0, y1) + pad);

            if (minX < 0) minX = 0;
            if (minY < 0) minY = 0;
            if (maxX > w - 1) maxX = w - 1;
            if (maxY > h - 1) maxY = h - 1;
            if (maxX < minX || maxY < minY) return;

            double vx = x1 - x0, vy = y1 - y0;
            double len2 = vx * vx + vy * vy;
            double invLen2 = len2 > 1e-12 ? 1.0 / len2 : 0.0;

            for (int py = minY; py <= maxY; py++)
            {
                double fy = py + 0.5;
                int row = py * w;
                for (int px = minX; px <= maxX; px++)
                {
                    double fx = px + 0.5;
                    double u = ((fx - x0) * vx + (fy - y0) * vy) * invLen2;
                    if (u < 0) u = 0; else if (u > 1) u = 1;
                    double cxp = x0 + vx * u;
                    double cyp = y0 + vy * u;
                    double ddx = fx - cxp, ddy = fy - cyp;
                    double dist = Math.Sqrt(ddx * ddx + ddy * ddy);
                    double cov = halfW - dist + 0.5;
                    if (cov <= 0.004) continue;
                    if (cov > 1) cov = 1;

                    // perspective-correct depth: 1/d is linear in screen space, d is not
                    double invLine = invD0 + (invD1 - invD0) * u;
                    double dline = invLine > 1e-9 ? 1.0 / invLine : d0;
                    int idx = row + px;
                    double eps = Math.Max(dline * wppPerDepth * epsPixels, 1e-4);
                    bool visible = through || dline <= depth[idx] + eps + dline * bias;
                    if (visible != (pass == 1)) continue;

                    double a = visible ? alpha : hiddenAlpha;
                    if (a <= 0.002) continue;
                    double ca = a * cov;
                    if (ca <= 0.002) continue;

                    int i3 = idx * 3;
                    double inv = 1.0 - ca;
                    rgb[i3] = (float)(rgb[i3] * inv + color.X * ca);
                    rgb[i3 + 1] = (float)(rgb[i3 + 1] * inv + color.Y * ca);
                    rgb[i3 + 2] = (float)(rgb[i3 + 2] * inv + color.Z * ca);
                }
            }
        }

        /// <summary>
        /// Marks the vanishing point of each world axis that lands inside the frame: a small ring
        /// plus a cross, in that axis' colour, so the converging guides can be identified.
        /// Only axes whose guides are actually on screen get a marker — a rotational body only
        /// has a vertical family, so its horizontal vanishing points would be lonely markers.
        /// </summary>
        public static void DrawVanishingPoints(Scene scene, Camera cam, List<Segment> segments,
                                               int w, int h, float[] rgb, int ss)
        {
            if (!scene.PerspectiveLines) return;

            bool hasX = false, hasY = false, hasZ = false;
            for (int i = 0; i < segments.Count; i++)
            {
                Segment sg = segments[i];
                if (!sg.HasTint) continue;
                if (SameTint(sg.Tint, Palette.PerspX)) hasX = true;
                else if (SameTint(sg.Tint, Palette.PerspY)) hasY = true;
                else if (SameTint(sg.Tint, Palette.PerspZ)) hasZ = true;
            }

            double tanHalf = Math.Tan(cam.FovYDeg * 0.5 * MathUtil.Deg2Rad);
            double tanX = tanHalf * ((double)w / h), tanY = tanHalf;

            if (hasX) PlotVanishingPoint(V3.UnitX, Palette.PerspX, cam, w, h, rgb, ss, tanX, tanY);
            if (hasY) PlotVanishingPoint(V3.UnitY, Palette.PerspY, cam, w, h, rgb, ss, tanX, tanY);
            if (hasZ) PlotVanishingPoint(V3.UnitZ, Palette.PerspZ, cam, w, h, rgb, ss, tanX, tanY);
        }

        private static bool SameTint(in V3 a, in V3 b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return dx * dx + dy * dy + dz * dz < 1e-12;
        }

        private static void PlotVanishingPoint(V3 axis, V3 color, Camera cam, int w, int h, float[] rgb, int ss,
                                               double tanX, double tanY)
        {
            // the visible vanishing point belongs to whichever end of that axis recedes from the eye
            double dd = V3.Dot(axis, cam.Forward);
            if (dd < -1e-3) { axis = new V3(-axis.X, -axis.Y, -axis.Z); dd = -dd; }
            if (dd <= 1e-3) return;                        // direction parallel to the image plane

            double tx = V3.Dot(axis, cam.Right) / dd;
            double ty = V3.Dot(axis, cam.Up) / dd;
            double sx = (tx / tanX + 1.0) * 0.5 * w;
            double sy = (1.0 - ty / tanY) * 0.5 * h;
            if (!double.IsFinite(sx) || !double.IsFinite(sy)) return;

            double m = 18.0 * ss;
            if (sx < -m || sy < -m || sx > w + m || sy > h + m) return;

            double r = 6.5 * ss, arm = 11.5 * ss, half = 1.05 * ss;
            const int steps = 24;
            double prevX = sx + r, prevY = sy;
            for (int i = 1; i <= steps; i++)
            {
                double t = i * (2 * Math.PI / steps);
                double px = sx + r * Math.Cos(t), py = sy + r * Math.Sin(t);
                PlotScreenLine(rgb, w, h, prevX, prevY, px, py, color, 0.9, half);
                prevX = px; prevY = py;
            }
            PlotScreenLine(rgb, w, h, sx - arm, sy, sx - r - ss, sy, color, 0.9, half);
            PlotScreenLine(rgb, w, h, sx + r + ss, sy, sx + arm, sy, color, 0.9, half);
            PlotScreenLine(rgb, w, h, sx, sy - arm, sx, sy - r - ss, color, 0.9, half);
            PlotScreenLine(rgb, w, h, sx, sy + r + ss, sx, sy + arm, color, 0.9, half);
        }

        /// <summary>Screen-space line with no depth test, used for the vanishing point markers.</summary>
        private static void PlotScreenLine(float[] rgb, int w, int h,
                                           double x0, double y0, double x1, double y1,
                                           V3 color, double alpha, double halfW)
        {
            double pad = halfW + 1.0;
            int minX = (int)Math.Floor(Math.Min(x0, x1) - pad);
            int maxX = (int)Math.Ceiling(Math.Max(x0, x1) + pad);
            int minY = (int)Math.Floor(Math.Min(y0, y1) - pad);
            int maxY = (int)Math.Ceiling(Math.Max(y0, y1) + pad);
            if (minX < 0) minX = 0;
            if (minY < 0) minY = 0;
            if (maxX > w - 1) maxX = w - 1;
            if (maxY > h - 1) maxY = h - 1;
            if (maxX < minX || maxY < minY) return;

            double vx = x1 - x0, vy = y1 - y0;
            double len2 = vx * vx + vy * vy;
            double invLen2 = len2 > 1e-12 ? 1.0 / len2 : 0.0;

            for (int py = minY; py <= maxY; py++)
            {
                double fy = py + 0.5;
                int row = py * w;
                for (int px = minX; px <= maxX; px++)
                {
                    double fx = px + 0.5;
                    double u = ((fx - x0) * vx + (fy - y0) * vy) * invLen2;
                    if (u < 0) u = 0; else if (u > 1) u = 1;
                    double ddx = fx - (x0 + vx * u), ddy = fy - (y0 + vy * u);
                    double cov = halfW - Math.Sqrt(ddx * ddx + ddy * ddy) + 0.5;
                    if (cov <= 0.004) continue;
                    if (cov > 1) cov = 1;
                    double ca = alpha * cov;
                    int i3 = (row + px) * 3;
                    double inv = 1.0 - ca;
                    rgb[i3] = (float)(rgb[i3] * inv + color.X * ca);
                    rgb[i3 + 1] = (float)(rgb[i3 + 1] * inv + color.Y * ca);
                    rgb[i3 + 2] = (float)(rgb[i3 + 2] * inv + color.Z * ca);
                }
            }
        }

        private static Style StyleFor(LineKind kind, ShadingMode mode)
        {
            switch (mode)
            {
                case ShadingMode.Outline:
                    return kind switch
                    {
                        LineKind.Edge => new Style { Color = Palette.Paper, Alpha = 0.98, HiddenAlpha = 0.16, Width = 1.15 },
                        LineKind.Silhouette => new Style { Color = Palette.Paper, Alpha = 1.0, HiddenAlpha = 0.16, Width = 1.15 },
                        LineKind.Structure => new Style { Color = Palette.StructureLit, Alpha = 0.96, HiddenAlpha = 0.46, Width = 1.45 },
                        LineKind.Mid => new Style { Color = Palette.MidRed, Alpha = 0.99, HiddenAlpha = 0.40, Width = 3.0 },
                        LineKind.Perspective => new Style { Color = Palette.PerspZ, Alpha = 0.55, HiddenAlpha = 0.20, Width = 0.95 },
                        _ => new Style { Color = Palette.Axis, Alpha = 0.92, HiddenAlpha = 0.44, Width = 1.3 }
                    };

                case ShadingMode.Translucent:
                    return kind switch
                    {
                        LineKind.Edge => new Style { Color = Palette.Ink, Alpha = 0.9, HiddenAlpha = 0.28, Width = 1.0 },
                        LineKind.Silhouette => new Style { Color = Palette.Ink, Alpha = 0.9, HiddenAlpha = 0.28, Width = 1.0 },
                        LineKind.Structure => new Style { Color = Palette.StructureInk, Alpha = 0.9, HiddenAlpha = 0.42, Width = 1.55 },
                        LineKind.Mid => new Style { Color = Palette.MidRed, Alpha = 0.97, HiddenAlpha = 0.38, Width = 2.9 },
                        LineKind.Perspective => new Style { Color = Palette.PerspZ, Alpha = 0.55, HiddenAlpha = 0.20, Width = 0.95 },
                        _ => new Style { Color = Palette.AxisInk, Alpha = 0.8, HiddenAlpha = 0.4, Width = 1.35 }
                    };

                default:
                    return kind switch
                    {
                        LineKind.Edge => new Style { Color = Palette.Ink, Alpha = 0.95, HiddenAlpha = 0.0, Width = 1.05 },
                        LineKind.Silhouette => new Style { Color = Palette.Ink, Alpha = 0.95, HiddenAlpha = 0.0, Width = 1.05 },
                        LineKind.Structure => new Style { Color = Palette.StructureInk, Alpha = 0.92, HiddenAlpha = 0.26, Width = 1.55 },
                        LineKind.Mid => new Style { Color = Palette.MidRed, Alpha = 0.99, HiddenAlpha = 0.36, Width = 3.0 },
                        LineKind.Perspective => new Style { Color = Palette.PerspZ, Alpha = 0.55, HiddenAlpha = 0.20, Width = 0.95 },
                        _ => new Style { Color = Palette.AxisInk, Alpha = 0.85, HiddenAlpha = 0.24, Width = 1.35 }
                    };
            }
        }
    }
}
