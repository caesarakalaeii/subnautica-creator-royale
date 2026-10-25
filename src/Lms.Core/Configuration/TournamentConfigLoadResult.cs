using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Lms.Core.Configuration
{
    /// <summary>The outcome of reading a config file: either rules, or every reason there are none.</summary>
    public sealed class TournamentConfigLoadResult
    {
        internal TournamentConfigLoadResult(TournamentConfig? config, IList<TournamentConfigProblem> problems)
        {
            Config = config;
            Problems = new ReadOnlyCollection<TournamentConfigProblem>(problems);
        }

        /// <summary>The rules, or null when anything at all was wrong.</summary>
        public TournamentConfig? Config { get; }

        public IReadOnlyList<TournamentConfigProblem> Problems { get; }

        public bool IsValid => Config != null && Problems.Count == 0;
    }
}
