using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace Lms.Core.Zones
{
    /// <summary>
    ///     A squeeze that does not know where it is going yet: the opening sphere, the shape of each
    ///     phase, and a pool of destinations one of which is drawn per match.
    /// </summary>
    /// <remarks>
    ///     Pacing is authored once and only the direction is drawn. Each phase moves in proportion to
    ///     its own shrink, so the moves sum to <see cref="Reach"/> and every sphere stays inside its
    ///     predecessor by construction; destinations beyond <see cref="Reach"/> are refused at load.
    ///     The draw is a pure function of the draw count and a seed, so it replays from the audit log.
    /// </remarks>
    public sealed class ZoneSqueeze
    {
        private readonly IReadOnlyList<ZonePhaseShape> shapes;
        private readonly IReadOnlyList<ZoneDestination> destinations;

        /// <param name="opening">Where the wall stands before the first phase fires.</param>
        /// <param name="openingDamagePerTick">What it costs to be outside the opening sphere.</param>
        /// <param name="phases">The shape of each phase, widest first. Centres are not authored.</param>
        /// <param name="pool">Everywhere a match may be made to finish. One is drawn per match.</param>
        /// <param name="listed">Play the pool in its own order rather than drawing from it.</param>
        public ZoneSqueeze(
            ZoneSphere opening,
            double openingDamagePerTick,
            IReadOnlyList<ZonePhaseShape> phases,
            IReadOnlyList<ZoneDestination> pool,
            bool listed = false)
        {
            if (phases == null)
            {
                throw new ArgumentNullException(nameof(phases));
            }

            if (pool == null)
            {
                throw new ArgumentNullException(nameof(pool));
            }

            if (phases.Count == 0)
            {
                throw new ArgumentException("A zone that never shrinks is not a squeeze; the match would never end.", nameof(phases));
            }

            if (pool.Count == 0)
            {
                throw new ArgumentException("A pool with nothing in it cannot be drawn from. Author a destination, or author the phase centres instead.", nameof(pool));
            }

            double previousRadius = opening.Radius;
            foreach (ZonePhaseShape shape in phases)
            {
                if (shape == null)
                {
                    throw new ArgumentException("A phase is missing.", nameof(phases));
                }

                if (shape.Radius >= previousRadius)
                {
                    throw new ArgumentException(string.Format(CultureInfo.InvariantCulture,
                        "the radius goes from {0:0} m to {1:0} m, so this phase squeezes nobody.",
                        previousRadius, shape.Radius), nameof(phases));
                }

                previousRadius = shape.Radius;
            }

            Opening = opening;
            OpeningDamagePerTick = openingDamagePerTick;
            this.shapes = new ReadOnlyCollection<ZonePhaseShape>(new List<ZonePhaseShape>(phases));

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ZoneDestination destination in pool)
            {
                if (destination == null)
                {
                    throw new ArgumentException("A destination is missing.", nameof(pool));
                }

                if (!seen.Add(destination.Name))
                {
                    throw new ArgumentException(string.Format(CultureInfo.InvariantCulture,
                        "two destinations are called \"{0}\". The draw is recorded by name, so a tie in the log cannot be settled.",
                        destination.Name), nameof(pool));
                }

                if (!CanReach(destination))
                {
                    throw new ArgumentException(string.Format(CultureInfo.InvariantCulture,
                        "\"{0}\" is {1:0} m outside what this squeeze can reach: it sits {2:0} m from the opening centre, and a {3:0} m final sphere leaves only {4:0} m of travel. Move the opening centre nearer it, widen the opening, or cut the final radius.",
                        destination.Name, Overhang(destination),
                        Opening.Centre.DistanceTo(destination.Centre), FinalRadius, Reach), nameof(pool));
                }
            }

            this.destinations = new ReadOnlyCollection<ZoneDestination>(new List<ZoneDestination>(pool));
            IsListed = listed;
        }

        /// <summary>
        ///     Whether group N plays the Nth destination rather than a shuffled one; the file is
        ///     written easy to hard, so later groups play harder rounds.
        /// </summary>
        public bool IsListed { get; }

        /// <summary>Where the wall stands before the first phase fires.</summary>
        public ZoneSphere Opening { get; }

        /// <summary>What it costs to be outside the opening sphere.</summary>
        public double OpeningDamagePerTick { get; }

        /// <summary>Everywhere a match may be made to finish.</summary>
        public IReadOnlyList<ZoneDestination> Destinations => destinations;

        /// <summary>How many phases the file wrote, which is how many cycles a match runs before the collapse.</summary>
        public int AuthoredPhaseCount => shapes.Count;

        /// <summary>How wide the last authored sphere is, before the collapse closes it to nothing.</summary>
        public double FinalRadius => shapes[shapes.Count - 1].Radius;

        /// <summary>
        ///     How far from the opening centre a destination may sit. The whole squeeze is a walk of
        ///     this length, spent a phase at a time.
        /// </summary>
        public double Reach => Opening.Radius - FinalRadius;

        /// <summary>Whether the final sphere could sit on this destination and still be inside the opening.</summary>
        public bool CanReach(ZoneDestination destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            return Opening.Contains(new ZoneSphere(destination.Centre, FinalRadius));
        }

        /// <summary>By how much a destination misses, in metres. Zero or less when it is reachable.</summary>
        public double Overhang(ZoneDestination destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            return Opening.Centre.DistanceTo(destination.Centre) + FinalRadius - Opening.Radius;
        }

        /// <summary>The squeeze this match runs: the authored shapes, walked towards one destination.</summary>
        public ZoneSchedule Toward(ZoneDestination destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            List<ZonePhase> phases = new List<ZonePhase>(shapes.Count);

            // Each phase moves by the share of the walk its own shrink buys, so the next sphere
            // stays visible from the current one all match.
            double travel = Reach;
            double spent = 0;
            double previousRadius = Opening.Radius;

            for (int i = 0; i < shapes.Count; i++)
            {
                ZonePhaseShape shape = shapes[i];
                spent += previousRadius - shape.Radius;

                // Pin the last phase exactly on the destination, avoiding rounding drift.
                WorldPoint centre = i == shapes.Count - 1
                    ? destination.Centre
                    : Opening.Centre.Towards(destination.Centre, spent / travel);

                phases.Add(new ZonePhase(i + 1, new ZoneSphere(centre, shape.Radius), shape.ShrinkFor, shape.DamagePerTick));
                previousRadius = shape.Radius;
            }

            return new ZoneSchedule(Opening, OpeningDamagePerTick, phases);
        }

        /// <summary>Which destination the next match gets.</summary>
        /// <param name="alreadyDrawn">
        ///     How many draws this tournament has already made: the count of
        ///     <c>ZoneDestinationDrawn</c> records in the audit log. Zero for the first match.
        /// </param>
        /// <param name="seed">
        ///     The tournament's seed, recorded in the log, so the order is not predictable in advance
        ///     but replays exactly.
        /// </param>
        /// <remarks>No repeat until the pool is exhausted, then a fresh shuffle.</remarks>
        public ZoneDestination Draw(int alreadyDrawn, int seed)
        {
            if (alreadyDrawn < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(alreadyDrawn), alreadyDrawn,
                    "A match cannot have drawn a negative number of destinations.");
            }

            int count = destinations.Count;

            // Past the end of a listed order, repeat the hardest (last) destination.
            if (IsListed)
            {
                return destinations[Math.Min(alreadyDrawn, count - 1)];
            }

            int pass = alreadyDrawn / count;
            int within = alreadyDrawn % count;

            int[] order = ShuffledOrder(count, Mix((ulong)(uint)seed, (ulong)(uint)pass));

            return destinations[order[within]];
        }

        /// <summary>Fisher-Yates over a stable generator.</summary>
        /// <remarks>
        ///     SplitMix64 rather than <see cref="Random"/>, whose algorithm differs across .NET
        ///     versions and would break replay from the log.
        /// </remarks>
        private static int[] ShuffledOrder(int count, ulong state)
        {
            int[] order = new int[count];
            for (int i = 0; i < count; i++)
            {
                order[i] = i;
            }

            for (int i = count - 1; i > 0; i--)
            {
                state = Mix(state, 0);
                int j = (int)(state % (ulong)(i + 1));
                int swap = order[i];
                order[i] = order[j];
                order[j] = swap;
            }

            return order;
        }

        private static ulong Mix(ulong state, ulong salt)
        {
            unchecked
            {
                ulong z = state + salt + 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }
    }
}
