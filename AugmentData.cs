using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace AramOverlay
{
    public class AugmentFit
    {
        public int AugmentId { get; set; }
        public string Name { get; set; }
        public string Rarity { get; set; }
        public string IconUrl { get; set; }
        public double PickRate { get; set; }
        public double CnWinRate { get; set; }
        public double Lift { get; set; }
        public int Score { get; set; }
        public string Rating { get; set; }

        public AugmentFit()
        {
            Name = "";
            Rarity = "";
            IconUrl = "";
            Rating = "";
        }
    }

    public class AugmentData
    {
        private readonly Dictionary<int, List<AugmentFit>> _byChampion = new Dictionary<int, List<AugmentFit>>();
        private readonly Dictionary<int, Dictionary<string, AugmentFit>> _byChampionName = new Dictionary<int, Dictionary<string, AugmentFit>>();

        public int ChampionCount
        {
            get { return _byChampion.Count; }
        }

        public static AugmentData Load(string path)
        {
            AugmentData data = new AugmentData();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return data;
            }

            string json = File.ReadAllText(path, Encoding.UTF8);
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;
            Dictionary<string, object> root = serializer.DeserializeObject(json) as Dictionary<string, object>;
            if (root == null)
            {
                return data;
            }

            object championsValue;
            if (!root.TryGetValue("champions", out championsValue) || championsValue == null)
            {
                return data;
            }
            Dictionary<string, object> champions = championsValue as Dictionary<string, object>;
            if (champions == null)
            {
                return data;
            }

            foreach (KeyValuePair<string, object> championEntry in champions)
            {
                int championId;
                if (!int.TryParse(championEntry.Key, out championId))
                {
                    continue;
                }
                object[] rows = championEntry.Value as object[];
                if (rows == null)
                {
                    continue;
                }

                List<AugmentFit> list = new List<AugmentFit>();
                Dictionary<string, AugmentFit> byName = new Dictionary<string, AugmentFit>();
                for (int i = 0; i < rows.Length; i++)
                {
                    Dictionary<string, object> row = rows[i] as Dictionary<string, object>;
                    if (row == null)
                    {
                        continue;
                    }
                    AugmentFit fit = new AugmentFit();
                    fit.AugmentId = ReadInt(row, "augmentId");
                    fit.Name = ReadString(row, "name");
                    fit.Rarity = ReadString(row, "rarity");
                    fit.IconUrl = ReadString(row, "iconUrl");
                    fit.PickRate = ReadDouble(row, "pickRate");
                    fit.CnWinRate = ReadDouble(row, "cnWinRate");
                    fit.Lift = ReadDouble(row, "lift");
                    fit.Score = ReadInt(row, "score");
                    fit.Rating = ReadString(row, "rating");
                    list.Add(fit);

                    string normalized = Normalize(fit.Name);
                    if (normalized.Length > 0)
                    {
                        byName[normalized] = fit;
                    }
                }
                data._byChampion[championId] = list;
                data._byChampionName[championId] = byName;
            }
            return data;
        }

        public List<AugmentFit> GetForChampion(int championId)
        {
            List<AugmentFit> list;
            if (_byChampion.TryGetValue(championId, out list))
            {
                return list;
            }
            return new List<AugmentFit>();
        }

        public AugmentFit Match(int championId, string ocrText)
        {
            Dictionary<string, AugmentFit> byName = null;
            _byChampionName.TryGetValue(championId, out byName);
            string normalized = Normalize(ocrText);
            if (normalized.Length == 0)
            {
                return null;
            }

            string ocrCjk = CjkOnly(ocrText);
            List<AugmentFit> list;
            if (_byChampion.TryGetValue(championId, out list))
            {
                string trimmed = (ocrText ?? "").Trim();
                for (int i = 0; i < list.Count; i++)
                {
                    if (string.Equals(list[i].Name, trimmed, StringComparison.Ordinal))
                    {
                        return list[i];
                    }
                    string fitCjk = CjkOnly(list[i].Name);
                    if (ocrCjk.Length >= 2 && fitCjk.Length >= 2 &&
                        ocrCjk.Substring(0, 2) == fitCjk.Substring(0, 2))
                    {
                        return list[i];
                    }
                    if (trimmed.Length >= 2 && list[i].Name.Length >= 2)
                    {
                        string prefix = list[i].Name.Substring(0, Math.Min(2, list[i].Name.Length));
                        if (trimmed.Contains(prefix) || list[i].Name.Contains(trimmed))
                        {
                            return list[i];
                        }
                    }
                    string candidate = Normalize(list[i].Name);
                    if (candidate.Length > 0 && candidate == normalized)
                    {
                        return list[i];
                    }
                }
            }

            if (byName == null || byName.Count == 0)
            {
                return null;
            }

            AugmentFit exact;
            if (byName.TryGetValue(normalized, out exact))
            {
                return exact;
            }

            AugmentFit best = null;
            int bestLength = 0;
            foreach (KeyValuePair<string, AugmentFit> entry in byName)
            {
                if (entry.Key.Length >= 2 && normalized.Contains(entry.Key) && entry.Key.Length > bestLength)
                {
                    best = entry.Value;
                    bestLength = entry.Key.Length;
                }
            }
            return best;
        }

        private static string ReadString(Dictionary<string, object> row, string key)
        {
            object value;
            if (row.TryGetValue(key, out value) && value != null)
            {
                return Convert.ToString(value);
            }
            return "";
        }

        private static int ReadInt(Dictionary<string, object> row, string key)
        {
            object value;
            int parsed;
            if (row.TryGetValue(key, out value) && value != null && int.TryParse(Convert.ToString(value), out parsed))
            {
                return parsed;
            }
            return 0;
        }

        private static double ReadDouble(Dictionary<string, object> row, string key)
        {
            object value;
            double parsed;
            if (row.TryGetValue(key, out value) && value != null && double.TryParse(Convert.ToString(value), out parsed))
            {
                return parsed;
            }
            return 0;
        }

        public static string CjkOnly(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if ((c >= '\u3400' && c <= '\u9FFF') || (c >= '\uF900' && c <= '\uFAFF'))
                {
                    builder.Append(c);
                }
            }
            return builder.ToString();
        }

        public static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if ((c >= '0' && c <= '9') ||
                    (c >= 'a' && c <= 'z') ||
                    (c >= 'A' && c <= 'Z') ||
                    (c >= '\u3400' && c <= '\u9FFF') ||
                    (c >= '\uF900' && c <= '\uFAFF'))
                {
                    builder.Append(char.ToLowerInvariant(c));
                }
            }
            return builder.ToString();
        }
    }
}
