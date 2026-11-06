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
        private readonly Dictionary<long, string> _summonerNames = new Dictionary<long, string>();
        private readonly Dictionary<string, HistoryCacheEntry> _historyCache = new Dictionary<string, HistoryCacheEntry>();

        private class HistoryCacheEntry
        {
            public RecentMayhem Value;
            public DateTime At;
        }

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

        public async Task ResolveTeamNamesAsync(ChampSelectState state)
        {
            if (state == null || state.Team == null)
            {
                return;
            }
            for (int i = 0; i < state.Team.Count; i++)
            {
                TeamMember member = state.Team[i];
                if (member.SummonerId <= 0)
                {
                    continue;
                }
                if (!string.IsNullOrEmpty(member.DisplayName))
                {
                    continue;
                }
                string cached;
                if (_summonerNames.TryGetValue(member.SummonerId, out cached))
                {
                    member.DisplayName = cached;
                    continue;
                }
                string name = await GetSummonerNameAsync(member.SummonerId);
                if (!string.IsNullOrEmpty(name))
                {
                    _summonerNames[member.SummonerId] = name;
                    member.DisplayName = name;
                }
            }
        }

        public async Task ResolveTeamStatsAsync(ChampSelectState state)
        {
            if (state == null || state.Team == null)
            {
                return;
            }
            for (int i = 0; i < state.Team.Count; i++)
            {
                TeamMember member = state.Team[i];
                if (string.IsNullOrEmpty(member.Puuid))
                {
                    continue;
                }
                HistoryCacheEntry cached;
                if (_historyCache.TryGetValue(member.Puuid, out cached) && (DateTime.UtcNow - cached.At).TotalMinutes < 10)
                {
                    member.Recent = cached.Value;
                    continue;
                }
                RecentMayhem recent = await GetRecentMayhemAsync(member.Puuid, 50).ConfigureAwait(false);
                _historyCache[member.Puuid] = new HistoryCacheEntry { Value = recent, At = DateTime.UtcNow };
                member.Recent = recent;
            }
        }

        private async Task<RecentMayhem> GetRecentMayhemAsync(string puuid, int wanted)
        {
            RecentMayhem result = new RecentMayhem();
            if (_http == null || string.IsNullOrEmpty(puuid))
            {
                return result;
            }

            int games = 0;
            int wins = 0;
            double kills = 0;
            double deaths = 0;
            double assists = 0;
            int fetched = 0;

            try
            {
                for (int start = 0; start < 100 && games < wanted; start += 20)
                {
                    int end = start + 19;
                    string url = "lol-match-history/v1/products/lol/" + Uri.EscapeDataString(puuid) +
                        "/matches?begIndex=" + start.ToString() + "&endIndex=" + end.ToString();
                    string json = await _http.GetStringAsync(url).ConfigureAwait(false);
                    JavaScriptSerializer serializer = new JavaScriptSerializer();
                    Dictionary<string, object> root = serializer.DeserializeObject(json) as Dictionary<string, object>;
                    if (root == null)
                    {
                        break;
                    }

                    object gamesRoot;
                    if (!root.TryGetValue("games", out gamesRoot) || gamesRoot == null)
                    {
                        break;
                    }
                    Dictionary<string, object> gamesObject = gamesRoot as Dictionary<string, object>;
                    if (gamesObject == null)
                    {
                        break;
                    }
                    object listValue;
                    if (!gamesObject.TryGetValue("games", out listValue) || listValue == null)
                    {
                        break;
                    }
                    object[] list = listValue as object[];
                    if (list == null || list.Length == 0)
                    {
                        break;
                    }

                    fetched += list.Length;
                    for (int i = 0; i < list.Length && games < wanted; i++)
                    {
                        Dictionary<string, object> game = list[i] as Dictionary<string, object>;
                        if (game == null)
                        {
                            continue;
                        }
                        int queueId;
                        if (!TryGetInt(game, "queueId", out queueId) || queueId != 2400)
                        {
                            continue;
                        }
                        Dictionary<string, object> participant = FindParticipant(game, puuid);
                        if (participant == null)
                        {
                            continue;
                        }
                        Dictionary<string, object> stats = null;
                        object statsValue;
                        if (participant.TryGetValue("stats", out statsValue))
                        {
                            stats = statsValue as Dictionary<string, object>;
                        }
                        if (stats == null)
                        {
                            continue;
                        }

                        bool won = false;
                        object winValue;
                        if (stats.TryGetValue("win", out winValue) && winValue is bool)
                        {
                            won = (bool)winValue;
                        }
                        int k;
                        int d;
                        int a;
                        TryGetInt(stats, "kills", out k);
                        TryGetInt(stats, "deaths", out d);
                        TryGetInt(stats, "assists", out a);
                        games++;
                        if (won)
                        {
                            wins++;
                        }
                        kills += k;
                        deaths += d;
                        assists += a;
                    }

                    if (list.Length < 20)
                    {
                        break;
                    }
                }
            }
            catch
            {
                return result;
            }

            if (games == 0)
            {
                result.SampleLabel = "无数据";
                return result;
            }

            result.Available = true;
            result.Games = games;
            result.Wins = wins;
            result.WinRate = (double)wins / games;
            result.Kda = (kills + assists) / Math.Max(1.0, deaths);
            result.AvgDeaths = deaths / games;
            result.WilsonLow = WilsonLow(wins, games);
            result.SampleLabel = games < 10 ? "样本少" : (games < 30 ? "样本中" : "样本足");
            return result;
        }

        private static Dictionary<string, object> FindParticipant(Dictionary<string, object> game, string puuid)
        {
            object listValue;
            if (!game.TryGetValue("participants", out listValue) || listValue == null)
            {
                return null;
            }
            object[] participants = listValue as object[];
            if (participants == null)
            {
                return null;
            }
            for (int i = 0; i < participants.Length; i++)
            {
                Dictionary<string, object> participant = participants[i] as Dictionary<string, object>;
                if (participant == null)
                {
                    continue;
                }
                object p;
                if (participant.TryGetValue("puuid", out p) && p != null &&
                    string.Equals(Convert.ToString(p), puuid, StringComparison.OrdinalIgnoreCase))
                {
                    return participant;
                }
            }

            object identitiesValue;
            if (game.TryGetValue("participantIdentities", out identitiesValue) && identitiesValue != null)
            {
                object[] identities = identitiesValue as object[];
                if (identities != null)
                {
                    int targetId = -1;
                    for (int i = 0; i < identities.Length; i++)
                    {
                        Dictionary<string, object> identity = identities[i] as Dictionary<string, object>;
                        if (identity == null)
                        {
                            continue;
                        }
                        Dictionary<string, object> player = null;
                        object playerValue;
                        if (identity.TryGetValue("player", out playerValue))
                        {
                            player = playerValue as Dictionary<string, object>;
                        }
                        if (player == null)
                        {
                            continue;
                        }
                        object p;
                        if (player.TryGetValue("puuid", out p) && p != null &&
                            string.Equals(Convert.ToString(p), puuid, StringComparison.OrdinalIgnoreCase))
                        {
                            TryGetInt(identity, "participantId", out targetId);
                            break;
                        }
                    }
                    if (targetId > 0)
                    {
                        for (int i = 0; i < participants.Length; i++)
                        {
                            Dictionary<string, object> participant = participants[i] as Dictionary<string, object>;
                            int id;
                            if (participant != null && TryGetInt(participant, "participantId", out id) && id == targetId)
                            {
                                return participant;
                            }
                        }
                    }
                }
            }
            return null;
        }

        private static bool TryGetInt(Dictionary<string, object> dict, string key, out int value)
        {
            value = 0;
            object raw;
            if (dict == null || !dict.TryGetValue(key, out raw) || raw == null)
            {
                return false;
            }
            return int.TryParse(Convert.ToString(raw), out value);
        }

        private static double WilsonLow(int wins, int games)
        {
            if (games <= 0)
            {
                return 0;
            }
            const double z = 1.959963984540054;
            double n = games;
            double p = (double)wins / n;
            double denominator = 1.0 + (z * z) / n;
            double centre = p + (z * z) / (2.0 * n);
            double margin = z * Math.Sqrt((p * (1.0 - p) + (z * z) / (4.0 * n)) / n);
            return Math.Max(0.0, (centre - margin) / denominator);
        }

        private async Task<string> GetSummonerNameAsync(long summonerId)
        {
            if (_http == null || summonerId <= 0)
            {
                return null;
            }
            try
            {
                string json = await _http.GetStringAsync("lol-summoner/v1/summoners/" + summonerId.ToString()).ConfigureAwait(false);
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                Dictionary<string, object> root = serializer.DeserializeObject(json) as Dictionary<string, object>;
                if (root == null)
                {
                    return null;
                }

                string displayName = null;
                object displayValue;
                if (root.TryGetValue("displayName", out displayValue) && displayValue != null)
                {
                    displayName = Convert.ToString(displayValue);
                }
                if (string.IsNullOrEmpty(displayName))
                {
                    string gameName = null;
                    string tagLine = null;
                    object gameValue;
                    object tagValue;
                    if (root.TryGetValue("gameName", out gameValue) && gameValue != null)
                    {
                        gameName = Convert.ToString(gameValue);
                    }
                    if (root.TryGetValue("tagLine", out tagValue) && tagValue != null)
                    {
                        tagLine = Convert.ToString(tagValue);
                    }
                    if (!string.IsNullOrEmpty(gameName))
                    {
                        displayName = string.IsNullOrEmpty(tagLine) ? gameName : gameName + "#" + tagLine;
                    }
                }
                return displayName;
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
                            if (int.TryParse(Convert.ToString(idValue), out id) && id > 0)
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
                        object summonerValue;
                        if (item.TryGetValue("summonerId", out summonerValue) && summonerValue != null)
                        {
                            long summonerId;
                            if (long.TryParse(Convert.ToString(summonerValue), out summonerId))
                            {
                                member.SummonerId = summonerId;
                            }
                        }
                        object puuidValue;
                        if (item.TryGetValue("puuid", out puuidValue) && puuidValue != null)
                        {
                            member.Puuid = Convert.ToString(puuidValue);
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
