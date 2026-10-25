using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Lms.Core.Zones
{
    /// <summary>Every problem with a squeeze, collected in one pass.</summary>
    public sealed class ZoneScheduleValidation
    {
        internal ZoneScheduleValidation(IList<ZoneScheduleProblem> problems)
        {
            Problems = new ReadOnlyCollection<ZoneScheduleProblem>(problems);
        }

        public IReadOnlyList<ZoneScheduleProblem> Problems { get; }

        public bool IsValid => Problems.Count == 0;
    }
}
