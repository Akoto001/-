using System;
using System.Collections.Generic;

namespace GeoViewer.Core;

/// <summary>
/// Hand-built "planes of the head" study model in the tradition of Asaro's plaster cast: the
/// skull is not a smooth egg but a crease network of large flat facets, which is what makes the
/// form read under light.
///
/// The head is described by 13 horizontal sections; every section carries 7 half-profile points
/// running from the front centre line (c0) to the back centre line (c6). Those seven columns are
/// exactly the crease lines of the cast: the edge of the forehead / cheek plate (c1), the
/// zygomatic corner (c2), the temple / jaw angle (c3), the rear quarter (c4) and the back (c5).
/// Because the tables are explicit (no trigonometry) each plane can be shaped on its own.
///
/// Conventions: symmetric about x = 0, the face looks along +Z, +Y is up, the chin is at y = 0
/// and the crown at y = 1.0. The plinth hangs below that, see <see cref="SourceHeight"/>. The
/// finished mesh is lifted so its lowest point is y = 0.
/// </summary>
public static class AsaroHead
{
    private const int HalfCols = 7;   // c0 .. c6
    private const int RingCols = 12;  // c0..c6 then the mirrored c5..c1

    /// <summary>Height of each section, chin (0) to crown (1).</summary>
    private static readonly double[] RowY =
    {
        0.000, 0.180, 0.330, 0.470, 0.556, 0.600, 0.630, 0.800, 0.920, 1.000,
    };

    private static readonly int RowCount = RowY.Length;

    /// <summary>|x| of the seven half-profile points of each section.</summary>
    private static readonly double[,] RowX =
    {
        //  c0     c1     c2     c3     c4     c5     c6
        { 0.000, 0.050, 0.088, 0.100, 0.086, 0.043, 0.000 },  // 0.000 chin point
        { 0.000, 0.130, 0.215, 0.248, 0.214, 0.106, 0.000 },  // 0.180 jaw (masseter shelf)
        { 0.000, 0.150, 0.262, 0.286, 0.246, 0.124, 0.000 },  // 0.330 muzzle
        { 0.000, 0.158, 0.272, 0.292, 0.252, 0.128, 0.000 },  // 0.470 zygoma top
        { 0.000, 0.156, 0.268, 0.290, 0.250, 0.127, 0.000 },  // 0.556 eye line
        { 0.000, 0.155, 0.266, 0.288, 0.248, 0.126, 0.000 },  // 0.600 brow eave
        { 0.000, 0.154, 0.264, 0.286, 0.247, 0.126, 0.000 },  // 0.630 brow ridge
        { 0.000, 0.143, 0.240, 0.262, 0.226, 0.115, 0.000 },  // 0.800 forehead
        { 0.000, 0.112, 0.186, 0.204, 0.176, 0.090, 0.000 },  // 0.920 crown shoulder
        { 0.000, 0.048, 0.082, 0.090, 0.077, 0.039, 0.000 },  // 1.000 crown
    };

    /// <summary>Depth of the same seven points; the front of the face is +Z, the back is -Z.</summary>
    private static readonly double[,] RowZ =
    {
        //   c0     c1     c2     c3      c4      c5      c6
        { 0.420, 0.400, 0.362, 0.286, 0.186, 0.104, 0.062 },   // chin point
        { 0.412, 0.396, 0.352, 0.246, 0.056, -0.180, -0.316 }, // jaw line
        { 0.446, 0.430, 0.330, 0.220, 0.006, -0.252, -0.392 }, // nose base / muzzle
        { 0.300, 0.318, 0.300, 0.150, -0.040, -0.320, -0.440 },// under-eye / zygoma shelf
        { 0.205, 0.214, 0.300, 0.120, -0.060, -0.352, -0.470 },// eye socket (temporal fossa)
        { 0.392, 0.410, 0.330, 0.110, -0.070, -0.366, -0.480 },// brow eave
        { 0.418, 0.470, 0.352, 0.108, -0.078, -0.380, -0.486 },// brow ridge
        { 0.352, 0.360, 0.300, 0.100, -0.100, -0.396, -0.486 },// forehead (single plane)
        { 0.286, 0.300, 0.240, 0.070, -0.150, -0.410, -0.474 },// crown shoulder
        { 0.120, 0.132, 0.100, 0.010, -0.190, -0.330, -0.380 },// crown (slanted plate)
    };

    /// <summary>Chin to crown is 1.0; the plinth reaches down to -0.115.</summary>
    public const double SourceHeight = 1.310;

    public static MeshSolid Build()
    {
        var b = new Builder();

        var grid = new int[RowCount][];
        for (int r = 0; r < RowCount; r++)
        {
            grid[r] = new int[RingCols];
            for (int k = 0; k < RingCols; k++)
            {
                int c = k < HalfCols ? k : RingCols - k;
                double sign = k < HalfCols ? 1.0 : -1.0;
                grid[r][k] = b.Add(new V3(sign * RowX[r, c], RowY[r], RowZ[r, c]));
            }
        }

        for (int r = 0; r < RowCount - 1; r++)
        {
            for (int k = 0; k < RingCols; k++)
            {
                int k2 = (k + 1) % RingCols;
                int a = grid[r][k], bb = grid[r][k2], cc = grid[r + 1][k2], d = grid[r + 1][k];
                b.Quad(a, bb, cc, d);
                b.Edge(a, bb);
                b.Edge(a, d);
            }
        }

        CloseRing(b, grid[RowCount - 1], new V3(0.0, 1.0, -0.080));
        CloseRing(b, grid[0], new V3(0.0, 0.0, 0.090));

        AddNeck(b);
        AddPlinth(b);
        AddNose(b);
        AddEyes(b);
        AddLips(b);
        AddEars(b);

        return new MeshSolid(b.Vertices.ToArray(), b.Triangles.ToArray(), b.Edges.ToArray());
    }

    /// <summary>
    /// The skull built the way the plate was read: pick the node's x/y off the front view, then
    /// its depth off the side view, then connect the nodes into the large facets of the cast.
    /// Nodes are named after the crease they sit on; every side node is mirrored to -x.
    /// </summary>
    private static void AddHeadShell(Builder b)
    {
        var p = new Dictionary<string, int>(64);

        void C(string n, double x, double y, double z) => p[n] = b.Add(new V3(x, y, z));

        void S(string n, double x, double y, double z)
        {
            p[n] = b.Add(new V3(x, y, z));
            p["m" + n] = b.Add(new V3(-x, y, z));
        }

        // ---- centre line: chin -> crown -> nape ----
        C("c0", 0.0, 0.000, 0.200);   // chin underside
        C("c1", 0.0, 0.060, 0.250);   // chin front
        C("c2", 0.0, 0.180, 0.300);   // mouth line
        C("c3", 0.0, 0.245, 0.290);   // upper lip
        C("c4", 0.0, 0.300, 0.285);   // nose base
        C("c7", 0.0, 0.630, 0.255);   // nose root
        C("c8", 0.0, 0.690, 0.265);   // glabella
        C("c9", 0.0, 0.800, 0.235);   // forehead mid
        C("c10", 0.0, 0.900, 0.150);  // forehead top
        C("c11", 0.0, 1.000, -0.060); // crown front
        C("c12", 0.0, 0.970, -0.340); // crown back
        C("c13", 0.0, 0.720, -0.400); // occiput
        C("c14", 0.0, 0.300, -0.360); // nape

        // ---- jaw and chin ----
        S("r0", 0.090, 0.020, 0.200);   // chin corner
        S("r1", 0.160, 0.070, 0.205);   // jaw front
        S("r2", 0.240, 0.120, 0.030);   // gonion
        S("r3", 0.265, 0.220, -0.090);  // jaw mid
        S("r4", 0.285, 0.320, -0.040);  // masseter

        // ---- muzzle and cheek ----
        S("r5", 0.150, 0.185, 0.260);   // mouth corner
        S("r6", 0.135, 0.250, 0.265);   // muzzle upper
        S("r7", 0.225, 0.220, 0.180);   // cheek low
        S("r8", 0.100, 0.300, 0.280);   // nasolabial top
        S("r9", 0.200, 0.330, 0.240);   // cheek front
        S("r10", 0.315, 0.440, 0.155);  // cheekbone
        S("r11", 0.260, 0.505, 0.190);  // cheek above

        // ---- eye and brow ----
        S("r12", 0.100, 0.575, 0.215);  // eye inner
        S("r13", 0.170, 0.610, 0.195);  // eye upper
        S("r14", 0.250, 0.565, 0.165);  // eye outer
        S("r15", 0.170, 0.530, 0.195);  // eye lower
        S("r16", 0.100, 0.690, 0.262);  // brow inner
        S("r17", 0.190, 0.705, 0.245);  // brow mid
        S("r18", 0.260, 0.665, 0.195);  // brow outer

        // ---- temple, cranium, back ----
        S("r19", 0.320, 0.720, 0.100);  // temple
        S("r20", 0.240, 0.820, 0.150);  // forehead side
        S("r21", 0.290, 0.860, -0.020); // upper side
        S("r22", 0.330, 0.550, -0.220); // ear front
        S("r23", 0.310, 0.500, -0.420); // ear back
        S("r24", 0.340, 0.450, -0.050); // side mid
        S("r25", 0.200, 0.900, -0.300); // back upper
        S("r26", 0.260, 0.650, -0.360); // back mid
        S("r27", 0.200, 0.350, -0.340); // back low

        void Face(bool mirror, params string[] names)
        {
            int n = names.Length;
            var idx = new int[n];
            for (int i = 0; i < n; i++)
            {
                string key = mirror && names[i][0] == 'r' ? "m" + names[i] : names[i];
                idx[i] = p[key];
            }

            for (int i = 0; i < n; i++)
            {
                b.Edge(idx[i], idx[(i + 1) % n]);
            }

            if (n == 3)
            {
                b.Tri(idx[0], idx[1], idx[2]);
            }
            else if (n == 4)
            {
                b.Quad(idx[0], idx[1], idx[2], idx[3]);
            }
            else
            {
                for (int i = 1; i < n - 1; i++)
                {
                    b.Tri(idx[0], idx[i], idx[i + 1]);
                }
            }
        }

        for (int m = 0; m < 2; m++)
        {
            bool x = m == 1;

            // forehead plate and the crown
            Face(x, "c8", "r16", "r17", "r18", "r19", "r20", "c9");
            Face(x, "c9", "r20", "r21", "c10");
            Face(x, "c10", "r21", "r25", "c11");
            Face(x, "c11", "r25", "r26", "c12");
            Face(x, "c12", "r26", "r27", "c13");

            // the crease from the temple round the ear to the nape
            Face(x, "r19", "r22", "r24", "r21");
            Face(x, "r21", "r24", "r26", "r25");
            Face(x, "r26", "r24", "r23", "r27");
            Face(x, "r27", "r23", "c14", "c13");

            // brow shelf, socket and the big cheek plate
            Face(x, "r16", "c8", "c7", "r12");
            Face(x, "r16", "r12", "r13", "r17");
            Face(x, "r17", "r13", "r14", "r18");
            Face(x, "r12", "r15", "r13");
            Face(x, "r13", "r15", "r14");
            Face(x, "r15", "r14", "r11", "r10", "r9", "r8");

            // nose side, muzzle and mouth
            Face(x, "c7", "r8", "r12");
            Face(x, "c4", "r8", "c3");
            Face(x, "c3", "r8", "r6", "r5", "c2");
            Face(x, "c4", "c3", "r6", "r8");

            // cheek, jaw and chin
            Face(x, "r9", "r10", "r7");
            Face(x, "r8", "r9", "r7", "r5");
            Face(x, "r10", "r4", "r7");
            Face(x, "r7", "r4", "r3");
            Face(x, "r5", "r7", "r3", "r2");
            Face(x, "c2", "r5", "r2", "r1");
            Face(x, "c1", "r1", "r0", "c0");
            Face(x, "r1", "r2", "r0");
            Face(x, "r3", "r24", "r2");
        }
    }

    private static void CloseRing(Builder b, int[] ring, V3 centre)
    {
        int c = b.Add(centre);
        for (int k = 0; k < ring.Length; k++)
        {
            int k2 = (k + 1) % ring.Length;
            b.Tri(ring[k], ring[k2], c);
            b.Edge(ring[k], ring[k2]);
        }
    }

    /// <summary>
    /// Half width of the skull at a given height and depth: used to seat the ears flush on the
    /// head instead of floating beside it. Walks the profile columns of the interpolated section.
    /// </summary>
    private static double SkullSurfaceX(double y, double z)
    {
        int r = 0;
        while (r < RowCount - 2 && RowY[r + 1] < y) r++;
        double y0 = RowY[r], y1 = RowY[r + 1];
        double u = y1 > y0 ? MathUtil.Clamp01((y - y0) / (y1 - y0)) : 0.0;

        for (int c = 0; c < HalfCols - 1; c++)
        {
            double z0 = RowZ[r, c] + (RowZ[r + 1, c] - RowZ[r, c]) * u;
            double z1 = RowZ[r, c + 1] + (RowZ[r + 1, c + 1] - RowZ[r, c + 1]) * u;
            if (z > z0 || z < z1) continue;

            double x0 = RowX[r, c] + (RowX[r + 1, c] - RowX[r, c]) * u;
            double x1 = RowX[r, c + 1] + (RowX[r + 1, c + 1] - RowX[r, c + 1]) * u;
            double t = z0 - z1 > 1e-6 ? (z0 - z) / (z0 - z1) : 0.0;
            return x0 + (x1 - x0) * t;
        }

        return 0.0;
    }

    private static void AddNeck(Builder b)
    {
        const int segments = 10;
        const double yTop = 0.160, yBottom = -0.220;

        var top = new int[segments];
        var bottom = new int[segments];
        for (int k = 0; k < segments; k++)
        {
            double angle = k * (2.0 * Math.PI / segments);
            double s = Math.Sin(angle);
            double cz = Math.Cos(angle);

            double xTop = 0.140 * s;
            double zTop = cz >= 0 ? -0.050 + 0.230 * Math.Pow(cz, 0.6) : -0.050 - 0.330 * Math.Pow(-cz, 0.9);

            double xBot = 0.190 * s;
            double zBot = cz >= 0 ? -0.050 + 0.290 * Math.Pow(cz, 0.6) : -0.050 - 0.400 * Math.Pow(-cz, 0.9);

            top[k] = b.Add(new V3(xTop, yTop, zTop));
            bottom[k] = b.Add(new V3(xBot, yBottom, zBot));
        }

        for (int k = 0; k < segments; k++)
        {
            int k2 = (k + 1) % segments;
            b.Quad(top[k], top[k2], bottom[k2], bottom[k]);
            b.Edge(top[k], bottom[k]);
        }
    }

    /// <summary>The plaster cast stands on a chamfered square plinth marked "HEAD".</summary>
    private static void AddPlinth(Builder b)
    {
        const double yTop = -0.220;
        const double yBottom = -0.310;
        const double topX = 0.360, botX = 0.392;
        const double topZf = 0.300, botZf = 0.330;
        const double topZb = -0.300, botZb = -0.330;

        int[] t =
        {
            b.Add(new V3(-topX, yTop, topZf)),
            b.Add(new V3(topX, yTop, topZf)),
            b.Add(new V3(topX, yTop, topZb)),
            b.Add(new V3(-topX, yTop, topZb)),
        };
        int[] u =
        {
            b.Add(new V3(-botX, yBottom, botZf)),
            b.Add(new V3(botX, yBottom, botZf)),
            b.Add(new V3(botX, yBottom, botZb)),
            b.Add(new V3(-botX, yBottom, botZb)),
        };

        for (int k = 0; k < 4; k++)
        {
            int k2 = (k + 1) % 4;
            b.Quad(t[k], t[k2], u[k2], u[k]);
            b.Edge(t[k], t[k2]);
            b.Edge(t[k], u[k]);
        }

        b.Quad(t[3], t[2], t[1], t[0]);
        b.Quad(u[0], u[1], u[2], u[3]);
    }

    /// <summary>Wedge nose: narrow bridge plane, two side planes, tip block, base and wings.</summary>
    private static void AddNose(Builder b)
    {
        int root = b.Add(new V3(0.000, 0.620, 0.398));
        int bridge = b.Add(new V3(0.000, 0.532, 0.396));
        int tip = b.Add(new V3(0.000, 0.436, 0.492));
        int columella = b.Add(new V3(0.000, 0.382, 0.470));
        int philtrum = b.Add(new V3(0.000, 0.352, 0.448));

        var r = new int[2];
        var br = new int[2];
        var tp = new int[2];
        var wing = new int[2];
        var wbase = new int[2];
        var nostril = new int[2];

        for (int side = 0; side < 2; side++)
        {
            double sx = side == 0 ? 1.0 : -1.0;
            r[side] = b.Add(new V3(0.046 * sx, 0.616, 0.392));
            br[side] = b.Add(new V3(0.062 * sx, 0.532, 0.388));
            tp[side] = b.Add(new V3(0.098 * sx, 0.448, 0.476));
            wing[side] = b.Add(new V3(0.124 * sx, 0.382, 0.440));
            wbase[side] = b.Add(new V3(0.052 * sx, 0.370, 0.436));
            nostril[side] = b.Add(new V3(0.036 * sx, 0.366, 0.424));

            b.Quad(root, r[side], br[side], bridge);
            b.Quad(bridge, br[side], tp[side], tip);
            b.Quad(tip, tp[side], wing[side], columella);
            b.Quad(columella, wing[side], wbase[side], philtrum);
            b.Tri(philtrum, wbase[side], nostril[side]);

            b.Edge(r[side], br[side]);
            b.Edge(br[side], tp[side]);
            b.Edge(tp[side], wing[side]);
            b.Edge(wing[side], wbase[side]);
        }

        b.Edge(root, bridge);
        b.Edge(bridge, tip);
        b.Edge(tip, columella);
        b.Edge(columella, philtrum);
    }

    /// <summary>
    /// Almond eye socket cut into the face plate: an outer rim on the face surface, a wall that
    /// drops back, a dark floor, and an eyeball that just fills the opening.
    /// </summary>
    private static void AddEyes(Builder b)
    {
        // Almond outline in (x, y) plus the depth of the rim point on the face plate.
        double[,] outline =
        {
            { 0.055, 0.556, 0.300 },  // inner corner
            { 0.085, 0.590, 0.312 },  // upper inner
            { 0.145, 0.605, 0.316 },  // upper mid
            { 0.200, 0.588, 0.308 },  // upper outer
            { 0.235, 0.556, 0.298 },  // outer corner
            { 0.198, 0.528, 0.292 },  // lower outer
            { 0.143, 0.514, 0.286 },  // lower mid
            { 0.084, 0.528, 0.290 },  // lower inner
        };

        const int n = 8;
        for (int side = 0; side < 2; side++)
        {
            double sx = side == 0 ? 1.0 : -1.0;
            var rim = new int[n];
            var wall = new int[n];

            for (int k = 0; k < n; k++)
            {
                double x = outline[k, 0] * sx;
                double y = outline[k, 1];
                double z = outline[k, 2];
                rim[k] = b.Add(new V3(x, y, z));
                // The wall pulls inwards and backwards, which is what makes the socket dark.
                wall[k] = b.Add(new V3(x * 0.80 + 0.026 * sx, y + (0.556 - y) * 0.30, z - 0.120));
            }

            int floor = b.Add(new V3(0.145 * sx, 0.556, 0.104));
            for (int k = 0; k < n; k++)
            {
                int k2 = (k + 1) % n;
                b.Quad(rim[k], rim[k2], wall[k2], wall[k]);
                b.Tri(wall[k], wall[k2], floor);
                b.Edge(rim[k], rim[k2]);
            }

            // Eyeball: big enough to be seen through the opening, still behind the brow shelf.
            AddFacetedBall(b, new V3(0.146 * sx, 0.556, 0.182), 0.080, 8, 4);
        }
    }

    private static void AddLips(Builder b)
    {
        int upT = b.Add(new V3(0.000, 0.246, 0.450));
        int upC = b.Add(new V3(0.000, 0.210, 0.470));
        int midC = b.Add(new V3(0.000, 0.180, 0.464));
        int loC = b.Add(new V3(0.000, 0.148, 0.466));
        int loB = b.Add(new V3(0.000, 0.112, 0.452));

        var upR = new int[2];
        var midR = new int[2];
        var loR = new int[2];
        var cr = new int[2];

        for (int side = 0; side < 2; side++)
        {
            double sx = side == 0 ? 1.0 : -1.0;
            upR[side] = b.Add(new V3(0.130 * sx, 0.242, 0.444));
            midR[side] = b.Add(new V3(0.146 * sx, 0.180, 0.438));
            loR[side] = b.Add(new V3(0.128 * sx, 0.148, 0.442));
            cr[side] = b.Add(new V3(0.152 * sx, 0.182, 0.404));

            b.Tri(upT, upC, upR[side]);
            b.Quad(upC, midC, midR[side], upR[side]);
            b.Quad(midC, loC, loR[side], midR[side]);
            b.Tri(loC, loB, loR[side]);
            b.Tri(upR[side], cr[side], midR[side]);
            b.Tri(midR[side], cr[side], loR[side]);

            b.Edge(upC, upR[side]);
            b.Edge(midR[side], cr[side]);
            b.Edge(cr[side], loR[side]);
        }

        b.Edge(upT, upC);
        b.Edge(upC, midC);
        b.Edge(midC, loC);
        b.Edge(loC, loB);
    }

    /// <summary>
    /// Ear: a helix rim on the skull, an inner ridge (antihelix), a dark concha bowl and a lobe.
    /// </summary>
    private static void AddEars(Builder b)
    {
        const double yc = 0.520, zc = -0.200;
        const double ry = 0.125, rz = 0.082;
        const int segments = 10;

        for (int side = 0; side < 2; side++)
        {
            double sx = side == 0 ? 1.0 : -1.0;
            var rim = new int[segments];
            var ridge = new int[segments];
            var bowl = new int[segments];

            for (int k = 0; k < segments; k++)
            {
                double angle = 2.0 * Math.PI * k / segments;
                double y = yc + ry * Math.Sin(angle);
                double z = zc + rz * Math.Cos(angle);
                double surface = SkullSurfaceX(y, z);

                // Helix: the outer rim, standing proud of the skull.
                rim[k] = b.Add(new V3((surface + 0.012) * sx, y, z));
                // Antihelix: a second ridge inside the rim, a little lower.
                ridge[k] = b.Add(new V3((surface + 0.006) * sx, yc + (y - yc) * 0.78, zc + (z - zc) * 0.78));
                // Concha: the bowl, sunk into the skull.
                bowl[k] = b.Add(new V3((surface - 0.016) * sx, yc + (y - yc) * 0.50, zc + (z - zc) * 0.50));
            }

            int centre = b.Add(new V3((SkullSurfaceX(yc, zc) - 0.020) * sx, yc, zc));
            for (int k = 0; k < segments; k++)
            {
                int k2 = (k + 1) % segments;
                b.Quad(rim[k], rim[k2], ridge[k2], ridge[k]);
                b.Quad(ridge[k], ridge[k2], bowl[k2], bowl[k]);
                b.Tri(bowl[k], bowl[k2], centre);
                b.Edge(rim[k], rim[k2]);
                b.Edge(ridge[k], ridge[k2]);
            }
        }
    }

    private static void AddFacetedBall(Builder b, V3 c, double r, int segments, int rings)
    {
        var rows = new int[rings + 1][];
        for (int i = 0; i <= rings; i++)
        {
            double phi = Math.PI * i / rings;
            double y = Math.Cos(phi);
            double rad = Math.Sin(phi);
            rows[i] = new int[segments];
            for (int k = 0; k < segments; k++)
            {
                double theta = 2.0 * Math.PI * k / segments;
                rows[i][k] = b.Add(new V3(
                    c.X + r * rad * Math.Cos(theta),
                    c.Y + r * y,
                    c.Z + r * rad * Math.Sin(theta)));
            }
        }

        for (int i = 0; i < rings; i++)
        {
            for (int k = 0; k < segments; k++)
            {
                int k2 = (k + 1) % segments;
                b.Quad(rows[i][k], rows[i][k2], rows[i + 1][k2], rows[i + 1][k]);
            }
        }
    }

    private sealed class Builder
    {
        /// <summary>Depth scale applied to everything added after the shell (legacy feature frame).</summary>
        public double ZScale = 1.0;

        public readonly List<V3> Vertices = new();
        public readonly List<int> Triangles = new();
        public readonly List<int> Edges = new();
        private readonly HashSet<long> _edgeSet = new();

        public int Add(V3 p)
        {
            if (ZScale != 1.0) p = new V3(p.X, p.Y, p.Z * ZScale);
            Vertices.Add(p);
            return Vertices.Count - 1;
        }

        public void Tri(int a, int b, int c)
        {
            Triangles.Add(a);
            Triangles.Add(b);
            Triangles.Add(c);
        }

        public void Quad(int a, int b, int c, int d)
        {
            Tri(a, b, c);
            Tri(a, c, d);
        }

        public void Edge(int a, int b)
        {
            if (a == b) return;
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (!_edgeSet.Add(key)) return;
            Edges.Add(a);
            Edges.Add(b);
        }
    }
}
