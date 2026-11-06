using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AramOverlay
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                Native.SetProcessDPIAware();

                bool test = false;
                bool demo = false;
                bool renderTest = false;
                if (args != null)
                {
                    for (int i = 0; i < args.Length; i++)
                    {
                        if (string.Equals(args[i], "--test", StringComparison.OrdinalIgnoreCase))
                        {
                            test = true;
                        }
                        if (string.Equals(args[i], "--demo", StringComparison.OrdinalIgnoreCase))
                        {
                            demo = true;
                        }
                        if (string.Equals(args[i], "--render-test", StringComparison.OrdinalIgnoreCase))
                        {
                            renderTest = true;
                        }
                    }
                }

                if (test)
                {
                    RunSelfTest();
                    return;
                }

                if (renderTest)
                {
                    RunRenderTest();
                    return;
                }

                bool createdNew;
                using (Mutex mutex = new Mutex(true, "AramOverlay.SingleInstance", out createdNew))
                {
                    if (!createdNew)
                    {
                        MessageBox.Show("ARAM Overlay 已经在运行，请使用托盘图标。", "ARAM Overlay");
                        return;
                    }

                    OverlayApp app = new OverlayApp(demo);
                    app.Run();
                }
            }
            catch (Exception ex)
            {
                WriteFatal(ex);
            }
        }

        private static void RunRenderTest()
        {
            try
            {
                Console.WriteLine("render-test: start");
                System.Windows.Application app = new System.Windows.Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                OverlayWindow window = new OverlayWindow();
                window.Width = 2000;
                window.Height = 1125;
                window.Background = new SolidColorBrush(Color.FromRgb(18, 24, 32));

                List<BadgeItem> badges = new List<BadgeItem>();
                double[] bench = new double[] { 0.464, 0.461, 0.547, 0.508, 0.477, 0.502, 0.545, 0.466, 0.510, 0.495 };
                for (int i = 0; i < bench.Length; i++)
                {
                    badges.Add(new BadgeItem(0.292 + i * 0.0315, 0.090, (bench[i] * 100).ToString("0.0") + "%", bench[i]));
                }
                double[] ally = new double[] { 0.513, 0.470, 0.498, 0.522, 0.480 };
                for (int i = 0; i < ally.Length; i++)
                {
                    string allyText = "胜率" + (ally[i] * 100).ToString("0.0") + "% · 近50场 52.3% · 3.2KDA\n海斗分：60 · 人上人";
                    BadgeItem item = new BadgeItem(0.155, 0.394 + i * 0.093, allyText, ally[i]);
                    item.LeftAlign = true;
                    item.TopAlign = true;
                    badges.Add(item);
                }
                window.SetBadges(badges);
                System.Windows.Controls.Canvas canvas = window.Content as System.Windows.Controls.Canvas;
                if (canvas != null)
                {
                    canvas.Background = new SolidColorBrush(Color.FromRgb(18, 24, 32));
                }

                LeagueWindow fake = new LeagueWindow();
                fake.Handle = new IntPtr(1);
                Native.RECT rect = new Native.RECT();
                rect.Left = 0;
                rect.Top = 0;
                rect.Right = 2000;
                rect.Bottom = 1125;
                fake.ClientRect = rect;
                window.ShowOverlay(fake);
                window.UpdateLayout();
                window.Redraw();

                RenderTargetBitmap bitmap = new RenderTargetBitmap(2000, 1125, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render((Visual)window.Content);
                PngBitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "render-test.png");
                using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
                {
                    encoder.Save(stream);
                }
                Console.WriteLine("render-test: saved " + path);

                window.Close();
                app.Shutdown();
            }
            catch (Exception ex)
            {
                Console.WriteLine("render-test error: " + ex.GetType().FullName + " " + ex.Message);
                Console.WriteLine(ex.StackTrace);
                WriteFatal(ex);
            }
        }

        private static void WriteFatal(Exception ex)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup-error.txt");
                string text = "";
                try
                {
                    text += "type=" + ex.GetType().FullName + Environment.NewLine;
                    text += "message=" + ex.Message + Environment.NewLine;
                    text += "stack=" + ex.StackTrace + Environment.NewLine;
                    if (ex.InnerException != null)
                    {
                        text += "inner=" + ex.InnerException.GetType().FullName + " | " + ex.InnerException.Message + Environment.NewLine;
                    }
                }
                catch
                {
                }
                File.WriteAllText(path, text);
            }
            catch
            {
            }
        }

        private static void RunSelfTest()
        {
            string resultPath = Path.Combine(AppConfig.ConfigDir, "test-result.txt");
            try
            {
                AppConfig config = AppConfig.Load();
                DataStore data = new DataStore();
                bool ok = data.RefreshAsync(config).GetAwaiter().GetResult();
                LeagueWindow window = LeagueWindowFinder.Find();

                string text = "ok=" + ok.ToString()
                    + Environment.NewLine
                    + "count=" + data.Count.ToString()
                    + Environment.NewLine
                    + "date=" + data.DataDate
                    + Environment.NewLine
                    + "lastError=" + data.LastError
                    + Environment.NewLine
                    + "leagueWindow=" + (window != null && window.IsValid ? window.Handle.ToString() : "none")
                    + Environment.NewLine;
                File.WriteAllText(resultPath, text);
            }
            catch (Exception ex)
            {
                try
                {
                    File.WriteAllText(resultPath,
                        "errorType=" + ex.GetType().FullName + Environment.NewLine
                        + "errorMessage=" + ex.Message + Environment.NewLine
                        + "errorStack=" + ex.StackTrace + Environment.NewLine);
                }
                catch
                {
                }
            }
        }
    }
}
