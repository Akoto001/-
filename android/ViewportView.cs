using System;
using System.Globalization;
using System.Threading.Tasks;
using Android.Content;
using Android.Graphics;
using Android.Views;
using GeoViewer.Core;
using GeoViewer.Render;
using Camera = GeoViewer.Core.Camera;

namespace GeoViewer
{
    /// <summary>
    /// The 3D viewport: renders the scene with the shared software tracer and handles the touch
    /// gestures (one finger orbits, two fingers pinch to zoom and drag to pan).
    /// Everything is measured in device pixels, so the same code produces a sharp image on any
    /// screen density; the drawing itself is scaled to whatever size the layout gives us.
    /// </summary>
    internal sealed class ViewportView : View
    {
        private readonly Scene _scene;

        private Bitmap _bitmap;
        private bool _busy;
        private bool _dirty;
        private bool _wantFinal;
        private readonly object _gate = new object();

        private readonly Paint _bmpPaint = new Paint(PaintFlags.FilterBitmap) { AntiAlias = true, Dither = true };
        private readonly Paint _fill = new Paint { AntiAlias = true };
        private readonly Paint _stroke = new Paint { AntiAlias = true };
        private readonly Paint _text = new Paint { AntiAlias = true };

        private readonly Color _ink = Color.Argb(255, 226, 233, 240);
        private readonly Color _inkDim = Color.Argb(190, 200, 210, 220);
        private readonly Color _accent = Color.Argb(235, 79, 182, 204);
        private readonly Color _orange = Color.Argb(235, 226, 154, 60);
        private readonly Color _panelBg = Color.Argb(150, 12, 15, 20);

        // gesture state
        private float _lastX, _lastY, _lastPinch, _lastMidX, _lastMidY;
        private bool _gestureMoved;

        public Action CameraChanged;
        public Action<bool> RenderFinished;

        public ViewportView(Context ctx, Scene scene) : base(ctx)
        {
            _scene = scene;
            SetBackgroundColor(Color.ParseColor("#12141A"));
            Focusable = true;
            _stroke.SetStyle(Paint.Style.Stroke);
            _fill.SetStyle(Paint.Style.Fill);
        }

        public Scene Scene => _scene;

        // ------------------------------------------------------------------ sizing / framing

        public void FitCamera()
        {
            if (Width <= 0 || Height <= 0) return;
            _scene.Fit(Width, Height, FitMargin());
            RequestRender(true);
        }

        public void ResetCamera()
        {
            if (Width <= 0 || Height <= 0) return;
            _scene.ResetView(Width, Height);
            _scene.Fit(Width, Height, FitMargin());
            RequestRender(true);
        }

        /// <summary>
        /// A tall, narrow frame is bounded by the horizontal field of view, so it wastes a lot of
        /// vertical space; a tighter margin makes the solid read bigger on a phone in portrait
        /// while still keeping it inside the frame on any aspect ratio.
        /// </summary>
        private double FitMargin()
        {
            if (Width <= 0 || Height <= 0) return 1.16;
            double aspect = (double)Width / Height;
            if (aspect < 0.6) return 1.03;
            if (aspect < 0.85) return 1.08;
            return 1.16;
        }

        protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
        {
            base.OnSizeChanged(w, h, oldw, oldh);
            if (w <= 0 || h <= 0) return;

            try
            {
                if (oldw <= 0 || oldh <= 0)
                {
                    _scene.ResetView(w, h);
                    _scene.Fit(w, h, FitMargin());
                }
                else
                    _scene.Fit(w, h, FitMargin());
            }
            catch (Exception ex)
            {
                Diagnostics.Write("size fit failed", ex);
            }

            Diagnostics.Write("size " + w + "x" + h);
            // Let the window become visible before the first (heaviest) frame is traced.
            PostDelayed(() => { try { RequestRender(true); } catch (Exception ex) { Diagnostics.Write("render request failed", ex); } }, 120);
        }

        // ------------------------------------------------------------------ rendering

        public void RequestRender(bool final)
        {
            lock (_gate)
            {
                _wantFinal |= final;
                if (_busy) { _dirty = true; return; }
                _busy = true;
                _dirty = false;
            }
            Task.Run(() => RenderLoop());
        }

        private void RenderLoop()
        {
            while (true)
            {
                bool final;
                lock (_gate)
                {
                    final = _wantFinal;
                    _wantFinal = false;
                    _dirty = false;
                }
                try { RenderOnce(final); }
                catch (Exception ex) { Diagnostics.Write("render failed", ex); }

                lock (_gate)
                {
                    if (!_dirty) { _busy = false; return; }
                }
            }
        }

        /// <summary>
        /// Phone screens are huge compared with what a software tracer can afford, so the target
        /// image gets a pixel budget of its own: the bitmap is handed to the GPU anyway and the
        /// perspective does not change with resolution, so a smaller target keeps the heap small
        /// and the frame quick without looking soft.
        /// </summary>
        private const double MaxFinalPixels = 1150.0 * 1150.0;
        private const double PreviewScale = 0.34;

        private int[] _px;

        private void RenderOnce(bool final)
        {
            int vw = Width, vh = Height;
            if (vw < 16 || vh < 16) return;

            double scale = final
                ? Math.Min(1.0, Math.Sqrt(MaxFinalPixels / ((double)vw * vh)))
                : PreviewScale;
            int rw = Math.Max(96, (int)Math.Round(vw * scale));
            int rh = Math.Max(96, (int)Math.Round(vh * scale));

            var opt = new RenderOptions
            {
                Width = rw,
                Height = rh,
                Supersample = 1,
                DrawLines = true,
                Exposure = final ? 1.05 : 1.0
            };

            Diagnostics.Write("render " + (final ? "final" : "preview") + " " + rw + "x" + rh);
            RenderResult res = Renderer.Render(_scene, opt);
            byte[] bgra = res.Bgra;
            int n = rw * rh;
            if (bgra.Length < n * 4) return;

            if (_px == null || _px.Length != n) _px = new int[n];
            int[] px = _px;
            for (int i = 0; i < n; i++)
            {
                int o = i * 4;
                px[i] = (bgra[o + 3] << 24) | (bgra[o + 2] << 16) | (bgra[o + 1] << 8) | bgra[o];
            }

            Bitmap bmp = Bitmap.CreateBitmap(rw, rh, Bitmap.Config.Argb8888);
            bmp.SetPixels(px, 0, rw, 0, 0, rw, rh);

            Post(() =>
            {
                // Do not recycle the old bitmap: with hardware acceleration the previous frame may
                // still reference it on the GPU, and recycling in that window can kill the process
                // natively. A few megabytes per frame is cheap enough to leave to the GC.
                _bitmap = bmp;
                Invalidate();
                Diagnostics.Write("render shown " + (final ? "final" : "preview"));
                RenderFinished?.Invoke(final);
            });
        }

        // ------------------------------------------------------------------ drawing

        private bool _drawFailed;
        private string _drawError = "";

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);

            if (_drawFailed)
            {
                _text.SetColor(Color.Argb(255, 226, 96, 88).ToArgb());
                _text.TextSize = Dp(11);
                _text.TextAlign = Paint.Align.Left;
                canvas.DrawText("绘制出错：" + _drawError, Dp(10), Dp(48), _text);
                return;
            }

            try
            {
                if (_bitmap != null && !_bitmap.IsRecycled)
                    canvas.DrawBitmap(_bitmap, null, new Rect(0, 0, Width, Height), _bmpPaint);
                else
                {
                    _text.SetColor(_inkDim.ToArgb());
                    _text.TextSize = Dp(14);
                    _text.TextAlign = Paint.Align.Center;
                    canvas.DrawText("正在渲染…", Width * 0.5f, Height * 0.5f, _text);
                }

                DrawStatus(canvas);
                DrawTopView(canvas);
            }
            catch (Exception ex)
            {
                _drawFailed = true;
                _drawError = ex.GetType().Name + ": " + ex.Message;
                Diagnostics.Write("onDraw failed", ex);
                Invalidate();
            }
        }

        private void DrawStatus(Canvas canvas)
        {
            string mode = _scene.Mode switch
            {
                ShadingMode.Translucent => "半透明",
                ShadingMode.Outline => "仅轮廓线",
                _ => "不透明"
            };
            string status = mode + " · 结构线 " + (_scene.StructureLines ? "开" : "关");
            if (_scene.PerspectiveLines) status += " · 透视线 开";

            _text.SetColor(Color.Argb(150, 226, 233, 240).ToArgb());
            _text.TextSize = Dp(11.5f);
            _text.TextAlign = Paint.Align.Left;
            canvas.DrawText(status, Dp(12), Dp(18), _text);
        }

        // ------------------------------------------------------------------ top view inset

        private void DrawTopView(Canvas canvas)
        {
            float size = Math.Min(Dp(104), Math.Min(Width, Height) * 0.34f);
            if (size < Dp(56)) return;

            float left = Width - size - Dp(10);
            float top = Dp(10);
            var box = new RectF(left, top, left + size, top + size);

            _fill.SetColor(_panelBg.ToArgb());
            canvas.DrawRoundRect(box, Dp(8), Dp(8), _fill);
            _stroke.SetColor(Color.Argb(55, 255, 255, 255).ToArgb());
            _stroke.StrokeWidth = Dp(1);
            canvas.DrawRoundRect(box, Dp(8), Dp(8), _stroke);

            float a = (float)Math.Max(0.2, _scene.EdgeLength);
            float pad = Dp(10);
            var inner = new RectF(box.Left + pad, box.Top + pad + Dp(11), box.Right - pad, box.Bottom - pad);

            // world extent: footprints of every solid plus one edge length of margin
            float span = Math.Max(3.0f * a, 2.2f * a);
            float scale = Math.Min(inner.Width(), inner.Height()) / span;
            float cx = inner.CenterX();
            float cy = inner.CenterY() + Dp(4);
            float MapX(double wx) => cx + (float)(wx * scale);
            float MapY(double wz) => cy + (float)(wz * scale);

            // grid
            _stroke.SetColor(Color.Argb(38, 255, 255, 255).ToArgb());
            _stroke.StrokeWidth = Dp(0.8f);
            int steps = (int)Math.Ceiling(span / a);
            for (int i = -steps; i <= steps; i++)
            {
                float x = MapX(i * a), y = MapY(i * a);
                if (x >= inner.Left && x <= inner.Right)
                    canvas.DrawLine(x, inner.Top, x, inner.Bottom, _stroke);
                if (y >= inner.Top && y <= inner.Bottom)
                    canvas.DrawLine(inner.Left, y, inner.Right, y, _stroke);
            }

            // world axes
            _stroke.SetColor(Color.Argb(70, 226, 154, 60).ToArgb());
            _stroke.StrokeWidth = Dp(1.1f);
            canvas.DrawLine(MapX(-span), MapY(0), MapX(span), MapY(0), _stroke);
            canvas.DrawLine(MapX(0), MapY(-span), MapX(0), MapY(span), _stroke);

            // footprint of the displayed solid
            Solid s = _scene.Solids[_scene.Selected];
            bool square = s.Kind == SolidKind.Cube || s.Kind == SolidKind.Cuboid;
            float hw = (float)(square ? s.HalfWidth : s.Radius);
            float fx = MapX(s.Center.X), fy = MapY(s.Center.Z);
            _fill.SetColor(Color.Argb(38, 216, 209, 198).ToArgb());
            _stroke.SetColor(Color.Argb(190, 216, 209, 198).ToArgb());
            _stroke.StrokeWidth = Dp(1.2f);
            if (square)
            {
                var r = new RectF(fx - hw * scale, fy - hw * scale, fx + hw * scale, fy + hw * scale);
                canvas.DrawRect(r, _fill);
                canvas.DrawRect(r, _stroke);
            }
            else
            {
                canvas.DrawOval(new RectF(fx - hw * scale, fy - hw * scale, fx + hw * scale, fy + hw * scale), _fill);
                canvas.DrawOval(new RectF(fx - hw * scale, fy - hw * scale, fx + hw * scale, fy + hw * scale), _stroke);
            }

            // camera
            Camera cam = _scene.Camera;
            double ex = cam.Eye.X, ez = cam.Eye.Z;
            float pxc = MapX(ex), pyc = MapY(ez);
            bool clamped = false;
            float m = Dp(5);
            if (pxc < inner.Left + m) { pxc = inner.Left + m; clamped = true; }
            if (pxc > inner.Right - m) { pxc = inner.Right - m; clamped = true; }
            if (pyc < inner.Top + m) { pyc = inner.Top + m; clamped = true; }
            if (pyc > inner.Bottom - m) { pyc = inner.Bottom - m; clamped = true; }

            // field-of-view wedge (only when the camera is inside the panel)
            if (!clamped && cam.Distance > 1e-6)
            {
                double len = cam.Distance;
                double half = cam.FovYDeg * 0.5 * MathUtil.Deg2Rad;
                half = Math.Min(half, 45.0 * MathUtil.Deg2Rad);
                double baseAng = Math.Atan2(ez - cam.Target.Z, ex - cam.Target.X);
                _fill.SetColor(Color.Argb(34, 79, 182, 204).ToArgb());
                var path = new Path();
                path.MoveTo(pxc, pyc);
                path.LineTo(MapX(cam.Target.X + Math.Cos(baseAng - half) * len),
                            MapY(cam.Target.Z + Math.Sin(baseAng - half) * len));
                path.LineTo(MapX(cam.Target.X + Math.Cos(baseAng + half) * len),
                            MapY(cam.Target.Z + Math.Sin(baseAng + half) * len));
                path.Close();
                canvas.DrawPath(path, _fill);
                path.Dispose();
            }

            // line of sight
            _stroke.SetColor(Color.Argb(150, 79, 182, 204).ToArgb());
            _stroke.StrokeWidth = Dp(1);
            var dash = new DashPathEffect(new float[] { Dp(4), Dp(3) }, 0);
            _stroke.SetPathEffect(dash);
            canvas.DrawLine(pxc, pyc, MapX(cam.Target.X), MapY(cam.Target.Z), _stroke);
            _stroke.SetPathEffect(null);

            _fill.SetColor(_accent.ToArgb());
            canvas.DrawCircle(pxc, pyc, Dp(3.6f), _fill);
            _fill.SetColor(_orange.ToArgb());
            canvas.DrawCircle(MapX(cam.Target.X), MapY(cam.Target.Z), Dp(2.6f), _fill);

            // distance read-out
            _text.SetColor(_inkDim.ToArgb());
            _text.TextSize = Dp(10.5f);
            _text.TextAlign = Paint.Align.Left;
            string dist = (cam.Distance / a).ToString("0.0", CultureInfo.InvariantCulture);
            canvas.DrawText("视距 " + dist + " a", box.Left + Dp(10), box.Top + Dp(15), _text);
        }

        // ------------------------------------------------------------------ touch

        public override bool OnTouchEvent(MotionEvent e)
        {
            switch (e.ActionMasked)
            {
                case MotionEventActions.Down:
                    _lastX = e.GetX();
                    _lastY = e.GetY();
                    _gestureMoved = false;
                    Parent?.RequestDisallowInterceptTouchEvent(true);
                    return true;

                case MotionEventActions.PointerDown:
                    if (e.PointerCount >= 2)
                    {
                        _lastPinch = Spacing(e);
                        _lastMidX = (e.GetX(0) + e.GetX(1)) * 0.5f;
                        _lastMidY = (e.GetY(0) + e.GetY(1)) * 0.5f;
                    }
                    return true;

                case MotionEventActions.Move:
                    if (e.PointerCount >= 2)
                    {
                        float pinch = Spacing(e);
                        float midX = (e.GetX(0) + e.GetX(1)) * 0.5f;
                        float midY = (e.GetY(0) + e.GetY(1)) * 0.5f;
                        if (_lastPinch > 1f && pinch > 1f)
                            _scene.Camera.Zoom(pinch / _lastPinch);
                        _scene.Camera.PanPixels(midX - _lastMidX, midY - _lastMidY);
                        _scene.Camera.Update();
                        _lastPinch = pinch;
                        _lastMidX = midX;
                        _lastMidY = midY;
                        _gestureMoved = true;
                        RequestRender(false);
                    }
                    else
                    {
                        float dx = e.GetX() - _lastX;
                        float dy = e.GetY() - _lastY;
                        _lastX = e.GetX();
                        _lastY = e.GetY();
                        if (Math.Abs(dx) + Math.Abs(dy) > 1.5f)
                        {
                            // ~200 degrees of yaw across the full width of the viewport, whatever the size
                            double perPixel = 200.0 / Math.Max(1, Width);
                            _scene.Camera.Orbit(dx * perPixel / 0.32, dy * perPixel / 0.32);
                            _scene.Camera.Update();
                            _gestureMoved = true;
                            RequestRender(false);
                        }
                    }
                    return true;

                case MotionEventActions.PointerUp:
                    _lastPinch = 0;
                    _lastX = e.GetX();
                    _lastY = e.GetY();
                    return true;

                case MotionEventActions.Up:
                case MotionEventActions.Cancel:
                    Parent?.RequestDisallowInterceptTouchEvent(false);
                    if (_gestureMoved)
                    {
                        RequestRender(true);
                        CameraChanged?.Invoke();
                    }
                    return true;
            }
            return base.OnTouchEvent(e);
        }

        private static float Spacing(MotionEvent e)
        {
            if (e.PointerCount < 2) return 0f;
            float dx = e.GetX(0) - e.GetX(1);
            float dy = e.GetY(0) - e.GetY(1);
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        // ------------------------------------------------------------------ helpers

        public float Dp(float value) => value * Resources.DisplayMetrics.Density;
    }
}
