using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GeoViewer.Core;
using GeoViewer.Render;
using GeoViewer.UI;

namespace GeoViewer
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            CliOptions cli = CliOptions.Parse(e.Args);

            if (!string.IsNullOrEmpty(cli.UiShot))
            {
                try
                {
                    string path = RunUiShot(cli);
                    Console.WriteLine("wrote " + path);
                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.ToString());
                    Environment.Exit(2);
                }
                return;
            }

            if (!string.IsNullOrEmpty(cli.Shot))
            {
                try
                {
                    string path = RunShot(cli);
                    Console.WriteLine("wrote " + path);
                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.ToString());
                    Environment.Exit(2);
                }
                return;
            }

            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }

        /// <summary>Renders the real window (including the sidebar) to a PNG; used for visual checks.</summary>
        public static string RunUiShot(CliOptions cli)
        {
            int w = cli.Width > 200 ? cli.Width : 1340;
            int h = cli.Height > 200 ? cli.Height : 860;

            var win = new MainWindow
            {
                Width = w,
                Height = h,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 40,
                Top = 40,
                ShowInTaskbar = false
            };
            win.Show();

            // let the asynchronous viewport render finish before capturing
            var frame = new System.Windows.Threading.DispatcherFrame();
            var timer = new System.Windows.Threading.DispatcherTimer(
                TimeSpan.FromMilliseconds(2600), System.Windows.Threading.DispatcherPriority.Background,
                (s, a) => { ((System.Windows.Threading.DispatcherTimer)s).Stop(); frame.Continue = false; },
                System.Windows.Threading.Dispatcher.CurrentDispatcher);
            System.Windows.Threading.Dispatcher.PushFrame(frame);
            timer.Stop();

            if (win is GeoViewer.UI.MainWindow mw) mw.ApplyCliState(cli);

            // give the deferred render a moment to land before the visual tree is captured
            var settleFrame = new System.Windows.Threading.DispatcherFrame();
            var settleTimer = new System.Windows.Threading.DispatcherTimer(
                TimeSpan.FromMilliseconds(4200), System.Windows.Threading.DispatcherPriority.Background,
                (s, a2) => { ((System.Windows.Threading.DispatcherTimer)s).Stop(); settleFrame.Continue = false; },
                System.Windows.Threading.Dispatcher.CurrentDispatcher);
            System.Windows.Threading.Dispatcher.PushFrame(settleFrame);
            settleTimer.Stop();

            win.UpdateLayout();
            var content = win.Content as System.Windows.FrameworkElement;
            var visual = content ?? (System.Windows.Media.Visual)win;
            double cwD = content != null && content.ActualWidth > 50 ? content.ActualWidth : win.ActualWidth;
            double chD = content != null && content.ActualHeight > 50 ? content.ActualHeight : win.ActualHeight;
            if (cwD < 50 || chD < 50) { cwD = w; chD = h; }

            // rasterise at the window's real DPI so the capture matches what the screen shows
            DpiScale dpi = VisualTreeHelper.GetDpi(visual);
            double scale = dpi.DpiScaleX > 0.5 ? dpi.DpiScaleX : 1.0;
            int cw = (int)Math.Ceiling(cwD * scale);
            int ch = (int)Math.Ceiling(chD * scale);

            var rtb = new RenderTargetBitmap(cw, ch, 96.0 * scale, 96.0 * scale, PixelFormats.Pbgra32);
            rtb.Render(visual);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));

            string path = Path.GetFullPath(cli.UiShot);
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            using (FileStream fs = File.Create(path))
                encoder.Save(fs);

            win.Close();
            return path;
        }

        public static string RunShot(CliOptions cli)
        {
            var scene = new Scene();
            cli.Apply(scene);

            RenderResult r = cli.Render(scene);

            BitmapSource bs = BitmapSource.Create(r.Width, r.Height, 96, 96,
                PixelFormats.Bgra32, null, r.Bgra, r.Width * 4);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bs));

            string path = Path.GetFullPath(cli.Shot);
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            using (FileStream fs = File.Create(path))
                encoder.Save(fs);
            return path;
        }
    }
}
