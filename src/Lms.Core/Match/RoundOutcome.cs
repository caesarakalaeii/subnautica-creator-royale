using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Lms.Core.Match
{
    /// <summary>
    ///     What the server made of a prepared round. The adapter does the work; Core records it. A
    ///     failure writes nothing to the audit log.
    /// </summary>
    public readonly struct RoundOutcome
    {
        private static readonly ReadOnlyCollection<PlayerMode> NobodyCorrected =
            new ReadOnlyCollection<PlayerMode>(new PlayerMode[0]);

        private static readonly ReadOnlyCollection<string> NobodyUnseen =
            new ReadOnlyCollection<string>(new string[0]);

        private RoundOutcome(bool prepared, string message, string detail,
            IReadOnlyList<PlayerMode>? corrected, IReadOnlyList<string>? unseen)
        {
            IsPrepared = prepared;
            Message = message;
            Detail = detail;
            Corrected = corrected == null || corrected.Count == 0
                ? NobodyCorrected
                : new ReadOnlyCollection<PlayerMode>(new List<PlayerMode>(corrected));
            Unseen = unseen == null || unseen.Count == 0
                ? NobodyUnseen
                : new ReadOnlyCollection<string>(new List<string>(unseen));
        }

        /// <summary>The round is set up and the hold is on. Anything else is a failure.</summary>
        public bool IsPrepared { get; }

        /// <summary>What to tell whoever typed the verb, whether it worked or not.</summary>
        public string Message { get; }

        /// <summary>What the server did, in its own words, for the <c>RoundPrepared</c> record's detail.</summary>
        public string Detail { get; }

        /// <summary>
        ///     Contestants the round put back into survival, and the mode each was holding.
        ///     <see cref="MatchHost"/> writes one <c>GameModeCorrected</c> record per name.
        /// </summary>
        public IReadOnlyList<PlayerMode> Corrected { get; }

        /// <summary>Contestants whose mode could not be read at all.</summary>
        public IReadOnlyList<string> Unseen { get; }

        /// <summary>It worked.</summary>
        public static RoundOutcome Prepared(string message, string detail,
            IReadOnlyList<PlayerMode>? corrected = null, IReadOnlyList<string>? unseen = null) =>
            new RoundOutcome(true, message ?? string.Empty, detail ?? string.Empty, corrected, unseen);

        /// <summary>It did not, and this is why. Nothing is recorded.</summary>
        public static RoundOutcome Nothing(string message) =>
            new RoundOutcome(false, message ?? string.Empty, string.Empty, null, null);

        public override string ToString() =>
            IsPrepared ? $"prepared: {Detail}" : $"nothing: {Message}";
    }
}
