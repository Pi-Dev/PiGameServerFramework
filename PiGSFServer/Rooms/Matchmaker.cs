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
            MaxPlayers = maxAllowedPlayers;
            MinPlayers = minNeededPlayers;
            _matchFound = OnMatchFound;

            minWait = MinWaitTime;
            maxWait = MaxWaitTime;
            TickRate = 1000 * TickInterval;
            _msgReceivedFunc = MessageReceivedFunc;
            _missGapIncrese = SkillDistIncreasePerTick;
            _noGapOnTimeout = NoSkillGapOnTimeout;

            skillMin = Math.Min(SkillMinDistance, SkillMaxDistance);
            skillMax = Math.Max(SkillMinDistance, SkillMaxDistance);
            Log.Write($"Matchmaker {Name} started.");
            timer.Start();
        }

        Stopwatch timer = new Stopwatch();
        int MissCount = 0;

        bool MatchmakerTick()
        {
            if (players.Count < MinPlayers)
                return false; // Not enough players to form a match

            bool isTimeOut = timer.Elapsed.TotalSeconds > maxWait;
            int currentSkillMin = skillMin - MissCount;
            int currentSkillMax = skillMax + MissCount;

            if (isTimeOut && _noGapOnTimeout)
            {
                Log.Write("Matchmaker timeout reached, ignoring skill gaps.");
                currentSkillMin = int.MinValue;
                currentSkillMax = int.MaxValue;
            }

            // 1-1: Collect players into a local list
            List<Player> localPlayers = players.Copy();

            // 1-2: Compute and store skill values
            List<(Player player, int skill)> playerSkillPairs = localPlayers
                .Select(p => (p, _skillFunc(p)))
                .ToList();

            // 1-3: Sort players by skill
            playerSkillPairs.Sort((a, b) => a.skill.CompareTo(b.skill));

            // 1-4: Pre-group players by skill level
            Dictionary<int, List<Player>> skillBuckets = new Dictionary<int, List<Player>>();
            foreach (var (player, skill) in playerSkillPairs)
            {
                if (!skillBuckets.ContainsKey(skill))
                {
                    skillBuckets[skill] = new List<Player>();
                }
                skillBuckets[skill].Add(player);
            }

            // 1-5: Initialize necessary data structures
            List<List<Player>> validGroups = new List<List<Player>>();
            int left = 0;

            // 1-6: Iterate through players
            for (int right = 0; right < playerSkillPairs.Count; right++)
            {
                var (rightPlayer, rightSkill) = playerSkillPairs[right];

                // 1-7: Shrink window efficiently
                while (left < right && (rightSkill - playerSkillPairs[left].skill) > currentSkillMax)
                {
                    left++;
                }

                // 1-8: Collect players within range using skill buckets
                List<Player> potentialGroup = new List<Player>();
                for (int skill = rightSkill - currentSkillMax; skill <= rightSkill + currentSkillMax; skill++)
                {
                    if (skillBuckets.ContainsKey(skill))
                    {
                        foreach (var candidate in skillBuckets[skill])
                        {
                            int diff = Math.Abs(rightSkill - skill);
                            if (diff >= currentSkillMin)
                            {
                                potentialGroup.Add(candidate);
                            }
                        }
                    }
                }

                // 1-9: Validate group size
                if (potentialGroup.Count >= MinPlayers && potentialGroup.Count <= MaxPlayers)
                {
                    validGroups.Add(potentialGroup);
                }
            }

            // 🔧 Fix 3: Remove duplicate groups efficiently (Sorting inside HashSet Key)
            HashSet<string> uniqueGroups = new HashSet<string>();
            validGroups = validGroups
                .Where(g => uniqueGroups.Add(string.Join(",", g.OrderBy(p => p.uid).Select(p => p.uid))))
                .ToList();

            // 3: Form max-player matches first
            List<List<Player>> matchedGroups = validGroups
                .Where(g => g.Count == MaxPlayers)
                .ToList();

            // 4: If timed out, use the largest available group
            if (isTimeOut && matchedGroups.Count == 0)
            {
                var bestAvailableGroup = validGroups
                    .Where(g => g.Count >= MinPlayers)
                    .OrderByDescending(g => g.Count) // **Fix 5 Stays the Same**
                    .FirstOrDefault();

                if (bestAvailableGroup != null)
                {
                    matchedGroups.Add(bestAvailableGroup);
                }
            }

            // 5: Process matched groups
            if (matchedGroups.Count > 0)
            {
                foreach (var match in matchedGroups)
                {
                    try
                    {
                        Room newRoom = _matchFound(match);
                        foreach (var player in match)
                        {
                            players.Remove(player);
                        }
                        Log.Write($"Match created: {newRoom.Name} with {match.Count} players.");
                    }
                    catch (Exception ex)
                    {
                        Log.Write($"Error creating match: {ex.Message}");
                    }
                }
                return true;
            }

            Log.Write($"Matchmaker found only {players.Count}/{MinPlayers} players within skill range ({currentSkillMin} - {currentSkillMax}).");
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
    }
}
