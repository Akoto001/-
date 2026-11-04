using System;
using System.Collections.Generic;
using GeoViewer.Core;

namespace GeoViewer.Render
{
    public enum LineKind
    {
        /// <summary>Hard crease on the surface (cube edges, rims).</summary>
        Edge = 0,
        /// <summary>Contour generator: where the surface turns away from the viewer.</summary>
        Silhouette = 1,
        /// <summary>Construction line that lives inside the volume.</summary>
        Structure = 2,
        /// <summary>Vertical construction axis (dash-dot).</summary>
        Axis = 3,
        /// <summary>Perspective guide that converges on a vanishing point.</summary>
        Perspective = 4,
        /// <summary>Heavier red "waist" line: the mid lines that sit on the surface itself.</summary>
        Mid = 5
    }

    public struct Segment
    {
        public V3 A;
        public V3 B;
        public LineKind Kind;
        public double Scale;
        public bool HasTint;
        public V3 Tint;
        /// <summary>Construction line that runs through the body: never hidden by the surface.</summary>
        public bool Through;

        public Segment(V3 a, V3 b, LineKind kind, double scale = 1.0)
        {
            A = a; B = b; Kind = kind; Scale = scale; HasTint = false; Tint = V3.Zero; Through = false;
        }

        public Segment(V3 a, V3 b, LineKind kind, V3 tint, double scale = 1.0)
        {
            A = a; B = b; Kind = kind; Scale = scale; HasTint = true; Tint = tint; Through = false;
        }
    }

    /// <summary>
    /// Turns the analytic solids into the line work of the drawing: hard edges, silhouette
    /// contours and construction ("structural") lines.
    /// </summary>
    public static class LineGen
    {
        public const int CircleSamples = 108;

        public static void Build(Scene scene, Camera cam, out List<Segment> segments)
        {
            segments = new List<Segment>(4096);
            V3 eye = cam.Eye;

            foreach (Solid s in scene.ActiveSolids)
            {
                if (s.Mesh != null)
                {
                    // Faceted meshes carry their own plane boundaries; silhouette / structure /
                    // mid lines all assume an analytic solid, so only the facet edges apply.
                    AddMeshEdges(s, segments);
                    if (scene.PerspectiveLines) AddPerspective(s, cam, segments);
                    continue;
                }
                AddEdges(s, segments);
                AddSilhouette(s, eye, segments);
                if (scene.StructureLines)
                {
                    AddStructure(s, eye, segments);
                    AddMidLines(s, segments);
                }
                if (scene.PerspectiveLines) AddPerspective(s, cam, segments);
            }

            // the model may be spun in place: rotate its lines, never the ground or the perspective guides
            double roll = scene.ModelRollDeg;
            if (Math.Abs(roll) > 1e-9)
            {
                double lift = 0.0;
                for (int i4 = 0; i4 < scene.Solids.Length; i4++) lift = Math.Max(lift, scene.Solids[i4].RollLift);
                double r2 = roll * MathUtil.Deg2Rad;
                V3 ax2 = scene.Solids[0].SpinAxis;
                double c2 = Math.Cos(r2), s2 = Math.Sin(r2);
                for (int i3 = 0; i3 < segments.Count; i3++)
                {
                    Segment g = segments[i3];
                    if (g.Kind == LineKind.Perspective) continue;
                    segments[i3] = new Segment(
                        RotAxis(g.A, ax2, r2),
                        RotAxis(g.B, ax2, r2),
                        g.Kind, g.Scale);
                }
            }

            double yaw = scene.ModelYawDeg;
            if (Math.Abs(yaw) > 1e-9)
            {
                double rr = yaw * MathUtil.Deg2Rad;
                double cc = Math.Cos(rr), ss = Math.Sin(rr);
                for (int i2 = 0; i2 < segments.Count; i2++)
                {
                    Segment g = segments[i2];
                    if (g.Kind == LineKind.Perspective) continue;
                    segments[i2] = new Segment(
                        new V3(g.A.X * cc + g.A.Z * ss, g.A.Y, -g.A.X * ss + g.A.Z * cc),
                        new V3(g.B.X * cc + g.B.Z * ss, g.B.Y, -g.B.X * ss + g.B.Z * cc),
                        g.Kind, g.Scale);
                }
            }
        }

        private static V3 RotAxis(in V3 p, in V3 axis, double radians)
        {
            double c = Math.Cos(radians), s = Math.Sin(radians);
            if (Math.Abs(axis.X) < 1e-12 && Math.Abs(axis.Y) < 1e-12 && Math.Abs(axis.Z - 1.0) < 1e-12)
                return new V3(p.X * c - p.Y * s, p.X * s + p.Y * c, p.Z);
            double kd = V3.Dot(axis, p) * (1 - c);
            V3 cr = V3.Cross(axis, p) * s;
            return new V3(p.X * c + kd * axis.X + cr.X, p.Y * c + kd * axis.Y + cr.Y, p.Z * c + kd * axis.Z + cr.Z);
        }

        private static void AddMeshEdges(Solid s, List<Segment> list)
        {
            MeshSolid mesh = s.Mesh!;
            for (int i = 0; i + 1 < mesh.Edges.Length; i += 2)
            {
                V3 a = mesh.Vertices[mesh.Edges[i]];
                V3 b = mesh.Vertices[mesh.Edges[i + 1]];
                list.Add(new Segment(a, b, LineKind.Edge));
            }
        }

        // ---------------------------------------------------------------- hard edges

        private static void AddEdges(Solid s, List<Segment> list)
        {
            switch (s.Kind)
            {
                case SolidKind.Cube:
                case SolidKind.Cuboid:
                    AddBoxEdges(s, list);
                    break;
                case SolidKind.Cylinder:
                    AddCircle(list, new V3(s.Center.X, s.BaseY + s.Height, s.Center.Z), s.Radius, LineKind.Edge);
                    AddCircle(list, new V3(s.Center.X, s.BaseY, s.Center.Z), s.Radius, LineKind.Edge);
                    break;
                case SolidKind.Cone:
                    AddCircle(list, new V3(s.Center.X, s.BaseY, s.Center.Z), s.Radius, LineKind.Edge);
                    break;
            }
        }

        private static void AddBoxEdges(Solid s, List<Segment> list)
        {
            V3[] c = new V3[8];
            for (int i = 0; i < 8; i++)
            {
                c[i] = new V3((i & 1) == 0 ? s.Min.X : s.Max.X,
                              (i & 2) == 0 ? s.Min.Y : s.Max.Y,
                              (i & 4) == 0 ? s.Min.Z : s.Max.Z);
            }
            // bottom 0-1-3-2, top 4-5-7-6, verticals
            Add(list, c[0], c[1]); Add(list, c[1], c[3]); Add(list, c[3], c[2]); Add(list, c[2], c[0]);
            Add(list, c[4], c[5]); Add(list, c[5], c[7]); Add(list, c[7], c[6]); Add(list, c[6], c[4]);
            Add(list, c[0], c[4]); Add(list, c[1], c[5]); Add(list, c[2], c[6]); Add(list, c[3], c[7]);

            static void Add(List<Segment> l, V3 a, V3 b) => l.Add(new Segment(a, b, LineKind.Edge));
        }

        private static void AddCircle(List<Segment> list, V3 center, double r, LineKind kind, double scale = 1.0)
        {
            if (r <= 1e-9) return;
            V3 prev = new(center.X + r, center.Y, center.Z);
            for (int i = 1; i <= CircleSamples; i++)
            {
                double a = i * (2 * Math.PI / CircleSamples);
                V3 p = new(center.X + r * Math.Cos(a), center.Y, center.Z + r * Math.Sin(a));
                list.Add(new Segment(prev, p, kind, scale));
                prev = p;
            }
        }

        // ---------------------------------------------------------------- silhouettes

        private static void AddSilhouette(Solid s, V3 eye, List<Segment> list)
        {
            switch (s.Kind)
            {
                case SolidKind.Sphere: AddSphereContour(s, eye, list); break;
                case SolidKind.Cylinder: AddCylinderContour(s, eye, list); break;
                case SolidKind.Cone: AddConeContour(s, eye, list); break;
                // Boxes: every silhouette edge is already one of the 12 creases.
            }
        }

        private static void AddSphereContour(Solid s, V3 eye, List<Segment> list)
        {
            V3 c = new(s.Center.X, s.BaseY + s.Radius, s.Center.Z);
            V3 v = (eye - c).Normalized();
            if (v.LengthSq < 0.5) v = V3.UnitZ;
            V3 a = V3.Cross(v, V3.UnitY);
            if (a.LengthSq < 1e-6) a = V3.Cross(v, V3.UnitZ);
            a = a.Normalized();
            V3 b = V3.Cross(v, a).Normalized();

            V3 prev = c + a * s.Radius;
            for (int i = 1; i <= CircleSamples; i++)
            {
                double t = i * (2 * Math.PI / CircleSamples);
                V3 p = c + (a * Math.Cos(t) + b * Math.Sin(t)) * s.Radius;
                list.Add(new Segment(prev, p, LineKind.Silhouette));
                prev = p;
            }
        }

        private static void AddCylinderContour(Solid s, V3 eye, List<Segment> list)
        {
            double vx = eye.X - s.Center.X;
            double vz = eye.Z - s.Center.Z;
            if (vx * vx + vz * vz < 1e-9) return;

            // Normal of the lateral surface: n(theta) = (cos, 0, sin); contour where n . view = 0
            double t0 = Math.Atan2(-vx, vz);
            for (int k = 0; k < 2; k++)
            {
                double t = t0 + k * Math.PI;
                double nx = Math.Cos(t), nz = Math.Sin(t);
                V3 a = new(s.Center.X + nx * s.Radius, s.BaseY, s.Center.Z + nz * s.Radius);
                V3 b = new(a.X, s.BaseY + s.Height, a.Z);
                list.Add(new Segment(a, b, LineKind.Silhouette));
            }
        }

        private static void AddConeContour(Solid s, V3 eye, List<Segment> list)
        {
            double k = s.Radius / s.Height;
            V3 apex = new(s.Center.X, s.BaseY + s.Height, s.Center.Z);

            double vx = eye.X - s.Center.X;
            double vz = eye.Z - s.Center.Z;
            double vy = eye.Y - (s.BaseY + s.Height);
            double m = Math.Sqrt(vx * vx + vz * vz);
            if (m < 1e-9) return;

            double cosT = MathUtil.Clamp(-k * vy / m, -1.0, 1.0);
            double phi = Math.Atan2(vz, vx);
            double d = Math.Acos(cosT);

            for (int i = 0; i < 2; i++)
            {
                double t = phi + (i == 0 ? d : -d);
                V3 baseP = new(s.Center.X + Math.Cos(t) * s.Radius, s.BaseY, s.Center.Z + Math.Sin(t) * s.Radius);
                list.Add(new Segment(baseP, apex, LineKind.Silhouette));
            }
        }

        // ---------------------------------------------------------------- structure lines

        private static void AddStructure(Solid s, V3 eye, List<Segment> list)
        {
            double y0 = s.BaseY, y1 = s.BaseY + s.Height;

            // vertical axis, dash-dot
            AddDashed(list, new V3(s.Center.X, y0, s.Center.Z), new V3(s.Center.X, y1, s.Center.Z),
                      LineKind.Axis, 0.11 * s.A, 0.055 * s.A, 0.016 * s.A);

            // cross sections at 1/4, 1/2, 3/4
            for (int i = 1; i <= 3; i++)
            {
                double f = i * 0.25;
                AddCrossSection(s, y0 + s.Height * f, list);
            }

            // diameter lines through the middle section
            double ym = y0 + s.Height * 0.5;
            double hw = HalfWidthAt(s, ym);
            list.Add(new Segment(new V3(s.Center.X - hw, ym, s.Center.Z), new V3(s.Center.X + hw, ym, s.Center.Z), LineKind.Structure, 0.85));
            list.Add(new Segment(new V3(s.Center.X, ym, s.Center.Z - hw), new V3(s.Center.X, ym, s.Center.Z + hw), LineKind.Structure, 0.85));

            switch (s.Kind)
            {
                case SolidKind.Sphere:
                {
                    V3 c = new(s.Center.X, s.BaseY + s.Radius, s.Center.Z);
                    AddCircle(list, c, s.Radius, LineKind.Structure, 0.9);
                    // meridian facing the viewer
                    V3 v = eye - c;
                    V3 h = new(v.X, 0, v.Z);
                    if (h.LengthSq < 1e-9) h = V3.UnitZ;
                    h = h.Normalized();
                    V3 prev = c + h * s.Radius;
                    for (int i = 1; i <= CircleSamples; i++)
                    {
                        double t = i * (2 * Math.PI / CircleSamples);
                        V3 p = c + (h * Math.Cos(t) + V3.UnitY * Math.Sin(t)) * s.Radius;
                        list.Add(new Segment(prev, p, LineKind.Structure, 0.9));
                        prev = p;
                    }
                    break;
                }
                case SolidKind.Cylinder:
                {
                    // four generatrices
                    for (int k = 0; k < 4; k++)
                    {
                        double t = k * Math.PI * 0.5;
                        V3 a = new(s.Center.X + Math.Cos(t) * s.Radius, y0, s.Center.Z + Math.Sin(t) * s.Radius);
                        V3 b = new(a.X, y1, a.Z);
                        list.Add(new Segment(a, b, LineKind.Structure, 0.8));
                    }
                    break;
                }
                case SolidKind.Cone:
                {
                    for (int k = 0; k < 4; k++)
                    {
                        double t = k * Math.PI * 0.5;
                        V3 a = new(s.Center.X + Math.Cos(t) * s.Radius, y0, s.Center.Z + Math.Sin(t) * s.Radius);
                        list.Add(new Segment(a, new V3(s.Center.X, y1, s.Center.Z), LineKind.Structure, 0.8));
                    }
                    break;
                }
                case SolidKind.Cube:
                case SolidKind.Cuboid:
                {
                    double ym2 = y0 + s.Height;
                    list.Add(new Segment(new V3(s.Min.X, ym2, s.Min.Z), new V3(s.Max.X, ym2, s.Max.Z), LineKind.Structure, 0.75));
                    list.Add(new Segment(new V3(s.Min.X, ym2, s.Max.Z), new V3(s.Max.X, ym2, s.Min.Z), LineKind.Structure, 0.75));
                    break;
                }
            }
        }

        // ---------------------------------------------------------------- red mid lines

        /// <summary>
        /// The heavier red lines: the "waist" construction lines that lie on the surface itself —
        /// the mid lines of every flat face, the mid plane traces of the bodies of revolution and,
        /// for the sphere, the meridians through the poles (the poles being where the vertical
        /// axis through the centre of the middle circle meets the surface).
        /// </summary>
        private static void AddMidLines(Solid s, List<Segment> list)
        {
            double y0 = s.BaseY, y1 = s.BaseY + s.Height;
            double cx = s.Center.X, cz = s.Center.Z;
            double ym = (y0 + y1) * 0.5;

            switch (s.Kind)
            {
                case SolidKind.Cube:
                case SolidKind.Cuboid:
                {
                    double x0 = s.Min.X, x1 = s.Max.X, z0 = s.Min.Z, z1 = s.Max.Z;

                    // vertical mid line on the four side faces
                    AddMid(list, new V3(x0, y0, cz), new V3(x0, y1, cz));
                    AddMid(list, new V3(x1, y0, cz), new V3(x1, y1, cz));
                    AddMid(list, new V3(cx, y0, z0), new V3(cx, y1, z0));
                    AddMid(list, new V3(cx, y0, z1), new V3(cx, y1, z1));

                    // waist line round the four side faces
                    AddMid(list, new V3(x0, ym, z0), new V3(x0, ym, z1));
                    AddMid(list, new V3(x1, ym, z0), new V3(x1, ym, z1));
                    AddMid(list, new V3(x0, ym, z0), new V3(x1, ym, z0));
                    AddMid(list, new V3(x0, ym, z1), new V3(x1, ym, z1));

                    // mid cross on the top and bottom faces
                    AddMid(list, new V3(x0, y1, cz), new V3(x1, y1, cz));
                    AddMid(list, new V3(cx, y1, z0), new V3(cx, y1, z1));
                    AddMid(list, new V3(x0, y0, cz), new V3(x1, y0, cz));
                    AddMid(list, new V3(cx, y0, z0), new V3(cx, y0, z1));
                    break;
                }

                case SolidKind.Cylinder:
                {
                    double r = s.Radius;
                    V3 e0 = new(cx + r, 0, cz), e1 = new(cx - r, 0, cz);
                    V3 e2 = new(cx, 0, cz + r), e3 = new(cx, 0, cz - r);

                    // cross on both rims
                    AddMid(list, new V3(e0.X, y1, e0.Z), new V3(e1.X, y1, e1.Z));
                    AddMid(list, new V3(e2.X, y1, e2.Z), new V3(e3.X, y1, e3.Z));
                    AddMid(list, new V3(e0.X, y0, e0.Z), new V3(e1.X, y0, e1.Z));
                    AddMid(list, new V3(e2.X, y0, e2.Z), new V3(e3.X, y0, e3.Z));

                    // the four generatrices at those rim points
                    AddMid(list, new V3(e0.X, y0, e0.Z), new V3(e0.X, y1, e0.Z));
                    AddMid(list, new V3(e1.X, y0, e1.Z), new V3(e1.X, y1, e1.Z));
                    AddMid(list, new V3(e2.X, y0, e2.Z), new V3(e2.X, y1, e2.Z));
                    AddMid(list, new V3(e3.X, y0, e3.Z), new V3(e3.X, y1, e3.Z));
                    break;
                }

                case SolidKind.Cone:
                {
                    double r = s.Radius;
                    V3 apex = new(cx, y1, cz);
                    V3 e0 = new(cx + r, y0, cz), e1 = new(cx - r, y0, cz);
                    V3 e2 = new(cx, y0, cz + r), e3 = new(cx, y0, cz - r);

                    // cross on the base
                    AddMid(list, e0, e1);
                    AddMid(list, e2, e3);

                    // the four slanted generatrices up to the apex
                    AddMid(list, e0, apex);
                    AddMid(list, e1, apex);
                    AddMid(list, e2, apex);
                    AddMid(list, e3, apex);
                    break;
                }

                case SolidKind.Sphere:
                {
                    V3 c = new(cx, y0 + s.Radius, cz);

                    // polar axis: the perpendicular to the middle circle through its centre
                    AddMid(list, new V3(c.X, c.Y - s.Radius, c.Z), new V3(c.X, c.Y + s.Radius, c.Z), true);

                    // cross on the middle (horizontal) plane, meeting the axis at the centre
                    AddMid(list, new V3(c.X - s.Radius, c.Y, c.Z), new V3(c.X + s.Radius, c.Y, c.Z), true);
                    AddMid(list, new V3(c.X, c.Y, c.Z - s.Radius), new V3(c.X, c.Y, c.Z + s.Radius), true);

                    // the two meridians through those poles
                    AddMeridian(list, c, s.Radius, true);
                    AddMeridian(list, c, s.Radius, false);
                    break;
                }
            }
        }

        private static void AddMid(List<Segment> list, V3 a, V3 b, bool through = false)
        {
            list.Add(new Segment(a, b, LineKind.Mid) { Through = through });
        }

        /// <summary>Great circle through the poles, in the plane x = cx (planeX) or z = cz.</summary>
        private static void AddMeridian(List<Segment> list, V3 c, double r, bool planeX)
        {
            V3 prev = default;
            for (int i = 0; i <= CircleSamples; i++)
            {
                double t = i * (2 * Math.PI / CircleSamples);
                double ct = Math.Cos(t), st = Math.Sin(t);
                V3 p = planeX
                    ? new V3(c.X, c.Y + r * st, c.Z + r * ct)
                    : new V3(c.X + r * ct, c.Y + r * st, c.Z);
                if (i > 0) list.Add(new Segment(prev, p, LineKind.Mid));
                prev = p;
            }
        }

        private static void AddCrossSection(Solid s, double y, List<Segment> list)
        {
            double hw = HalfWidthAt(s, y);
            if (hw <= 1e-9) return;

            if (s.Kind is SolidKind.Cube or SolidKind.Cuboid)
            {
                V3 a0 = new(s.Center.X - hw, y, s.Center.Z - hw);
                V3 a1 = new(s.Center.X + hw, y, s.Center.Z - hw);
                V3 a2 = new(s.Center.X + hw, y, s.Center.Z + hw);
                V3 a3 = new(s.Center.X - hw, y, s.Center.Z + hw);
                selfEdges(list, a0, a1, a2, a3);
            }
            else
            {
                AddCircle(list, new V3(s.Center.X, y, s.Center.Z), hw, LineKind.Structure, 0.9);
            }

            static void selfEdges(List<Segment> l, V3 a0, V3 a1, V3 a2, V3 a3)
            {
                l.Add(new Segment(a0, a1, LineKind.Structure, 0.9));
                l.Add(new Segment(a1, a2, LineKind.Structure, 0.9));
                l.Add(new Segment(a2, a3, LineKind.Structure, 0.9));
                l.Add(new Segment(a3, a0, LineKind.Structure, 0.9));
            }
        }

        /// <summary>Half width of the solid at the given height (0 when outside).</summary>
        public static double HalfWidthAt(Solid s, double y)
        {
            double h = s.Height;
            double f = (y - s.BaseY) / h;
            if (f < -1e-9 || f > 1 + 1e-9) return 0;
            return s.Kind switch
            {
                SolidKind.Sphere => s.Radius * Math.Sqrt(Math.Max(0, 1 - (2 * f - 1) * (2 * f - 1))),
                SolidKind.Cone => s.Radius * (1 - f),
                _ => s.Kind is SolidKind.Cube or SolidKind.Cuboid ? s.A * 0.5 : s.Radius
            };
        }

        private static void AddDashed(List<Segment> list, V3 a, V3 b, LineKind kind, double dash, double gap, double dot)
        {
            V3 dir = b - a;
            double len = dir.Length;
            if (len <= 1e-9) return;
            dir = dir / len;

            double t = 0;
            bool longDash = true;
            while (t < len)
            {
                double l = longDash ? dash : dot;
                double e = Math.Min(len, t + l);
                list.Add(new Segment(a + dir * t, a + dir * e, kind, longDash ? 1.0 : 1.0));
                t = e + gap;
                longDash = !longDash;
            }
        }

        // ---------------------------------------------------------------- perspective guides

        /// <summary>Colour of the guides for each world axis (X: warm red, Y: green, Z: blue).</summary>
        private static V3 TintX => Palette.PerspX;
        private static V3 TintY => Palette.PerspY;
        private static V3 TintZ => Palette.PerspZ;

        /// <summary>
        /// Rays running towards the vanishing point of each world axis. They start at the far end
        /// of the solid and are cut far away from it: since the projected point approaches the
        /// vanishing point monotonically as t grows, 220a lands within a pixel of it, so the fan
        /// visibly converges and stops there instead of shooting past.
        /// </summary>
        public static void AddPerspective(Solid s, Camera cam, List<Segment> list)
        {
            double len = 220.0 * s.A;

            if (s.Kind is SolidKind.Cube or SolidKind.Cuboid)
            {
                AddBoxPerspective(s, cam, len, list);
                return;
            }

            AddVerticalPerspective(s, cam, len, list);
        }

        private static void AddBoxPerspective(Solid s, Camera cam, double len, List<Segment> list)
        {
            V3[] c = new V3[8];
            for (int i = 0; i < 8; i++)
            {
                c[i] = new V3((i & 1) == 0 ? s.Min.X : s.Max.X,
                              (i & 2) == 0 ? s.Min.Y : s.Max.Y,
                              (i & 4) == 0 ? s.Min.Z : s.Max.Z);
            }

            int[] e = { 0, 1, 2, 3, 4, 5, 6, 7,      // X-family edges
                        0, 2, 1, 3, 4, 6, 5, 7,      // Y-family edges
                        0, 4, 1, 5, 2, 6, 3, 7 };    // Z-family edges

            for (int k = 0; k < e.Length; k += 2)
                AddGuideTowardAxis(cam, c[e[k]], c[e[k + 1]], len, list);
        }

        private static void AddVerticalPerspective(Solid s, Camera cam, double len, List<Segment> list)
        {
            bool down = V3.Dot(V3.UnitY, cam.Forward) < 0;
            V3 vdir = down ? new V3(0, -1, 0) : V3.UnitY;

            // axis through the centre – the one guide every revolved solid has
            V3 aBot = new(s.Center.X, s.BaseY, s.Center.Z);
            V3 aTop = new(s.Center.X, s.BaseY + s.Height, s.Center.Z);
            AddGuide(cam, down ? aBot : aTop, vdir, len, TintY, list);

            // a cylinder also owns four vertical generators on its surface
            if (s.Kind != SolidKind.Cylinder) return;

            double r = s.Radius;
            double yBot = s.BaseY, yTop = s.BaseY + s.Height;
            V3[] around =
            {
                new(s.Center.X + r, 0, s.Center.Z),
                new(s.Center.X - r, 0, s.Center.Z),
                new(s.Center.X, 0, s.Center.Z + r),
                new(s.Center.X, 0, s.Center.Z - r)
            };

            foreach (V3 q in around)
            {
                V3 bottom = new(q.X, yBot, q.Z);
                V3 top = new(q.X, yTop, q.Z);
                AddGuide(cam, down ? bottom : top, vdir, len, TintY, list);
            }
        }

        private static void AddGuideTowardAxis(Camera cam, V3 pa, V3 pb, double len, List<Segment> list)
        {
            V3 d = pb - pa;
            double ax = Math.Abs(d.X), ay = Math.Abs(d.Y), az = Math.Abs(d.Z);
            V3 axis;
            V3 tint;
            if (ax >= ay && ax >= az) { axis = V3.UnitX; tint = TintX; }
            else if (ay >= az) { axis = V3.UnitY; tint = TintY; }
            else { axis = V3.UnitZ; tint = TintZ; }

            V3 dir = V3.Dot(axis, cam.Forward) > 0 ? axis : new V3(-axis.X, -axis.Y, -axis.Z);
            V3 start = V3.Dot(pa, dir) >= V3.Dot(pb, dir) ? pa : pb;
            AddGuide(cam, start, dir, len, tint, list);
        }

        private static void AddGuide(Camera cam, V3 start, V3 dir, double len, V3 tint, List<Segment> list)
        {
            if (!double.IsFinite(start.X) || !double.IsFinite(start.Y) || !double.IsFinite(start.Z)) return;
            list.Add(new Segment(start, start + dir * len, LineKind.Perspective, tint));
        }
    }
}
