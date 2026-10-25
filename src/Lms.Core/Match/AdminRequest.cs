using System;

namespace Lms.Core.Match
{
    /// <summary>
    ///     A client asking the server to run an admin verb. The server checks the sender, then runs the
    ///     verb through the same <see cref="MatchHost"/> path as <c>/lms</c>.
    /// </summary>
    /// <remarks>
    ///     Rides the report packet, told apart from snapshots and event batches by its header. A
    ///     missing or unknown verb is refused rather than defaulted to <see cref="AdminCommand.Status"/>.
    /// </remarks>
    public static class AdminRequest
    {
        /// <summary>The first token of the wire form. It moves when the meaning of what follows moves.</summary>
        public const string WireHeader = "lms-admin/1";

        private const string VerbField = "verb";

        /// <summary>Whether this is an admin request at all, which is the server's first question.</summary>
        public static bool IsRequest(string? wire) =>
            !string.IsNullOrWhiteSpace(wire) && Head(wire!) == WireHeader;

        /// <summary>Writes one request. The verb is the word the console uses.</summary>
        public static string Encode(AdminCommand command) =>
            WireHeader + "\t" + VerbField + "=" + AdminCommands.Word(command);

        /// <summary>Reads a request back, or declines it.</summary>
        public static bool TryDecode(string? wire, out AdminCommand command)
        {
            command = default;

            if (!IsRequest(wire))
            {
                return false;
            }

            string[] fields = wire!.Split('\t');
            string? verb = null;

            for (int field = 1; field < fields.Length; field++)
            {
                int split = fields[field].IndexOf('=');
                if (split <= 0)
                {
                    continue;
                }

                // Unknown fields are ignored (unknown versions are refused) so mixed mod builds interoperate.
                if (string.Equals(fields[field].Substring(0, split), VerbField, StringComparison.Ordinal))
                {
                    verb = fields[field].Substring(split + 1);
                }
            }

            if (string.IsNullOrWhiteSpace(verb))
            {
                return false;
            }

            return AdminCommands.TryParse(verb, out command);
        }

        private static string Head(string wire)
        {
            int tab = wire.IndexOf('\t');

            return tab < 0 ? wire.Trim() : wire.Substring(0, tab);
        }
    }
}
