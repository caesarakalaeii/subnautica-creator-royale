using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Lms.Core.Match
{
    /// <summary>The tournament rules, on their way to every client.</summary>
    /// <remarks>
    ///     The whole config document travels, minus comment lines; the client parses it with the
    ///     same loader as the file. The revision lets a client log which rules it is running.
    /// </remarks>
    public readonly struct RulesBroadcast
    {
        /// <summary>The first token of the wire form. It moves when the meaning of a field moves.</summary>
        public const string WireHeader = "lms-rules/1";

        private const string RevisionField = "rev";
        private const string YamlField = "yaml";

        private readonly string? yaml;

        /// <param name="revision">The backend's revision, or zero for the file this server started with.</param>
        /// <param name="yaml">The config document. Stripped by <see cref="Encode"/>, not here.</param>
        public RulesBroadcast(long revision, string? yaml)
        {
            Revision = revision;
            this.yaml = yaml;
        }

        /// <summary>Which revision these rules are. Zero is the file the server started with.</summary>
        public long Revision { get; }

        /// <summary>The config document.</summary>
        public string Yaml => yaml ?? string.Empty;

        /// <summary>
        ///     The rules as one line, stripped. Throws on a tab, which separates fields and which YAML
        ///     forbids as indentation anyway.
        /// </summary>
        public string Encode()
        {
            string stripped = Strip(Yaml);

            if (stripped.IndexOf('\t') >= 0)
            {
                throw new ArgumentException(
                    "A tournament config with a tab in it cannot be sent to clients, and YAML does not " +
                    "allow one as indentation. Use spaces.", nameof(Yaml));
            }

            StringBuilder line = new StringBuilder(stripped.Length + 64);
            line.Append(WireHeader);
            line.Append('\t').Append(RevisionField).Append('=')
                .Append(Revision.ToString(CultureInfo.InvariantCulture));
            line.Append('\t').Append(YamlField).Append('=').Append(stripped);

            return line.ToString();
        }

        /// <summary>Whether a line is one of these, without reading it.</summary>
        public static bool IsRules(string? line) =>
            line != null && line.StartsWith(WireHeader, StringComparison.Ordinal);

        /// <summary>
        ///     Reads the rules back. Unknown fields are ignored; a line with no document is declined,
        ///     since "no rules" would turn a client's zone off mid-match.
        /// </summary>
        public static bool TryDecode(string? line, out RulesBroadcast rules)
        {
            rules = default;

            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            string[] tokens = line!.Split('\t');
            if (tokens[0] != WireHeader)
            {
                return false;
            }

            long revision = 0;
            string? document = null;

            for (int i = 1; i < tokens.Length; i++)
            {
                string token = tokens[i];
                int split = token.IndexOf('=');
                if (split <= 0)
                {
                    continue;
                }

                string name = token.Substring(0, split);
                string value = token.Substring(split + 1);

                if (name == RevisionField
                    && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long read))
                {
                    revision = read;
                }
                else if (name == YamlField)
                {
                    document = value;
                }
            }

            if (string.IsNullOrWhiteSpace(document))
            {
                return false;
            }

            rules = new RulesBroadcast(revision, document);

            return true;
        }

        /// <summary>
        ///     The document without whole-line comments and blank lines. Trailing comments stay, since
        ///     a <c>#</c> can sit inside a quoted value such as <c>"#FFD65C"</c>.
        /// </summary>
        public static string Strip(string? document)
        {
            if (string.IsNullOrEmpty(document))
            {
                return string.Empty;
            }

            List<string> kept = new List<string>();

            foreach (string line in document!.Split('\n'))
            {
                string trimmed = line.TrimEnd('\r');
                string bare = trimmed.Trim();

                if (bare.Length == 0 || bare[0] == '#')
                {
                    continue;
                }

                kept.Add(trimmed);
            }

            return string.Join("\n", kept);
        }
    }
}
