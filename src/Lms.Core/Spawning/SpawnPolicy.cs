using System;
using Lms.Core.Diagnostics;

namespace Lms.Core.Spawning
{
    /// <summary>Where players start a match.</summary>
    public sealed class SpawnPolicy
    {
        public SpawnPolicy(bool ownPod, double minSeparation, int groupSize = DefaultGroupSize,
            double circleRadius = 0)
        {
            if (!(minSeparation >= 0))
            {
                throw new ArgumentOutOfRangeException(nameof(minSeparation), minSeparation,
                    "A separation is a distance between two pods, zero or more.");
            }

            if (groupSize < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(groupSize), groupSize,
                    "A group has at least one player in it.");
            }

            if (!(circleRadius >= 0) || double.IsInfinity(circleRadius))
            {
                throw new ArgumentOutOfRangeException(nameof(circleRadius), circleRadius,
                    "A circle of pods has a radius in metres, zero or more, and zero means no circle.");
            }

            OwnPod = ownPod;
            MinSeparation = minSeparation;
            GroupSize = groupSize;
            CircleRadius = circleRadius;
        }

        /// <summary>Five players per match.</summary>
        public const int DefaultGroupSize = 5;

        /// <summary>Off, which is stock Nitrox: everybody shares one escape pod.</summary>
        /// <remarks>Off by default so an unset config leaves the Nitrox join path untouched.</remarks>
        public static SpawnPolicy Default { get; } = new SpawnPolicy(false, 0);

        /// <summary>Whether every player gets their own escape pod (and fabricator) rather than sharing one.</summary>
        public bool OwnPod { get; }

        /// <summary>How far apart pods are wanted, in metres. A target, never a veto.</summary>
        /// <remarks>
        ///     When the arena lacks room, players are crowded rather than left without a pod
        ///     (<see cref="SpawnScatter"/>).
        /// </remarks>
        public double MinSeparation { get; }

        /// <summary>How many pods to plan the arena for: the size of a group.</summary>
        /// <remarks>
        ///     Not a cap: extra players are placed from what is left (<see cref="SpawnScatter.Plan"/>).
        /// </remarks>
        public int GroupSize { get; }

        /// <summary>
        ///     How far from the origin to put every pod, or zero to use the game's own start points.
        /// </summary>
        /// <remarks>
        ///     Off by default: a circle can place a pod somewhere that is not water
        ///     (<see cref="SpawnCircle"/>). Choose the radius by testing on the actual world.
        /// </remarks>
        public double CircleRadius { get; }

        /// <summary>Whether the pods go on a circle rather than on the game's own start points.</summary>
        public bool OnACircle => CircleRadius > 0;

        public override string ToString()
        {
            if (!OwnPod)
            {
                return "one pod between everybody";
            }

            return OnACircle
                ? Readable.Line($"a pod each for a group of {GroupSize} ",
                    $"on a circle {CircleRadius:0} m out, {MinSeparation:0} m apart wanted")
                : Readable.Line($"a pod each for a group of {GroupSize}, {MinSeparation:0} m apart");
        }
    }
}
