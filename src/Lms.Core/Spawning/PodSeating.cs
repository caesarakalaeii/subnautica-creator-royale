using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Lms.Core.Telemetry;
using Lms.Core.Zones;

namespace Lms.Core.Spawning
{
    /// <summary>Decides who sits in which pod for <c>lms podspawn</c>.</summary>
    /// <remarks>
    ///     Nitrox assigns a pod only when a player joins
    ///     (<c>EscapePodManager.AssignPlayerToEscapePod</c>), so later groups need re-seating on demand.
    /// </remarks>
    public static class PodSeating
    {
        /// <summary>
        ///     Pairs the players present with the plan's spots: a player whose own pod is on the layout
        ///     stays in it, and everybody else gets the best spot left, in name order.
        /// </summary>
        /// <param name="spots">
        ///     The layout's pods, in the order it hands them out (<see cref="SpawnLayout.Pods"/>).
        /// </param>
        /// <param name="pods">
        ///     Escape pods already in the world and who is assigned to each. A seat names its pod by
        ///     its index in this list.
        /// </param>
        /// <param name="players">
        ///     Who is present, in any order: the contestants of a <see cref="Lms.Core.Match.RoundPlan"/>,
        ///     without ghosts. Anyone passed in is seated.
        /// </param>
        /// <remarks>
        ///     Sorted by name (ordinal), with the id only as a tie-break, so seats do not depend on
        ///     connection order.
        /// </remarks>
        public static PodSeatingPlan Plan(
            IReadOnlyList<WorldPoint> spots,
            IReadOnlyList<PodInTheWorld> pods,
            IReadOnlyList<PlayerReading> players)
        {
            if (spots == null)
            {
                throw new ArgumentNullException(nameof(spots));
            }

            if (pods == null)
            {
                throw new ArgumentNullException(nameof(pods));
            }

            if (players == null)
            {
                throw new ArgumentNullException(nameof(players));
            }

            List<PlayerReading> order = new List<PlayerReading>(players);
            order.Sort((a, b) =>
            {
                int byName = string.CompareOrdinal(a.Name, b.Name);
                return byName != 0 ? byName : a.Id.CompareTo(b.Id);
            });

            List<int> ids = new List<int>(order.Count);
            foreach (PlayerReading player in order)
            {
                ids.Add(player.Id);
            }

            return SeatsFor(spots, pods, ids);
        }

        /// <summary>Seats players, already in name order, onto spots and pods.</summary>
        /// <remarks>
        ///     A player keeps their own pod when it is on the layout: a client only treats the pod it
        ///     adopted at spawn (<c>EscapePodEntitySpawner.IsOurs</c>) as its lifepod, and reassigning
        ///     it later does not move that. A pod owned by someone present is never given to someone
        ///     else; an unowned pod on a spot is reused, otherwise a new one is made.
        /// </remarks>
        private static PodSeatingPlan SeatsFor(
            IReadOnlyList<WorldPoint> spots,
            IReadOnlyList<PodInTheWorld> pods,
            List<int> inOrder)
        {
            int wide = Math.Min(inOrder.Count, spots.Count);
            int[] spotOf = new int[wide];
            int?[] podOf = new int?[wide];
            bool[] spotTaken = new bool[spots.Count];
            bool[] podTaken = new bool[pods.Count];

            for (int seat = 0; seat < wide; seat++)
            {
                spotOf[seat] = -1;

                for (int pod = 0; pod < pods.Count && spotOf[seat] < 0; pod++)
                {
                    if (podTaken[pod] || !pods[pod].Players.Contains(inOrder[seat]))
                    {
                        continue;
                    }

                    int spot = FreeSpotAt(spots, spotTaken, pods[pod].Where);

                    if (spot >= 0)
                    {
                        spotOf[seat] = spot;
                        podOf[seat] = pod;
                        spotTaken[spot] = true;
                        podTaken[pod] = true;
                    }
                }
            }

            List<PodSeat> seats = new List<PodSeat>(wide);
            int toMake = 0;

            for (int seat = 0; seat < wide; seat++)
            {
                if (spotOf[seat] < 0)
                {
                    // Always found: there are at least as many spots as seats.
                    spotOf[seat] = Array.IndexOf(spotTaken, false);
                    spotTaken[spotOf[seat]] = true;
                    podOf[seat] = Spare(pods, podTaken, spots[spotOf[seat]], inOrder);

                    if (podOf[seat] is int spare)
                    {
                        podTaken[spare] = true;
                    }
                }

                if (podOf[seat] == null)
                {
                    toMake++;
                }

                seats.Add(new PodSeat(inOrder[seat], spots[spotOf[seat]], podOf[seat]));
            }

            return new PodSeatingPlan(seats, inOrder.Count - wide, toMake);
        }

        private static int FreeSpotAt(IReadOnlyList<WorldPoint> spots, bool[] taken, WorldPoint pod)
        {
            for (int spot = 0; spot < spots.Count; spot++)
            {
                if (!taken[spot] && PodSpot.IsAt(spots[spot], pod))
                {
                    return spot;
                }
            }

            return -1;
        }

        /// <summary>A pod on this spot that nobody present owns and nobody has been given yet.</summary>
        private static int? Spare(IReadOnlyList<PodInTheWorld> pods, bool[] taken, WorldPoint spot,
            List<int> present)
        {
            for (int pod = 0; pod < pods.Count; pod++)
            {
                if (!taken[pod] && PodSpot.IsAt(pods[pod].Where, spot) && !pods[pod].Players.Any(present.Contains))
                {
                    return pod;
                }
            }

            return null;
        }
    }

    /// <summary>What a <c>podspawn</c> or <c>podclear</c> actually did, as reported by the adapter.</summary>
    /// <remarks>
    ///     Can differ from the plan, e.g. when a player disconnects mid-command; the log records this
    ///     outcome.
    /// </remarks>
    public sealed class PodCommandOutcome
    {
        private static readonly IReadOnlyList<string> Nobody = new string[0];

        public PodCommandOutcome(int changed, string message, string? detail = null,
            IReadOnlyList<string>? seated = null, IReadOnlyList<string>? unseated = null)
        {
            if (changed < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(changed), changed,
                    "A number of pods or players changed is nought or more.");
            }

            Changed = changed;
            Message = message ?? string.Empty;
            Detail = detail;
            Seated = Copy(seated);
            Unseated = Copy(unseated);
        }

        public static PodCommandOutcome Nothing(string message) => new PodCommandOutcome(0, message);

        /// <summary>How many players were seated or pods removed. Zero writes no record.</summary>
        public int Changed { get; }

        /// <summary>The line to say back to whoever typed the verb.</summary>
        public string Message { get; }

        /// <summary>What the audit record's <c>Detail</c> says, or null for none.</summary>
        public string? Detail { get; }

        /// <summary>Who got a spot, by name.</summary>
        /// <remarks>
        ///     Lets the round check the roster the seating ran on. Empty for <c>podclear</c> or a
        ///     <c>podspawn</c> that refused before planning.
        /// </remarks>
        public IReadOnlyList<string> Seated { get; }

        /// <summary>Who was in the plan but got no spot, and was left where they were.</summary>
        public IReadOnlyList<string> Unseated { get; }

        /// <summary>The round's contestants that are in neither list.</summary>
        /// <remarks>A non-empty answer means the seating ran on a different roster; the caller refuses the round.</remarks>
        public IReadOnlyList<string> Missing(IReadOnlyList<string> contestants)
        {
            if (contestants == null)
            {
                throw new ArgumentNullException(nameof(contestants));
            }

            List<string> missing = new List<string>();

            foreach (string contestant in contestants)
            {
                if (!AccountedFor(Seated, contestant) && !AccountedFor(Unseated, contestant))
                {
                    missing.Add(contestant);
                }
            }

            return missing;
        }

        private static bool AccountedFor(IReadOnlyList<string> names, string contestant)
        {
            foreach (string name in names)
            {
                if (name == contestant)
                {
                    return true;
                }
            }

            return false;
        }

        private static IReadOnlyList<string> Copy(IReadOnlyList<string>? names) =>
            names == null || names.Count == 0 ? Nobody : new List<string>(names);
    }

    /// <summary>One player, the spot they are going to, and whether a pod has to be made on it.</summary>
    public readonly struct PodSeat : IEquatable<PodSeat>
    {
        public PodSeat(int player, WorldPoint spot, int? pod)
        {
            Player = player;
            Spot = spot;
            Pod = pod;
        }

        /// <summary>Nitrox's own id for the player.</summary>
        public int Player { get; }

        /// <summary>Where their pod should be.</summary>
        public WorldPoint Spot { get; }

        /// <summary>Index of the existing pod they sit in, or null when one has to be made.</summary>
        public int? Pod { get; }

        /// <summary>Whether a pod has to be made before they sit in it.</summary>
        public bool NeedsAPod => Pod == null;

        public bool Equals(PodSeat other) =>
            Player == other.Player && Spot.Equals(other.Spot) && Pod == other.Pod;

        public override bool Equals(object? obj) => obj is PodSeat other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Player;
                hash = hash * 397 ^ Spot.GetHashCode();
                return hash * 397 ^ Pod.GetHashCode();
            }
        }

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "player {0} to {1}{2}",
                Player, Spot, NeedsAPod ? " (new pod)" : string.Empty);
    }

    /// <summary>What <c>lms podspawn</c> is about to do, and what it cannot.</summary>
    public sealed class PodSeatingPlan
    {
        internal PodSeatingPlan(IReadOnlyList<PodSeat> seats, int unseated, int podsToMake)
        {
            Seats = seats;
            Unseated = unseated;
            PodsToMake = podsToMake;
        }

        /// <summary>One per player who has a spot, in the order the spots are handed out.</summary>
        public IReadOnlyList<PodSeat> Seats { get; }

        /// <summary>How many players the plan had no spot for; they are left where they are.</summary>
        public int Unseated { get; }

        /// <summary>How many seats have no pod for them yet, so one is made.</summary>
        public int PodsToMake { get; }

        public override string ToString()
        {
            if (Seats.Count == 0)
            {
                return Unseated == 0
                    ? "nobody to seat"
                    : Readable(Unseated, "player with nowhere to go", "players with nowhere to go");
            }

            string line = string.Format(CultureInfo.InvariantCulture,
                "{0} player(s) to their pods, {1} of which had to be made", Seats.Count, PodsToMake);

            return Unseated == 0
                ? line
                : string.Format(CultureInfo.InvariantCulture,
                    "{0}; {1} left where they were, because the plan is {2} spots wide",
                    line, Unseated, Seats.Count);
        }

        private static string Readable(int count, string one, string many) =>
            string.Format(CultureInfo.InvariantCulture, "{0} {1}", count, count == 1 ? one : many);
    }
}
