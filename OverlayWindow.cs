using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace AramOverlay
{
    public class OverlayWindow : Window
    {
        private readonly Canvas _canvas;
        private readonly List<BadgeItem> _badges;
        private IntPtr _handle;
        private bool _visible;

        public IntPtr Handle
        {
            get { return _handle; }
        }

        public OverlayWindow()
        {
            _badges = new List<BadgeItem>();
            _canvas = new Canvas();
            _canvas.IsHitTestVisible = false;
            _canvas.Background = Brushes.Transparent;
            _canvas.ClipToBounds = false;
            _canvas.HorizontalAlignment = HorizontalAlignment.Stretch;
            _canvas.VerticalAlignment = VerticalAlignment.Stretch;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            ShowActivated = false;
            IsHitTestVisible = false;
            Focusable = false;
            Left = 0;
            Top = 0;
            Width = 100;
            Height = 100;
            Content = _canvas;

            SourceInitialized += OnSourceInitialized;
            SizeChanged += delegate { Render(); };
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            _handle = new WindowInteropHelper(this).Handle;
            Native.MakeClickThrough(_handle);
        }

        public void SetBadges(List<BadgeItem> badges)
        {
            _badges.Clear();
            if (badges != null)
            {
                _badges.AddRange(badges);
            }
            Render();
        }

        public void Redraw()
        {
            Render();
        }

        public void ShowOverlay(LeagueWindow client)
        {
            if (client == null || !client.IsValid)
            {
                HideOverlay();
                return;
            }

            if (!_visible)
            {
                Show();
                _visible = true;
                if (_handle == IntPtr.Zero)
                {
                    _handle = new WindowInteropHelper(this).Handle;
                    Native.MakeClickThrough(_handle);
                }
            }

            Native.RECT rect = client.ClientRect;
            Native.SetWindowPos(
                _handle,
                Native.HWND_TOPMOST,
                rect.Left,
                rect.Top,
                rect.Right - rect.Left,
                rect.Bottom - rect.Top,
                Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);

            Dispatcher.BeginInvoke(new Action(Render));
        }

        public void HideOverlay()
        {
            if (!_visible)
            {
                return;
            }
            _visible = false;
            Hide();
        }

        public bool IsOverlayVisible
        {
            get { return _visible; }
        }

        private void Render()
        {
            if (!_visible || _canvas == null)
            {
                return;
            }

            double width = ActualWidth;
            double height = ActualHeight;
            if (width <= 1 || height <= 1)
            {
                return;
            }

            _canvas.Children.Clear();
            _canvas.Width = width;
            _canvas.Height = height;
            double fontSize = Math.Max(14.0, height * GetFontRatio());
            FontFamily font = new FontFamily("Segoe UI");

            for (int i = 0; i < _badges.Count; i++)
            {
                BadgeItem badge = _badges[i];
                TextBlock text = new TextBlock();
                text.Text = badge.Text;
                text.FontFamily = font;
                text.FontWeight = FontWeights.SemiBold;
                text.FontSize = fontSize;
                text.Foreground = badge.Highlight ? Brushes.Gold : GetBrush(badge.WinRate);
                text.IsHitTestVisible = false;

                DropShadowEffect shadow = new DropShadowEffect();
                shadow.Color = Colors.Black;
                shadow.BlurRadius = Math.Max(2.0, fontSize * 0.18);
                shadow.ShadowDepth = 0;
                shadow.Opacity = 0.95;
                text.Effect = shadow;

                text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double x = badge.LeftAlign
                    ? width * badge.XRatio
                    : width * badge.XRatio - text.DesiredSize.Width / 2.0;
                double y = badge.TopAlign
                    ? height * badge.YRatio
                    : height * badge.YRatio - text.DesiredSize.Height / 2.0;
                Canvas.SetLeft(text, x);
                Canvas.SetTop(text, y);
                _canvas.Children.Add(text);
            }
            _canvas.UpdateLayout();
        }

        private double GetFontRatio()
        {
            return _fontRatio;
        }

        private double _fontRatio = 0.0115;

        public void SetFontRatio(double ratio)
        {
            if (ratio >= 0.005 && ratio <= 0.05)
            {
                _fontRatio = ratio;
            }
        }

        private static Brush GetBrush(double? winRate)
        {
            if (!winRate.HasValue)
            {
                return Brushes.White;
            }
            if (winRate.Value >= 0.52)
            {
                return new SolidColorBrush(Color.FromRgb(140, 255, 184));
            }
            if (winRate.Value <= 0.48)
            {
                return new SolidColorBrush(Color.FromRgb(255, 155, 155));
            }
            return Brushes.White;
        }
    }
}
