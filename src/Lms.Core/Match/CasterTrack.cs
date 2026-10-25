using System;
using System.Collections.Generic;
using Lms.Core.Zones;

namespace Lms.Core.Match
{
    /// <summary>
    ///     Keeps a caster's plates on the players by interpolating each one between its last two
    ///     sightings, one server tick behind the wall clock.
    /// </summary>
    /// <remarks>
    ///     Never runs past the newest sample, so nothing snaps back. A jump faster than
    ///     <see cref="FastestPlausible"/> is a teleport and is not swept. Names missing from the
    ///     newest feed are dropped.
    /// </remarks>
    public sealed class CasterTrack
    {
        /// <summary>How far the render clock runs behind the wall clock, in seconds: one server tick.</summary>
        /// <remarks>
        ///     Must equal the server's <c>LmsTournament.TickIntervalSeconds</c>; nothing enforces it, so
        ///     change both together.
        /// </remarks>
        public const double MaxLeadSeconds = 0.25;

        /// <summary>
        ///     The fastest anything in this game plausibly moves, in metres a second (a Seamoth is under
        ///     twenty). Anything faster is a teleport, such as <c>lms round</c> seating.
        /// </summary>
        public const double FastestPlausible = 30;

        private readonly Dictionary<string, Trail> trails =
            new Dictionary<string, Trail>(StringComparer.OrdinalIgnoreCase);

        private readonly List<string> names = new List<string>();

        /// <summary>Everybody the newest feed named, in the server's stable order.</summary>
        public IReadOnlyList<string> Names => names;

        /// <summary>Takes one feed, and forgets anybody it does not mention.</summary>
        public void Receive(CasterFeed feed, TimeSpan at)
        {
            names.Clear();

            Dictionary<string, Trail> updated =
                new Dictionary<string, Trail>(StringComparer.OrdinalIgnoreCase);

            foreach (CasterSighting one in feed.Seen)
            {
                Trail trail = trails.TryGetValue(one.Name, out Trail existing)
                    ? existing.Then(one, at)
                    : Trail.Starting(one, at);

                // A name repeated within one feed is one player: the first row wins.
                if (updated.ContainsKey(one.Name))
                {
                    continue;
                }

                updated[one.Name] = trail;
                names.Add(one.Name);
            }

            trails.Clear();
            foreach (KeyValuePair<string, Trail> pair in updated)
            {
                trails[pair.Key] = pair.Value;
            }
        }

        /// <summary>
        ///     Where somebody probably is, one tick behind the wall clock, with their newest vitals
        ///     (never interpolated). Names match case-insensitively since an operator typed them.
        /// </summary>
        public bool TryFind(string? name, TimeSpan now, out CasterSighting sighting)
        {
            sighting = default;

            if (string.IsNullOrWhiteSpace(name) || !trails.TryGetValue(name!, out Trail trail))
            {
                return false;
            }

            sighting = trail.At(now - TimeSpan.FromSeconds(MaxLeadSeconds));

            return true;
        }

        /// <summary>The last two things the feed said about one player.</summary>
        private readonly struct Trail
        {
            private readonly CasterSighting newest;
            private readonly TimeSpan newestAt;
            private readonly WorldPoint before;
            private readonly TimeSpan beforeAt;
            private readonly bool hasPair;

            private Trail(CasterSighting newest, TimeSpan newestAt, WorldPoint before,
                TimeSpan beforeAt, bool hasPair)
            {
                this.newest = newest;
                this.newestAt = newestAt;
                this.before = before;
                this.beforeAt = beforeAt;
                this.hasPair = hasPair;
            }

            /// <summary>A player with one sample, who is drawn where they were and carried nowhere.</summary>
            internal static Trail Starting(CasterSighting seen, TimeSpan at) =>
                new Trail(seen, at, seen.Where, at, hasPair: false);

            internal Trail Then(CasterSighting seen, TimeSpan at) =>
                new Trail(seen, at, newest.Where, newestAt, hasPair: true);

            internal CasterSighting At(TimeSpan render)
            {
                return new CasterSighting(newest.Name, Sweep(render), newest.Health,
                    newest.Oxygen, newest.Alive);
            }

            private WorldPoint Sweep(TimeSpan render)
            {
                if (!hasPair)
                {
                    return newest.Where;
                }

                double span = (newestAt - beforeAt).TotalSeconds;
                if (span <= 0)
                {
                    // Two feeds with the same timestamp; dividing would put NaN on a camera transform.
                    return newest.Where;
                }

                // One tick behind, the render clock is still inside the segment when the next sample arrives.
                if (render >= newestAt)
                {
                    return newest.Where;
                }

                if (render <= beforeAt)
                {
                    return before;
                }

                double dx = newest.Where.X - before.X;
                double dy = newest.Where.Y - before.Y;
                double dz = newest.Where.Z - before.Z;

                // Divide by at least one tick: arrival jitter would otherwise make a Seamoth look like a teleport.
                if (Math.Sqrt(dx * dx + dy * dy + dz * dz) / Math.Max(span, MaxLeadSeconds) > FastestPlausible)
                {
                    // A teleport, not a swim.
                    return newest.Where;
                }

                double t = (render - beforeAt).TotalSeconds / span;

                return new WorldPoint(
                    before.X + dx * t,
                    before.Y + dy * t,
                    before.Z + dz * t);
            }
        }
    }
}
