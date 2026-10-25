using Lms.Core.Zones;

namespace Lms.Core.Membranes
{
    /// <summary>Tracks one player against one stasis field: caught, held, out of bounds, let go.</summary>
    /// <remarks>
    ///     Containment belongs to the player, which makes the wall one-way: the collider only
    ///     applies to players already caught. Movement is checked as a segment from the last
    ///     position, so nothing is missed between samples. The wall is passed in each frame (the
    ///     adapter reads the live sphere); a zero radius means the field has gone. One watch per
    ///     player per field.
    /// </remarks>
    public sealed class ContainmentWatch
    {
        private WorldPoint lastPosition;
        private bool contained;
        private bool finished;

        /// <param name="startingAt">
        ///     Where the player was when the field landed; the first frame's movement starts here, so
        ///     a player already inside is caught.
        /// </param>
        public ContainmentWatch(WorldPoint startingAt)
        {
            lastPosition = startingAt;
        }

        /// <summary>Whether the ring is holding this player.</summary>
        public bool IsContained => contained;

        /// <summary>Whether the field has gone, so this watch has nothing left to say.</summary>
        public bool IsFinished => finished;

        /// <param name="position">Where the player is now.</param>
        /// <param name="wall">
        ///     The ring this frame. Zero radius means the field has ended, the only thing that frees anybody.
        /// </param>
        public ContainmentAssessment Update(WorldPoint position, ZoneSphere wall)
        {
            WorldPoint from = lastPosition;
            lastPosition = position;

            if (wall.Radius <= 0)
            {
                // Released is reported once, and only to a player who was being held.
                bool letGo = contained;
                finished = true;
                contained = false;
                return new ContainmentAssessment(letGo ? ContainmentVerdict.Released : ContainmentVerdict.Clear,
                    false, default);
            }

            MembraneCrossing crossing = MembraneCrossing.Through(wall, from, position);

            if (!contained)
            {
                if (!crossing.TouchedTheRing)
                {
                    return new ContainmentAssessment(ContainmentVerdict.Clear, false, crossing);
                }

                // In and out in one frame reports Caught; the escape follows next frame, after the
                // adapter has enabled the collider and dismounted them.
                contained = true;
                return new ContainmentAssessment(ContainmentVerdict.Caught, true, crossing);
            }

            // Still held even when the shrinking wall has closed past them; only the field ending frees anybody.
            return new ContainmentAssessment(
                crossing.EndedInside ? ContainmentVerdict.Held : ContainmentVerdict.Escaped, true, crossing);
        }
    }
}
