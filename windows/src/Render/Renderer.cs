using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GeoViewer.Core;

namespace GeoViewer.Render
{
    /// <summary>Colours of the studio, in linear space.</summary>
    public static class Palette
    {
        public static readonly V3 BgTop = ColorUtil.FromSrgb(0x33, 0x39, 0x44);
        public static readonly V3 BgBottom = ColorUtil.FromSrgb(0x14, 0x16, 0x1b);
        public static readonly V3 Horizon = ColorUtil.FromSrgb(0x5a, 0x64, 0x72);

        public static readonly V3 Ground = ColorUtil.FromSrgb(0x4a, 0x50, 0x5a);
        public static readonly V3 Plaster = ColorUtil.FromSrgb(0xd8, 0xd1, 0xc6);

        public static readonly V3 Ink = ColorUtil.FromSrgb(0x0b, 0x0d, 0x10);
        public static readonly V3 Paper = ColorUtil.FromSrgb(0xec, 0xf0, 0xf6);
        public static readonly V3 Structure = ColorUtil.FromSrgb(0x1f, 0xb4, 0xcb);
        public static readonly V3 StructureInk = ColorUtil.FromSrgb(0x0e, 0x6f, 0x84);
        public static readonly V3 StructureLit = ColorUtil.FromSrgb(0x63, 0xdf, 0xef);
        public static readonly V3 Axis = ColorUtil.FromSrgb(0xe2, 0x9a, 0x3c);
        public static readonly V3 AxisInk = ColorUtil.FromSrgb(0xb2, 0x6a, 0x18);
        /// <summary>Heavier red waist lines that lie on the surface.</summary>
        public static readonly V3 MidRed = ColorUtil.FromSrgb(0xe8, 0x3b, 0x2f);
        /// <summary>Perspective guides: one colour per world axis.</summary>
        public static readonly V3 PerspX = ColorUtil.FromSrgb(0xe2, 0x6d, 0x5c);
        public static readonly V3 PerspY = ColorUtil.FromSrgb(0x86, 0xc4, 0x6a);
        public static readonly V3 PerspZ = ColorUtil.FromSrgb(0x63, 0xa6, 0xe2);

        // lights (direction towards the light, intensity)
        public static readonly V3 KeyDir = new V3(-0.46, 0.80, 0.60).Normalized();
        public static readonly V3 KeyColor = new(1.05, 1.00, 0.93);
        public static readonly V3 FillDir = new V3(0.85, 0.22, 0.42).Normalized();
        public static readonly V3 FillColor = new(0.30, 0.36, 0.46);
        public static readonly V3 BackDir = new V3(0.10, 0.45, -0.88).Normalized();
        public static readonly V3 BackColor = new(0.18, 0.21, 0.27);

        public static readonly V3 SkyAmbient = new(0.30, 0.35, 0.44);
        public static readonly V3 GroundAmbient = new(0.16, 0.14, 0.12);
    }

    public sealed class RenderOptions
    {
        public int Width = 960;
        public int Height = 640;
        public int Supersample = 1;
        public bool DrawLines = true;
        public double Exposure = 1.05;
    }

    public sealed class RenderResult
    {
        public int Width;
        public int Height;
        public byte[] Bgra = Array.Empty<byte>();
    }

    /// <summary>
    /// Reusable working buffers for the ray caster. Rendering at interactive rates on a phone means
    /// a few dozen frames per second, and allocating three pixel sized arrays per frame is what makes
    /// that feel like GC stalls; pass one of these in and they are recycled instead.
    /// </summary>
    public sealed class RenderBuffers
    {
        public float[] Rgb = Array.Empty<float>();
        public float[] Depth = Array.Empty<float>();
        public byte[] Bgra = Array.Empty<byte>();

        public void Ensure(int px, int outPx)
        {
            if (Rgb == null || Rgb.Length < px * 3) Rgb = new float[px * 3];
            if (Depth == null || Depth.Length < px) Depth = new float[px];
            if (Bgra == null || Bgra.Length < outPx * 4) Bgra = new byte[outPx * 4];
        }
    }

    /// <summary>
    /// Software ray caster that produces the shaded view together with its line work.
    /// No third party dependencies; everything is analytic.
    /// </summary>
    public static class Renderer
    {
        public static RenderResult Render(Scene scene, RenderOptions opt)
        {
            return Render(scene, opt, null);
        }

        public static RenderResult Render(Scene scene, RenderOptions opt, RenderBuffers buffers)
        {
            int ss = Math.Max(1, Math.Min(3, opt.Supersample));
            int w = Math.Max(4, opt.Width) * ss;
            int h = Math.Max(4, opt.Height) * ss;

            Camera cam = scene.Camera.Clone();
            cam.Width = w;
            cam.Height = h;
            cam.Update();

            var tracer = new Tracer(scene, cam, w, h);
            float[] rgb;
            float[] depth;
            if (buffers != null)
            {
                buffers.Ensure(w * h, opt.Width * opt.Height);
                rgb = buffers.Rgb;
                depth = buffers.Depth;
            }
            else
            {
                rgb = new float[w * h * 3];
                depth = new float[w * h];
            }
            // "nothing in front" by default: only solid hits write depth, so background and floor
            // pixels never occlude the construction lines
            for (int i = 0; i < depth.Length; i++) depth[i] = float.PositiveInfinity;

            switch (scene.Mode)
            {
                case ShadingMode.Outline:
                    tracer.TraceDepthOnly(rgb, depth);
                    break;
                case ShadingMode.Translucent:
                    tracer.TraceTranslucent(rgb, depth);
                    break;
                default:
                    tracer.TraceOpaque(rgb, depth);
                    break;
            }

            if (opt.DrawLines)
            {
                LineGen.Build(scene, cam, out List<Segment> segments);
                LineCanvas.Draw(scene, cam, segments, w, h, rgb, depth, ss);
                LineCanvas.DrawVanishingPoints(scene, cam, segments, w, h, rgb, ss);
            }

            byte[] bgra = Resolve(rgb, w, h, opt.Width, opt.Height, ss, opt.Exposure,
                                  buffers != null ? buffers.Bgra : null);
            if (buffers != null) buffers.Bgra = bgra;

            return new RenderResult { Width = opt.Width, Height = opt.Height, Bgra = bgra };
        }

        // ------------------------------------------------------------------ resolve

        private static byte[] Resolve(float[] rgb, int w, int h, int outW, int outH, int ss,
                                      double exposure, byte[] reuse)
        {
            byte[] dst = reuse != null && reuse.Length >= outW * outH * 4
                ? reuse
                : new byte[outW * outH * 4];
            double inv = 1.0 / (ss * ss);

            for (int y = 0; y < outH; y++)
            {                for (int x = 0; x < outW; x++)
                {
                    double r = 0, g = 0, b = 0;
                    for (int sy = 0; sy < ss; sy++)
                    {
                        int row = (y * ss + sy) * w;
                        for (int sx = 0; sx < ss; sx++)
                        {
                            int i = (row + x * ss + sx) * 3;
                            r += rgb[i];
                            g += rgb[i + 1];
                            b += rgb[i + 2];
                        }
                    }
                    r *= inv; g *= inv; b *= inv;

                    double nx = (x + 0.5) / outW * 2 - 1;
                    double ny = (y + 0.5) / outH * 2 - 1;
                    double vig = 1.0 - 0.16 * (nx * nx * 0.75 + ny * ny);
                    r *= vig; g *= vig; b *= vig;

                    double dith = Hash(x, y) - 0.5;

                    int o = (y * outW + x) * 4;
                    dst[o + 0] = Encode(b, exposure, dith);
                    dst[o + 1] = Encode(g, exposure, dith);
                    dst[o + 2] = Encode(r, exposure, dith);
                    dst[o + 3] = 255;
                }
            }
            return dst;
        }

        private static double Hash(int x, int y)
        {
            uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663);
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return (h & 0xffff) / 65535.0;
        }

        private static byte Encode(double linear, double exposure, double dither)
        {
            double x = linear * exposure;
            if (x <= 0) return (byte)(dither > 0.5 ? 1 : 0);

            // tone map + sRGB encode through a lookup table (Math.Exp/Pow per channel was the
            // single most expensive thing in the whole pipeline)
            double f = x * ToneScale;
            if (f >= ToneSteps - 1) return 255;
            int i = (int)f;
            double s = ToneLut[i] + (ToneLut[i + 1] - ToneLut[i]) * (f - i) + dither;
            int v = (int)Math.Round(s);
            return (byte)(v < 0 ? 0 : (v > 255 ? 255 : v));
        }

        private const int ToneSteps = 2048;
        private const double ToneMax = 6.0;
        private const double ToneScale = ToneSteps / ToneMax;
        private static readonly double[] ToneLut = BuildToneLut();

        private static double[] BuildToneLut()
        {
            var lut = new double[ToneSteps + 1];
            for (int i = 0; i <= ToneSteps; i++)
            {
                double x = i / ToneScale;
                double v = 1.0 - Math.Exp(-x);
                lut[i] = ColorUtil.LinearToSrgb(v) * 255.0;
            }
            return lut;
        }
    }

    /// <summary>Per-pixel ray casting.</summary>
    internal sealed class Tracer
    {
        private readonly Scene _scene;
        private readonly Camera _cam;
        private readonly int _w, _h;
        private readonly V3 _eye, _fwd, _right, _up;
        private readonly double _tanHalf, _aspect, _halfAngle;
        private readonly bool _fisheye;
        private readonly bool _groundVisible;
        /// <summary>Snapshot of the visible solids; iterating a yield-iterator per pixel allocates.</summary>
        private readonly Solid[] _solids;

        private const int MaxHits = 16;

        public Tracer(Scene scene, Camera cam, int w, int h)
        {
            _scene = scene;
            _solids = new List<Solid>(scene.ActiveSolids).ToArray();
            _cam = cam;
            _w = w;
            _h = h;
            _eye = cam.Eye;
            _fwd = cam.Forward;
            _right = cam.Right;
            _up = cam.Up;
            _halfAngle = cam.FovYDeg * 0.5 * MathUtil.Deg2Rad;
            _tanHalf = Math.Tan(_halfAngle);
            _aspect = (double)w / h;
            _fisheye = cam.Fisheye;
            _groundVisible = _eye.Y > 0.02 * scene.EdgeLength;
        }

        private void RayFor(int px, int py, out V3 origin, out V3 dir)
        {
            double ndcX = 2.0 * (px + 0.5) / _w - 1.0;
            double ndcY = 1.0 - 2.0 * (py + 0.5) / _h;
            origin = _eye;

            if (_fisheye)
            {
                double dx = ndcX * _aspect;
                double dy = ndcY;
                double rr = Math.Sqrt(dx * dx + dy * dy);
                double theta = rr * _halfAngle;
                if (rr < 1e-9)
                {
                    dir = _fwd;
                }
                else
                {
                    V3 radial = (_right * (dx / rr) + _up * (dy / rr)).Normalized();
                    dir = (_fwd * Math.Cos(theta) + radial * Math.Sin(theta)).Normalized();
                }
                return;
            }

            dir = (_fwd + _right * (ndcX * _tanHalf * _aspect) + _up * (ndcY * _tanHalf)).Normalized();
        }

        // ------------------------------------------------------------------ opaque

        public void TraceOpaque(float[] rgb, float[] depth)
        {
            Parallel.For(0, _h, py =>
            {
                Span<double> ts = stackalloc double[2];
                Span<V3> ns = stackalloc V3[2];
                int row = py * _w;
                for (int px = 0; px < _w; px++)
                {
                    RayFor(px, py, out V3 o, out V3 d);
                    double bestT = double.PositiveInfinity;
                    V3 bestN = V3.Zero;
                    Solid bestS = null;

                    for (int si2 = 0; si2 < _solids.Length; si2++)
                    {
                        Solid s = _solids[si2];
                        int n = s.Intersect(o, d, ts, ns);
                        if (n > 0 && ts[0] < bestT)
                        {
                            bestT = ts[0];
                            bestN = ns[0];
                            bestS = s;
                        }
                    }

                    V3 col;
                    int idx = row + px;
                    if (bestS != null)
                    {
                        V3 p = o + d * bestT;
                        V3 v = -d;
                        double ao = SurfaceAo(bestS, p);
                        col = Shade(bestN, v, Palette.Plaster, ao);
                        depth[idx] = (float)bestT;
                    }
                    else if (_groundVisible && d.Y < -1e-6)
                    {
                        double t = -o.Y / d.Y;
                        V3 p = o + d * t;
                        col = ShadeGround(p, d);
                        // the floor does not occlude line work: guides that dive below the ground
                        // (the vertical vanishing point usually sits there) must stay visible
                    }
                    else
                    {
                        col = Background(d, 0.0);
                    }

                    int i3 = idx * 3;
                    rgb[i3] = (float)col.X;
                    rgb[i3 + 1] = (float)col.Y;
                    rgb[i3 + 2] = (float)col.Z;
                }
            });
        }

        // ------------------------------------------------------------------ translucent

        public void TraceTranslucent(float[] rgb, float[] depth)
        {
            Parallel.For(0, _h, py =>
            {
                Span<double> ts = stackalloc double[2];
                Span<V3> ns = stackalloc V3[2];
                Span<double> hitT = stackalloc double[MaxHits];
                Span<V3> hitN = stackalloc V3[MaxHits];
                Span<int> hitS = stackalloc int[MaxHits];
                int row = py * _w;

                for (int px = 0; px < _w; px++)
                {
                    RayFor(px, py, out V3 o, out V3 d);
                    int count = 0;

                    for (int si = 0; si < _solids.Length; si++)
                    {
                        Solid s = _solids[si];
                        int n = s.Intersect(o, d, ts, ns);
                        for (int k = 0; k < n && count < MaxHits; k++)
                        {
                            hitT[count] = ts[k];
                            hitN[count] = ns[k];
                            hitS[count] = si;
                            count++;
                        }
                    }

                    // insertion sort by distance
                    for (int i = 1; i < count; i++)
                    {
                        double t = hitT[i]; V3 n0 = hitN[i]; int s0 = hitS[i];
                        int j = i - 1;
                        while (j >= 0 && hitT[j] > t)
                        {
                            hitT[j + 1] = hitT[j]; hitN[j + 1] = hitN[j]; hitS[j + 1] = hitS[j];
                            j--;
                        }
                        hitT[j + 1] = t; hitN[j + 1] = n0; hitS[j + 1] = s0;
                    }

                    V3 baseCol;
                    int idx = row + px;
                    if (_groundVisible && d.Y < -1e-6)
                    {
                        double t = -o.Y / d.Y;
                        baseCol = ShadeGround(o + d * t, d);
                    }
                    else
                    {
                        baseCol = Background(d, 0.0);
                    }

                    V3 v = -d;
                    double alpha = count > 0 ? MathUtil.Clamp(0.62 / count + 0.16, 0.20, 0.55) : 0.0;

                    for (int i = count - 1; i >= 0; i--)
                    {
                        V3 n = hitN[i];
                        if (V3.Dot(n, d) > 0) n = -n; // back face, shade as if lit from outside
                        V3 p = o + d * hitT[i];
                        V3 col = Shade(n, v, Palette.Plaster, 0.55 + 0.45 * SurfaceAo(null, p));
                        baseCol = col * alpha + baseCol * (1 - alpha);
                        if (i == 0) depth[idx] = (float)hitT[i];
                    }

                    int i3 = idx * 3;
                    rgb[i3] = (float)baseCol.X;
                    rgb[i3 + 1] = (float)baseCol.Y;
                    rgb[i3 + 2] = (float)baseCol.Z;
                }
            });
        }

        // ------------------------------------------------------------------ outline

        public void TraceDepthOnly(float[] rgb, float[] depth)
        {
            Parallel.For(0, _h, py =>
            {
                Span<double> ts = stackalloc double[2];
                Span<V3> ns = stackalloc V3[2];
                int row = py * _w;

                for (int px = 0; px < _w; px++)
                {
                    RayFor(px, py, out V3 o, out V3 d);
                    double bestT = double.PositiveInfinity;

                    for (int si = 0; si < _solids.Length; si++)
                    {
                        int n = _solids[si].Intersect(o, d, ts, ns);
                        if (n > 0 && ts[0] < bestT) bestT = ts[0];
                    }

                    V3 col = Background(d, 0.0);
                    int idx = row + px;
                    depth[idx] = bestT > 0 && !double.IsInfinity(bestT) ? (float)bestT : float.PositiveInfinity;

                    int i3 = idx * 3;
                    rgb[i3] = (float)col.X;
                    rgb[i3 + 1] = (float)col.Y;
                    rgb[i3 + 2] = (float)col.Z;
                }
            });
        }

        // ------------------------------------------------------------------ shading

        private double SurfaceAo(Solid s, in V3 p)
        {
            if (s == null) return 1.0;
            double f = MathUtil.Clamp01((p.Y - s.BaseY) / Math.Max(1e-6, s.Height));
            // ambient occlusion from the floor bounce
            return 0.55 + 0.45 * MathUtil.SmoothStep(0.0, 0.45, f);
        }

        public static V3 Background(in V3 dir, double extra)
        {
            // sampled table: Math.Pow + Math.Exp per pixel is far too slow on one core
            double f = (MathUtil.Clamp(dir.Y, -1, 1) + 1.0) * 128.0;
            int i = (int)f;
            if (i < 0) i = 0; else if (i > 255) i = 255;
            V3 c = BgLut[i].Lerp(BgLut[i + 1], f - i);
            return c + new V3(extra, extra, extra);
        }

        private static readonly V3[] BgLut = BuildBgLut();

        private static V3[] BuildBgLut()
        {
            var lut = new V3[257];
            for (int i = 0; i <= 256; i++)
            {
                double y = i / 128.0 - 1.0;
                double t = MathUtil.Clamp01(0.5 + 0.5 * y);
                V3 c = Palette.BgBottom.Lerp(Palette.BgTop, Math.Sqrt(t * Math.Sqrt(t)));
                double hz = Math.Exp(-Math.Abs(y) * 9.0);
                lut[i] = c + Palette.Horizon * (hz * 0.055);
            }
            return lut;
        }

        private V3 ShadeGround(in V3 p, in V3 d)
        {
            V3 n = V3.UnitY;
            double a = Math.Max(0.05, _scene.EdgeLength);

            // Contact occlusion: a narrow band hugging the outline of every footprint.
            // The interior of a footprint is left alone so that the visible sliver of
            // floor just in front of a solid does not turn into a puddle.
            double occ = 1.0;
            for (int i = 0; i < _solids.Length; i++)
            {
                Solid s = _solids[i];
                double sd = FootprintDistance(s, s.Center.X, s.Center.Z, p.X, p.Z);
                double outside = 1.0 - MathUtil.SmoothStep(0.0, 0.34 * a, Math.Max(0.0, sd));
                double inside = MathUtil.SmoothStep(-0.30 * a, 0.0, sd);
                occ *= 1.0 - 0.48 * outside * inside;
            }

            double ndl = Math.Max(0, V3.Dot(n, Palette.KeyDir));
            V3 ambient = Palette.SkyAmbient * 1.15 + Palette.GroundAmbient;
            V3 c = Palette.Ground * (occ * (ambient + Palette.KeyColor * (ndl * 0.85)));

            // aerial perspective: the floor dissolves into the backdrop at the horizon
            double viewDist = (p - _eye).Length;
            double fog = 1.0 - Math.Exp(-Math.Max(0.0, viewDist - 5.0 * a) * 0.020 / a);
            V3 backdrop = Background(new V3(d.X, 0.012, d.Z).Normalized(), 0.0);
            c = c.Lerp(backdrop, MathUtil.Clamp01(fog));
            return c;
        }

        private static double FootprintDistance(Solid s, double cx, double cz, double x, double z)
        {
            double dx = x - cx, dz = z - cz;
            if (s.Kind is SolidKind.Cube or SolidKind.Cuboid)
            {
                double h = s.HalfWidth;
                double qx = Math.Abs(dx) - h;
                double qz = Math.Abs(dz) - h;
                double ox = Math.Max(qx, 0), oz = Math.Max(qz, 0);
                return Math.Sqrt(ox * ox + oz * oz) + Math.Min(Math.Max(qx, qz), 0);
            }
            return Math.Sqrt(dx * dx + dz * dz) - s.Radius;
        }

        public static V3 Shade(in V3 n, in V3 v, in V3 albedo, double ao)
        {
            V3 key = Palette.KeyColor * Math.Max(0, V3.Dot(n, Palette.KeyDir));
            V3 fill = Palette.FillColor * Math.Max(0, V3.Dot(n, Palette.FillDir));
            V3 back = Palette.BackColor * Math.Max(0, V3.Dot(n, Palette.BackDir));

            double up = 0.5 + 0.5 * n.Y;
            V3 ambient = Palette.GroundAmbient.Lerp(Palette.SkyAmbient, up) * ao;

            V3 c = albedo * (ambient + key + fill + back);

            // sheen
            V3 h = (Palette.KeyDir + v).Normalized();
            double hd = V3.Dot(n, h);
            double spec = hd > 0.05 ? Math.Pow(hd, 42.0) * 0.10 : 0.0;
            c += new V3(spec, spec, spec) * (0.7 + 0.3 * up);

            // subtle gradient falloff so large surfaces do not read as flat cards
            double e0 = 1.0 - MathUtil.Clamp01(Math.Abs(V3.Dot(n, v)));
            double edge = e0 * e0 * e0;
            c += Palette.SkyAmbient * (edge * 0.10);
            return c;
        }
    }
}
