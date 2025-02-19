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
        Func<Player, int> _skillFunc;
        int _missGapIncrese = 1;
        int skillMin, skillMax;
        int minWait, maxWait;
        int minNeededPlayers, maxNeededPlayers;
        bool _noGapOnTimeout;

        public Matchmaker(int minNeededPlayers, int maxAllowedPlayers,
            MatchFound OnMatchFound,
            int MinWaitTime = 5, int MaxWaitTime = 30, string name = "",
            int SkillMinDistance = 1, int SkillMaxDistance = 5,
            int SkillDistIncreasePerTick = 1,
            int TickInterval = 1,
            bool NoSkillGapOnTimeout = true,
            Func<Player, int>? SkillFunc = null,
            Action<byte[], Player>? MessageReceivedFunc = null
            ) : base(name)
        {
            // PIGSF Room settings:
            MinPlayers = 0;
            MaxPlayers = int.MaxValue;
            TickRate = 1 / TickInterval;

            // Matchmaker settings
            maxNeededPlayers = maxAllowedPlayers;
            this.minNeededPlayers = minNeededPlayers;
            _matchFound = OnMatchFound;

            minWait = MinWaitTime;
            maxWait = MaxWaitTime;
            TickRate = 1000 / TickInterval;
            _msgReceivedFunc = MessageReceivedFunc;
            _missGapIncrese = SkillDistIncreasePerTick;
            _noGapOnTimeout = NoSkillGapOnTimeout;
            _skillFunc = SkillFunc;

            skillMin = Math.Min(SkillMinDistance, SkillMaxDistance);
            skillMax = Math.Max(SkillMinDistance, SkillMaxDistance);
            Log.Write($"Matchmaker {Name} started.");
            timer.Start();
        }

        Stopwatch timer = new Stopwatch();
        int MissCount = 0;

        bool MatchmakerTick()
        {
            if (players.Count < minNeededPlayers) return false;

            Stopwatch sw = new Stopwatch();
            sw.Start();

            List<List<(Player, int)>> matchedGroups = new();
            int left = 0;
            var cp = players.Copy();
            var plrs = cp.Select(p=> (p, _skillFunc(p))).OrderBy(p => p.Item2).ToList();

            int currentSkillMin = skillMin + MissCount;
            int currentSkillMax = skillMax + MissCount;
            bool isTimeOut = timer.Elapsed.Seconds > maxWait;

            for (int right = 0; right < plrs.Count; right++)
            {
                int mmr = plrs[right].Item2;

                // Shrink the window while keeping plrs in range [MMR-currentSkillMin, MMR+currentSkillMax]
                while (left < right && plrs[left].Item2 < mmr - currentSkillMin) left++;

                // Ensure we have exactly maxNeededPlayers plrs in range
                int windowSize = right - left + 1;
                if (windowSize >= maxNeededPlayers)
                {
                    // Take exactly maxNeededPlayers plrs for a match
                    // CHANGE: if isTimeOut, allow between minNeededPlayers and maxNeededPlayers
                    matchedGroups.Add(plrs.GetRange(left, maxNeededPlayers));

                    // Move left pointer forward to ensure non-overlapping matches
                    left += maxNeededPlayers;
                }
            }

            // 5: Process matched groups
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
            Log.Write($"No match found with: dev={currentSkillMin}..{currentSkillMax} (tick={MissCount}) T = {sw.ElapsedMilliseconds}");
            return false;
        }

        protected override void Update(float dt)
        {
            var found = MatchmakerTick();
            if (!found) MissCount += _missGapIncrese;
            else
            {
                MissCount = 0;
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
