using System;

namespace GeoViewer.Core
{
    public enum SolidKind
    {
        Sphere = 0,
        Cylinder = 1,
        Cube = 2,
        Cuboid = 3,
        Cone = 4,
        Asaro = 5
    }

    /// <summary>
    /// One of the five standard reference solids. All solids rest on the ground plane (y = 0)
    /// and are built from a single edge length <see cref="A"/>.
    ///
    /// Reference dimensions with cube edge a:
    ///   sphere   : diameter = sqrt(2) a
    ///   cylinder : base diameter a, height 2a
    ///   cube     : a x a x a
    ///   cuboid   : a x a base, height 2a
    ///   cone     : base diameter a, height 2a
    /// </summary>
    public sealed class Solid
    {
        public SolidKind Kind;
        public string Name = "";
        public string ShortName = "";
        public double A = 1.0;

        /// <summary>Footprint centre on the ground plane (y = 0).</summary>
        public V3 Center;

        /// <summary>Radius for sphere / cylinder / cone.</summary>
        public double Radius;

        public double Height;

        /// <summary>Half of the footprint width along X and Z (used for layout).</summary>
        public double HalfWidth;

        public V3 Min;
        public V3 Max;

        /// <summary>Triangle mesh for the faceted head; null for the analytic solids.</summary>
        public MeshSolid? Mesh;

        public V3 Albedo = new(0.80, 0.78, 0.745);

        public static Solid Create(SolidKind kind, double a = 1.0)
        {
            var s = new Solid { Kind = kind, A = a };
            switch (kind)
            {
                case SolidKind.Sphere:
                    s.Name = "球体"; s.ShortName = "球";
                    s.Radius = a * Math.Sqrt(2.0) * 0.5;
                    s.Height = 2 * s.Radius;
                    s.HalfWidth = s.Radius;
                    break;
                case SolidKind.Cylinder:
                    s.Name = "圆柱体"; s.ShortName = "柱";
                    s.Radius = a * 0.5;
                    s.Height = 2 * a;
                    s.HalfWidth = s.Radius;
                    break;
                case SolidKind.Cube:
                    s.Name = "正方体"; s.ShortName = "方";
                    s.Radius = a * 0.5;
                    s.Height = a;
                    s.HalfWidth = a * 0.5;
                    break;
                case SolidKind.Cuboid:
                    s.Name = "长方体"; s.ShortName = "长";
                    s.Radius = a * 0.5;
                    s.Height = 2 * a;
                    s.HalfWidth = a * 0.5;
                    break;
                case SolidKind.Cone:
                    s.Name = "圆锥体"; s.ShortName = "锥";
                    s.Radius = a * 0.5;
                    s.Height = 2 * a;
                    s.HalfWidth = a * 0.5;
                    break;
                
            }
            s.UpdateBounds();
            return s;
        }

        /// <summary>Height of the finished head, in units of the cube edge a.</summary>
        public const double AsaroHeight = 1.86;

        /// <summary>Height of the raw mesh emitted by <see cref="AsaroHead.Build"/> (plinth included).</summary>
        public const double AsaroSourceHeight = AsaroHead.SourceHeight;

        public void UpdateBounds()
        {
            if (Mesh != null)
            {
                Min = Mesh.Min;
                Max = Mesh.Max;
                Height = Max.Y - Min.Y;
                return;
            }
            double cy = Center.Y;
            double hw = HalfWidth;
            double hd = Kind == SolidKind.Sphere || Kind == SolidKind.Cylinder || Kind == SolidKind.Cone
                ? hw : A * 0.5;
            Min = new V3(Center.X - hw, cy, Center.Z - hd);
            Max = new V3(Center.X + hw, cy + Height, Center.Z + hd);
        }

        public double TopY => Min.Y + Height;
        public double BaseY => Min.Y;

        /// <summary>
        /// Intersects a ray with this solid. Returns the number of hits written (0..2) sorted by
        /// increasing distance. Normals point outwards (they may face away from the viewer).
        /// </summary>
        public int Intersect(in V3 o, in V3 d, Span<double> ts, Span<V3> ns)
        {
            if (Mesh != null) return Mesh.Intersect(o, d, ts, ns);
            switch (Kind)
            {
                case SolidKind.Sphere: return IntersectSphere(o, d, ts, ns);
                case SolidKind.Cylinder: return IntersectCylinder(o, d, ts, ns);
                case SolidKind.Cone: return IntersectCone(o, d, ts, ns);
                default: return IntersectBox(o, d, ts, ns);
            }
        }

        private const double Eps = 1e-7;

        private int IntersectSphere(in V3 o, in V3 d, Span<double> ts, Span<V3> ns)
        {
            V3 c = new(Center.X, BaseY + Radius, Center.Z);
            V3 oc = o - c;
            double b = V3.Dot(oc, d);
            double cc = V3.Dot(oc, oc) - Radius * Radius;
            double disc = b * b - cc;
            if (disc <= 0) return 0;
            double sq = Math.Sqrt(disc);
            double t0 = -b - sq, t1 = -b + sq;
            int n = 0;
            if (t0 > Eps) { ts[n] = t0; ns[n] = (o + d * t0 - c) / Radius; n++; }
            if (t1 > Eps) { ts[n] = t1; ns[n] = (o + d * t1 - c) / Radius; n++; }
            return n;
        }

        private int IntersectBox(in V3 o, in V3 d, Span<double> ts, Span<V3> ns)
        {
            double tmin = double.NegativeInfinity, tmax = double.PositiveInfinity;
            V3 nmin = V3.Zero, nmax = V3.Zero;

            for (int axis = 0; axis < 3; axis++)
            {
                double od = axis == 0 ? o.X : axis == 1 ? o.Y : o.Z;
                double dd = axis == 0 ? d.X : axis == 1 ? d.Y : d.Z;
                double mn = axis == 0 ? Min.X : axis == 1 ? Min.Y : Min.Z;
                double mx = axis == 0 ? Max.X : axis == 1 ? Max.Y : Max.Z;
                V3 unit = axis == 0 ? V3.UnitX : axis == 1 ? V3.UnitY : V3.UnitZ;

                if (Math.Abs(dd) < 1e-12)
                {
                    if (od < mn || od > mx) return 0;
                    continue;
                }
                double inv = 1.0 / dd;
                double ta = (mn - od) * inv;
                double tb = (mx - od) * inv;
                V3 na = -unit, nb = unit;
                if (ta > tb) { (ta, tb) = (tb, ta); (na, nb) = (nb, na); }
                if (ta > tmin) { tmin = ta; nmin = na; }
                if (tb < tmax) { tmax = tb; nmax = nb; }
                if (tmin > tmax) return 0;
            }

            int n = 0;
            if (tmin > Eps) { ts[n] = tmin; ns[n] = nmin; n++; }
            if (tmax > Eps) { ts[n] = tmax; ns[n] = nmax; n++; }
            return n;
        }

        private int IntersectCylinder(in V3 o, in V3 d, Span<double> ts, Span<V3> ns)
        {
            double ox = o.X - Center.X, oz = o.Z - Center.Z;
            double y0 = BaseY, y1 = BaseY + Height;
            double r = Radius;
            Span<double> lt = stackalloc double[4];
            Span<V3> ln = stackalloc V3[4];
            int count = 0;

            double a = d.X * d.X + d.Z * d.Z;
            if (a > 1e-12)
            {
                double b = 2 * (ox * d.X + oz * d.Z);
                double c = ox * ox + oz * oz - r * r;
                double disc = b * b - 4 * a * c;
                if (disc >= 0)
                {
                    double sq = Math.Sqrt(disc);
                    double t0 = (-b - sq) / (2 * a);
                    double t1 = (-b + sq) / (2 * a);
                    for (int i = 0; i < 2; i++)
                    {
                        double t = i == 0 ? t0 : t1;
                        if (t <= Eps) continue;
                        double y = o.Y + t * d.Y;
                        if (y < y0 - 1e-6 || y > y1 + 1e-6) continue;
                        double px = o.X + t * d.X, pz = o.Z + t * d.Z;
                        lt[count] = t;
                        ln[count] = new V3((px - Center.X) / r, 0, (pz - Center.Z) / r);
                        count++;
                    }
                }
            }

            if (Math.Abs(d.Y) > 1e-12)
            {
                for (int i = 0; i < 2; i++)
                {
                    double py = i == 0 ? y0 : y1;
                    double t = (py - o.Y) / d.Y;
                    if (t <= Eps) continue;
                    double px = o.X + t * d.X, pz = o.Z + t * d.Z;
                    double dx = px - Center.X, dz = pz - Center.Z;
                    if (dx * dx + dz * dz > r * r * (1 + 1e-6)) continue;
                    lt[count] = t;
                    ln[count] = i == 0 ? -V3.UnitY : V3.UnitY;
                    count++;
                }
            }

            return SortHits(lt, ln, count, ts, ns);
        }

        private int IntersectCone(in V3 o, in V3 d, Span<double> ts, Span<V3> ns)
        {
            double y0 = BaseY, y1 = BaseY + Height;
            double r = Radius;
            double h = Height;
            double k = r / h;

            double ex = o.X - Center.X, ez = o.Z - Center.Z, ey = y1 - o.Y;

            Span<double> lt = stackalloc double[4];
            Span<V3> ln = stackalloc V3[4];
            int count = 0;

            double a = d.X * d.X + d.Z * d.Z - k * k * d.Y * d.Y;
            double b = 2 * (ex * d.X + ez * d.Z + k * k * ey * d.Y);
            double c = ex * ex + ez * ez - k * k * ey * ey;

            if (Math.Abs(a) < 1e-12)
            {
                if (Math.Abs(b) > 1e-12)
                {
                    double t = -c / b;
                    if (t > Eps)
                    {
                        double y = o.Y + t * d.Y;
                        if (y >= y0 - 1e-6 && y <= y1 + 1e-6)
                        {
                            V3 p = o + d * t;
                            lt[count] = t;
                            ln[count] = ConeNormal(p, y1);
                            count++;
                        }
                    }
                }
            }
            else
            {
                double disc = b * b - 4 * a * c;
                if (disc >= 0)
                {
                    double sq = Math.Sqrt(disc);
                    double t0 = (-b - sq) / (2 * a);
                    double t1 = (-b + sq) / (2 * a);
                    if (t1 < t0) { (t0, t1) = (t1, t0); }
                    for (int i = 0; i < 2; i++)
                    {
                        double t = i == 0 ? t0 : t1;
                        if (t <= Eps) continue;
                        double y = o.Y + t * d.Y;
                        if (y < y0 - 1e-6 || y > y1 + 1e-6) continue;
                        V3 p = o + d * t;
                        lt[count] = t;
                        ln[count] = ConeNormal(p, y1);
                        count++;
                    }
                }
            }

            // Base disc
            if (Math.Abs(d.Y) > 1e-12)
            {
                double t = (y0 - o.Y) / d.Y;
                if (t > Eps)
                {
                    double px = o.X + t * d.X, pz = o.Z + t * d.Z;
                    double dx = px - Center.X, dz = pz - Center.Z;
                    if (dx * dx + dz * dz <= r * r * (1 + 1e-6))
                    {
                        lt[count] = t;
                        ln[count] = -V3.UnitY;
                        count++;
                    }
                }
            }

            return SortHits(lt, ln, count, ts, ns);
        }

        private V3 ConeNormal(in V3 p, double apexY)
        {
            double k = Radius / Height;
            V3 n = new(p.X - Center.X, k * k * (apexY - p.Y), p.Z - Center.Z);
            return n.Normalized();
        }

        private static int SortHits(Span<double> lt, Span<V3> ln, int count, Span<double> ts, Span<V3> ns)
        {
            // insertion sort (count <= 4)
            for (int i = 1; i < count; i++)
            {
                double t = lt[i];
                V3 n = ln[i];
                int j = i - 1;
                while (j >= 0 && lt[j] > t)
                {
                    lt[j + 1] = lt[j];
                    ln[j + 1] = ln[j];
                    j--;
                }
                lt[j + 1] = t;
                ln[j + 1] = n;
            }

            int written = 0;
            for (int i = 0; i < count && written < 2; i++)
            {
                if (lt[i] <= Eps) continue;
                if (written > 0 && Math.Abs(ts[written - 1] - lt[i]) < 1e-9) continue;
                ts[written] = lt[i];
                ns[written] = ln[i];
                written++;
            }
            return written;
        }

        /// <summary>True when the point is inside the solid (used for structural line styling).</summary>
        public bool Contains(in V3 p)
        {
            if (Mesh != null) return Mesh.Contains(p);
            switch (Kind)
            {
                case SolidKind.Sphere:
                {
                    V3 c = new(Center.X, BaseY + Radius, Center.Z);
                    return (p - c).LengthSq <= Radius * Radius;
                }
                case SolidKind.Cylinder:
                {
                    double dx = p.X - Center.X, dz = p.Z - Center.Z;
                    return p.Y >= BaseY && p.Y <= BaseY + Height && dx * dx + dz * dz <= Radius * Radius;
                }
                case SolidKind.Cone:
                {
                    if (p.Y < BaseY || p.Y > BaseY + Height) return false;
                    double rr = Radius * (1.0 - (p.Y - BaseY) / Height);
                    double dx = p.X - Center.X, dz = p.Z - Center.Z;
                    return dx * dx + dz * dz <= rr * rr;
                }
                default:
                    return p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y &&
                           p.Z >= Min.Z && p.Z <= Max.Z;
            }
        }
    }
}
