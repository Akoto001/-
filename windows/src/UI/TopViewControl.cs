using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using GeoViewer.Core;

namespace GeoViewer.UI
{
    /// <summary>Top-down plan view: object footprints plus the camera position and its field of view.</summary>
    public sealed class TopViewControl : FrameworkElement
    {
        private Scene _scene;

        private readonly Brush _bg = Freeze(new SolidColorBrush(Color.FromRgb(0x14, 0x17, 0x1C)));
        private readonly Pen _border = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x2C, 0x32, 0x3B)), 1));
        private readonly Pen _grid = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x33, 0x5A, 0x66, 0x78)), 1));
        private readonly Pen _axisX = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x66, 0xC0, 0x6A, 0x5A)), 1));
        private readonly Pen _axisZ = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x66, 0x4F, 0x8F, 0xC8)), 1));
        private readonly Brush _footFill = Freeze(new SolidColorBrush(Color.FromRgb(0x36, 0x3E, 0x4B)));
        private readonly Pen _footEdge = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x8C, 0xA0, 0xB6)), 1.1));
        private readonly Pen _rayLine = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0xDD, 0x4F, 0xB6, 0xCC)), 1.1));
        private readonly Pen _rayFaint = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x77, 0x4F, 0xB6, 0xCC)), 1)
        { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) });
        private readonly Brush _camDot = Freeze(new SolidColorBrush(Color.FromRgb(0x4F, 0xB6, 0xCC)));
        private readonly Brush _targetDot = Freeze(new SolidColorBrush(Color.FromRgb(0xE2, 0x9A, 0x3C)));
        private readonly Brush _labelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x93, 0x9E, 0xAD)));

        private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

        public TopViewControl()
        {
            ClipToBounds = true;
            SnapsToDevicePixels = true;
        }

        public Scene Scene
        {
            get => _scene;
            set { _scene = value; InvalidateVisual(); }
        }

        public void Refresh() => InvalidateVisual();

        private static double FootHalf(Solid s) =>
            (s.Kind == SolidKind.Cube || s.Kind == SolidKind.Cuboid) ? s.HalfWidth : s.Radius;

        protected override void OnRender(DrawingContext dc)
        {
            var rect = new Rect(0, 0, ActualWidth, ActualHeight);
            dc.DrawRoundedRectangle(_bg, null, rect, 4, 4);

            if (_scene == null || ActualWidth < 30 || ActualHeight < 30)
            {
                StrokeBorder(dc, rect);
                return;
            }

            Camera cam = _scene.Camera;

            // ---- extent of the object row (the camera is plotted clamped inside, so it must not dominate)
            double minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
            void Add(double x, double z)
            {
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (z < minZ) minZ = z;
                if (z > maxZ) maxZ = z;
            }

            foreach (Solid s in _scene.ActiveSolids)
            {
                double hh = FootHalf(s);
                Add(s.Center.X - hh, s.Center.Z - hh);
                Add(s.Center.X + hh, s.Center.Z + hh);
            }

            if (minX == double.MaxValue)
            {
                double e = 0.5 * _scene.EdgeLength;
                Add(-e, -e);
                Add(e, e);
            }

            double padPx = 16.0;
            // keep a bit of context around the footprint: never zoom in tighter than ~3a across
            double spanX = Math.Max(maxX - minX, 3.0 * _scene.EdgeLength);
            double spanZ = Math.Max(maxZ - minZ, 3.0 * _scene.EdgeLength);
            spanX *= 1.18;
            spanZ *= 1.18;

            double scale = Math.Min((ActualWidth - padPx * 2) / spanX, (ActualHeight - padPx * 2) / spanZ);
            if (!(scale > 1e-6)) scale = 10;

            double cx = (minX + maxX) * 0.5, cz = (minZ + maxZ) * 0.5;
            double ox = ActualWidth * 0.5, oy = ActualHeight * 0.5;
            Point Map(double wx, double wz) => new(ox + (wx - cx) * scale, oy + (wz - cz) * scale);

            dc.PushClip(new RectangleGeometry(rect));

            // ---- grid
            double step = Math.Max(0.25, _scene.EdgeLength);
            double gx0 = Math.Floor((cx - ActualWidth / 2 / scale) / step) * step;
            double gx1 = cx + ActualWidth / 2 / scale;
            double gz0 = Math.Floor((cz - ActualHeight / 2 / scale) / step) * step;
            double gz1 = cz + ActualHeight / 2 / scale;
            for (double g = gx0; g <= gx1; g += step)
                dc.DrawLine(_grid, Map(g, gz0), Map(g, gz1));
            for (double g = gz0; g <= gz1; g += step)
                dc.DrawLine(_grid, Map(gx0, g), Map(gx1, g));

            // ---- world axes
            dc.DrawLine(_axisX, Map(-500, 0), Map(500, 0));
            dc.DrawLine(_axisZ, Map(0, -500), Map(0, 500));

            // ---- footprints
            foreach (Solid s in _scene.ActiveSolids)
            {
                bool round = s.Kind != SolidKind.Cube && s.Kind != SolidKind.Cuboid;
                Point c = Map(s.Center.X, s.Center.Z);
                double r = FootHalf(s) * scale;
                if (round) dc.DrawEllipse(_footFill, _footEdge, c, r, r);
                else dc.DrawRectangle(_footFill, _footEdge, new Rect(c.X - r, c.Y - r, r * 2, r * 2));

                if (r > 13)
                {
                    FormattedText lbl = Text(s.ShortName, 9.5, _labelBrush);
                    dc.DrawText(lbl, new Point(c.X - lbl.Width / 2, c.Y - lbl.Height / 2));
                }
            }

            // ---- camera position, clamped into the panel
            double inset = 13.0;
            var inner = new Rect(inset, inset, Math.Max(1, ActualWidth - inset * 2), Math.Max(1, ActualHeight - inset * 2));

            Point eye = Map(cam.Eye.X, cam.Eye.Z);
            Point tgt = Map(cam.Target.X, cam.Target.Z);
            bool clamped = !inner.Contains(eye);
            if (clamped)
            {
                eye = new Point(Math.Max(inner.Left, Math.Min(inner.Right, eye.X)),
                                Math.Max(inner.Top, Math.Min(inner.Bottom, eye.Y)));
            }

            double vx = tgt.X - eye.X, vy = tgt.Y - eye.Y;
            double vlen = Math.Sqrt(vx * vx + vy * vy);
            if (vlen > 0.5)
            {
                vx /= vlen; vy /= vlen;
                double len = Math.Min(ActualWidth, ActualHeight) * 0.6;
                if (clamped)
                {
                    dc.DrawLine(_rayFaint, eye, tgt);
                }
                else
                {
                    double half = Math.Atan(Math.Tan(cam.FovYDeg * Math.PI / 360.0) * cam.Aspect);
                    double yaw = Math.Atan2(vy, vx);
                    foreach (int sign in new[] { -1, 1 })
                    {
                        double a = yaw + sign * half;
                        dc.DrawLine(_rayLine, eye, new Point(eye.X + Math.Cos(a) * len, eye.Y + Math.Sin(a) * len));
                    }
                    dc.DrawLine(_rayLine, eye, tgt);
                }
            }

            dc.DrawEllipse(_targetDot, null, tgt, 2.6, 2.6);

            // camera marker: dot plus a chevron when the true position is outside the panel
            dc.DrawEllipse(_camDot, null, eye, 4.4, 4.4);
            dc.DrawEllipse(_bg, null, eye, 1.9, 1.9);
            if (clamped && vlen > 0.5)
            {
                var p1 = new Point(eye.X + vx * 11 - vy * 5, eye.Y + vy * 11 + vx * 5);
                var p2 = new Point(eye.X + vx * 11 + vy * 5, eye.Y + vy * 11 - vx * 5);
                dc.DrawLine(_rayLine, eye, p1);
                dc.DrawLine(_rayLine, eye, p2);
            }

            dc.Pop();

            // ---- labels
            Point o = Map(0, 0);
            dc.DrawText(Text("X", 9, _axisX.Brush), new Point(ActualWidth - 13, Math.Max(2, o.Y - 14)));
            dc.DrawText(Text("Z", 9, _axisZ.Brush), new Point(o.X + 3, ActualHeight - 15));
            dc.DrawText(Text("前", 9, _labelBrush), new Point(ActualWidth - 16, ActualHeight - 15));

            string dist = (cam.Distance / Math.Max(0.01, _scene.EdgeLength)).ToString("0.0", CultureInfo.InvariantCulture) + " a";
            FormattedText d = Text("视距 " + dist, 9.5, _labelBrush);
            dc.DrawText(d, new Point(6, 4));

            StrokeBorder(dc, rect);
        }

        private void StrokeBorder(DrawingContext dc, Rect rect)
        {
            dc.DrawRoundedRectangle(null, _border,
                new Rect(rect.X + 0.5, rect.Y + 0.5, Math.Max(1, rect.Width - 1), Math.Max(1, rect.Height - 1)), 4, 4);
        }

        private FormattedText Text(string s, double size, Brush brush)
        {
            return new FormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Microsoft YaHei UI"), size, brush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
        }
    }
}
