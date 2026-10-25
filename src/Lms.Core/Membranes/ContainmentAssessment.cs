namespace Lms.Core.Membranes
{
    /// <summary>What the ring says about one player at one moment, and the geometry behind it.</summary>
    public readonly struct ContainmentAssessment
    {
        internal ContainmentAssessment(ContainmentVerdict verdict, bool isContained, MembraneCrossing crossing)
        {
            Verdict = verdict;
            IsContained = isContained;
            Crossing = crossing;
        }

        public ContainmentVerdict Verdict { get; }

        /// <summary>
        ///     Whether the ring is holding this player: from the frame they are caught until the
        ///     field ends, even while out of bounds.
        /// </summary>
        public bool IsContained { get; }

        /// <summary>
        ///     Where this frame's movement entered and left the wall: the catch happens at the
        ///     entry, recovery starts from the exit.
        /// </summary>
        public MembraneCrossing Crossing { get; }

        public override string ToString() => Verdict.ToString();
    }
}
