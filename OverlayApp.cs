using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
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
        private OverlayWindow _augmentOverlay;
        private AugmentData _augmentData;
        private string _augmentOcrCommand;
        private int _lastChampionId;
        private bool _augmentScanning;
        private WinForms.NotifyIcon _tray;
        private DispatcherTimer _pollTimer;
        private DispatcherTimer _dataTimer;
        private bool _polling;
        private bool _preview;
        private DateTime _previewUntil;
        private bool _enabled;
        private int _clientWidth;
        private int _clientHeight;
        private bool _demo;
        private IntPtr _hotkeyHandle;
        private HwndSource _hotkeySource;
        private string _lastPhase;
        private bool _capturedThisChampSelect;

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
            _augmentOverlay = new OverlayWindow();
            _augmentOverlay.SetFontRatio(0.022);
            _augmentData = AugmentData.Load(FindAugmentDataPath());
            _augmentOcrCommand = !string.IsNullOrEmpty(_config.OcrCommand)
                ? _config.OcrCommand
                : AugmentOcr.FindCommand();
            Log("augment data champions=" + (_augmentData == null ? 0 : _augmentData.ChampionCount));
            Log("ocr command=" + (_augmentOcrCommand ?? ""));

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

            DispatcherTimer augmentTimer = new DispatcherTimer();
            augmentTimer.Interval = TimeSpan.FromMilliseconds(1200);
            augmentTimer.Tick += delegate { ScanAugmentsAsync(); };
            augmentTimer.Start();

            _dataTimer = new DispatcherTimer();
            _dataTimer.Interval = TimeSpan.FromMinutes(Math.Max(5, _config.RefreshMinutes));
            _dataTimer.Tick += delegate { RefreshDataAsync(); };
            _dataTimer.Start();

            Log("started");
            RefreshDataAsync();

            if (_config.PreviewOnStart)
            {
                SetPreview(true);
            }
        }

        private void RegisterHotkeys()
        {
            if (_hotkeyHandle == IntPtr.Zero)
            {
                return;
            }
            bool h1 = Native.RegisterHotKey(_hotkeyHandle, 1, Native.MOD_CONTROL | Native.MOD_SHIFT, 0x78); // F9
            bool h2 = Native.RegisterHotKey(_hotkeyHandle, 2, Native.MOD_CONTROL | Native.MOD_SHIFT, 0x79); // F10
            bool h3 = Native.RegisterHotKey(_hotkeyHandle, 3, Native.MOD_CONTROL | Native.MOD_SHIFT, 0x7A); // F11
            bool h4 = Native.RegisterHotKey(_hotkeyHandle, 4, Native.MOD_CONTROL | Native.MOD_SHIFT, 0x7B); // F12
            Log("hotkeys F9=" + h1.ToString() + " F10=" + h2.ToString() + " F11=" + h3.ToString() + " F12=" + h4.ToString());
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
                    CaptureSnapshot("manual", false);
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
            preview.Click += delegate { SetPreview(!_preview); Log("preview=" + _preview.ToString()); };
            menu.Items.Add(preview);

            WinForms.ToolStripMenuItem refresh = new WinForms.ToolStripMenuItem("刷新数据");
            refresh.Click += delegate { RefreshDataAsync(); };
            menu.Items.Add(refresh);

            WinForms.ToolStripMenuItem snapshot = new WinForms.ToolStripMenuItem("保存当前截图");
            snapshot.Click += delegate { CaptureSnapshot("tray", false); };
            menu.Items.Add(snapshot);

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
            _tray.DoubleClick += delegate { SetPreview(!_preview); };
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

                _clientWidth = window.ClientRect.Right - window.ClientRect.Left;
                _clientHeight = window.ClientRect.Bottom - window.ClientRect.Top;

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

                if (_preview && DateTime.Now > _previewUntil)
                {
                    SetPreview(false);
                }

                _league.EnsureConnected();
                string phase = await _league.GetPhaseAsync();
                _lastPhase = phase == null ? "" : phase;
                if (phase == null || !string.Equals(phase, "ChampSelect", StringComparison.OrdinalIgnoreCase))
                {
                    _capturedThisChampSelect = false;
                    if (_preview)
                    {
                        SetPreview(false);
                    }
                    _overlay.HideOverlay();
                    return;
                }

                ChampSelectState session = await _league.GetChampSelectAsync();
                if (session == null)
                {
                    _overlay.HideOverlay();
                    return;
                }

                _augmentOverlay.HideOverlay();
                int localCell = session.LocalPlayerCellId;
                for (int i = 0; i < session.Team.Count; i++)
                {
                    if (session.Team[i].CellId == localCell && session.Team[i].ChampionId > 0)
                    {
                        _lastChampionId = session.Team[i].ChampionId;
                        break;
                    }
                }
                if (_lastChampionId <= 0 && session.Team.Count > 0 && session.Team[0].ChampionId > 0)
                {
                    _lastChampionId = session.Team[0].ChampionId;
                }

                if (!_capturedThisChampSelect)
                {
                    _capturedThisChampSelect = true;
                    CaptureSnapshot("auto", true);
                }

                await _league.ResolveTeamNamesAsync(session);
                await _league.ResolveTeamStatsAsync(session);
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

        private static string FindAugmentDataPath()
        {
            string[] candidates = new string[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "augment-fit.json"),
                Path.Combine(AppConfig.ConfigDir, "augment-fit.json"),
                @"C:\Users\50353\Desktop\dsh\aram-overlay\data\augment-fit.json"
            };
            for (int i = 0; i < candidates.Length; i++)
            {
                if (File.Exists(candidates[i]))
                {
                    return candidates[i];
                }
            }
            return "";
        }

        private async void ScanAugmentsAsync()
        {
            if (_augmentScanning || _demo)
            {
                return;
            }
            if (_lastChampionId <= 0 || _augmentData == null || _augmentData.ChampionCount == 0 ||
                string.IsNullOrEmpty(_augmentOcrCommand) || !File.Exists(_augmentOcrCommand))
            {
                _augmentOverlay.HideOverlay();
                return;
            }

            LeagueWindow game = GameWindowFinder.Find();
            if (game == null || !game.IsValid)
            {
                _augmentOverlay.HideOverlay();
                return;
            }

            _augmentScanning = true;
            try
            {
                int width = game.ClientRect.Right - game.ClientRect.Left;
                int height = game.ClientRect.Bottom - game.ClientRect.Top;
                if (width <= 0 || height <= 0)
                {
                    _augmentOverlay.HideOverlay();
                    return;
                }

                System.Drawing.Bitmap screen = new System.Drawing.Bitmap(width, height);
                using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(screen))
                {
                    graphics.CopyFromScreen(game.ClientRect.Left, game.ClientRect.Top, 0, 0, screen.Size);
                }

                List<AugmentOcrLine> lines = await Task.Run(delegate
                {
                    return AugmentOcr.Scan(screen, game.ClientRect, _augmentOcrCommand);
                });
                screen.Dispose();

                List<BadgeItem> badges = new List<BadgeItem>();
                double[] centers = new double[] { 0.226, 0.445, 0.668 };
                int matched = 0;
                for (int i = 0; i < lines.Count && i < centers.Length; i++)
                {
                    if (lines[i] == null || lines[i].Score < 0.25 || string.IsNullOrEmpty(lines[i].Text))
                    {
                        continue;
                    }
                    AugmentFit fit = _augmentData.Match(_lastChampionId, lines[i].Text);
                    if (fit == null)
                    {
                        continue;
                    }
                    matched++;
                    double rate = fit.Score / 100.0;
                    badges.Add(new BadgeItem(centers[i], 0.045, fit.Rating + " " + fit.Score.ToString(), rate));
                }

                if (matched >= 2)
                {
                    _augmentOverlay.SetBadges(badges);
                    _augmentOverlay.ShowOverlay(game);
                }
                else
                {
                    _augmentOverlay.HideOverlay();
                }
            }
            catch
            {
                _augmentOverlay.HideOverlay();
            }
            finally
            {
                _augmentScanning = false;
            }
        }

        private static void GetBenchLayout(int width, out double first, out double pitch)
        {
            // 四档客户端尺寸的实测值（1920/1600 来自真实截图，1280/1024 按同比例外推）。
            if (width >= 1800)
            {
                first = 0.2948;
                pitch = 0.0453;
            }
            else if (width >= 1440)
            {
                first = 0.2931;
                pitch = 0.0466;
            }
            else if (width >= 1150)
            {
                first = 0.2940;
                pitch = 0.0475;
            }
            else
            {
                first = 0.2950;
                pitch = 0.0490;
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
            double benchFirst;
            double benchPitch;
            GetBenchLayout(_clientWidth > 0 ? _clientWidth : 1920, out benchFirst, out benchPitch);
            for (int i = 0; i < benchCount; i++)
            {
                int championId = session.BenchChampionIds[i];
                double x = benchFirst + i * benchPitch;
                double y = _config.BenchY;
                ChampionStat stat = _data.Get(championId);
                double? rate = stat == null ? (double?)null : stat.WinRate;
                badges.Add(new BadgeItem(x, y, _data.GetWinRateText(championId), rate));
            }

            if (_config.ShowAllyRows)
            {
                int teamRows = Math.Min(_config.AllyRows, session.Team.Count);
                for (int i = 0; i < teamRows; i++)
                {
                    TeamMember member = session.Team[i];
                    int championId = member.ChampionId;
                    if (championId <= 0)
                    {
                        continue;
                    }
                    double y = _config.AllyFirstY + i * _config.AllyPitchY;
                    ChampionStat stat = _data.Get(championId);
                    double? rate = stat == null ? (double?)null : stat.WinRate;
                    string text = "胜率" + _data.GetWinRateText(championId);
                    if (member.Recent != null && member.Recent.Available)
                    {
                        text += " · 近" + member.Recent.Games.ToString() + "场 " +
                            (member.Recent.WinRate * 100.0).ToString("0.0") + "%" +
                            " · " + member.Recent.Kda.ToString("0.0") + "KDA" +
                            "\n海斗分：" + member.Recent.HaidouScore.ToString() +
                            " · " + member.Recent.HaidouRating;
                    }
                    else
                    {
                        text += " · 近50场 —\n海斗分：--";
                    }
                    BadgeItem item = new BadgeItem(_config.AllyX, y, text, rate);
                    item.LeftAlign = true;
                    item.TopAlign = true;
                    badges.Add(item);
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
                    BadgeItem item = new BadgeItem(_config.AllyX, y, (ally[i] * 100).ToString("0.0") + "%", ally[i]);
                    item.LeftAlign = true;
                    item.TopAlign = true;
                    badges.Add(item);
                }
            }
            return badges;
        }

        private void CaptureSnapshot(string prefix, bool hideOverlay)
        {
            try
            {
                if (hideOverlay)
                {
                    _overlay.HideOverlay();
                    System.Threading.Thread.Sleep(150);
                }

                LeagueWindow window = LeagueWindowFinder.Find();
                int left;
                int top;
                int width;
                int height;
                string kind;

                if (window != null && window.IsValid)
                {
                    left = window.ClientRect.Left;
                    top = window.ClientRect.Top;
                    width = window.ClientRect.Right - window.ClientRect.Left;
                    height = window.ClientRect.Bottom - window.ClientRect.Top;
                    kind = "client";
                }
                else
                {
                    System.Drawing.Rectangle bounds = WinForms.Screen.PrimaryScreen.Bounds;
                    left = bounds.Left;
                    top = bounds.Top;
                    width = bounds.Width;
                    height = bounds.Height;
                    kind = "screen";
                    Log("snapshot: no league window, capturing full screen");
                }

                string dir = Path.Combine(AppConfig.ConfigDir, "captures");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, prefix + "-" + kind + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");
                using (System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(width, height))
                using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(left, top, 0, 0, bitmap.Size);
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

        private void SetPreview(bool enabled)
        {
            _preview = enabled;
            _previewUntil = enabled ? DateTime.Now.AddSeconds(30) : DateTime.MinValue;
            UpdateTrayText();
            if (!enabled)
            {
                _overlay.HideOverlay();
            }
        }

        private void UpdateTrayText()
        {
            if (_tray == null)
            {
                return;
            }
            string text = "ARAM Overlay";
            if (_preview)
            {
                text += " · PREVIEW";
            }
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
