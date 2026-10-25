using System;
using System.Globalization;
using Lms.Core.Zones;

namespace Lms.Core.Match
{
    /// <summary>Where a caster's camera is being sent, and who is there.</summary>
    /// <remarks>
    ///     The server sends the position because a far-away player may not be loaded on the caster's
    ///     client. Sent only to a ghost: a contestant must never learn where another contestant is.
    /// </remarks>
    public readonly struct LookBroadcast : IEquatable<LookBroadcast>
    {
        /// <summary>The first token of the wire form. It moves when the meaning of a field moves.</summary>
        public const string WireHeader = "lms-look/1";

        private const string NameField = "name";
        private const string XField = "x";
        private const string YField = "y";
        private const string ZField = "z";

        public LookBroadcast(string name, WorldPoint where)
        {
            Name = name ?? string.Empty;
            Where = where;
        }

        /// <summary>Who the camera is being put on.</summary>
        public string Name { get; }

        /// <summary>Where they were when the verb ran, up to one tick old.</summary>
        public WorldPoint Where { get; }

        /// <summary>Whether a line is one of these, without reading it.</summary>
        public static bool IsLook(string? line) =>
            line != null && line.StartsWith(WireHeader, StringComparison.Ordinal);

        public string Encode()
        {
            return WireHeader +
                   "\t" + NameField + "=" + Clean(Name) +
                   "\t" + XField + "=" + Number(Where.X) +
                   "\t" + YField + "=" + Number(Where.Y) +
                   "\t" + ZField + "=" + Number(Where.Z);
        }

        /// <summary>Reads a line back, declining it if the name or any axis is missing or unparseable.</summary>
        public static bool TryDecode(string? line, out LookBroadcast look)
        {
            look = default;

            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            string[] tokens = line!.Split('\t');
            if (tokens[0] != WireHeader)
            {
                return false;
            }

            string name = string.Empty;
            double? x = null;
            double? y = null;
            double? z = null;

            for (int i = 1; i < tokens.Length; i++)
            {
                string token = tokens[i];
                int split = token.IndexOf('=');
                if (split < 1)
                {
                    continue;
                }

                string key = token.Substring(0, split);
                string value = token.Substring(split + 1);

                switch (key)
                {
                    case NameField: name = value; break;
                    case XField: x = Number(value); break;
                    case YField: y = Number(value); break;
                    case ZField: z = Number(value); break;
                }
            }

            if (name.Length == 0 || x == null || y == null || z == null)
            {
                return false;
            }

            look = new LookBroadcast(name, new WorldPoint(x.Value, y.Value, z.Value));

            return true;
        }

        public bool Equals(LookBroadcast other) =>
            string.Equals(Name, other.Name, StringComparison.Ordinal) && Where.Equals(other.Where);

        public override bool Equals(object? obj) => obj is LookBroadcast other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return Name.GetHashCode() * 397 ^ Where.GetHashCode();
            }
        }

        public override string ToString() => $"look at {Name} ({Where})";

        /// <summary>Strips separators so a player name cannot corrupt the fields after it.</summary>
        private static string Clean(string value) =>
            value.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');

        private static string Number(double value) =>
            value.ToString("0.###", CultureInfo.InvariantCulture);

        private static double? Number(string value) =>
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
                out double parsed)
                ? parsed
                : (double?)null;
    }
}
