using System;
using System.Collections.Generic;
using System.Globalization;
using Lms.Core.Zones;

namespace Lms.Core.Spawning
{
    /// <summary>Decides which escape pods are derelicts and safe to remove.</summary>
    /// <remarks>
    ///     Changing <c>spawn.circleRadius</c> leaves the previous circle's pods behind, and they
    ///     count towards the separation the layout reports.
    /// </remarks>
    public static class PodClearing
    {
        /// <summary>How close a player has to be to count as being in a pod, in metres.</summary>
        /// <remarks>
        ///     A lifepod is about five metres across; ten errs towards keeping a pod rather than
        ///     deleting one around a player.
        /// </remarks>
        public const double InsideMetres = 10;

        /// <summary>Says which of the world's pods are derelicts.</summary>
        /// <param name="spots">The layout's spots; a pod on one of these is not a derelict.</param>
        /// <param name="pods">Every escape pod in the world, in the caller's own order.</param>
        /// <param name="players">Where the connected players are.</param>
        /// <remarks>
        ///     A pod is a derelict only when it is off the layout, nobody is assigned to it (absent
        ///     players keep their assignment) and nobody is standing in it (a client treats an
        ///     unassigned pod as its own). Stowed items are cleared too, but counted. An empty layout
        ///     clears nothing, since every pod would look off-plan.
        /// </remarks>
        public static PodClearingPlan Plan(
            IReadOnlyList<WorldPoint> spots,
            IReadOnlyList<PodInTheWorld> pods,
            IReadOnlyList<WorldPoint> players)
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

            if (spots.Count == 0)
            {
                return new PodClearingPlan(NoPods, pods.Count, 0, 0, NoPods, noLayout: true);
            }

            List<int> clear = new List<int>();
            int inside = 0;
            int stowed = 0;

            for (int index = 0; index < pods.Count; index++)
            {
                PodInTheWorld pod = pods[index];

                if (PodSpot.AnyAt(spots, pod.Where) || pod.Players.Count > 0)
                {
                    continue;
                }

                if (SomebodyIn(pod, players))
                {
                    inside++;
                    continue;
                }

                stowed += pod.StowedItems;
                clear.Add(index);
            }

            return new PodClearingPlan(clear, pods.Count - clear.Count, stowed, inside, NoPods,
                noLayout: false);
        }

        /// <summary>
        ///     Says to remove every escape pod in the world, or refuses because somebody is in one
        ///     (<c>lms podclear all</c>).
        /// </summary>
        /// <param name="pods">Every escape pod in the world, in the caller's own order.</param>
        /// <param name="players">Where the connected players are.</param>
        /// <remarks>
        ///     Ignores the layout and assignments. Anybody standing in a pod refuses the whole sweep
        ///     and is named in <see cref="PodClearingPlan.Occupants"/>, so nothing is half-removed.
        /// </remarks>
        public static PodClearingPlan PlanAll(
            IReadOnlyList<PodInTheWorld> pods,
            IReadOnlyList<WorldPoint> players)
        {
            if (pods == null)
            {
                throw new ArgumentNullException(nameof(pods));
            }

            if (players == null)
            {
                throw new ArgumentNullException(nameof(players));
            }

            List<int> occupants = new List<int>();

            for (int player = 0; player < players.Count; player++)
            {
                if (InAnyPod(players[player], pods))
                {
                    occupants.Add(player);
                }
            }

            if (occupants.Count > 0)
            {
                return new PodClearingPlan(NoPods, pods.Count, 0, occupants.Count, occupants,
                    noLayout: false);
            }

            List<int> clear = new List<int>(pods.Count);
            int stowed = 0;

            for (int index = 0; index < pods.Count; index++)
            {
                stowed += pods[index].StowedItems;
                clear.Add(index);
            }

            return new PodClearingPlan(clear, 0, stowed, 0, NoOccupants, noLayout: false);
        }

        private static readonly int[] NoPods = new int[0];

        private static readonly int[] NoOccupants = new int[0];

        private static bool SomebodyIn(PodInTheWorld pod, IReadOnlyList<WorldPoint> players)
        {
            foreach (WorldPoint player in players)
            {
                if (player.DistanceTo(pod.Where) <= InsideMetres)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool InAnyPod(WorldPoint player, IReadOnlyList<PodInTheWorld> pods)
        {
            foreach (PodInTheWorld pod in pods)
            {
                if (player.DistanceTo(pod.Where) <= InsideMetres)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>One escape pod in the world: where it is, who is assigned to it and what it holds.</summary>
    /// <remarks>
    ///     The caller identifies a pod by its index in the list it handed over. Assigned players
    ///     matter because a pod given to someone other than its owner renders as a wreck for them.
    /// </remarks>
    public readonly struct PodInTheWorld
    {
        public PodInTheWorld(WorldPoint where, IReadOnlyList<int> players, int stowedItems)
        {
            if (players == null)
            {
                throw new ArgumentNullException(nameof(players));
            }

            if (stowedItems < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(stowedItems), stowedItems,
                    "A pod holds nought or more things.");
            }

            Where = where;
            Players = players;
            StowedItems = stowedItems;
        }

        public WorldPoint Where { get; }

        /// <summary>The ids of the players assigned to it, present or not.</summary>
        public IReadOnlyList<int> Players { get; }

        /// <summary>How many things are stowed in it, anywhere in its hierarchy.</summary>
        public int StowedItems { get; }

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "{0}, {1} player(s), {2} thing(s)",
                Where, Players.Count, StowedItems);
    }

    /// <summary>What <c>lms podclear</c> is about to take out, and what it is leaving alone.</summary>
    public sealed class PodClearingPlan
    {
        internal PodClearingPlan(IReadOnlyList<int> clear, int kept, int stowedItems, int somebodyInside,
            IReadOnlyList<int> occupants, bool noLayout)
        {
            Clear = clear;
            Kept = kept;
            StowedItems = stowedItems;
            SomebodyInside = somebodyInside;
            Occupants = occupants;
            NoLayout = noLayout;
        }

        /// <summary>Which pods to take out, as indices into the list that was handed in.</summary>
        public IReadOnlyList<int> Clear { get; }

        /// <summary>How many pods are staying.</summary>
        public int Kept { get; }

        /// <summary>How many stowed items are removed with them.</summary>
        public int StowedItems { get; }

        /// <summary>How many were kept only because somebody was standing in them.</summary>
        public int SomebodyInside { get; }

        /// <summary>Players blocking the sweep, as indices into the roster that was handed in.</summary>
        /// <remarks>Only <see cref="PodClearing.PlanAll"/> fills this in.</remarks>
        public IReadOnlyList<int> Occupants { get; }

        /// <summary>Whether the layout had no spots, in which case nothing was cleared.</summary>
        public bool NoLayout { get; }

        public override string ToString()
        {
            if (NoLayout)
            {
                return "the placement planned no pods, so nothing here is a derelict";
            }

            if (Occupants.Count > 0)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "{0} player(s) are standing in a pod, so nothing was removed", Occupants.Count);
            }

            if (Clear.Count == 0)
            {
                return Kept == 0
                    ? "no escape pods in the world at all"
                    : string.Format(CultureInfo.InvariantCulture,
                        "no derelicts: all {0} pod(s) are the layout, somebody's, or occupied", Kept);
            }

            string line = string.Format(CultureInfo.InvariantCulture,
                "{0} derelict pod(s) to take out, {1} staying", Clear.Count, Kept);

            if (StowedItems > 0)
            {
                line += string.Format(CultureInfo.InvariantCulture,
                    ", and {0} stowed thing(s) go with them", StowedItems);
            }

            return line;
        }
    }
}
