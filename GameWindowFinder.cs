using System;
using System.Text;

namespace AramOverlay
{
    public static class GameWindowFinder
    {
        public static LeagueWindow Find()
        {
            LeagueWindow best = null;
            long bestArea = 0;
            Native.EnumWindows(delegate(IntPtr hwnd, IntPtr lParam)
            {
                if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd))
                {
                    return true;
                }

                StringBuilder className = new StringBuilder(128);
                Native.GetClassName(hwnd, className, className.Capacity);
                if (!string.Equals(className.ToString(), "RiotWindowClass", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                StringBuilder title = new StringBuilder(256);
                Native.GetWindowText(hwnd, title, title.Capacity);
                if (title.ToString().IndexOf("League of Legends", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return true;
                }

                Native.RECT rect = Native.GetClientRectOnScreen(hwnd);
                long area = (long)(rect.Right - rect.Left) * (rect.Bottom - rect.Top);
                if (area <= 0)
                {
                    return true;
                }
                if (area > bestArea)
                {
                    bestArea = area;
                    best = new LeagueWindow();
                    best.Handle = hwnd;
                    best.ClientRect = rect;
                }
                return true;
            }, IntPtr.Zero);
            return best;
        }
    }
}
