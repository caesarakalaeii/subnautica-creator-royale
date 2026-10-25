namespace Lms.Core.Telemetry
{
    /// <summary>
    ///     How many items of each queue an accepted console report carried, so each queue is trimmed by
    ///     its own count. A struct so the two counts cannot be swapped.
    /// </summary>
    public readonly struct ConsoleCarried
    {
        public ConsoleCarried(int answers, int rules)
        {
            Answers = answers;
            Rules = rules;
        }

        /// <summary>How many command answers it carried.</summary>
        public int Answers { get; }

        /// <summary>How many config outcomes it carried.</summary>
        public int Rules { get; }

        public override string ToString() => $"{Answers} answer(s), {Rules} outcome(s)";
    }
}
