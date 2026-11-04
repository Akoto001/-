using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using Android.Widget;

using GeoViewer.Core;
using GeoViewer.Render;

using Camera = GeoViewer.Core.Camera;
using Color = Android.Graphics.Color;

namespace GeoViewer
{
    // NOTE: there is deliberately no custom [Application] subclass and no custom View subclass here.
    // Probe C showed that an [Application] subclass kills this app on Android 16, and Probe D showed
    // that a plain ImageView fed with a rendered bitmap works fine. Everything below sticks to stock
    // widgets for that reason; the label/icon/theme live in AndroidManifest.xml.

    /// <summary>Breadcrumb trail for a device we cannot attach a debugger to. Never throws.</summary>
    internal static class Diag
    {
        private static readonly List<string> Dirs = new List<string>();
        private static int _count;

        public static void Init(Context context)
        {
            try
            {
                try
                {
                    Java.IO.File[] media = context.GetExternalMediaDirs();
                    if (media != null && media.Length > 0 && media[0] != null)
                        Dirs.Add(media[0].AbsolutePath);
                }
                catch { }
                if (context.FilesDir != null) Dirs.Add(context.FilesDir.AbsolutePath);
            }
            catch { }
        }

        public static string Where()
        {
            return Dirs.Count > 0 ? Dirs[0] : "(none)";
        }

        public static void Write(string message, Exception error = null)
        {
            // Writing a file is expensive; keep a short breadcrumb trail and then stay silent, so the
            // log never competes with rendering. Errors are always written.
            int n = Interlocked.Increment(ref _count);
            if (error == null && n > 60 && (n % 120) != 0) return;

            string line = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message;
            if (error != null) line += "\n    " + error.GetType().Name + ": " + error.Message;

            for (int i = 0; i < Dirs.Count; i++)
            {
                try
                {
                    Directory.CreateDirectory(Dirs[i]);
                    File.AppendAllText(System.IO.Path.Combine(Dirs[i], "app.log"), line + "\n");
                }
                catch { }
            }
        }
    }

    [Activity(MainLauncher = true, Theme = "@android:style/Theme.Material.NoActionBar")]
    public sealed class MainActivity : Activity
    {
        private static readonly string[] ModelNames = { "球体", "圆柱体", "正方体", "长方体", "圆锥体" };
        private static readonly string[] ModeNames = { "不透明", "半透明", "仅轮廓线" };

        private static readonly Color BgColor = Color.ParseColor("#12141A");
        private static readonly Color PanelColor = Color.ParseColor("#171A1F");
        private static readonly Color FieldColor = Color.ParseColor("#20242B");
        private static readonly Color TextColor = Color.ParseColor("#E2E5EC");
        private static readonly Color DimColor = Color.ParseColor("#9AA2AE");
        private static readonly Color AccentColor = Color.ParseColor("#4FB6CC");

        private readonly Scene _scene = new Scene();

        private FrameLayout _frame;
        private ImageView _image;
        private TextView _status;
        private LinearLayout _root;
        private LinearLayout _panel;
        private ScrollView _panelScroll;
        private Button _foldButton;
        private LinearLayout _panelHost;
        private Button _orientToggle;
        private bool _panelCollapsed;
        private bool _forcedLandscape;

        private Button _modelValue;
        private Button _modeValue;
        private Button _structureButton;
        private Button _perspectiveButton;
        private Button[] _angleValues;
        private Button _focalValue;

        private Bitmap _bitmap;
        private int[] _pxA, _pxB;
        private bool _flip;
        private Bitmap _buffer;
        private int _bufferW, _bufferH;
        private readonly RenderBuffers _buffers = new RenderBuffers();
        private int _rendering;
        private bool _again;
        private int _lastPreviewTick;

        private float _lastX, _lastY, _lastPinch;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            Diag.Init(this);
            Diag.Write("OnCreate");
            try
            {
                BuildUi();
                Diag.Write("UI ready");
            }
            catch (Exception ex)
            {
                Diag.Write("BuildUi failed", ex);
                ShowFatal(ex);
            }
        }

        // ------------------------------------------------------------------ ui

        private void BuildUi()
        {
            Window.SetBackgroundDrawable(new ColorDrawable(BgColor));

            _image = new ImageView(this);
            _image.SetScaleType(ImageView.ScaleType.FitXy);
            _image.SetBackgroundColor(BgColor);
            _image.SetOnTouchListener(new TouchRelay(this));

            _status = new TextView(this) { TextSize = 11.5f };
            _status.SetTextColor(Color.Argb(170, 226, 233, 240));
            _status.SetPadding(Dp(12), Dp(10), Dp(12), Dp(10));

            _frame = new FrameLayout(this);
            _frame.AddView(_image, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            _frame.AddView(_status, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent,
                GravityFlags.Top | GravityFlags.Left));

            // Always reachable, even with the panel put away. The panel's own fold/unfold triangle
            // lives on the panel's top edge (see BuildPanelArea), this strip is only the orientation
            // switch.
            var corner = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            _orientToggle = CornerButton("横屏");
            _orientToggle.Click += (s, e) => ToggleLandscape();
            corner.AddView(_orientToggle, new LinearLayout.LayoutParams(Dp(56), Dp(32)));
            corner.SetPadding(0, Dp(8), Dp(10), 0);
            _frame.AddView(corner, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent,
                GravityFlags.Top | GravityFlags.Right));

            Diag.Write("frame ready");

#if PROBE_VIEWPORT
            _panel = new LinearLayout(this) { Orientation = Orientation.Vertical };
#else
            _panel = BuildPanel();
#endif
            Diag.Write("panel ready");

            _panelScroll = new ScrollView(this);
            _panelScroll.VerticalScrollBarEnabled = false;
            _panelScroll.AddView(_panel, new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

            // The fold strip sits on the panel's top edge, so it stays put when the panel folds away.
            _foldButton = new Button(this) { Text = "▼", TextSize = 12f };
            _foldButton.SetAllCaps(false);
            _foldButton.SetTextColor(Color.Argb(210, 226, 233, 240));
            _foldButton.SetPadding(0, 0, 0, 0);
            _foldButton.Background = RoundRect(Color.Argb(70, 255, 255, 255), Dp(6), Color.Transparent);
            _foldButton.Click += (s, e) => TogglePanel();

            var foldRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            foldRow.SetGravity(GravityFlags.Center);
            foldRow.SetPadding(Dp(6), Dp(3), Dp(6), Dp(3));
            foldRow.AddView(_foldButton, new LinearLayout.LayoutParams(Dp(64), Dp(20)));

            _panelHost = new LinearLayout(this) { Orientation = Orientation.Vertical };
            _panelHost.AddView(foldRow, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
            _panelHost.AddView(_panelScroll, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, 0, 1f));

            _root = new LinearLayout(this) { Orientation = Orientation.Vertical };
            _root.SetBackgroundColor(BgColor);
            SetContentView(_root);
            ApplyOrientation();
            Diag.Write("content set");

            _frame.PostDelayed(() =>
            {
                Diag.Write("first render requested");
                RefreshPanel();
                RequestRender(true);
            }, 200);
        }

        private void ApplyOrientation()
        {
            if (_root == null) return;
            bool landscape = Resources.Configuration.Orientation == Android.Content.Res.Orientation.Landscape;

            _root.RemoveAllViews();
            _root.Orientation = landscape ? Orientation.Horizontal : Orientation.Vertical;

            if (landscape)
            {
                _root.AddView(_frame, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f));
                _root.AddView(_panelHost, new LinearLayout.LayoutParams(
                    _panelCollapsed ? Dp(18) : Dp(310), ViewGroup.LayoutParams.MatchParent));
            }
            else
            {
                _root.AddView(_frame, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));
                _root.AddView(_panelHost, _panelCollapsed
                    ? new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(26))
                    : new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 0.9f));
            }

            _panelScroll.Visibility = _panelCollapsed ? ViewStates.Gone : ViewStates.Visible;
            if (_foldButton != null) _foldButton.Text = _panelCollapsed ? "▲" : "▼";
        }

        private void TogglePanel()
        {
            _panelCollapsed = !_panelCollapsed;
            try
            {
                ApplyOrientation();
                _frame.PostDelayed(() => RequestRender(true), 60);
            }
            catch (Exception ex) { Diag.Write("toggle panel failed", ex); }
        }

        private void ToggleLandscape()
        {
            _forcedLandscape = !_forcedLandscape;
            try
            {
                if (_forcedLandscape)
                {
                    // SensorLandscape: stays landscape whichever way round the phone is held, and it
                    // wins over the system "auto-rotate off" setting - that is the whole point.
                    RequestedOrientation = Android.Content.PM.ScreenOrientation.SensorLandscape;
                    SetBarsHidden(true);
                }
                else
                {
                    RequestedOrientation = Android.Content.PM.ScreenOrientation.Unspecified;
                    SetBarsHidden(false);
                }

                if (_orientToggle != null)
                {
                    _orientToggle.Text = _forcedLandscape ? "竖屏" : "横屏";
                    _orientToggle.Background = RoundRect(
                        _forcedLandscape ? AccentColor : Color.Argb(190, 32, 36, 43),
                        Dp(7), Color.Argb(_forcedLandscape ? 0 : 60, 255, 255, 255));
                    _orientToggle.SetTextColor(_forcedLandscape ? Color.ParseColor("#0E1A1E") : TextColor);
                }

                Diag.Write("landscape " + (_forcedLandscape ? "on" : "off"));
                ApplyOrientation();
                _frame.PostDelayed(() => RequestRender(true), 120);
            }
            catch (Exception ex) { Diag.Write("toggle orientation failed", ex); }
        }

        private void SetBarsHidden(bool hidden)
        {
            try
            {
                // The legacy immersive flags are deprecated on Android 11+ but still honoured, and
                // unlike the newer insets controller they exist in every binding we target.
#pragma warning disable CA1422
                StatusBarVisibility flags = hidden
                    ? (StatusBarVisibility)(SystemUiFlags.LayoutStable | SystemUiFlags.LayoutHideNavigation
                        | SystemUiFlags.LayoutFullscreen | SystemUiFlags.HideNavigation
                        | SystemUiFlags.Fullscreen | SystemUiFlags.ImmersiveSticky)
                    : (StatusBarVisibility)(SystemUiFlags.LayoutStable | SystemUiFlags.LayoutFullscreen
                        | SystemUiFlags.Visible);
                if (Window?.DecorView != null) Window.DecorView.SystemUiVisibility = flags;
#pragma warning restore CA1422
            }
            catch (Exception ex) { Diag.Write("system bars failed", ex); }
        }

        public override void OnConfigurationChanged(Android.Content.Res.Configuration newConfig)
        {
            base.OnConfigurationChanged(newConfig);
            try { ApplyOrientation(); RequestRender(true); }
            catch (Exception ex) { Diag.Write("orientation failed", ex); }
        }

        protected override void OnPause()
        {
            base.OnPause();
        }

        protected override void OnResume()
        {
            base.OnResume();
            try
            {
                if (_forcedLandscape) SetBarsHidden(true);
            }
            catch (Exception ex) { Diag.Write("resume failed", ex); }
        }

        // ------------------------------------------------------------------ panel

        private LinearLayout BuildPanel()
        {
            var panel = new LinearLayout(this) { Orientation = Orientation.Vertical };
            panel.SetBackgroundColor(PanelColor);
            int pad = Dp(12);
            panel.SetPadding(pad, pad, pad, pad);

            _modelValue = FieldButton();
            _modelValue.Click += (s, e) => ShowList("当前模型", ModelNames, _scene.Selected, i =>
            {
                _scene.Select(i);
                RequestRender(true);
            });
            panel.AddView(Row("模型", _modelValue));

            _modeValue = FieldButton();
            _modeValue.Click += (s, e) => ShowList("建模样貌", ModeNames, (int)_scene.Mode, i =>
            {
                _scene.Mode = (ShadingMode)i;
                RequestRender(true);
            });
            panel.AddView(Row("模样", _modeValue));

            var toggles = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            toggles.SetPadding(0, Dp(4), 0, 0);
            _structureButton = ActionButton("");
            _structureButton.Click += (s, e) =>
            {
                _scene.StructureLines = !_scene.StructureLines;
                RequestRender(true);
            };
            _perspectiveButton = ActionButton("");
            _perspectiveButton.Click += (s, e) =>
            {
                _scene.PerspectiveLines = !_scene.PerspectiveLines;
                RequestRender(true);
            };
            toggles.AddView(_structureButton, new LinearLayout.LayoutParams(0, Dp(38), 1f) { RightMargin = Dp(5) });
            toggles.AddView(_perspectiveButton, new LinearLayout.LayoutParams(0, Dp(38), 1f) { LeftMargin = Dp(5) });
            panel.AddView(toggles);

            panel.AddView(SectionLabel("摄像机角度"));
            _angleValues = new Button[3];
            _angleValues[0] = AngleRow(panel, "X 俯仰", 0, -90, 90);
            _angleValues[1] = AngleRow(panel, "Y 方位", 1, -180, 180);
            _angleValues[2] = AngleRow(panel, "Z 滚转", 2, -90, 90);

            _focalValue = FieldButton();
            _focalValue.Click += (s, e) => ShowFocalList();
            panel.AddView(StepRow("焦距", _focalValue, () => StepFocal(-1), () => StepFocal(1)));

            var actions = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            actions.SetPadding(0, Dp(8), 0, 0);
            Button fit = ActionButton("适配窗口");
            fit.Click += (s, e) => { FitCamera(); RequestRender(true); };
            Button reset = ActionButton("重置视角");
            reset.Click += (s, e) => { ResetCamera(); RequestRender(true); };
            actions.AddView(fit, new LinearLayout.LayoutParams(0, Dp(40), 1f) { RightMargin = Dp(5) });
            actions.AddView(reset, new LinearLayout.LayoutParams(0, Dp(40), 1f) { LeftMargin = Dp(5) });
            panel.AddView(actions);

            var hint = new TextView(this) { Text = "单指拖拽旋转 · 双指缩放与平移 · 点数值挑预设", TextSize = 11.5f };
            hint.SetTextColor(Color.Argb(190, 154, 162, 174));
            hint.SetPadding(0, Dp(10), 0, 0);
            panel.AddView(hint);

            return panel;
        }

        private Button AngleRow(LinearLayout panel, string label, int axis, double min, double max)
        {
            Button value = FieldButton();
            value.Click += (s, e) => ShowList(label, AngleItems(min, max), NearestAngleIndex(axis, min), i =>
            {
                SetAxisAngle(axis, min + i * 15.0);
            });
            panel.AddView(StepRow(label, value, () => NudgeAxis(axis, -1, min, max), () => NudgeAxis(axis, 1, min, max)));
            return value;
        }

        private LinearLayout StepRow(string label, Button value, Action minus, Action plus)
        {
            var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            row.SetPadding(0, Dp(3), 0, Dp(3));

            var text = new TextView(this) { Text = label, TextSize = 13f };
            text.SetTextColor(DimColor);
            text.Gravity = GravityFlags.CenterVertical;
            row.AddView(text, new LinearLayout.LayoutParams(Dp(58), Dp(40)));

            Button m = ActionButton("−");
            m.Click += (s, e) => minus();
            row.AddView(m, new LinearLayout.LayoutParams(Dp(42), Dp(40)) { RightMargin = Dp(6) });

            row.AddView(value, new LinearLayout.LayoutParams(0, Dp(40), 1f) { RightMargin = Dp(6) });

            Button p = ActionButton("+");
            p.Click += (s, e) => plus();
            row.AddView(p, new LinearLayout.LayoutParams(Dp(42), Dp(40)));
            return row;
        }

        private LinearLayout Row(string label, Button value)
        {
            var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            row.SetPadding(0, Dp(3), 0, Dp(3));

            var text = new TextView(this) { Text = label, TextSize = 13f };
            text.SetTextColor(DimColor);
            text.Gravity = GravityFlags.CenterVertical;
            row.AddView(text, new LinearLayout.LayoutParams(Dp(58), Dp(40)));
            row.AddView(value, new LinearLayout.LayoutParams(0, Dp(40), 1f));
            return row;
        }

        // ------------------------------------------------------------------ camera plumbing

        private double FitMargin()
        {
            int w = _image.Width, h = _image.Height;
            if (w <= 0 || h <= 0) return 1.16;
            double aspect = (double)w / h;
            if (aspect < 0.6) return 1.03;
            if (aspect < 0.85) return 1.08;
            return 1.16;
        }

        private void FitCamera()
        {
            int w = _image.Width, h = _image.Height;
            if (w <= 0 || h <= 0) return;
            _scene.Fit(w, h, FitMargin());
            RefreshPanel();
        }

        private void ResetCamera()
        {
            int w = _image.Width, h = _image.Height;
            if (w <= 0 || h <= 0) return;
            _scene.ResetView(w, h);
            _scene.Fit(w, h, FitMargin());
            RefreshPanel();
        }

        private double CurrentAngle(int axis)
        {
            if (axis == 0) return _scene.CurrentPitch;
            if (axis == 1) return _scene.CurrentYaw;
            return _scene.CurrentRoll;
        }

        private void SetAxisAngle(int axis, double value)
        {
            double pitch = _scene.CurrentPitch;
            double yaw = _scene.CurrentYaw;
            double roll = _scene.CurrentRoll;
            if (axis == 0) pitch = value;
            else if (axis == 1) yaw = value;
            else roll = value;

            _scene.ApplyAxisAngles(pitch, yaw, roll);
            FitCamera();
            RequestRender(true);
        }

        private void NudgeAxis(int axis, int dir, double min, double max)
        {
            double current = CurrentAngle(axis);
            double next = dir > 0
                ? Math.Floor(current / 15.0 + 1e-6) * 15.0 + 15.0
                : Math.Ceiling(current / 15.0 - 1e-6) * 15.0 - 15.0;
            SetAxisAngle(axis, Math.Clamp(next, min, max));
        }

        private void StepFocal(int dir)
        {
            double[] presets = Optics.PresetFocalMm;
            double current = _scene.FocalMm;
            int idx = 0;
            double best = double.MaxValue;
            for (int i = 0; i < presets.Length; i++)
            {
                double d = Math.Abs(presets[i] - current);
                if (d < best) { best = d; idx = i; }
            }
            if (dir > 0 && presets[idx] <= current + 0.5) idx = Math.Min(presets.Length - 1, idx + 1);
            if (dir < 0 && presets[idx] >= current - 0.5) idx = Math.Max(0, idx - 1);
            SetFocal(presets[idx]);
        }

        private void SetFocal(double mm)
        {
            _scene.SetFocalMm(mm);
            FitCamera();
            RequestRender(true);
        }

        private void ShowFocalList()
        {
            double[] presets = Optics.PresetFocalMm;
            var items = new string[presets.Length];
            int selected = 0;
            double best = double.MaxValue;
            for (int i = 0; i < presets.Length; i++)
            {
                items[i] = presets[i].ToString("0", CultureInfo.InvariantCulture) + " mm　"
                           + Optics.FovYFromFocal(presets[i]).ToString("0.#", CultureInfo.InvariantCulture) + "°";
                double d = Math.Abs(presets[i] - _scene.FocalMm);
                if (d < best) { best = d; selected = i; }
            }
            ShowList("焦距（35mm 等效）", items, selected, i => SetFocal(presets[i]));
        }

        private static string[] AngleItems(double min, double max)
        {
            int n = (int)Math.Round((max - min) / 15.0) + 1;
            var items = new string[n];
            for (int i = 0; i < n; i++)
            {
                double v = min + i * 15.0;
                string sign = v > 0.001 ? "+" : v < -0.001 ? "−" : "";
                items[i] = sign + Math.Abs(v).ToString("0", CultureInfo.InvariantCulture) + "°";
            }
            return items;
        }

        private int NearestAngleIndex(int axis, double min)
        {
            double current = CurrentAngle(axis);
            int idx = (int)Math.Round((current - min) / 15.0);
            int count = (int)Math.Round((axis == 1 ? 360.0 : 180.0) / 15.0) + 1;
            return Math.Clamp(idx, 0, count - 1);
        }

        // ------------------------------------------------------------------ rendering

        private int ViewportWidth
        {
            get
            {
                if (_image != null && _image.Width > 16) return _image.Width;
                int w = Resources.DisplayMetrics.WidthPixels;
                return w > 16 ? w : 720;
            }
        }

        private int ViewportHeight
        {
            get
            {
                if (_image != null && _image.Height > 16) return _image.Height;
                int h = Resources.DisplayMetrics.HeightPixels;
                return h > 16 ? (int)(h * 0.55) : 480;
            }
        }

        internal void RequestRender(bool final)
        {
            try
            {
                // While a finger is down we only need a handful of frames per second; dropping most of
                // the requests is what keeps dragging responsive on a phone.
                if (!final)
                {
                    int now = System.Environment.TickCount;
                    if (now - _lastPreviewTick < 45) return;
                    _lastPreviewTick = now;
                }

                if (Interlocked.CompareExchange(ref _rendering, 1, 0) != 0)
                {
                    _again = true;
                    return;
                }
                Task.Run(() => RenderLoop(final));
            }
            catch (Exception ex) { Diag.Write("render start failed", ex); }
        }

        private void RenderLoop(bool final)
        {
            try
            {
                while (true)
                {
                    RenderOnce(final);
                    if (!_again) break;
                    _again = false;
                    final = true;
                }
            }
            catch (Exception ex) { Diag.Write("render loop failed", ex); }
            finally { Interlocked.Exchange(ref _rendering, 0); }
        }

        private void RenderOnce(bool final)
        {
            int vw = ViewportWidth, vh = ViewportHeight;

            // Cost is decided by pixel count, not by a fixed fraction: phones report huge pixel sizes
            // (2656x1220 here), and 0.34 of that is a million rays per frame.
            double budget = final ? 760.0 * 760.0 : 250.0 * 250.0;
            double scale = Math.Min(1.0, Math.Sqrt(budget / ((double)vw * vh)));
            if (!final) scale = Math.Min(scale, 0.34);
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

            RenderResult res = Renderer.Render(_scene, opt, _buffers);
            byte[] bgra = res.Bgra;
            int n = rw * rh;
            if (bgra == null || bgra.Length < n * 4) { Diag.Write("render short buffer"); return; }

            // Convert here, on the render thread, and hand the finished row buffer to the UI thread:
            // the next frame then writes into the other buffer, so the two never step on each other.
            int[] px = _flip ? _pxB : _pxA;
            _flip = !_flip;
            if (px == null || px.Length != n) px = new int[n];
            if (_flip) _pxA = px; else _pxB = px;

            for (int i = 0; i < n; i++)
            {
                int o = i * 4;
                px[i] = (bgra[o + 3] << 24) | (bgra[o + 2] << 16) | (bgra[o + 1] << 8) | bgra[o];
            }

            RunOnUiThread(() =>
            {
                try
                {
                    if (_buffer == null || _bufferW != rw || _bufferH != rh)
                    {
                        _buffer = Bitmap.CreateBitmap(rw, rh, Bitmap.Config.Argb8888);
                        _bufferW = rw;
                        _bufferH = rh;
                        if (_buffer == null) { Diag.Write("bitmap null"); return; }
                    }

                    _buffer.SetPixels(px, 0, rw, 0, 0, rw, rh);
                    _bitmap = _buffer;
                    _image.SetImageBitmap(_buffer);
                    _image.Invalidate();
                    // Re-laying out every button mid-gesture costs more than the picture itself;
                    // the small status line is enough feedback while a finger is down.
                    if (final) RefreshPanel();
                    else RefreshStatus();
                }
                catch (Exception ex) { Diag.Write("show failed", ex); }
            });
        }

        // ------------------------------------------------------------------ gestures

        private sealed class TouchRelay : Java.Lang.Object, View.IOnTouchListener
        {
            private readonly MainActivity _activity;

            public TouchRelay(MainActivity activity) { _activity = activity; }

            public bool OnTouch(View v, MotionEvent e)
            {
                try { return _activity.HandleTouch(e); }
                catch (Exception ex) { Diag.Write("touch failed", ex); return false; }
            }
        }

        internal bool HandleTouch(MotionEvent e)
        {
            switch (e.ActionMasked)
            {
                case MotionEventActions.Down:
                    _lastX = e.GetX();
                    _lastY = e.GetY();
                    _lastPinch = 0f;
                    return true;

                case MotionEventActions.PointerDown:
                    _lastPinch = Spacing(e);
                    _lastX = e.GetX();
                    _lastY = e.GetY();
                    return true;

                case MotionEventActions.Move:
                    if (e.PointerCount >= 2)
                    {
                        // Pinch changes the distance; the midpoint drag slides the camera inside the
                        // plane it is facing, so the solid keeps its pose and only the framing moves.
                        float pinch = Spacing(e);
                        if (_lastPinch > 1f && pinch > 1f)
                        {
                            double ratio = pinch / _lastPinch;
                            if (Math.Abs(ratio - 1.0) > 0.006) _scene.Camera.Zoom(ratio);
                        }
                        _lastPinch = pinch;

                        float mx = (e.GetX(0) + e.GetX(1)) * 0.5f;
                        float my = (e.GetY(0) + e.GetY(1)) * 0.5f;
                        _scene.Camera.PanPixels(mx - _lastX, my - _lastY);
                        _lastX = mx;
                        _lastY = my;
                    }
                    else
                    {
                        float dx = e.GetX() - _lastX;
                        float dy = e.GetY() - _lastY;
                        _lastX = e.GetX();
                        _lastY = e.GetY();
                        if (Math.Abs(dx) + Math.Abs(dy) < 0.7f) return true;
                        double perPixel = 200.0 / Math.Max(120, _image.Width);
                        _scene.Camera.Orbit(dx * perPixel / 0.32, dy * perPixel / 0.32);
                    }
                    RequestRender(false);
                    return true;

                case MotionEventActions.Up:
                case MotionEventActions.Cancel:
                    _lastPinch = 0f;
                    RequestRender(true);
                    RefreshPanel();
                    return true;

                case MotionEventActions.PointerUp:
                    _lastPinch = 0f;
                    _lastX = e.GetX();
                    _lastY = e.GetY();
                    return true;
            }
            return false;
        }

        private static float Spacing(MotionEvent e)
        {
            if (e.PointerCount < 2) return 0f;
            float dx = e.GetX(1) - e.GetX(0);
            float dy = e.GetY(1) - e.GetY(0);
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        // ------------------------------------------------------------------ refresh / widgets

        private void RefreshPanel()
        {
            if (_panel == null) return;
            try
            {
                if (_modelValue != null) _modelValue.Text = ModelNames[_scene.Selected] + "  ▾";
                if (_modeValue != null) _modeValue.Text = ModeNames[(int)_scene.Mode] + "  ▾";
                if (_structureButton != null) _structureButton.Text = _scene.StructureLines ? "结构线 开" : "结构线 关";
                if (_perspectiveButton != null) _perspectiveButton.Text = _scene.PerspectiveLines ? "透视线 开" : "透视线 关";

                if (_angleValues != null)
                {
                    _angleValues[0].Text = Fmt(_scene.CurrentPitch) + "  ▾";
                    _angleValues[1].Text = Fmt(_scene.CurrentYaw) + "  ▾";
                    _angleValues[2].Text = Fmt(_scene.CurrentRoll) + "  ▾";
                }
                if (_focalValue != null)
                {
                    _focalValue.Text = _scene.FocalMm.ToString("0.#", CultureInfo.InvariantCulture) + " mm · "
                        + _scene.FovYDeg.ToString("0.#", CultureInfo.InvariantCulture) + "°  ▾";
                }

                if (_status != null)
                {
                    string mode = _scene.Mode == ShadingMode.Translucent ? "半透明"
                        : _scene.Mode == ShadingMode.Outline ? "仅轮廓线" : "不透明";
                    string text = mode + " · 结构线 " + (_scene.StructureLines ? "开" : "关");
                    if (_scene.PerspectiveLines) text += " · 透视线 开";
                    _status.Text = text;
                }
            }
            catch (Exception ex) { Diag.Write("refresh failed", ex); }
        }

        private void RefreshStatus()
        {
            if (_status == null) return;
            try
            {
                string mode = _scene.Mode == ShadingMode.Translucent ? "半透明"
                    : _scene.Mode == ShadingMode.Outline ? "仅轮廓线" : "不透明";
                string text = mode + " · 结构线 " + (_scene.StructureLines ? "开" : "关");
                if (_scene.PerspectiveLines) text += " · 透视线 开";
                _status.Text = text;
            }
            catch { }
        }

        private static string Fmt(double v)
        {
            string sign = v > 0.05 ? "+" : v < -0.05 ? "−" : "";
            return sign + Math.Abs(v).ToString("0.#", CultureInfo.InvariantCulture) + "°";
        }

        private void ShowList(string title, string[] items, int selected, Action<int> pick)
        {
            var builder = new AlertDialog.Builder(this);
            builder.SetTitle(title);
            builder.SetItems(items, (s, e) => { pick(e.Which); RefreshPanel(); });
            builder.Create().Show();
        }

        private Button FieldButton()
        {
            var b = new Button(this) { TextSize = 13f };
            b.SetAllCaps(false);
            b.SetTextColor(TextColor);
            b.Gravity = GravityFlags.CenterVertical | GravityFlags.Left;
            b.SetPadding(Dp(10), 0, Dp(10), 0);
            b.Background = RoundRect(FieldColor, Dp(7), Color.Argb(60, 255, 255, 255));
            return b;
        }

        private Button CornerButton(string text)
        {
            var b = new Button(this) { Text = text, TextSize = 11.5f };
            b.SetAllCaps(false);
            b.SetTextColor(TextColor);
            b.SetPadding(0, 0, 0, 0);
            b.Background = RoundRect(Color.Argb(190, 32, 36, 43), Dp(7), Color.Argb(60, 255, 255, 255));
            return b;
        }

        private Button ActionButton(string text)
        {
            var b = new Button(this) { Text = text, TextSize = 13f };
            b.SetAllCaps(false);
            b.SetTextColor(TextColor);
            b.SetPadding(0, 0, 0, 0);
            b.Background = RoundRect(FieldColor, Dp(7), Color.Argb(55, 255, 255, 255));
            return b;
        }

        private TextView SectionLabel(string text)
        {
            var t = new TextView(this) { Text = text, TextSize = 12f };
            t.SetTextColor(AccentColor);
            t.SetPadding(0, Dp(12), 0, Dp(2));
            return t;
        }

        private static Drawable RoundRect(Color fill, int radius, Color stroke)
        {
            var d = new GradientDrawable();
            d.SetShape(ShapeType.Rectangle);
            d.SetColor(fill);
            d.SetCornerRadius(radius);
            d.SetStroke(1, stroke);
            return d;
        }

        private int Dp(float value) => (int)(value * Resources.DisplayMetrics.Density + 0.5f);

        private void ShowFatal(Exception ex)
        {
            try
            {
                var box = new LinearLayout(this) { Orientation = Orientation.Vertical };
                box.SetBackgroundColor(Color.Rgb(18, 20, 26));
                int pad = Dp(16);
                box.SetPadding(pad, pad, pad, pad);

                var title = new TextView(this) { Text = "启动失败，请把这段文字发给开发者", TextSize = 16f };
                title.SetTextColor(Color.Rgb(226, 96, 88));
                box.AddView(title);

                var body = new TextView(this) { Text = ex.ToString() + "\n\n日志目录：\n" + Diag.Where(), TextSize = 11f };
                body.SetTextColor(Color.Rgb(220, 225, 232));
                body.SetPadding(0, pad, 0, 0);
                box.AddView(body);

                var scroll = new ScrollView(this);
                scroll.AddView(box);
                SetContentView(scroll);
            }
            catch { }
        }
    }
}
