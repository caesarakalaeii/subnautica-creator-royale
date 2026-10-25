using System;
using System.Collections.Generic;

namespace Lms.Core.Telemetry
{
    /// <summary>
    ///     The tournament config the casting booth sent, and the revision that stops one being taken
    ///     twice.
    /// </summary>
    /// <remarks>
    ///     A revision taken but not yet adoptable (a match is running) is held, so the booth's resends
    ///     are not re-parsed every push; only a newer revision replaces it. Not thread-safe; the host
    ///     drives it under its match lock.
    /// </remarks>
    public sealed class BoothRules
    {
        /// <summary>
        ///     Maximum config document size: ample headroom over the example config, small enough to
        ///     bound memory on the match tick.
        /// </summary>
        private const int MaxBytes = 256 * 1024;

        /// <summary>How many outcomes are held while the booth is not taking the console channel.</summary>
        private const int MaxOutcomes = 16;

        private readonly List<BoothRulesOutcome> outcomes = new List<BoothRulesOutcome>();

        private long offeredRevision;
        private string? offeredYaml;
        private string offeredBy = string.Empty;

        /// <param name="accepts">
        ///     <c>telemetry.acceptsCommands</c>, the same switch that gates booth commands.
        /// </param>
        public BoothRules(bool accepts = true)
        {
            Accepts = accepts;
        }

        /// <summary>Whether this server takes its rules from the booth.</summary>
        public bool Accepts { get; }

        /// <summary>The revision this server is actually running. Zero before the booth has sent one.</summary>
        public long Revision { get; private set; }

        /// <summary>The revision handed out and not yet settled, or null.</summary>
        public long? Holding { get; private set; }

        /// <summary>
        ///     The running-revision header value, or null to send none; the booth only sends configs to a
        ///     server that reports one.
        /// </summary>
        public long? Cursor => Accepts ? Revision : (long?)null;

        /// <summary>What this server did about the revisions it has been sent, for the console.</summary>
        public IReadOnlyList<BoothRulesOutcome> Outcomes => outcomes;

        /// <summary>
        ///     Takes a config the booth sent and returns whether it is new. Revisions already running,
        ///     held, or older are refused; a newer one replaces a held one.
        /// </summary>
        public bool Offer(long revision, string? yaml, string? by = null)
        {
            if (!Accepts || revision <= 0 || string.IsNullOrWhiteSpace(yaml))
            {
                return false;
            }

            if (yaml!.Length > MaxBytes)
            {
                Note(revision, $"that config is larger than this server will read ({MaxBytes} bytes).");

                return false;
            }

            if (revision <= Revision || revision == Holding || revision == offeredRevision)
            {
                return false;
            }

            if (Holding.HasValue && revision < Holding.Value)
            {
                return false;
            }

            offeredRevision = revision;
            offeredYaml = yaml;
            offeredBy = by ?? string.Empty;

            return true;
        }

        /// <summary>Takes the config waiting to be tried, and remembers that it is out.</summary>
        public bool TryTake(out long revision, out string yaml) =>
            TryTake(out revision, out yaml, out _);

        /// <summary>Takes the config waiting to be tried, with whoever wrote it.</summary>
        public bool TryTake(out long revision, out string yaml, out string by)
        {
            revision = 0;
            yaml = string.Empty;
            by = string.Empty;

            if (!Accepts || offeredYaml == null)
            {
                return false;
            }

            revision = offeredRevision;
            yaml = offeredYaml;
            by = offeredBy;

            Holding = offeredRevision;
            offeredRevision = 0;
            offeredYaml = null;
            offeredBy = string.Empty;

            return true;
        }

        /// <summary>
        ///     Reports a non-final outcome, e.g. a valid config waiting for the match to end. The revision
        ///     stays held.
        /// </summary>
        public void Note(long revision, string message)
        {
            Record(new BoothRulesOutcome(revision, false, message, null, final: false));
        }

        /// <summary>
        ///     Finishes with a revision: adopted, or refused with the loader's own problems.
        /// </summary>
        /// <param name="revision">The revision this is about.</param>
        /// <param name="adopted">Whether the match is now running these rules.</param>
        /// <param name="message">What to say, in the words whoever wrote it will read.</param>
        /// <param name="problems">The loader's problems by field path, verbatim, or null.</param>
        public void Settled(long revision, bool adopted, string message,
            IReadOnlyList<string>? problems = null)
        {
            if (Holding == revision)
            {
                Holding = null;
            }

            if (adopted && revision > Revision)
            {
                Revision = revision;
            }

            Record(new BoothRulesOutcome(revision, adopted, message, problems, final: true));
        }

        /// <summary>Forgets the outcomes the booth has taken, from the front.</summary>
        public void Forget(int count)
        {
            if (count <= 0)
            {
                return;
            }

            outcomes.RemoveRange(0, Math.Min(count, outcomes.Count));
        }

        private void Record(BoothRulesOutcome outcome)
        {
            outcomes.Add(outcome);

            while (outcomes.Count > MaxOutcomes)
            {
                outcomes.RemoveAt(0);
            }
        }

        public override string ToString() =>
            !Accepts
                ? "the booth's rules are switched off"
                : $"running revision {Revision}" + (Holding.HasValue ? $", holding {Holding}" : string.Empty);
    }

    /// <summary>
    ///     What this server did about one config revision, on its way back to the console.
    /// </summary>
    public readonly struct BoothRulesOutcome
    {
        private static readonly string[] Nothing = new string[0];

        public BoothRulesOutcome(long revision, bool adopted, string message,
            IReadOnlyList<string>? problems, bool final = true)
        {
            Revision = revision;
            Adopted = adopted;
            Message = message ?? string.Empty;
            Problems = problems ?? Nothing;
            Final = final;
        }

        /// <summary>The booth's own revision number.</summary>
        public long Revision { get; }

        /// <summary>Whether the match is running these rules now.</summary>
        public bool Adopted { get; }

        /// <summary>
        ///     Whether this server has finished with the revision. False means valid and waiting for the
        ///     match to end, so the console can tell waiting from refused.
        /// </summary>
        public bool Final { get; }

        /// <summary>What to say, verbatim.</summary>
        public string Message { get; }

        /// <summary>
        ///     The loader's problems by field path, empty when it parsed. The loader is the only validator.
        /// </summary>
        public IReadOnlyList<string> Problems { get; }

        public override string ToString() =>
            Adopted ? $"#{Revision} adopted: {Message}"
                : Final ? $"#{Revision} not adopted: {Message}"
                : $"#{Revision} waiting: {Message}";
    }
}
