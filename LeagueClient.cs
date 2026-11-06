using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace AramOverlay
{
    public static class LeagueWindowFinder
    {
        public static LeagueWindow Find()
        {
            Process[] processes = Process.GetProcessesByName("LeagueClientUx");
            if (processes == null || processes.Length == 0)
            {
                return null;
            }

            HashSet<uint> pids = new HashSet<uint>();
            for (int i = 0; i < processes.Length; i++)
            {
                try
                {
                    pids.Add((uint)processes[i].Id);
                }
                catch
                {
                }
            }

            LeagueWindow best = null;
            long bestArea = 0;

            Native.EnumWindowsProc callback = delegate(IntPtr hwnd, IntPtr lparam)
            {
                uint pid;
                Native.GetWindowThreadProcessId(hwnd, out pid);
                if (!pids.Contains(pid))
                {
                    return true;
                }
                if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd))
                {
                    return true;
                }

                Native.RECT rect = Native.GetClientRectOnScreen(hwnd);
                int width = rect.Right - rect.Left;
                int height = rect.Bottom - rect.Top;
                if (width < 640 || height < 480)
                {
                    return true;
                }

                long area = (long)width * (long)height;
                if (area > bestArea)
                {
                    bestArea = area;
                    LeagueWindow window = new LeagueWindow();
                    window.Handle = hwnd;
                    window.ClientRect = rect;
                    best = window;
                }
                return true;
            };

            Native.EnumWindows(callback, IntPtr.Zero);

            if (best == null)
            {
                for (int i = 0; i < processes.Length; i++)
                {
                    try
                    {
                        IntPtr hwnd = processes[i].MainWindowHandle;
                        if (hwnd == IntPtr.Zero || !Native.IsWindowVisible(hwnd))
                        {
                            continue;
                        }
                        Native.RECT rect = Native.GetClientRectOnScreen(hwnd);
                        int width = rect.Right - rect.Left;
                        int height = rect.Bottom - rect.Top;
                        if (width >= 640 && height >= 480)
                        {
                            LeagueWindow window = new LeagueWindow();
                            window.Handle = hwnd;
                            window.ClientRect = rect;
                            best = window;
                            break;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return best;
        }

        public static bool IsLeagueForeground()
        {
            IntPtr hwnd = Native.GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }
            uint pid;
            Native.GetWindowThreadProcessId(hwnd, out pid);
            try
            {
                Process process = Process.GetProcessById((int)pid);
                return string.Equals(process.ProcessName, "LeagueClientUx", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }

    public class LeagueApi
    {
        private static readonly Regex PortRegex = new Regex("--app-port=(\\d+)", RegexOptions.IgnoreCase);
        private static readonly Regex TokenRegex = new Regex("--remoting-auth-token=([^\\s\"]+)", RegexOptions.IgnoreCase);

        private HttpClient _http;
        private DateTime _lastConnectAttempt;
        private int _port;
        private string _token;

        public bool IsConnected
        {
            get { return _http != null; }
        }

        public LeagueApi()
        {
            ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
        }

        public void EnsureConnected()
        {
            if (_http != null)
            {
                return;
            }
            if (DateTime.Now - _lastConnectAttempt < TimeSpan.FromSeconds(5))
            {
                return;
            }
            _lastConnectAttempt = DateTime.Now;

            int port;
            string token;
            if (!TryReadCredentials(out port, out token))
            {
                return;
            }

            try
            {
                HttpClientHandler handler = new HttpClientHandler();
                handler.UseProxy = false;
                HttpClient client = new HttpClient(handler);
                client.Timeout = TimeSpan.FromSeconds(3);
                client.BaseAddress = new Uri("https://127.0.0.1:" + port.ToString() + "/");
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                    "Basic",
                    Convert.ToBase64String(Encoding.ASCII.GetBytes("riot:" + token)));
                _http = client;
                _port = port;
                _token = token;
            }
            catch
            {
                _http = null;
            }
        }

        public void Disconnect()
        {
            if (_http != null)
            {
                try
                {
                    _http.Dispose();
                }
                catch
                {
                }
            }
            _http = null;
        }

        private bool TryReadCredentials(out int port, out string token)
        {
            port = 0;
            token = null;

            Process[] processes = Process.GetProcessesByName("LeagueClientUx");
            if (processes == null || processes.Length == 0)
            {
                return false;
            }

            string exePath = null;
            int pid = 0;
            try
            {
                exePath = processes[0].MainModule.FileName;
                pid = processes[0].Id;
            }
            catch
            {
            }

            if (pid > 0)
            {
                try
                {
                    using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                        "SELECT CommandLine FROM Win32_Process WHERE ProcessId = " + pid.ToString()))
                    {
                        foreach (ManagementObject item in searcher.Get())
                        {
                            object value = item["CommandLine"];
                            string commandLine = value == null ? "" : value.ToString();
                            Match portMatch = PortRegex.Match(commandLine);
                            Match tokenMatch = TokenRegex.Match(commandLine);
                            if (portMatch.Success && tokenMatch.Success)
                            {
                                if (int.TryParse(portMatch.Groups[1].Value, out port))
                                {
                                    token = tokenMatch.Groups[1].Value;
                                    return !string.IsNullOrEmpty(token);
                                }
                            }
                        }
                    }
                }
                catch
                {
                }
            }

            if (!string.IsNullOrEmpty(exePath))
            {
                string dir = Path.GetDirectoryName(exePath);
                for (int level = 0; level < 4 && !string.IsNullOrEmpty(dir); level++)
                {
                    string lockPath = Path.Combine(dir, "lockfile");
                    if (File.Exists(lockPath))
                    {
                        try
                        {
                            string text = File.ReadAllText(lockPath).Trim();
                            string[] parts = text.Split(':');
                            if (parts.Length >= 5)
                            {
                                int parsedPort;
                                if (int.TryParse(parts[2], out parsedPort))
                                {
                                    port = parsedPort;
                                    token = parts[3];
                                    return !string.IsNullOrEmpty(token);
                                }
                            }
                        }
                        catch
                        {
                        }
                    }
                    dir = Path.GetDirectoryName(dir);
                }
            }

            return false;
        }

        public async Task<string> GetPhaseAsync()
        {
            if (_http == null)
            {
                return null;
            }
            try
            {
                string json = await _http.GetStringAsync("lol-gameflow/v1/gameflow-phase").ConfigureAwait(false);
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                string phase = serializer.Deserialize<string>(json);
                return phase;
            }
            catch
            {
                Disconnect();
                return null;
            }
        }

        public async Task<ChampSelectState> GetChampSelectAsync()
        {
            if (_http == null)
            {
                return null;
            }
            try
            {
                string json = await _http.GetStringAsync("lol-champ-select/v1/session").ConfigureAwait(false);
                return ParseChampSelect(json);
            }
            catch
            {
                return null;
            }
        }

        private static ChampSelectState ParseChampSelect(string json)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            Dictionary<string, object> root = serializer.DeserializeObject(json) as Dictionary<string, object>;
            if (root == null)
            {
                return null;
            }

            ChampSelectState state = new ChampSelectState();

            object benchValue;
            if (root.TryGetValue("benchChampions", out benchValue))
            {
                object[] bench = benchValue as object[];
                if (bench != null)
                {
                    for (int i = 0; i < bench.Length; i++)
                    {
                        Dictionary<string, object> item = bench[i] as Dictionary<string, object>;
                        if (item == null)
                        {
                            continue;
                        }
                        object idValue;
                        if (item.TryGetValue("championId", out idValue) && idValue != null)
                        {
                            int id;
                            if (int.TryParse(Convert.ToString(idValue), out id))
                            {
                                state.BenchChampionIds.Add(id);
                            }
                        }
                    }
                }
            }

            object teamValue;
            if (root.TryGetValue("myTeam", out teamValue))
            {
                object[] team = teamValue as object[];
                if (team != null)
                {
                    for (int i = 0; i < team.Length; i++)
                    {
                        Dictionary<string, object> item = team[i] as Dictionary<string, object>;
                        if (item == null)
                        {
                            continue;
                        }
                        TeamMember member = new TeamMember();
                        object cellValue;
                        if (item.TryGetValue("cellId", out cellValue) && cellValue != null)
                        {
                            int cellId;
                            if (int.TryParse(Convert.ToString(cellValue), out cellId))
                            {
                                member.CellId = cellId;
                            }
                        }
                        object champValue;
                        if (item.TryGetValue("championId", out champValue) && champValue != null)
                        {
                            int champId;
                            if (int.TryParse(Convert.ToString(champValue), out champId))
                            {
                                member.ChampionId = champId;
                            }
                        }
                        state.Team.Add(member);
                    }
                }
            }

            object localValue;
            if (root.TryGetValue("localPlayerCellId", out localValue) && localValue != null)
            {
                int localCell;
                if (int.TryParse(Convert.ToString(localValue), out localCell))
                {
                    state.LocalPlayerCellId = localCell;
                }
            }

            return state;
        }
    }
}
