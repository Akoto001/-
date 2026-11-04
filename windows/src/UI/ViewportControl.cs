using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GeoViewer.Core;
using GeoViewer.Render;
using RenderOptions = GeoViewer.Render.RenderOptions;

namespace GeoViewer.UI
{
    /// <summary>Interactive 3D view: shows the rendered frame and drives the camera.</summary>
    public sealed class ViewportControl : FrameworkElement
    {
        private Scene _scene;
        private WriteableBitmap _bitmap;
        private bool _rendering;
        private bool _dirty;
        private bool _wantFinal;
        private bool _needFit = true;

        private Point _last;
        private bool _orbiting;
        private bool _panning;

        private readonly Brush _bg = Frozen(new SolidColorBrush(Color.FromRgb(0x14, 0x16, 0x1B)));

        public event EventHandler CameraChanged;

        public ViewportControl()
        {
            Focusable = true;
            ClipToBounds = true;
        }

        private static Brush Frozen(Brush b) { b.Freeze(); return b; }

        public Scene Scene
        {
            get => _scene;
            set { _scene = value; _needFit = true; _bitmap = null; RequestRender(true); }
        }

        /// <summary>Number of device pixels per DIP.</summary>
        private double Dpi
        {
            get
            {
                try { return VisualTreeHelper.GetDpi(this).DpiScaleX; }
                catch { return 1.0; }
            }
        }

        public void RequestRender(bool final)
        {
            if (_scene == null) return;
            _wantFinal |= final;
            if (_rendering) { _dirty = true; return; }
            _ = RenderAsync();
        }

        private async Task RenderAsync()
        {
            if (_scene == null) return;

            double dpi = Dpi;
            int w = (int)Math.Round(ActualWidth * dpi);
            int h = (int)Math.Round(ActualHeight * dpi);
            if (w < 8 || h < 8) return;

            _rendering = true;
            _dirty = false;
            bool final = _wantFinal;
            _wantFinal = false;

            int rw = final ? w : Math.Max(8, (int)(w * 0.58));
            int rh = final ? h : Math.Max(8, (int)(h * 0.58));

            var opt = new RenderOptions
            {
                Width = rw,
                Height = rh,
                Supersample = final ? 2 : 1,
                DrawLines = true
            };

            RenderResult result = null;
            try
            {
                Scene snapshot = _scene;
                if (Environment.GetEnvironmentVariable("GEO_UI_LOG") == "1")
                    MainWindow.DiagLog($"render start final={final} {w}x{h} el={snapshot.Camera.ElevationDeg:F1} az={snapshot.Camera.AzimuthDeg:F1} dist={snapshot.Camera.Distance:F2} show={string.Join("", snapshot.Show.Select(b => b ? 1 : 0))}");
                result = await Task.Run(() => Renderer.Render(snapshot, opt));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }

            if (result != null)
            {
                if (_bitmap == null || _bitmap.PixelWidth != result.Width || _bitmap.PixelHeight != result.Height)
                    _bitmap = new WriteableBitmap(result.Width, result.Height, 96, 96, PixelFormats.Bgra32, null);
                _bitmap.WritePixels(new Int32Rect(0, 0, result.Width, result.Height),
                                    result.Bgra, result.Width * 4, 0);
                InvalidateVisual();
            }

            _rendering = false;
            if (_dirty) _ = RenderAsync();
        }

        protected override void OnRender(DrawingContext dc)
        {
            var rect = new Rect(0, 0, ActualWidth, ActualHeight);
            dc.DrawRectangle(_bg, null, rect);
            if (_bitmap != null)
            {
                System.Windows.Media.RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
                dc.DrawImage(_bitmap, rect);
            }
            else
            {
                var ft = new FormattedText("正在渲染…", System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), 12,
                    Frozen(new SolidColorBrush(Color.FromRgb(0x6C, 0x74, 0x81))),
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(ft, new Point((ActualWidth - ft.Width) / 2, (ActualHeight - ft.Height) / 2));
            }
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            if (_scene != null)
            {
                double dpi = Dpi;
                _scene.Camera.Width = Math.Max(4, (int)Math.Round(ActualWidth * dpi));
                _scene.Camera.Height = Math.Max(4, (int)Math.Round(ActualHeight * dpi));
                _scene.Camera.Update();

                // The very first frame has to be framed once the control really has its size;
                // fitting earlier (while Width/Height are still 0) produces a bogus distance.
                if (_needFit && ActualWidth > 40 && ActualHeight > 40)
                {
                    _needFit = false;
                    _scene.ResetView(ActualWidth * dpi, ActualHeight * dpi);
                    if (Environment.GetEnvironmentVariable("GEO_UI_LOG") == "1")
                        MainWindow.DiagLog($"needFit ResetView: {ActualWidth:F0}x{ActualHeight:F0} el={_scene.Camera.ElevationDeg:F1} dist={_scene.Camera.Distance:F2}");
                }
            }
            RequestRender(true);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            Focus();
            if (e.ClickCount == 2)
            {
                _scene.ResetView(ActualWidth * Dpi, ActualHeight * Dpi);
                RequestRender(true);
                CameraChanged?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                return;
            }
            _last = e.GetPosition(this);
            _orbiting = (Keyboard.Modifiers & ModifierKeys.Shift) == 0;
            _panning = !_orbiting;
            CaptureMouse();
            Cursor = _orbiting ? Cursors.SizeAll : Cursors.Hand;
            e.Handled = true;
        }

        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
        {
            Focus();
            _last = e.GetPosition(this);
            _panning = true;
            _orbiting = false;
            CaptureMouse();
            Cursor = Cursors.Hand;
            e.Handled = true;
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            // Middle drag is the modelling-app "truck/pedestal" move: the solid keeps its pose and
            // only the camera slides inside the plane it is looking through.
            if (e.ChangedButton != MouseButton.Middle) return;
            Focus();
            _last = e.GetPosition(this);
            _panning = true;
            _orbiting = false;
            CaptureMouse();
            Cursor = Cursors.SizeAll;
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!_orbiting && !_panning) return;
            Point p = e.GetPosition(this);
            double dx = p.X - _last.X;
            double dy = p.Y - _last.Y;
            _last = p;
            if (Math.Abs(dx) < 0.01 && Math.Abs(dy) < 0.01) return;

            // The camera measures in device pixels (it is told the rendered size), the mouse in DIUs.
            double dpi = Dpi;
            if (_orbiting) _scene.TryOrbit(dx, dy);
            else _scene.Camera.PanPixels(dx * dpi, dy * dpi);
            _scene.Camera.Update();

            RequestRender(false);
            CameraChanged?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            if (!_orbiting && !_panning) return;
            _orbiting = false;
            _panning = false;
            ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
            RequestRender(true);
            CameraChanged?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            _scene.Camera.Zoom(Math.Pow(1.0016, e.Delta));
            _scene.Camera.Update();
            RequestRender(true);
            CameraChanged?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.F)
            {
                _scene.Fit(ActualWidth * Dpi, ActualHeight * Dpi);
                RequestRender(true);
                CameraChanged?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
        }
    }
}
