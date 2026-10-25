using System;

namespace Lms.Core.Match
{
    /// <summary>Caster settings: what the person filming sees. Every value has a working default.</summary>
    /// <remarks>
    ///     Separate from <c>spectator:</c> because a spectator is a dead contestant and a caster is a
    ///     live non-contestant; only <c>spectator.flySpeed</c> is shared between them.
    /// </remarks>
    public sealed class CasterPolicy
    {
        public CasterPolicy(bool nameplates, double followDistance, double followHeight,
            double followClosest, double followFurthest)
        {
            if (!(followDistance > 0))
            {
                throw new ArgumentOutOfRangeException(nameof(followDistance), followDistance,
                    "A camera cannot open a shot from nowhere. Write metres.");
            }

            if (!(followClosest > 0))
            {
                throw new ArgumentOutOfRangeException(nameof(followClosest), followClosest,
                    "A camera pushed to nothing is a camera inside somebody's head. Write metres.");
            }

            if (!(followFurthest > followClosest))
            {
                throw new ArgumentOutOfRangeException(nameof(followFurthest), followFurthest,
                    "The furthest a following camera may drift has to be further than the nearest " +
                    "it may be pushed.");
            }

            Nameplates = nameplates;
            FollowDistance = followDistance;
            FollowHeight = followHeight;
            FollowClosest = followClosest;
            FollowFurthest = followFurthest;
        }

        /// <summary>Plates on, and a shot the numbers in <see cref="CasterShot"/> chose.</summary>
        public static CasterPolicy Default { get; } = new CasterPolicy(
            nameplates: true,
            followDistance: CasterShot.OpeningDistance,
            followHeight: CasterShot.OpeningHeight,
            followClosest: CasterShot.ClosestKeep,
            followFurthest: CasterShot.FurthestKeep);

        /// <summary>Whether a caster is drawn names and vitals over the contestants.</summary>
        /// <remarks>
        ///     Switches only the drawing: the feed still arrives and <c>lms follow</c> still works.
        /// </remarks>
        public bool Nameplates { get; }

        /// <summary>How far out <c>lms follow</c> opens the shot, in metres.</summary>
        public double FollowDistance { get; }

        /// <summary>How far above the contestant it opens, in metres. Negative films from below.</summary>
        public double FollowHeight { get; }

        /// <summary>The nearest a following camera may be pushed, in metres.</summary>
        public double FollowClosest { get; }

        /// <summary>The furthest it may drift before it is pulled back, in metres.</summary>
        public double FollowFurthest { get; }
    }
}
