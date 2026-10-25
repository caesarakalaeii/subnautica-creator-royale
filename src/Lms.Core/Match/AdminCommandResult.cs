using System.Collections.Generic;

namespace Lms.Core.Match
{
    /// <summary>
    ///     What running an admin command did, and the sentence to say about it. The message lives in
    ///     Core so the console, the settings buttons and the audit log all say the same thing.
    /// </summary>
    public readonly struct AdminCommandResult
    {
        internal AdminCommandResult(bool applied, string message, IReadOnlyList<MatchClockEvent> events)
        {
            Applied = applied;
            Message = message;
            Events = events;
        }

        /// <summary>Whether the match moved. False covers both a refusal and a question.</summary>
        public bool Applied { get; }

        /// <summary>What happened, or why it did not, in one line an operator can read mid-match.</summary>
        public string Message { get; }

        /// <summary>
        ///     Clock events the command caused, to apply exactly as if they had fallen due on their
        ///     own. Only a skip produces any.
        /// </summary>
        public IReadOnlyList<MatchClockEvent> Events { get; }

        public override string ToString() => Message;
    }
}
