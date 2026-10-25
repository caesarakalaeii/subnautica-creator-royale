using System;
using System.Globalization;
using Lms.Core.Diagnostics;
using Lms.Core.Zones;

namespace Lms.Core.Match
{
    /// <summary>
    ///     One contestant as a caster is shown them: where they are and how they are doing. Health is
    ///     a percent and oxygen is in seconds; see <see cref="Vitals"/>.
    /// </summary>
    public readonly struct CasterSighting : IEquatable<CasterSighting>
    {
        /// <summary>What separates the fields inside one row; the line itself is tab-separated.</summary>
        internal const char FieldSplit = ':';

        /// <summary>
        ///     Fewest fields a row may have. A six-field row from an older sender reads as living; the
        ///     seventh carries the alive flag.
        /// </summary>
        private const int Fields = 6;
        private const int FieldsWithAlive = 7;

        /// <summary>The wire's way to say living, in the seventh field.</summary>
        private const string AliveMark = "1";
        private const string DeadMark = "0";

        public CasterSighting(string name, WorldPoint where, double health, double oxygen,
            bool alive = true)
        {
            Name = name ?? string.Empty;
            Where = where;
            Health = health;
            Oxygen = oxygen;
            Alive = alive;
        }

        /// <summary>Who this is, as the plate says it.</summary>
        public string Name { get; }

        /// <summary>
        ///     Where they were when the server last ticked, up to a quarter second old; the client
        ///     smooths it through <see cref="CasterTrack"/>.
        /// </summary>
        public WorldPoint Where { get; }

        /// <summary>Out of a hundred, as the dashboard shows it.</summary>
        public double Health { get; }

        /// <summary>In seconds of air, which is not a percentage. See <see cref="Vitals"/>.</summary>
        public double Oxygen { get; }

        /// <summary>
        ///     Still in the match. Absent on the wire means living, so a living player is never shown
        ///     dead on camera.
        /// </summary>
        public bool Alive { get; }

        /// <summary>Whether this row says anything. A nameless one matches nobody.</summary>
        internal bool IsSomebody => !string.IsNullOrWhiteSpace(Name);

        public bool Equals(CasterSighting other) =>
            string.Equals(Name, other.Name, StringComparison.Ordinal) &&
            Where.Equals(other.Where) &&
            Health.Equals(other.Health) &&
            Oxygen.Equals(other.Oxygen) &&
            Alive == other.Alive;

        public override bool Equals(object? obj) => obj is CasterSighting other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Name.GetHashCode();
                hash = hash * 397 ^ Where.GetHashCode();
                hash = hash * 397 ^ Health.GetHashCode();
                hash = hash * 397 ^ Oxygen.GetHashCode();

                return hash * 397 ^ Alive.GetHashCode();
            }
        }

        public override string ToString() =>
            Alive
                ? Readable.Line($"{Name} at {Where}, {Health:0} hp, {Oxygen:0} s")
                : Readable.Line($"{Name} at {Where}, dead, {Health:0} hp, {Oxygen:0} s");

        /// <summary>The row as one field of the line.</summary>
        internal string Encode() =>
            Clean(Name.Trim()) + FieldSplit +
            Number(Where.X) + FieldSplit +
            Number(Where.Y) + FieldSplit +
            Number(Where.Z) + FieldSplit +
            Number(Health) + FieldSplit +
            Number(Oxygen) + FieldSplit +
            (Alive ? AliveMark : DeadMark);

        /// <summary>
        ///     Reads one row back, or declines it. Only the bad row is dropped, not the whole feed
        ///     line, so one bad row costs one nameplate.
        /// </summary>
        internal static bool TryDecode(string value, out CasterSighting sighting)
        {
            sighting = default;

            string[] parts = value.Split(FieldSplit);
            if (parts.Length < Fields)
            {
                return false;
            }

            string name = parts[0];
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            double? x = Number(parts[1]);
            double? y = Number(parts[2]);
            double? z = Number(parts[3]);
            double? health = Number(parts[4]);
            double? oxygen = Number(parts[5]);

            if (x == null || y == null || z == null || health == null || oxygen == null)
            {
                return false;
            }

            // Only an explicit dead mark means dead.
            bool alive = parts.Length < FieldsWithAlive || parts[6] != DeadMark;

            sighting = new CasterSighting(name, new WorldPoint(x.Value, y.Value, z.Value),
                health.Value, oxygen.Value, alive);

            return true;
        }

        /// <summary>
        ///     Strips both the line's and the row's separators so a name cannot corrupt the fields after it.
        /// </summary>
        private static string Clean(string value) =>
            value.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ').Replace(FieldSplit, ' ');

        private static string Number(double value) =>
            value.ToString("0.###", CultureInfo.InvariantCulture);

        private static double? Number(string value) =>
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                ? parsed
                : (double?)null;
    }
}
