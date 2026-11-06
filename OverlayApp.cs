using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace AramOverlay
{
    public class OverlayApp : Application
    {
        private AppConfig _config;
        private DataStore _data;
        private LeagueApi _league;
        private OverlayWindow _overlay;
        private WinForms.NotifyIcon _tray;
        private DispatcherTimer _pollTimer;
        private DispatcherTimer _dataTimer;
        private bool _polling;
        private bool _preview;
        private bool _enabled;
        private bool _demo;
        private IntPtr _hotkeyHandle;
        private HwndSource _hotkeySource;
        private string _lastPhase;

        public OverlayApp()
            : this(false)
        {
        }

        public OverlayApp(bool demo)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _enabled = true;
            _demo = demo;
            _lastPhase = "";
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _config = AppConfig.Load();
            _data = new DataStore();
            _league = new LeagueApi();
            _overlay = new OverlayWindow();
            _overlay.SetFontRatio(_config.BadgeFontRatio);

            _hotkeyHandle = new WindowInteropHelper(_overlay).EnsureHandle();
            Native.MakeClickThrough(_hotkeyHandle);
            _hotkeySource = HwndSource.FromHwnd(_hotkeyHandle);
            if (_hotkeySource != null)
            {
                _hotkeySource.AddHook(WndProc);
            }
            RegisterHotkeys();

            CreateTray();

            _pollTimer = new DispatcherTimer();
            _pollTimer.Interval = TimeSpan.FromMilliseconds(1000);
            _pollTimer.Tick += delegate { PollAsync(); };
            _pollTimer.Start();

            _dataTimer = new DispatcherTimer();
            _dataTimer.Interval = TimeSpan.FromMinutes(Math.Max(5, _config.RefreshMinutes));
            _dataTimer.Tick += delegate { RefreshDataAsync(); };
            _dataTimer.Start();

            Log("started");
            RefreshDataAsync();

            if (_config.PreviewOnStart)
            {
                _preview = true;
            }
        }

        private void RegisterHotkeys()
        {
            if (_hotkeyHandle == IntPtr.Zero)
            {
                return;
            }
            Native.RegisterHotKey(_hotkeyHandle, 1, Native.MOD_CONTROL | Native.MOD_SHIFT, 0x78); // F9
            Native.RegisterHotKey(_hotkeyHandle, 2, Native.MOD_CONTROL | Native.MOD_SHIFT, 0x79); // F10
            Native.RegisterHotKey(_hotkeyHandle, 3, Native.MOD_CONTROL | Native.MOD_SHIFT, 0x7A); // F11
            Native.RegisterHotKey(_hotkeyHandle, 4, Native.MOD_CONTROL | Native.MOD_SHIFT, 0x7B); // F12
        }

        private void UnregisterHotkeys()
        {
            if (_hotkeyHandle == IntPtr.Zero)
            {
                return;
            }
            Native.UnregisterHotKey(_hotkeyHandle, 1);
            Native.UnregisterHotKey(_hotkeyHandle, 2);
            Native.UnregisterHotKey(_hotkeyHandle, 3);
            Native.UnregisterHotKey(_hotkeyHandle, 4);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (id == 1)
                {
                    _enabled = !_enabled;
                    if (!_enabled)
                    {
                        _overlay.HideOverlay();
                    }
                    Log("overlay enabled=" + _enabled.ToString());
                    handled = true;
                }
                else if (id == 2)
                {
                    RefreshDataAsync();
                    handled = true;
                }
                else if (id == 3)
                {
                    ReloadConfig();
                    handled = true;
                }
                else if (id == 4)
                {
                    CaptureSnapshot();
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        private void CreateTray()
        {
            _tray = new WinForms.NotifyIcon();
            _tray.Icon = System.Drawing.SystemIcons.Application;
            _tray.Text = "ARAM Overlay";
            _tray.Visible = true;

            WinForms.ContextMenuStrip menu = new WinForms.ContextMenuStrip();
            WinForms.ToolStripMenuItem preview = new WinForms.ToolStripMenuItem("预览 Overlay");
            preview.Click += delegate { _preview = !_preview; Log("preview=" + _preview.ToString()); };
            menu.Items.Add(preview);

            WinForms.ToolStripMenuItem refresh = new WinForms.ToolStripMenuItem("刷新数据");
            refresh.Click += delegate { RefreshDataAsync(); };
            menu.Items.Add(refresh);

            WinForms.ToolStripMenuItem reload = new WinForms.ToolStripMenuItem("重新加载配置");
            reload.Click += delegate { ReloadConfig(); };
            menu.Items.Add(reload);

            WinForms.ToolStripMenuItem open = new WinForms.ToolStripMenuItem("打开配置目录");
            open.Click += delegate { OpenConfigDir(); };
            menu.Items.Add(open);

            menu.Items.Add(new WinForms.ToolStripSeparator());

            WinForms.ToolStripMenuItem exit = new WinForms.ToolStripMenuItem("退出");
            exit.Click += delegate { Shutdown(); };
            menu.Items.Add(exit);

            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += delegate { _preview = !_preview; };
        }

        private async void RefreshDataAsync()
        {
            try
            {
                bool ok = await _data.RefreshAsync(_config);
                Log("data refresh=" + ok.ToString() + " count=" + _data.Count.ToString() + " date=" + _data.DataDate);
                UpdateTrayText();
            }
            catch (Exception ex)
            {
                Log("data refresh error: " + ex.Message);
            }
        }

        private async void PollAsync()
        {
            if (_polling)
            {
                return;
            }
            _polling = true;
            try
            {
                LeagueWindow window = LeagueWindowFinder.Find();
                if ((window == null || !window.IsValid) && _demo)
                {
                    System.Drawing.Rectangle bounds = WinForms.Screen.PrimaryScreen.Bounds;
                    window = new LeagueWindow();
                    window.Handle = new IntPtr(1);
                    Native.RECT rect = new Native.RECT();
                    rect.Left = bounds.Left;
                    rect.Top = bounds.Top;
                    rect.Right = bounds.Right;
                    rect.Bottom = bounds.Bottom;
                    window.ClientRect = rect;
                    _preview = true;
                }
                if (window == null || !window.IsValid)
                {
                    _overlay.HideOverlay();
                    _lastPhase = "";
                    return;
                }

                if (_demo)
                {
                    _overlay.SetBadges(BuildPreviewBadges());
                    _overlay.ShowOverlay(window);
                    return;
                }

                if (_config.HideWhenNotForeground && !_preview && !LeagueWindowFinder.IsLeagueForeground())
                {
                    _overlay.HideOverlay();
                    return;
                }

                if (!_enabled)
                {
                    _overlay.HideOverlay();
                    return;
                }

                if (_preview)
                {
                    _overlay.SetBadges(BuildPreviewBadges());
                    _overlay.ShowOverlay(window);
                    return;
                }

                _league.EnsureConnected();
                string phase = await _league.GetPhaseAsync();
                _lastPhase = phase == null ? "" : phase;
                if (phase == null || !string.Equals(phase, "ChampSelect", StringComparison.OrdinalIgnoreCase))
                {
                    _overlay.HideOverlay();
                    return;
                }

                ChampSelectState session = await _league.GetChampSelectAsync();
                if (session == null)
                {
                    _overlay.HideOverlay();
                    return;
                }

                List<BadgeItem> badges = BuildBadges(session);
                _overlay.SetBadges(badges);
                _overlay.ShowOverlay(window);
            }
            catch (Exception ex)
            {
                Log("poll error: " + ex.Message);
                _overlay.HideOverlay();
            }
            finally
            {
                _polling = false;
            }
        }

        private List<BadgeItem> BuildBadges(ChampSelectState session)
        {
            List<BadgeItem> badges = new List<BadgeItem>();
            int benchCount = session.BenchChampionIds.Count;
            if (benchCount > _config.BenchSlots)
            {
                benchCount = _config.BenchSlots;
            }
            for (int i = 0; i < benchCount; i++)
            {
                int championId = session.BenchChampionIds[i];
                double x = _config.BenchFirstX + i * _config.BenchPitchX;
                ChampionStat stat = _data.Get(championId);
                double? rate = stat == null ? (double?)null : stat.WinRate;
                badges.Add(new BadgeItem(x, _config.BenchY, _data.GetWinRateText(championId), rate));
            }

            if (_config.ShowAllyRows)
            {
                for (int i = 0; i < _config.AllyRows; i++)
                {
                    int championId = 0;
                    if (i < session.Team.Count)
                    {
                        championId = session.Team[i].ChampionId;
                    }
                    double y = _config.AllyFirstY + i * _config.AllyPitchY;
                    ChampionStat stat = championId > 0 ? _data.Get(championId) : null;
                    double? rate = stat == null ? (double?)null : stat.WinRate;
                    string text = championId > 0 ? _data.GetWinRateText(championId) : "—";
                    badges.Add(new BadgeItem(_config.AllyX, y, text, rate));
                }
            }

            return badges;
        }

        private List<BadgeItem> BuildPreviewBadges()
        {
            List<BadgeItem> badges = new List<BadgeItem>();
            double[] samples = new double[] { 0.464, 0.461, 0.547, 0.508, 0.477, 0.502, 0.545, 0.466, 0.510, 0.495 };
            for (int i = 0; i < _config.BenchSlots && i < samples.Length; i++)
            {
                double x = _config.BenchFirstX + i * _config.BenchPitchX;
                badges.Add(new BadgeItem(x, _config.BenchY, (samples[i] * 100).ToString("0.0") + "%", samples[i]));
            }
            if (_config.ShowAllyRows)
            {
                double[] ally = new double[] { 0.513, 0.470, 0.498, 0.522, 0.480 };
                for (int i = 0; i < _config.AllyRows && i < ally.Length; i++)
                {
                    double y = _config.AllyFirstY + i * _config.AllyPitchY;
                    badges.Add(new BadgeItem(_config.AllyX, y, (ally[i] * 100).ToString("0.0") + "%", ally[i]));
                }
            }
            return badges;
        }

        private void CaptureSnapshot()
        {
            try
            {
                LeagueWindow window = LeagueWindowFinder.Find();
                if (window == null || !window.IsValid)
                {
                    Log("snapshot: no league window");
                    return;
                }
                _overlay.HideOverlay();
                System.Threading.Thread.Sleep(150);
                string dir = Path.Combine(AppConfig.ConfigDir, "captures");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "client-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");
                int width = window.ClientRect.Right - window.ClientRect.Left;
                int height = window.ClientRect.Bottom - window.ClientRect.Top;
                using (System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(width, height))
                using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(window.ClientRect.Left, window.ClientRect.Top, 0, 0, bitmap.Size);
                    bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
                Log("snapshot saved: " + path);
            }
            catch (Exception ex)
            {
                Log("snapshot error: " + ex.Message);
            }
        }

        private void ReloadConfig()
        {
            _config = AppConfig.Load();
            _overlay.SetFontRatio(_config.BadgeFontRatio);
            _dataTimer.Interval = TimeSpan.FromMinutes(Math.Max(5, _config.RefreshMinutes));
            Log("config reloaded");
            RefreshDataAsync();
        }

        private void OpenConfigDir()
        {
            try
            {
                Process.Start("explorer.exe", AppConfig.ConfigDir);
            }
            catch
            {
            }
        }

        private void UpdateTrayText()
        {
            if (_tray == null)
            {
                return;
            }
            string text = "ARAM Overlay";
            if (_data != null && _data.Count > 0)
            {
                text = "ARAM Overlay · " + _data.DataDate + " · " + _data.Count.ToString() + " heroes";
            }
            if (text.Length > 63)
            {
                text = text.Substring(0, 63);
            }
            _tray.Text = text;
        }

        private static void Log(string message)
        {
            try
            {
                string path = Path.Combine(AppConfig.ConfigDir, "overlay.log");
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine);
            }
            catch
            {
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            UnregisterHotkeys();
            if (_hotkeySource != null)
            {
                _hotkeySource.RemoveHook(WndProc);
            }
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
            }
            _league.Disconnect();
            base.OnExit(e);
        }
    }
}
