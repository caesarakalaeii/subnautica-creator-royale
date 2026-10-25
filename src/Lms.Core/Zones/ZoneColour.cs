using System;
using System.Globalization;

namespace Lms.Core.Zones
{
    /// <summary>A colour from the config, as four bytes with no game types attached.</summary>
    /// <remarks>
    ///     Core cannot reference <c>UnityEngine.Color</c>. Bytes because the notation is hex, so
    ///     the value round-trips exactly into the log.
    /// </remarks>
    public readonly struct ZoneColour : IEquatable<ZoneColour>
    {
        public ZoneColour(byte red, byte green, byte blue, byte alpha = 255)
        {
            Red = red;
            Green = green;
            Blue = blue;
            Alpha = alpha;
        }

        public byte Red { get; }

        public byte Green { get; }

        public byte Blue { get; }

        /// <summary>
        ///     How solid the wall is drawn, relative to the material's own transparency: 255 is the
        ///     stasis field as the game authored it.
        /// </summary>
        public byte Alpha { get; }

        /// <summary>Reads <c>#RRGGBB</c> or <c>#RRGGBBAA</c>, in either case, with or without the hash.</summary>
        /// <remarks>The hash is optional because YAML reads an unquoted <c>#</c> as a comment.</remarks>
        public static bool TryParse(string? written, out ZoneColour colour)
        {
            colour = default;
            if (written == null)
            {
                return false;
            }

            string digits = written.Trim();
            if (digits.Length > 0 && digits[0] == '#')
            {
                digits = digits.Substring(1);
            }

            if (digits.Length != 6 && digits.Length != 8)
            {
                return false;
            }

            byte[] parts = { 0, 0, 0, 255 };
            for (int index = 0; index * 2 < digits.Length; index++)
            {
                int high = Hex(digits[index * 2]);
                int low = Hex(digits[index * 2 + 1]);
                if (high < 0 || low < 0)
                {
                    return false;
                }

                parts[index] = (byte)((high << 4) | low);
            }

            colour = new ZoneColour(parts[0], parts[1], parts[2], parts[3]);
            return true;
        }

        /// <summary>
        ///     One hex digit's value, or -1. Avoids <c>NumberStyles.HexNumber</c>, which accepts
        ///     surrounding whitespace.
        /// </summary>
        private static int Hex(char digit)
        {
            if (digit >= '0' && digit <= '9')
            {
                return digit - '0';
            }

            if (digit >= 'a' && digit <= 'f')
            {
                return digit - 'a' + 10;
            }

            if (digit >= 'A' && digit <= 'F')
            {
                return digit - 'A' + 10;
            }

            return -1;
        }

        public bool Equals(ZoneColour other) =>
            Red == other.Red && Green == other.Green && Blue == other.Blue && Alpha == other.Alpha;

        public override bool Equals(object? obj) => obj is ZoneColour other && Equals(other);

        public override int GetHashCode() => (Red << 24) | (Green << 16) | (Blue << 8) | Alpha;

        public static bool operator ==(ZoneColour left, ZoneColour right) => left.Equals(right);

        public static bool operator !=(ZoneColour left, ZoneColour right) => !left.Equals(right);

        /// <summary>Always eight digits, so the log shows the alpha the operator left implicit.</summary>
        public override string ToString() =>
            "#" + ((Red << 24) | (Green << 16) | (Blue << 8) | Alpha).ToString("X8", CultureInfo.InvariantCulture);
    }
}
