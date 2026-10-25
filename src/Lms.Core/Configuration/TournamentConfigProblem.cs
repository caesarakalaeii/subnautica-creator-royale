namespace Lms.Core.Configuration
{
    /// <summary>One thing wrong with a config file, tied to the field that caused it.</summary>
    /// <remarks>A value rather than an exception, so every problem can be reported at once.</remarks>
    public sealed class TournamentConfigProblem
    {
        internal TournamentConfigProblem(string path, string message)
        {
            Path = path;
            Message = message;
        }

        /// <summary>The field, as written in the file: <c>match.cycleLength</c>, <c>cull.order[3]</c>.</summary>
        public string Path { get; }

        public string Message { get; }

        public override string ToString() => Path.Length > 0 ? $"{Path}: {Message}" : Message;
    }
}
