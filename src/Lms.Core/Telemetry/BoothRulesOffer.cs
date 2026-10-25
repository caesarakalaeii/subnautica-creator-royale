using System;

namespace Lms.Core.Telemetry
{
    /// <summary>
    ///     A tournament config the casting booth sent back on the reply to a push.
    /// </summary>
    /// <remarks>
    ///     Validation is <c>TournamentConfigLoader</c>'s job and adoption is
    ///     <see cref="Match.MatchHost"/>'s; the booth decides neither.
    /// </remarks>
    public readonly struct BoothRulesOffer
    {
        public BoothRulesOffer(long revision, string yaml, string? by = null)
        {
            if (revision <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(revision), revision,
                    "A revision with no number is one nothing can acknowledge, so it would arrive on every push.");
            }

            if (string.IsNullOrWhiteSpace(yaml))
            {
                throw new ArgumentException("An empty document is not a tournament config.", nameof(yaml));
            }

            Revision = revision;
            Yaml = yaml;
            By = by ?? string.Empty;
        }

        /// <summary>The booth's own revision, counting from one.</summary>
        public long Revision { get; }

        /// <summary>The config document, as somebody typed it.</summary>
        public string Yaml { get; }

        /// <summary>
        ///     Who wrote it, for the audit record. A label, not an identity: the operator token is shared.
        /// </summary>
        public string By { get; }

        public override string ToString() => $"revision {Revision}, {Yaml.Length} bytes";
    }
}
