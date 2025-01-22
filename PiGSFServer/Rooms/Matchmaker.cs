using System.Text;
using PiGSF.Server;
using System.Text.Json.Nodes;
using RazorGenerator.Templating;
using System.Diagnostics;

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
        int MissCount = 0; // The more ticks, the big this value becomes, altering the SkillMinDistance / SkillMaxDistance tollerance
        bool MatchmakerTick()
        {
            if (players.Count < MinPlayers)
                return false; // Not enough players to form a match

            // Determine if timeout occurred
            bool isTimeOut = timer.Elapsed.TotalSeconds > maxWait;

            // Adjust skill range based on miss count and timeout status
            int currentSkillMin = skillMin + MissCount;
            int currentSkillMax = Math.Min(skillMax, skillMin + MissCount);

            // If timeout occurs and NoSkillGapOnTimeout is true, allow matching regardless of skill gaps
            if (isTimeOut && _noGapOnTimeout)
            {
                currentSkillMin = int.MinValue; // No lower limit
                currentSkillMax = int.MaxValue; // No upper limit
            }

            // Match players
            List<Player> matchedPlayers;
            if (_skillFunc == null)
            {
                // Randomly match players
                matchedPlayers = players.OrderBy(_ => Guid.NewGuid()).Take(MinPlayers).ToList();
            }
            else
            {
                // Match players based on skill within the allowed range
                matchedPlayers = players
                    .Where(p => _skillFunc(p) >= currentSkillMin && _skillFunc(p) <= currentSkillMax)
                    .Take(MinPlayers)
                    .ToList();
            }

            // If enough players are matched, create a room
            if (matchedPlayers.Count >= MinPlayers)
            {
                // Create a new room using the provided factory delegate
                Room newRoom = _matchFound(matchedPlayers);

                // Remove matched players from the queue
                foreach (var player in matchedPlayers)
                {
                    players.Remove(player);
                }

                Log.Write($"Match created: {newRoom.Name} with {matchedPlayers.Count} players.");
                return true; // Match successfully created
            }

            return false; // No match was made
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
            // MM can be forced from console / management eventually
        }

        protected override void OnShutdownRequested()
        {
            base.OnShutdownRequested();
            eligibleForDeletion = true; // Matchmakers become eligible for instant deletion
        }
    }
}
