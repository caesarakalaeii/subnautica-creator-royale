using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using Lms.Core.Zones;

namespace Lms.Core.Spawning
{
    /// <summary>Where a whole group's escape pods go, planned at once.</summary>
    /// <remarks>
    ///     Pods are in hand-out order, each the furthest from all before it, so any prefix is as
    ///     spread out as possible.
    /// </remarks>
    public sealed class SpawnLayout
    {
        private readonly ReadOnlyCollection<WorldPoint> pods;

        internal SpawnLayout(IReadOnlyList<WorldPoint> pods, int wanted, double separation, double target)
        {
            this.pods = new ReadOnlyCollection<WorldPoint>(new List<WorldPoint>(pods));
            Wanted = wanted;
            Separation = separation;
            Target = target;
        }

        /// <summary>The spots, in the order they should be handed out.</summary>
        public IReadOnlyList<WorldPoint> Pods => pods;

        /// <summary>How many were asked for, which may exceed how many the arena had room for.</summary>
        public int Wanted { get; }

        /// <summary>
        ///     Distance between the closest two points in the layout, counting pods already in the
        ///     world. Zero when there is nothing to compare.
        /// </summary>
        public double Separation { get; }

        /// <summary>What was wanted, from <c>spawn.minSeparation</c>.</summary>
        public double Target { get; }

        /// <summary>Whether the arena had room for the whole group at the separation asked for.</summary>
        /// <remarks>False is a warning only; it never stops anybody spawning.</remarks>
        public bool MeetsTarget =>
            pods.Count == Wanted && (Separation >= Target || Separation == 0);

        public override string ToString() =>
            pods.Count == 0
                ? "no pods planned"
                : string.Format(CultureInfo.InvariantCulture,
                    "{0} pods, closest two {1:0} m apart against a target of {2:0} m",
                    pods.Count, Separation, Target);
    }
}
