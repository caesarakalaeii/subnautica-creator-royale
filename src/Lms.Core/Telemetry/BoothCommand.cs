using System;

namespace Lms.Core.Telemetry
{
    /// <summary>
    ///     One verb the casting booth asked this server to run. Authorisation stays in
    ///     <see cref="Match.MatchHost"/>; the booth only sends the typed verb and an id.
    /// </summary>
    /// <remarks>
    ///     The id is the booth's monotonic sequence and the only duplicate suppression: the booth
    ///     resends until acknowledged, so this end must recognise repeats.
    /// </remarks>
    public readonly struct BoothCommand : IEquatable<BoothCommand>
    {
        public BoothCommand(long id, string verb)
        {
            if (id <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(id), id,
                    "A command with no id is one nothing can acknowledge, so it would run on every push.");
            }

            if (string.IsNullOrWhiteSpace(verb))
            {
                throw new ArgumentException("A command with no verb is not a command.", nameof(verb));
            }

            Id = id;
            Verb = verb.Trim();
        }

        /// <summary>The booth's own id, counting from one.</summary>
        public long Id { get; }

        /// <summary>The verb as an operator typed it, such as <c>round force</c> or <c>ghost Ada</c>.</summary>
        public string Verb { get; }

        public bool Equals(BoothCommand other) =>
            Id == other.Id && string.Equals(Verb, other.Verb, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is BoothCommand other && Equals(other);

        public override int GetHashCode() => unchecked((Id.GetHashCode() * 397) ^ Verb.GetHashCode());

        public override string ToString() => $"#{Id} {Verb}";
    }
}
