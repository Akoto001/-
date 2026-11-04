using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GeoViewer.Core;

namespace GeoViewer.UI
{
    public partial class MainWindow : Window
    {
        private readonly Scene _scene = new Scene();
        private bool _sync;

        public MainWindow()
        {
            InitializeComponent();

            BuildAngleCombos();
            BuildFovCombo();

            AttachRowDrop(PitchCombo);
            AttachRowDrop(YawCombo);
            AttachRowDrop(RollCombo);
            AttachRowDrop(FovCombo);
            AttachRowWheel(PitchRow, StepPitch);
            AttachRowWheel(YawRow, StepYaw);
            AttachRowWheel(RollRow, StepRoll);
            AttachRowWheel(FovRow, StepFov);

            Viewport.Scene = _scene;
            TopView.Scene = _scene;
            Viewport.CameraChanged += OnCameraChanged;

            Loaded += OnWindowLoaded;

            // never open bigger than the usable desktop (small laptops / 125% scaling)
            Rect wa = SystemParameters.WorkArea;
            if (wa.Width > 200 && wa.Height > 200)
            {
                Width = Math.Min(Width, Math.Max(920, wa.Width - 40));
                Height = Math.Min(Height, Math.Max(640, wa.Height - 40));
            }
        }

        private double Dpi =>
            (VisualTreeHelper.GetDpi(this).DpiScaleX <= 0) ? 1.0 : VisualTreeHelper.GetDpi(this).DpiScaleX;

        private const string FreeAngle = "自定义";

        private void BuildAngleCombos()
        {
            PitchCombo.Items.Add(FreeAngle);
            YawCombo.Items.Add(FreeAngle);
            RollCombo.Items.Add(FreeAngle);
            for (int v = -180; v <= 180; v += 15) PitchCombo.Items.Add(v);
            for (int v = -180; v <= 180; v += 15) YawCombo.Items.Add(v);
            for (int v = -180; v <= 180; v += 15) RollCombo.Items.Add(v);
            SpinCombo.Items.Add(FreeAngle);
            for (int v = -180; v <= 180; v += 15) SpinCombo.Items.Add(v);
        }

        /// <summary>One entry of the focal length drop down (35 mm equivalent).</summary>
        private sealed class LensChoice
        {
            public LensChoice(string label, double mm)
            {
                Label = label;
                Mm = mm;
            }

            public string Label { get; }
            public double Mm { get; }
            public override string ToString() => Label;
        }

        private void BuildFovCombo()
        {
            FovCombo.Items.Add(new LensChoice(FreeAngle, double.NaN));
            foreach (double mm in Optics.PresetFocalMm)
                FovCombo.Items.Add(new LensChoice(mm.ToString("0", CultureInfo.InvariantCulture) + " mm", mm));
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            _scene.Mode = ShadingMode.Opaque;
            _scene.StructureLines = StructCheck.IsChecked == true;
            // The viewport frames itself the first time it gets a real size (see OnRenderSizeChanged).
            Refresh();
            Viewport.Focus();
        }

        /// <summary>Applies a startup state given on the command line (used by the screenshot mode).</summary>
        public void ApplyCliState(GeoViewer.CliOptions cli)
        {
            if (cli == null) return;
            _sync = true;
            try
            {
                _scene.Mode = cli.Mode;
                _scene.StructureLines = cli.Lines;
                _scene.PerspectiveLines = cli.Persp;
            if (cli.ModelRoll.HasValue) _scene.SetModelRoll(cli.ModelRoll.Value);
            if (cli.Sweep.HasValue) _scene.SetLensSweep(cli.Sweep.Value);
                _scene.Select(cli.SelectedSolidIndex);
                if (cli.Edge.HasValue) _scene.SetEdgeLength(cli.Edge.Value);

                ModeOpaque.IsChecked = cli.Mode == ShadingMode.Opaque;
                ModeTranslucent.IsChecked = cli.Mode == ShadingMode.Translucent;
                ModeOutline.IsChecked = cli.Mode == ShadingMode.Outline;
                StructCheck.IsChecked = cli.Lines;
                PerspCheck.IsChecked = cli.Persp;
                SyncModelRadios();
            }
            finally
            {
                _sync = false;
            }

            if (!string.IsNullOrWhiteSpace(cli.Lens)) _scene.SetLens(Lens.Parse(cli.Lens, _scene.LensMode));

            if (cli.Azimuth.HasValue || cli.Elevation.HasValue || cli.Roll.HasValue)
                _scene.ApplyAxisAngles(cli.Elevation ?? _scene.CurrentPitch,
                                       cli.Azimuth ?? _scene.CurrentYaw,
                                       cli.Roll ?? _scene.CurrentRoll);

            if (cli.FocalMm.HasValue) _scene.SetFocalMm(cli.FocalMm.Value);
            if (cli.FovYDeg.HasValue) _scene.SetFovYDeg(cli.FovYDeg.Value);

            Refresh(refit: true);

            if (Environment.GetEnvironmentVariable("GEO_UI_LOG") == "1")
                DiagLog($"ApplyCliState end: el={_scene.Camera.ElevationDeg:F1} az={_scene.Camera.AzimuthDeg:F1} roll={_scene.Camera.RollDeg:F1} dist={_scene.Camera.Distance:F2} vp={Viewport.ActualWidth:F0}x{Viewport.ActualHeight:F0}");
        }

        private void SyncModelRadios()
        {
            switch (_scene.Selected)
            {
                case 0: ModelSphere.IsChecked = true; break;
                case 1: ModelCylinder.IsChecked = true; break;
                case 2: ModelCube.IsChecked = true; break;
                case 3: ModelCuboid.IsChecked = true; break;
                case 4: ModelCone.IsChecked = true; break;
                default: ModelCone.IsChecked = true; break;
            }
        }

        public static void DiagLog(string msg)
        {
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(AppContext.BaseDirectory, "ui_log.txt"),
                    DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + "  " + msg + Environment.NewLine);
            }
            catch { }
        }

        // ------------------------------------------------------------------ refresh

        private void Refresh(bool refit = false)
        {
            if (refit)
            {
                double w = Math.Max(80, Viewport.ActualWidth * Dpi);
                double h = Math.Max(80, Viewport.ActualHeight * Dpi);
                _scene.Fit(w, h);
            }
            Viewport.RequestRender(true);
            TopView.Refresh();
            SyncAngleUi();
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            string mode = _scene.Mode switch
            {
                ShadingMode.Translucent => "半透明",
                ShadingMode.Outline => "透明轮廓",
                _ => "不透明"
            };
            StatusText.Text = mode
                + (_scene.StructureLines ? " · 结构线 开" : " · 结构线 关")
                + (_scene.PerspectiveLines ? " · 透视线 开" : "");
        }

        private static string Fmt(double v) => v.ToString("0.0", CultureInfo.InvariantCulture) + "°";

        private void SyncAngleUi()
        {
            _sync = true;
            try
            {
                SelectNearest(PitchCombo, _scene.CurrentPitch);
                SelectNearest(YawCombo, _scene.CurrentYaw);
                SelectNearest(RollCombo, _scene.CurrentRoll);
                PitchValue.Text = "X " + Fmt(_scene.CurrentPitch);
                YawValue.Text = "Y " + Fmt(_scene.CurrentYaw);
                RollValue.Text = "Z " + Fmt(_scene.CurrentRoll);
                SyncFovUi();
            SyncSpinUi();
                SyncLensUi();
            }
            finally
            {
                _sync = false;
            }
        }

        /// <summary>Shows the nearest lens preset (or "自定义") plus the resulting field of view.</summary>
        private void SyncFovUi()
        {
            double focal = _scene.FocalMm;
            int best = 0;
            double bestErr = 0.6;
            for (int i = 0; i < FovCombo.Items.Count; i++)
            {
                if (FovCombo.Items[i] is not LensChoice c || double.IsNaN(c.Mm)) continue;
                double err = Math.Abs(c.Mm - focal);
                if (err <= bestErr) { bestErr = err; best = i; }
            }
            if (FovCombo.SelectedIndex != best) FovCombo.SelectedIndex = best;
            FovValue.Text = Fmt(_scene.FovYDeg);
        }

        private static void SelectNearest(ComboBox combo, double value)
        {
            int best = -1;
            double bestErr = 0.6;
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i] is not int v) continue;
                double err = Math.Abs(v - value);
                if (err <= bestErr) { bestErr = err; best = i; }
            }
            if (best < 0) best = 0; // "自定义" keeps the list from looking empty after a free rotation
            if (combo.SelectedIndex != best) combo.SelectedIndex = best;
        }

        private static double SelectedValue(ComboBox combo, double fallback)
        {
            return combo.SelectedItem is int v ? v : fallback;
        }

        // ------------------------------------------------------------------ handlers

        private void OnCameraChanged(object sender, EventArgs e)
        {
            TopView.Refresh();
            SyncAngleUi();
        }

        private void OnLensRotateClick(object sender, RoutedEventArgs e)
        {
            _scene.SetLensRotate(!_scene.LensRotate);
            SyncLensUi();
            Viewport.RequestRender(true);
        }
        private void OnLensClick(object sender, RoutedEventArgs e)
        {
            if (_sync) return;
            int index = 0;
            if (sender is FrameworkElement fe && fe.Tag != null) int.TryParse(fe.Tag.ToString(), out index);
            _scene.SetLens((LensKind)Math.Clamp(index, 0, Lens.Count - 1));
            Refresh(refit: true);
        }

        /// <summary>Locks the axis rows that the active perspective convention pins.</summary>
        private void ApplyLensLocks()
        {
            LensKind kind = _scene.LensMode;
            bool prescribed = Lens.IsPrescribed(kind);
            bool canRotate = Lens.RotatableAxis(kind) != 0;
            LensRotateButton.Visibility = canRotate ? Visibility.Visible : Visibility.Collapsed;
            LensRotateButton.Content = _scene.LensRotate ? "旋转：开" : "旋转：关";
            int rotAxis = Lens.RotatableAxis(kind);
            bool rot = _scene.LensRotate;
            PitchRow.IsEnabled = (!prescribed && !Lens.LocksElevation(kind)) || (rot && rotAxis == 2);
            YawRow.IsEnabled = (!prescribed && !Lens.LocksAzimuth(kind)) || (rot && rotAxis == 1);
            RollRow.IsEnabled = prescribed ? true : !Lens.LocksRoll(kind);
            // the focal length stays adjustable: it scales where the vanishing points sit,
            // while their directions (the perspective structure) are what the mode pins.
            FovRow.IsEnabled = true;
            FovRow.Opacity = 1.0;
            PitchRow.Opacity = PitchRow.IsEnabled ? 1.0 : 0.42;
            YawRow.Opacity = YawRow.IsEnabled ? 1.0 : 0.42;
            RollRow.Opacity = RollRow.IsEnabled ? 1.0 : 0.42;
        }

        private void SyncLensUi()
        {
            LensKind kind = _scene.LensMode;
            Button[] buttons = { LensFree, LensOne, LensTwo, LensThree, LensFish };
            string[] names = { "自由", "一点", "两点", "三点", "鱼眼" };
            for (int i = 0; i < buttons.Length; i++)
            {
                bool on = (int)kind == i;
                buttons[i].Content = on ? "●" + names[i] : names[i];
                buttons[i].FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            }
            ApplyLensLocks();
            PitchValue.Text = "X " + Fmt(_scene.CurrentPitch) + (PitchRow.IsEnabled ? "" : " 锁");
            YawValue.Text = "Y " + Fmt(_scene.CurrentYaw) + (YawRow.IsEnabled ? "" : " 锁");
            RollValue.Text = "Z " + Fmt(GeoViewer.Core.Lens.IsPrescribed(_scene.LensMode) ? _scene.Camera.RollDeg : _scene.CurrentRoll) + (RollRow.IsEnabled ? "" : " 锁");
            if (Lens.IsPrescribed(_scene.LensMode))
                StatusText.Text = _scene.Mode switch { ShadingMode.Translucent => "半透明", ShadingMode.Outline => "透明", _ => "不透明" }
                    + " · 结构线 " + (_scene.StructureLines ? "开" : "关") + " · 灭点已固定：" + Lens.ShortName(_scene.LensMode);
        }
        private void ApplyAngles()
        {
            if (_sync || _scene == null) return;
            double pitch = SelectedValue(PitchCombo, _scene.CurrentPitch);
            double yaw = SelectedValue(YawCombo, _scene.CurrentYaw);
            double roll = SelectedValue(RollCombo, GeoViewer.Core.Lens.IsPrescribed(_scene.LensMode) ? _scene.Camera.RollDeg : _scene.CurrentRoll);
            _scene.ApplyAxisAngles(pitch, yaw, roll);
            Refresh(refit: true);
        }

        private void OnPitchChanged(object sender, SelectionChangedEventArgs e) => ApplyAngles();
        private void OnYawChanged(object sender, SelectionChangedEventArgs e) => ApplyAngles();
        private void OnRollChanged(object sender, SelectionChangedEventArgs e) => ApplyAngles();

        // ---- step buttons: always work, even if a popup misbehaves ----

        private const double AngleStep = 15.0;

        private static double Snap(double value, double dir, double min, double max)
        {
            double next = dir > 0
                ? Math.Floor(value / AngleStep + 1e-9) * AngleStep + AngleStep
                : Math.Ceiling(value / AngleStep - 1e-9) * AngleStep - AngleStep;
            return Math.Clamp(next, min, max);
        }

        private void StepPitch(int dir)
        {
            if (_scene == null) return;
            _scene.ApplyAxisAngles(Snap(_scene.CurrentPitch, dir, -180, 180), _scene.CurrentYaw, _scene.CurrentRoll);
            FinishAngleStep();
        }

        private void StepYaw(int dir)
        {
            if (_scene == null) return;
            _scene.ApplyAxisAngles(_scene.CurrentPitch, Snap(_scene.CurrentYaw, dir, -180, 180), _scene.CurrentRoll);
            FinishAngleStep();
        }

        private void StepRoll(int dir)
        {
            if (_scene == null) return;
            _scene.ApplyAxisAngles(_scene.CurrentPitch, _scene.CurrentYaw, Snap(_scene.CurrentRoll, dir, -180, 180));
            FinishAngleStep();
        }

        private void FinishAngleStep()
        {
            SyncAngleUi();
            TopView.Refresh();
            Refresh(refit: true);
        }

        private void OnPitchMinus(object sender, RoutedEventArgs e) => StepPitch(-1);
        private void OnPitchPlus(object sender, RoutedEventArgs e) => StepPitch(1);
        private void OnYawMinus(object sender, RoutedEventArgs e) => StepYaw(-1);
        private void OnYawPlus(object sender, RoutedEventArgs e) => StepYaw(1);
        private void OnRollMinus(object sender, RoutedEventArgs e) => StepRoll(-1);
        private void OnRollPlus(object sender, RoutedEventArgs e) => StepRoll(1);

        // ---- field of view / focal length ----

        private void OnSpinChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_sync) return;
            if (!(SpinCombo.SelectedItem is int v)) return;
            _scene.SetLensSweep(v);
            SpinValue.Text = Fmt(_scene.Camera.RollDeg);
            Viewport.RequestRender(true);
            TopView.Refresh();
        }

        private void OnSpinMinus(object sender, RoutedEventArgs e) => StepSpin(-1);
        private void OnSpinPlus(object sender, RoutedEventArgs e) => StepSpin(1);

        private void StepSpin(int dir)
        {
            double next = Snap(_scene.Camera.RollDeg, dir, -180.0, 180.0);
            _scene.SetLensSweep(next);
            SyncAngleUi();
            TopView.Refresh();
            Viewport.RequestRender(true);
        }

        private void SyncSpinUi()
        {
            _sync = true;
            SelectNearest(SpinCombo, _scene.Camera.RollDeg);
            _sync = false;
            SpinValue.Text = Fmt(_scene.Camera.RollDeg);
        }
        private void OnFovChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_sync || _scene == null) return;
            if (FovCombo.SelectedItem is not LensChoice c || double.IsNaN(c.Mm)) return;
            _scene.SetFocalMm(c.Mm);
            Refresh(refit: true);
        }

        private void StepFov(int dir)
        {
            if (_scene == null) return;
            double focal = _scene.FocalMm;
            int idx = 0;
            double err = double.MaxValue;
            for (int i = 0; i < Optics.PresetFocalMm.Length; i++)
            {
                double d = Math.Abs(Optics.PresetFocalMm[i] - focal);
                if (d < err) { err = d; idx = i; }
            }
            idx = Math.Clamp(idx + dir, 0, Optics.PresetFocalMm.Length - 1);
            _scene.SetFocalMm(Optics.PresetFocalMm[idx]);
            SyncFovUi();
            SyncSpinUi();
            Refresh(refit: true);
        }

        private void OnFovMinus(object sender, RoutedEventArgs e) => StepFov(-1);
        private void OnFovPlus(object sender, RoutedEventArgs e) => StepFov(1);

        // The wheel over a row steps that row and never scrolls the sidebar.
        private static void AttachRowWheel(FrameworkElement row, Action<int> step)
        {
            if (row == null || step == null) return;
            row.PreviewMouseWheel += (s, e) =>
            {
                step(e.Delta > 0 ? 1 : -1);
                e.Handled = true;
            };
        }

        // Any click inside the angle row opens the list, so the whole line behaves like a field.
        private void AttachRowDrop(ComboBox combo)
        {
            if (combo?.Parent is not FrameworkElement row) return;
            row.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if (combo.IsDropDownOpen) return;
                DependencyObject src = e.OriginalSource as DependencyObject;
                if (src != null && (IsInside<System.Windows.Controls.Primitives.ButtonBase>(src) || IsInside<ComboBox>(src))) return;
                combo.IsDropDownOpen = true;
                e.Handled = true;
            };
        }

        private static bool IsInside<T>(DependencyObject start) where T : DependencyObject
        {
            for (DependencyObject d = start; d != null; d = System.Windows.Media.VisualTreeHelper.GetParent(d))
                if (d is T) return true;
            return false;
        }

        private void OnFitClick(object sender, RoutedEventArgs e) => Refresh(refit: true);

        private void OnTopmostClick(object sender, RoutedEventArgs e)
        {
            Topmost = !Topmost;
            TopmostToggle.Content = Topmost ? "窗口置顶：开" : "窗口置顶：关";
        }

        private void OnResetClick(object sender, RoutedEventArgs e)
        {
            double w = Math.Max(80, Viewport.ActualWidth * Dpi);
            double h = Math.Max(80, Viewport.ActualHeight * Dpi);
            _scene.ResetView(w, h);
            Refresh();
        }

        private void OnModeChecked(object sender, RoutedEventArgs e)
        {
            if (_scene == null) return;
            if (sender is not RadioButton rb || rb.Tag is not string tag) return;
            if (!int.TryParse(tag, out int m)) return;
            _scene.Mode = (ShadingMode)m;
            if (_sync) return;
            Refresh();
        }

        private void OnStructToggled(object sender, RoutedEventArgs e)
        {
            if (_scene == null) return;
            _scene.StructureLines = StructCheck.IsChecked == true;
            if (_sync) return;
            Refresh();
        }

        private void OnPerspToggled(object sender, RoutedEventArgs e)
        {
            if (_scene == null) return;
            _scene.PerspectiveLines = PerspCheck.IsChecked == true;
            if (_sync) return;
            Refresh();
        }

        private void OnModelChecked(object sender, RoutedEventArgs e)
        {
            if (_scene == null) return;
            if (sender is not RadioButton rb || rb.Tag is not string tag) return;
            if (!int.TryParse(tag, out int i) || i < 0 || i >= Scene.Count) return;
            if (i == _scene.Selected) return;
            _scene.Select(i);
            if (_sync) return;
            // no refit: the framing is shared by all five solids, so the size difference stays readable
            Refresh();
        }
    }
}
