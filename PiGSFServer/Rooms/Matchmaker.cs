using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using PiGSF.Server;

namespace PiGSF.Rooms
{
    public class Matchmaker : Room
    {
        public delegate Room MatchFound(List<Player> matchedTogether);
        MatchFound _matchFound;
        Action<byte[], Player>? _msgReceivedFunc;
        Func<Player, double> _skillFunc;
        double _missGapIncrease = 1;
        double SkillWideningDistance;
        double minWait, maxWait;
        int minNeededPlayers, maxNeededPlayers;
        bool _noGapOnTimeout;

        public Matchmaker(int minNeededPlayers, int maxAllowedPlayers,
            MatchFound OnMatchFound,
            int MinWaitTime = 5, int MaxWaitTime = 30, string name = "",
            double SkillWideningDistance = 1,
            double SkillDistIncreasePerTick = 1,
            double SkillDistMaxIncrease = double.NaN,
            double TickInterval = 1,
            bool NoSkillGapOnTimeout = true,
            Func<Player, double>? SkillFunc = null,
            Action<byte[], Player>? MessageReceivedFunc = null
            ) : base(name)
        {
            // PIGSF Room settings:
            MinPlayers = 0;
            MaxPlayers = int.MaxValue;
            this.TickInterval = TickInterval;

            // Matchmaker settings
            maxNeededPlayers = maxAllowedPlayers;
            this.minNeededPlayers = minNeededPlayers;
            _matchFound = OnMatchFound;

            minWait = MinWaitTime;
            maxWait = MaxWaitTime;
            _msgReceivedFunc = MessageReceivedFunc;
            _missGapIncrease = SkillDistIncreasePerTick;
            _noGapOnTimeout = NoSkillGapOnTimeout;
            _skillFunc = SkillFunc;

            this.SkillWideningDistance = SkillWideningDistance;
            Log.Write($"Matchmaker {Name} started.");
            timer.Start();
        }

        Stopwatch timer = new Stopwatch();

        Dictionary<Player, double> PlayerWideningProgress;

        public static List<List<(Player p, double mmr)>> CalculateMatchingGroups(List<Player> players, Dictionary<Player, double>? playerWidenings, double defaultWidening, Func<Player, double> _skillFunc, int minNeededPlayers, int maxNeededPlayers, bool allowMinPlayers, double maxWait)
        {
            if (players.Count < minNeededPlayers) return new List<List<(Player, double)>>();

            List<List<(Player, double mmr)>> matchedGroups = new();
            int left = 0;
            var plrs = players.Select(p => (p, _skillFunc(p))).OrderBy(p => p.Item2).ToList();

            bool isTimeOut = allowMinPlayers;

            for (int right = 0; right < plrs.Count; right++)
            {
                double mmr = plrs[right].Item2;
                double wideningRight = playerWidenings != null && playerWidenings.TryGetValue(plrs[right].Item1, out double widenR) ? widenR : defaultWidening;

                // Shrink the window while keeping plrs in range based on the lower widening factor
                while (left < right)
                {
                    double wideningLeft = playerWidenings != null && playerWidenings.TryGetValue(plrs[left].Item1, out double widenL) ? widenL : defaultWidening;
                    double effectiveWidening = Math.Min(wideningLeft, wideningRight);

                    if (plrs[left].Item2 >= mmr - effectiveWidening) break;
                    left++;
                }

                // Ensure we have enough players in range
                int windowSize = right - left + 1;

                if (windowSize >= minNeededPlayers)
                {
                    int matchSize = isTimeOut ? Math.Min(windowSize, maxNeededPlayers) : maxNeededPlayers;

                    if (windowSize >= matchSize)
                    {
                        matchedGroups.Add(plrs.GetRange(left, matchSize));

                        // Move left pointer forward to prevent overlap
                        left += matchSize;
                    }
                }
            }
            return matchedGroups;
        }

        bool MatchmakerTick()
        {
            if (players.Count < minNeededPlayers) return false;

            Stopwatch sw = new Stopwatch();
            sw.Start();

            bool isTimeOut = timer.Elapsed.TotalSeconds > maxWait;
            var matchedGroups = CalculateMatchingGroups(players.Copy(), PlayerWideningProgress, SkillWideningDistance, _skillFunc, minNeededPlayers, maxNeededPlayers, isTimeOut, maxWait);

            if (matchedGroups.Count > 0)
            {
                foreach (var match in matchedGroups)
                {
                    var matchPlayers = match.Select(x => x.Item1).ToList();
                    Room? newRoom = null;
                    try
                    {
                        newRoom = _matchFound(matchPlayers);
                        Log.Write($"Match created: {newRoom.Name} with {match.Count} plrs.");
                        foreach (var player in matchPlayers) player.TransferToRoom(newRoom, true);
                    }
                    catch (Exception ex)
                    {
                        Log.Write($"Error creating match: {ex.Message}");
                    }
                }
                return true;
            }

            return false;
        }


        protected override void Update(float dt)
        {
            var found = MatchmakerTick();
            if (!found)
            {
                foreach(var p in players)
                {
                    if (!PlayerWideningProgress.ContainsKey(p))
                        PlayerWideningProgress[p] = SkillWideningDistance;
                    PlayerWideningProgress[p] += _missGapIncrease;
                }
            }
            else
            {
                timer.Restart();
            }
        }

        protected override void OnPlayerDisconnected(Player player, bool disband)
            => RemovePlayer(player);

        protected override void OnMessageReceived(byte[] message, Player sender)
            => _msgReceivedFunc?.Invoke(message, sender);

        protected override void OnServerCommand(string s)
        {
        }

        protected override void OnShutdownRequested()
        {
            base.OnShutdownRequested();
            eligibleForDeletion = true;
        }

        class HashSetComparer : IEqualityComparer<HashSet<int>>
        {
            public bool Equals(HashSet<int>? x, HashSet<int>? y)
            {
                if (x == null || y == null) return false;
                return x.SetEquals(y);
            }

            public int GetHashCode(HashSet<int> obj)
            {
                int hash = 0;
                foreach (var id in obj) // No need to re-sort here
                {
                    hash ^= id.GetHashCode();
                }
                return hash;
            }
        }
    }
}
