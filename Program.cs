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
                bool augmentTest = false;
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
                        if (string.Equals(args[i], "--augment-test", StringComparison.OrdinalIgnoreCase))
                        {
                            augmentTest = true;
                        }
                    }
                }

                if (augmentTest)
                {
                    RunAugmentTest();
                    return;
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

        private static void RunAugmentTest()
        {
            Console.WriteLine("augment-test: start");
            string dataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "augment-fit.json");
            if (!File.Exists(dataPath))
            {
                dataPath = @"C:\Users\50353\Desktop\dsh\aram-overlay\data\augment-fit.json";
            }
            AugmentData data = AugmentData.Load(dataPath);
            Console.WriteLine("augment-test: champions=" + data.ChampionCount);
            List<AugmentFit> fits = data.GetForChampion(105);
            if (fits.Count < 3)
            {
                Console.WriteLine("augment-test: no fit rows for champion 105");
                return;
            }
            Console.WriteLine("augment-test: fit0=" + fits[0].Name + " norm=" + AugmentData.Normalize(fits[0].Name));
            string hex = "";
            for (int h = 0; h < fits[0].Name.Length && h < 8; h++)
            {
                hex += ((int)fits[0].Name[h]).ToString("x4") + " ";
            }
            Console.WriteLine("augment-test: fit0hex=" + hex);
            Console.WriteLine("augment-test: direct=" + (data.Match(105, fits[0].Name) == null ? "-" : "ok"));
            Console.WriteLine("augment-test: literal=" + (data.Match(105, "小蜜蜂！！！") == null ? "-" : "ok"));

            string command = AugmentOcr.FindCommand();
            Console.WriteLine("augment-test: ocr=" + command);
            if (string.IsNullOrEmpty(command))
            {
                return;
            }

            int[] xs = new int[] { 462, 830, 1199 };
            using (System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(1920, 1080))
            using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(bitmap))
            {
                graphics.Clear(System.Drawing.Color.FromArgb(12, 18, 28));
                using (System.Drawing.Font font = new System.Drawing.Font("Microsoft YaHei", 22, System.Drawing.FontStyle.Bold))
                {
                    for (int i = 0; i < 3; i++)
                    {
                        graphics.DrawString(fits[i].Name, font, System.Drawing.Brushes.White, xs[i] + 4, 438);
                    }
                }
                Native.RECT rect = new Native.RECT();
                rect.Left = 0;
                rect.Top = 0;
                rect.Right = 1920;
                rect.Bottom = 1080;
                List<AugmentOcrLine> lines = AugmentOcr.Scan(bitmap, rect, command);
                for (int i = 0; i < lines.Count; i++)
                {
                    string ocrHex = "";
                    for (int h = 0; h < lines[i].Text.Length && h < 8; h++)
                    {
                        ocrHex += ((int)lines[i].Text[h]).ToString("x4") + " ";
                    }
                    Console.WriteLine("augment-test: ocrhex=" + ocrHex);
                    Console.WriteLine("augment-test: ocrnorm=" + AugmentData.Normalize(lines[i].Text));
                    AugmentFit match = data.Match(105, lines[i].Text);
                    Console.WriteLine("augment-test: [" + i + "] text=" + lines[i].Text +
                        " score=" + lines[i].Score.ToString("0.00") +
                        " match=" + (match == null ? "-" : match.Name + " " + match.Score + " " + match.Rating));
                }
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
