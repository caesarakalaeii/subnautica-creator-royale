namespace Lms.Core.Zones
{
    /// <summary>One thing wrong with a squeeze, worded so an author can fix it.</summary>
    public sealed class ZoneScheduleProblem
    {
        internal ZoneScheduleProblem(ZoneScheduleProblemKind kind, int phaseNumber, string message)
        {
            Kind = kind;
            PhaseNumber = phaseNumber;
            Message = message;
        }

        public ZoneScheduleProblemKind Kind { get; }

        /// <summary>Which phase it is about, counting from one.</summary>
        public int PhaseNumber { get; }

        public string Message { get; }

        public override string ToString() => $"phase {PhaseNumber}: {Message}";
    }
}
