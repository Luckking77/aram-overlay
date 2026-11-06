using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace AramOverlay
{
    public class DataStore
    {
        private readonly object _sync = new object();
        private Dictionary<int, ChampionStat> _stats;
        private bool _refreshing;
        private static readonly HttpClient Http = CreateHttpClient();

        public string DataDate { get; private set; }
        public DateTime LastUpdated { get; private set; }
        public string LastError { get; private set; }
        public int Count { get; private set; }

        public DataStore()
        {
            _stats = new Dictionary<int, ChampionStat>();
            DataDate = "";
            LastUpdated = DateTime.MinValue;
            LastError = "";
            Count = 0;
            LoadCache();
        }

        private static HttpClient CreateHttpClient()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            HttpClient client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AramOverlay/0.1");
            return client;
        }

        private static string CachePath
        {
            get { return Path.Combine(AppConfig.ConfigDir, "latest.json"); }
        }

        public ChampionStat Get(int championId)
        {
            lock (_sync)
            {
                ChampionStat stat;
                if (_stats.TryGetValue(championId, out stat))
                {
                    return stat;
                }
                return null;
            }
        }

        public string GetWinRateText(int championId)
        {
            ChampionStat stat = Get(championId);
            if (stat == null)
            {
                return "—";
            }
            return (stat.WinRate * 100.0).ToString("0.0", CultureInfo.InvariantCulture) + "%";
        }

        public void LoadCache()
        {
            string[] candidates = new string[]
            {
                CachePath,
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "latest.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "latest.json")
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                try
                {
                    if (!File.Exists(candidates[i]))
                    {
                        continue;
                    }
                    string json = File.ReadAllText(candidates[i]);
                    if (Parse(json, false))
                    {
                        return;
                    }
                }
                catch
                {
                }
            }
        }

        public async Task<bool> RefreshAsync(AppConfig config)
        {
            lock (_sync)
            {
                if (_refreshing)
                {
                    return false;
                }
                _refreshing = true;
            }

            try
            {
                string json = null;
                Exception last = null;

                string[] urls = new string[] { config.DataUrl, config.FallbackDataUrl };
                for (int i = 0; i < urls.Length; i++)
                {
                    if (string.IsNullOrEmpty(urls[i]))
                    {
                        continue;
                    }
                    try
                    {
                        json = await Http.GetStringAsync(urls[i]).ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(json))
                        {
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        last = ex;
                        json = null;
                    }
                }

                if (string.IsNullOrEmpty(json))
                {
                    LastError = last != null ? last.Message : "empty response";
                    return false;
                }

                bool parsed = Parse(json, true);
                if (parsed)
                {
                    try
                    {
                        File.WriteAllText(CachePath, json);
                    }
                    catch
                    {
                    }
                }
                return parsed;
            }
            finally
            {
                lock (_sync)
                {
                    _refreshing = false;
                }
            }
        }

        private bool Parse(string json, bool updateTimestamp)
        {
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                Dictionary<string, object> root = serializer.DeserializeObject(json) as Dictionary<string, object>;
                if (root == null)
                {
                    LastError = "root is not an object";
                    return false;
                }

                object championsValue;
                if (!root.TryGetValue("champions", out championsValue))
                {
                    LastError = "missing champions";
                    return false;
                }

                object[] rows = championsValue as object[];
                if (rows == null || rows.Length == 0)
                {
                    LastError = "empty champions";
                    return false;
                }

                Dictionary<int, ChampionStat> next = new Dictionary<int, ChampionStat>(rows.Length);
                for (int i = 0; i < rows.Length; i++)
                {
                    Dictionary<string, object> row = rows[i] as Dictionary<string, object>;
                    if (row == null)
                    {
                        continue;
                    }

                    int id = ToInt(row, "championId");
                    if (id <= 0)
                    {
                        continue;
                    }

                    ChampionStat stat = new ChampionStat();
                    stat.ChampionId = id;
                    stat.WinRate = ToDouble(row, "winRate");
                    stat.PickRate = ToDouble(row, "pickRate");
                    next[id] = stat;
                }

                if (next.Count == 0)
                {
                    LastError = "no usable champion rows";
                    return false;
                }

                string date = "";
                object dateValue;
                if (root.TryGetValue("date", out dateValue) && dateValue != null)
                {
                    date = Convert.ToString(dateValue, CultureInfo.InvariantCulture);
                }

                lock (_sync)
                {
                    _stats = next;
                    Count = next.Count;
                    DataDate = date;
                    if (updateTimestamp)
                    {
                        LastUpdated = DateTime.Now;
                    }
                    LastError = "";
                }
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }

        private static int ToInt(Dictionary<string, object> row, string key)
        {
            object value;
            if (!row.TryGetValue(key, out value) || value == null)
            {
                return 0;
            }
            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0;
            }
        }

        private static double ToDouble(Dictionary<string, object> row, string key)
        {
            object value;
            if (!row.TryGetValue(key, out value) || value == null)
            {
                return 0.0;
            }
            try
            {
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0.0;
            }
        }
    }
}
