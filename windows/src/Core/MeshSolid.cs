using System;
using System.Collections.Generic;

namespace GeoViewer.Core;

/// <summary>
/// A flat-shaded triangle mesh with a bounding volume hierarchy for ray queries.
/// Facet edges are kept as an explicit list so the line pass can draw the plane
/// boundaries (the whole point of a "planes of the head" model) without also
/// drawing the diagonals that split each quad.
/// </summary>
public sealed class MeshSolid
{
    public readonly V3[] Vertices;
    public readonly int[] Triangles;          // 3 indices per triangle
    public readonly int[] Edges;              // 2 vertex indices per facet edge

    public V3 Min;
    public V3 Max;
    public double Radius;                     // bounding sphere radius about Center
    public V3 Center;

    private readonly int[] _order;            // triangle ids, reordered by the BVH
    private readonly Node[] _nodes;
    private int _nodeCount;

    private struct Node
    {
        public V3 Min;
        public V3 Max;
        public int Start;                     // leaf: first triangle in _order
        public int Count;                     // leaf: triangle count, 0 for inner nodes
        public int Left;                      // inner: child index (right is Left + 1)
    }

    public MeshSolid(V3[] vertices, int[] triangles, int[] edges)
    {
        Vertices = vertices;
        Triangles = triangles;
        Edges = edges ?? Array.Empty<int>();

        int triCount = triangles.Length / 3;
        _order = new int[triCount];
        for (int i = 0; i < triCount; i++) _order[i] = i;

        _nodes = new Node[Math.Max(1, triCount * 2)];
        _nodeCount = 0;

        ComputeBounds();
        if (triCount > 0)
        {
            _nodeCount = 1;
            _nodes[0] = new Node { Start = 0, Count = triCount };
            BuildNode(0);
        }
    }

    private void ComputeBounds()
    {
        V3 mn = new(double.MaxValue, double.MaxValue, double.MaxValue);
        V3 mx = new(double.MinValue, double.MinValue, double.MinValue);
        for (int i = 0; i < Vertices.Length; i++)
        {
            mn = V3.Min(mn, Vertices[i]);
            mx = V3.Max(mx, Vertices[i]);
        }
        Min = mn;
        Max = mx;
        Center = (mn + mx) * 0.5;
        double r = 0.0;
        for (int i = 0; i < Vertices.Length; i++) r = Math.Max(r, (Vertices[i] - Center).Length);
        Radius = r;
    }

    private V3 TriMin(int tri)
    {
        int i0 = Triangles[tri * 3], i1 = Triangles[tri * 3 + 1], i2 = Triangles[tri * 3 + 2];
        return V3.Min(V3.Min(Vertices[i0], Vertices[i1]), Vertices[i2]);
    }

    private V3 TriMax(int tri)
    {
        int i0 = Triangles[tri * 3], i1 = Triangles[tri * 3 + 1], i2 = Triangles[tri * 3 + 2];
        return V3.Max(V3.Max(Vertices[i0], Vertices[i1]), Vertices[i2]);
    }

    private void BuildNode(int index)
    {
        ref Node node = ref _nodes[index];
        V3 mn = new(double.MaxValue, double.MaxValue, double.MaxValue);
        V3 mx = new(double.MinValue, double.MinValue, double.MinValue);
        for (int i = node.Start; i < node.Start + node.Count; i++)
        {
            int tri = _order[i];
            mn = V3.Min(mn, TriMin(tri));
            mx = V3.Max(mx, TriMax(tri));
        }
        node.Min = mn;
        node.Max = mx;

        if (node.Count <= 3) return;

        V3 size = mx - mn;
        int axis = size.X >= size.Y && size.X >= size.Z ? 0 : (size.Y >= size.Z ? 1 : 2);

        int start = node.Start;
        int end = node.Start + node.Count;
        // Median split on the triangle centres – good enough for a few hundred facets.
        Array.Sort(_order, start, node.Count, new CentreComparer(this, axis));
        int mid = start + node.Count / 2;

        int leftIndex = _nodeCount;
        _nodeCount += 2;
        int rightIndex = leftIndex + 1;

        _nodes[leftIndex] = new Node { Start = start, Count = mid - start };
        _nodes[rightIndex] = new Node { Start = mid, Count = end - mid };
        _nodes[index].Left = leftIndex;
        _nodes[index].Count = 0;

        BuildNode(leftIndex);
        BuildNode(rightIndex);
        _ = start; _ = end;
    }

    private sealed class CentreComparer : IComparer<int>
    {
        private readonly MeshSolid _mesh;
        private readonly int _axis;

        public CentreComparer(MeshSolid mesh, int axis) { _mesh = mesh; _axis = axis; }

        public int Compare(int a, int b)
        {
            double ca = _mesh.Centre(a, _axis);
            double cb = _mesh.Centre(b, _axis);
            return ca < cb ? -1 : (ca > cb ? 1 : 0);
        }
    }

    private double Centre(int tri, int axis)
    {
        int i0 = Triangles[tri * 3], i1 = Triangles[tri * 3 + 1], i2 = Triangles[tri * 3 + 2];
        double a = axis == 0 ? Vertices[i0].X : axis == 1 ? Vertices[i0].Y : Vertices[i0].Z;
        double b = axis == 0 ? Vertices[i1].X : axis == 1 ? Vertices[i1].Y : Vertices[i1].Z;
        double c = axis == 0 ? Vertices[i2].X : axis == 1 ? Vertices[i2].Y : Vertices[i2].Z;
        return (a + b + c) / 3.0;
    }

    /// <summary>Intersects a ray with the mesh, returning up to two hits sorted by distance.</summary>
    public int Intersect(in V3 o, in V3 d, Span<double> ts, Span<V3> ns)
    {
        if (_nodeCount == 0) return 0;
        double best0 = double.MaxValue, best1 = double.MaxValue;
        int tri0 = -1, tri1 = -1;

        Span<int> stack = stackalloc int[64];
        int sp = 0;
        stack[sp++] = 0;

        while (sp > 0)
        {
            int ni = stack[--sp];
            ref Node node = ref _nodes[ni];
            if (!HitBox(in o, in d, in node.Min, in node.Max, Math.Max(best0, best1))) continue;
            if (node.Count == 0)
            {
                stack[sp++] = node.Left + 1;
                stack[sp++] = node.Left;
                continue;
            }

            for (int i = node.Start; i < node.Start + node.Count; i++)
            {
                int tri = _order[i];
                int i0 = Triangles[tri * 3], i1 = Triangles[tri * 3 + 1], i2 = Triangles[tri * 3 + 2];
                if (!HitTriangle(in o, in d, Vertices[i0], Vertices[i1], Vertices[i2], out double t)) continue;
                if (t <= 1e-6) continue;
                if (t < best0) { best1 = best0; tri1 = tri0; best0 = t; tri0 = tri; }
                else if (t < best1) { best1 = t; tri1 = tri; }
            }
        }

        if (tri0 < 0) return 0;
        ts[0] = best0;
        ns[0] = TriNormal(tri0);
        if (tri1 >= 0 && best1 > best0 + 1e-6)
        {
            ts[1] = best1;
            ns[1] = TriNormal(tri1);
            return 2;
        }
        return 1;
    }

    /// <summary>Uniform scale, then lift so the lowest vertex sits on y = 0.</summary>
    public MeshSolid Transformed(double scale)
    {
        var verts = new V3[Vertices.Length];
        double minY = double.MaxValue;
        for (int i = 0; i < Vertices.Length; i++)
        {
            verts[i] = Vertices[i] * scale;
            if (verts[i].Y < minY) minY = verts[i].Y;
        }
        var lift = new V3(0.0, -minY, 0.0);
        for (int i = 0; i < verts.Length; i++) verts[i] = verts[i] + lift;
        return new MeshSolid(verts, Triangles, Edges);
    }

    /// <summary>Outward normal of one triangle (flat shading – each facet is one plane).</summary>
    public V3 TriNormal(int tri)
    {
        int i0 = Triangles[tri * 3], i1 = Triangles[tri * 3 + 1], i2 = Triangles[tri * 3 + 2];
        V3 a = Vertices[i0], b = Vertices[i1], c = Vertices[i2];
        V3 n = V3.Cross(b - a, c - a);
        double len = n.Length;
        if (len < 1e-12) return new V3(0, 1, 0);
        n /= len;
        // Keep the normal pointing away from the mesh centre so winding never matters.
        V3 mid = (a + b + c) / 3.0;
        return V3.Dot(n, mid - Center) < 0 ? -n : n;
    }

    /// <summary>Point-in-mesh test (odd number of crossings of a ray cast along +X).</summary>
    public bool Contains(in V3 p)
    {
        V3 o = p;
        V3 d = new(1.0, 0.0, 0.0);
        int crossing = 0;
        for (int tri = 0; tri < Triangles.Length / 3; tri++)
        {
            int i0 = Triangles[tri * 3], i1 = Triangles[tri * 3 + 1], i2 = Triangles[tri * 3 + 2];
            if (HitTriangle(in o, in d, Vertices[i0], Vertices[i1], Vertices[i2], out double t) && t > 1e-9) crossing++;
        }
        return (crossing & 1) == 1;
    }

    public static bool HitTriangle(in V3 o, in V3 d, in V3 a, in V3 b, in V3 c, out double t)
    {
        t = 0.0;
        V3 e1 = b - a;
        V3 e2 = c - a;
        V3 p = V3.Cross(d, e2);
        double det = V3.Dot(e1, p);
        if (Math.Abs(det) < 1e-12) return false;
        double inv = 1.0 / det;
        V3 tv = o - a;
        double u = V3.Dot(tv, p) * inv;
        if (u < -1e-9 || u > 1.0 + 1e-9) return false;
        V3 q = V3.Cross(tv, e1);
        double v = V3.Dot(d, q) * inv;
        if (v < -1e-9 || u + v > 1.0 + 1e-9) return false;
        t = V3.Dot(e2, q) * inv;
        return true;
    }

    private static bool HitBox(in V3 o, in V3 d, in V3 mn, in V3 mx, double maxT)
    {
        double t0 = 0.0, t1 = maxT;
        if (!Slab(o.X, d.X, mn.X, mx.X, ref t0, ref t1)) return false;
        if (!Slab(o.Y, d.Y, mn.Y, mx.Y, ref t0, ref t1)) return false;
        if (!Slab(o.Z, d.Z, mn.Z, mx.Z, ref t0, ref t1)) return false;
        return t1 >= t0;
    }

    private static bool Slab(double o, double d, double mn, double mx, ref double t0, ref double t1)
    {
        if (Math.Abs(d) < 1e-12) return o >= mn && o <= mx;
        double inv = 1.0 / d;
        double a = (mn - o) * inv;
        double b = (mx - o) * inv;
        if (a > b) { double tmp = a; a = b; b = tmp; }
        if (a > t0) t0 = a;
        if (b < t1) t1 = b;
        return t1 >= t0;
    }
}
