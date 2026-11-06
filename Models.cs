using System;
using System.Collections.Generic;

namespace AramOverlay
{
    public class ChampionStat
    {
        public int ChampionId { get; set; }
        public double WinRate { get; set; }
        public double PickRate { get; set; }
    }

    public class RecentMayhem
    {
        public bool Available { get; set; }
        public int Games { get; set; }
        public int Wins { get; set; }
        public double WinRate { get; set; }
        public double WilsonLow { get; set; }
        public double Kda { get; set; }
        public double AvgDeaths { get; set; }
        public string SampleLabel { get; set; }

        public RecentMayhem()
        {
            SampleLabel = "";
        }
    }

    public class TeamMember
    {
        public int CellId { get; set; }
        public int ChampionId { get; set; }
        public long SummonerId { get; set; }
        public string Puuid { get; set; }
        public string DisplayName { get; set; }
        public RecentMayhem Recent { get; set; }

        public TeamMember()
        {
            DisplayName = "";
            Puuid = "";
        }
    }

    public class ChampSelectState
    {
        public string Phase { get; set; }
        public List<int> BenchChampionIds { get; set; }
        public List<TeamMember> Team { get; set; }
        public int LocalPlayerCellId { get; set; }

        public ChampSelectState()
        {
            Phase = "";
            BenchChampionIds = new List<int>();
            Team = new List<TeamMember>();
            LocalPlayerCellId = -1;
        }
    }

    public class LeagueWindow
    {
        public IntPtr Handle { get; set; }
        public Native.RECT ClientRect { get; set; }

        public bool IsValid
        {
            get
            {
                return Handle != IntPtr.Zero
                    && ClientRect.Right > ClientRect.Left
                    && ClientRect.Bottom > ClientRect.Top;
            }
        }
    }

    public class BadgeItem
    {
        public double XRatio { get; set; }
        public double YRatio { get; set; }
        public string Text { get; set; }
        public double? WinRate { get; set; }

        public BadgeItem(double xRatio, double yRatio, string text, double? winRate)
        {
            XRatio = xRatio;
            YRatio = yRatio;
            Text = text;
            WinRate = winRate;
        }
    }
}
