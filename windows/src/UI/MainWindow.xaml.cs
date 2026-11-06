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
        private readonly PracticeBook _practice = new PracticeBook();
        private readonly Random _rng = new Random();
        private readonly System.Collections.Generic.List<Border> _cells = new System.Collections.Generic.List<Border>();
        private int _practiceRoll;
        private int _litX = -1, _litY = -1, _litZ = -1;
        private bool _bannerDismissed;
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

            BuildPracticeGrid();
            Loaded += (s, e) => SyncPracticeUi();

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
            SyncPracticeUi();
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
            _litX = _litY = _litZ = -1;
            _bannerDismissed = false;
            if (_sync) return;
            // no refit: the framing is shared by all five solids, so the size difference stays readable
            Refresh();
        }

        // ------------------------------------------------------------------ practice log

        private static readonly int[] AxisAngles = PracticeBook.Angles;

        /// <summary>Builds the 8×8 cell matrix once: first row/column are the angle labels.</summary>
        private void BuildPracticeGrid()
        {
            _cells.Clear();
            PracticeCells.Children.Clear();
            for (int row = 0; row < 8; row++)
            {
                for (int col = 0; col < 8; col++)
                {
                    var cell = new Border
                    {
                        Margin = new Thickness(2, 1, 2, 1),
                        Height = 26,
                        CornerRadius = new CornerRadius(4),
                        BorderThickness = new Thickness(1),
                        Background = Brushes.Transparent,
                        BorderBrush = Brushes.Transparent,
                        Child = new TextBlock
                        {
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            FontSize = 11,
                            Foreground = (Brush)FindResource("TextDim")
                        }
                    };

                    if (row == 0 && col == 0)
                    {
                        // corner
                    }
                    else if (row == 0)
                    {
                        ((TextBlock)cell.Child).Text = AxisAngles[col - 1].ToString(CultureInfo.InvariantCulture);
                        ((TextBlock)cell.Child).Foreground = (Brush)FindResource("TextFaint");
                    }
                    else if (col == 0)
                    {
                        ((TextBlock)cell.Child).Text = AxisAngles[row - 1].ToString(CultureInfo.InvariantCulture);
                        ((TextBlock)cell.Child).Foreground = (Brush)FindResource("TextFaint");
                    }
                    else
                    {
                        cell.Tag = (row - 1) * 8 + (col - 1); // 0..48 → (xIndex, yIndex)
                        cell.Cursor = System.Windows.Input.Cursors.Hand;
                        cell.MouseLeftButtonDown += OnPracticeCellClick;
                    }

                    _cells.Add(cell);
                    PracticeCells.Children.Add(cell);
                }
            }
        }

        private void OnPracticeCellClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not Border cell || cell.Tag is not int index) return;
            int xi = index / 8;
            int yi = index % 8;
            int x = AxisAngles[xi];
            int y = AxisAngles[yi];
            int z = _practiceRoll;

            _practice.Toggle(_scene.Selected, x, y, z);
            if (x == _litX && y == _litY && z == _litZ) _litX = _litY = _litZ = -1;
            _bannerDismissed = false;
            SyncPracticeUi();
        }

        private void OnPracticeModeClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string tag) return;
            if (!int.TryParse(tag, out int mode)) return;
            _practice.SetMode((PracticeMode)mode);
            if (!_practice.AllowsRoll(_practiceRoll)) _practiceRoll = 0;
            _litX = _litY = _litZ = -1;
            _bannerDismissed = false;
            SyncPracticeUi();
        }

        private void OnPracticeRollClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Border chip || chip.Tag is not int value) return;
            _practiceRoll = value;
            SyncPracticeUi();
        }

        private void OnPracticeRandom(object sender, RoutedEventArgs e)
        {
            if (!_practice.TryPick(_scene.Selected, _rng, out int x, out int y, out int z))
            {
                SyncPracticeUi();
                return;
            }

            _litX = x;
            _litY = y;
            _litZ = z;
            _practiceRoll = z;
            _bannerDismissed = false;

            // jump the camera to the drawn angle, otherwise the user cannot see what to practise
            _scene.ApplyAxisAngles(x, y, z);
            Refresh(refit: true);
        }

        private void OnPracticeReset(object sender, RoutedEventArgs e)
        {
            MessageBoxResult answer = MessageBox.Show(this,
                "清空当前形体的练习记录？其它形体不受影响。",
                "重置练习记录", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (answer != MessageBoxResult.OK) return;

            _practice.ResetSolid(_scene.Selected);
            _litX = _litY = _litZ = -1;
            _bannerDismissed = false;
            SyncPracticeUi();
        }

        private void OnPracticeDismiss(object sender, RoutedEventArgs e)
        {
            _bannerDismissed = true;
            SyncPracticeUi();
        }

        private void SyncPracticeUi()
        {
            if (PracticeCells == null || _cells.Count == 0) return;

            PracticeMode mode = _practice.Mode;
            ModeDense.Content = (mode == PracticeMode.Dense ? "●" : "") + "A 343";
            ModeMedium.Content = (mode == PracticeMode.Medium ? "●" : "") + "B 147";
            ModeSimple.Content = (mode == PracticeMode.Simple ? "●" : "") + "C 49";

            // roll chips for the current mode
            PracticeRollRow.Children.Clear();
            foreach (int roll in _practice.RollLevels)
            {
                bool on = roll == _practiceRoll;
                var chip = new Border
                {
                    Margin = new Thickness(2, 0, 2, 0),
                    Height = 24,
                    CornerRadius = new CornerRadius(4),
                    BorderThickness = new Thickness(on ? 2 : 1),
                    Background = (Brush)FindResource(on ? "FieldBg" : "WindowBg"),
                    BorderBrush = (Brush)FindResource(on ? "AccentBrush" : "PanelBorder"),
                    Tag = roll,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Child = new TextBlock
                    {
                        Text = "Z " + roll,
                        FontSize = 11,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = (Brush)FindResource(on ? "AccentBrush" : "TextDim")
                    }
                };
                chip.MouseLeftButtonDown += (s, ev) => OnPracticeRollClick(s, ev);
                PracticeRollRow.Children.Add(chip);
            }

            string[] names = { "球体", "圆柱体", "正方体", "长方体", "圆锥体" };
            PracticeName.Text = names[Math.Clamp(_scene.Selected, 0, names.Length - 1)];

            int done = _practice.DoneCount(_scene.Selected);
            int total = _practice.Total;
            PracticeCount.Text = "已画 " + done.ToString(CultureInfo.InvariantCulture) + " / "
                + total.ToString(CultureInfo.InvariantCulture);
            double trackWidth = ((FrameworkElement)PracticeBar.Parent).ActualWidth;
            PracticeBar.Width = Math.Max(0, trackWidth) * (total == 0 ? 0 : (double)done / total);

            Brush doneBg = new SolidColorBrush(Color.FromRgb(0x20, 0x4A, 0x56));
            Brush litBg = new SolidColorBrush(Color.FromRgb(0x17, 0x30, 0x3A));
            Brush emptyBg = (Brush)FindResource("FieldBg");
            Brush accent = (Brush)FindResource("AccentBrush");
            Brush border = (Brush)FindResource("PanelBorder");
            Brush tick = new SolidColorBrush(Color.FromRgb(0xAA, 0xE2, 0xEC));

            for (int row = 1; row < 8; row++)
            {
                for (int col = 1; col < 8; col++)
                {
                    Border cell = _cells[row * 8 + col];
                    int x = AxisAngles[row - 1];
                    int y = AxisAngles[col - 1];
                    int z = _practiceRoll;
                    bool isDone = _practice.IsDone(_scene.Selected, x, y, z);
                    bool isLit = x == _litX && y == _litY && z == _litZ;
                    TextBlock text = (TextBlock)cell.Child;

                    if (isDone)
                    {
                        cell.Background = doneBg;
                        cell.BorderBrush = accent;
                        cell.BorderThickness = new Thickness(1);
                        text.Text = "✓";
                        text.FontSize = 14;
                        text.Foreground = tick;
                    }
                    else if (isLit)
                    {
                        cell.Background = litBg;
                        cell.BorderBrush = accent;
                        cell.BorderThickness = new Thickness(2);
                        text.Text = "◎";
                        text.FontSize = 13;
                        text.Foreground = accent;
                    }
                    else
                    {
                        cell.Background = emptyBg;
                        cell.BorderBrush = border;
                        cell.BorderThickness = new Thickness(1);
                        text.Text = string.Empty;
                    }
                }
            }

            bool finished = done >= total && total > 0;
            PracticeBanner.Visibility = finished && !_bannerDismissed ? Visibility.Visible : Visibility.Collapsed;
            PracticeRandom.IsEnabled = !finished;
            PracticeRandom.Opacity = finished ? 0.45 : 1.0;
        }
    }
}
