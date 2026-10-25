using System;

namespace Lms.Core.Telemetry
{
    /// <summary>
    ///     The server's verbatim reply to one booth command. Replies are written for the operator, so
    ///     the console shows them unchanged.
    /// </summary>
    public readonly struct BoothCommandAnswer
    {
        public BoothCommandAnswer(long id, string verb, string answer)
        {
            if (id <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(id), id,
                    "An answer to no command is one the booth cannot file.");
            }

            Id = id;
            Verb = verb ?? string.Empty;
            Answer = answer ?? string.Empty;
        }

        /// <summary>The command this answers.</summary>
        public long Id { get; }

        /// <summary>The verb that was run, so the console can draw the pair without a lookup.</summary>
        public string Verb { get; }

        /// <summary>What the match said, word for word.</summary>
        public string Answer { get; }

        public override string ToString() => $"#{Id} {Verb}: {Answer}";
    }
}
