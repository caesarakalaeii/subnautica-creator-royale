using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Lms.Core.Telemetry;

namespace Lms.Core.Match
{
    /// <summary>
    ///     Whether a round can be prepared, and who is in it: the decision half of <c>lms round</c>.
    ///     The server does the destructive work, so this refuses up front rather than half-going.
    /// </summary>
    public static class RoundPreparation
    {
        /// <summary>Works out whether to go, and who with.</summary>
        /// <param name="state">Where the match clock is. Anything but the lobby is a refusal.</param>
        /// <param name="connected">Everybody in the world, as the server reads them.</param>
        /// <param name="ghosts">Who is present and not in the match.</param>
        /// <param name="groupSize">How many contestants a group should have.</param>
        /// <param name="forced">
        ///     Whether the operator typed <c>force</c>. Overrides the count and readiness checks only.
        /// </param>
        /// <param name="ready">
        ///     Who has reported the world loaded and precached. Null or empty blocks nobody.
        /// </param>
        /// <param name="bracket">
        ///     Who the bracket puts in the group now playing. Null or empty filters nobody.
        /// </param>
        public static RoundPlan Plan(MatchClockState state, IReadOnlyList<PlayerReading>? connected,
            GhostRoster? ghosts, int groupSize, bool forced, IReadOnlyList<string>? ready = null,
            BracketRoster? bracket = null)
        {
            // Not overridable by `force`: every step is destructive to a match in progress.
            if (state != MatchClockState.NotStarted)
            {
                return RoundPlan.Refused(
                    $"the match is {state}, so a round is already under way. Reset it first, or take " +
                    "the next group, then prepare a round.");
            }

            List<string> contestants = new List<string>();
            List<string> present = new List<string>();

            foreach (PlayerReading reading in connected ?? new PlayerReading[0])
            {
                present.Add(reading.Name);

                if ((ghosts == null || !ghosts.IsGhost(reading.Name)) &&
                    (bracket == null || bracket.Count == 0 || bracket.IsContesting(reading.Name)))
                {
                    contestants.Add(reading.Name);
                }
            }

            // Ordinal so the audit record reads the same on every machine.
            contestants.Sort(StringComparer.Ordinal);

            // Even `force` cannot prepare a round for nobody.
            if (contestants.Count == 0)
            {
                return RoundPlan.Refused(present.Count == 0
                    ? "nobody is connected, so there is no round to prepare."
                    : $"everybody connected is a ghost ({string.Join(", ", present)}), so there is " +
                      "nobody to put in the round.");
            }

            // Null or empty readiness blocks nobody: the check only engages once some client has reported.
            if (ready != null && ready.Count > 0)
            {
                HashSet<string> reported = new HashSet<string>(ready, StringComparer.Ordinal);
                List<string> unready = new List<string>();

                foreach (string contestant in contestants)
                {
                    if (!reported.Contains(contestant))
                    {
                        unready.Add(contestant);
                    }
                }

                // Name them so the operator knows which machine to check.
                if (unready.Count > 0 && !forced)
                {
                    return RoundPlan.Refused(
                        $"{unready.Count} of {contestants.Count} are still loading (no readiness " +
                        $"reported): {string.Join(", ", unready)}. Run `lms round force` to prepare " +
                        "it anyway.");
                }
            }

            if (contestants.Count != groupSize && !forced)
            {
                // Name them so the operator can spot who is missing or extra.
                return RoundPlan.Refused(
                    $"a group is {groupSize} and this counted {contestants.Count}: " +
                    $"{string.Join(", ", contestants)}. Run `lms round force` to prepare it anyway.");
            }

            return RoundPlan.Ready(contestants, forced && contestants.Count != groupSize);
        }
    }

    /// <summary>What <see cref="RoundPreparation.Plan"/> decided.</summary>
    public sealed class RoundPlan
    {
        private static readonly ReadOnlyCollection<string> Nobody =
            new ReadOnlyCollection<string>(new string[0]);

        private RoundPlan(bool refused, string message, IReadOnlyList<string> contestants,
            bool againstTheCount)
        {
            IsRefused = refused;
            Message = message;
            Contestants = contestants is ReadOnlyCollection<string> already
                ? already
                : new ReadOnlyCollection<string>(new List<string>(contestants));
            AgainstTheCount = againstTheCount;
        }

        internal static RoundPlan Refused(string why) => new RoundPlan(true, why, Nobody, false);

        internal static RoundPlan Ready(IReadOnlyList<string> contestants, bool againstTheCount) =>
            new RoundPlan(false, string.Empty, contestants, againstTheCount);

        /// <summary>Whether the verb is going nowhere, and <see cref="Message"/> says why.</summary>
        public bool IsRefused { get; }

        /// <summary>Why it was refused, written for whoever typed it. Empty when it was not.</summary>
        public string Message { get; }

        /// <summary>Who is in the round, in ordinal order. Empty when the plan was refused.</summary>
        public IReadOnlyList<string> Contestants { get; }

        /// <summary>
        ///     Whether this is going ahead with the wrong number of players because somebody forced it,
        ///     so the reply can say so.
        /// </summary>
        public bool AgainstTheCount { get; }

        public override string ToString() =>
            IsRefused ? $"refused: {Message}" : $"{Contestants.Count} contestant(s)";
    }
}
