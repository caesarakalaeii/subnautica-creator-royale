using System;
using System.Globalization;
using System.Text;

namespace Lms.Core.Telemetry
{
    /// <summary>
    ///     A minimal write-only JSON writer.
    /// </summary>
    /// <remarks>
    ///     Hand-written because <c>Lms.Core</c> targets <c>netstandard2.0</c> under Unity's Mono, where
    ///     <c>System.Text.Json</c> pulls in <c>System.Memory</c>, <c>System.Buffers</c> and
    ///     <c>System.Runtime.CompilerServices.Unsafe</c> and risks assembly conflicts. Structure is not
    ///     validated: unbalanced open/close calls produce invalid JSON.
    /// </remarks>
    public sealed class JsonText
    {
        /// <summary>
        ///     Decimal places kept (millimetres, milliseconds), matching the audit log so values agree exactly.
        /// </summary>
        private const int Precision = 3;

        private static readonly double Scale = Math.Pow(10, Precision);

        private readonly StringBuilder text = new StringBuilder();

        private bool needsComma;

        /// <summary>Opens an object as an element of an array, or as the whole document.</summary>
        public void OpenObject()
        {
            Separate();
            text.Append('{');
            needsComma = false;
        }

        /// <summary>Opens an object as a named field of the object being written.</summary>
        public void OpenObject(string name)
        {
            Name(name);
            text.Append('{');
            needsComma = false;
        }

        public void CloseObject()
        {
            text.Append('}');
            needsComma = true;
        }

        public void OpenArray(string name)
        {
            Name(name);
            text.Append('[');
            needsComma = false;
        }

        public void CloseArray()
        {
            text.Append(']');
            needsComma = true;
        }

        /// <summary>A string, or <c>null</c> when there is nothing to say.</summary>
        public void Text(string name, string? value)
        {
            Name(name);

            if (value == null)
            {
                text.Append("null");
            }
            else
            {
                Quoted(value);
            }

            needsComma = true;
        }

        /// <summary>A number truncated to three decimals; <c>null</c> for NaN or infinity.</summary>
        public void Number(string name, double value)
        {
            Name(name);
            text.Append(Written(value));
            needsComma = true;
        }

        /// <summary>A whole number, for counts and sequence numbers.</summary>
        public void Number(string name, long value)
        {
            Name(name);
            text.Append(value.ToString(CultureInfo.InvariantCulture));
            needsComma = true;
        }

        public void Flag(string name, bool value)
        {
            Name(name);
            text.Append(value ? "true" : "false");
            needsComma = true;
        }

        /// <summary>An instant, in UTC, to the millisecond, with a Z.</summary>
        public void Time(string name, DateTimeOffset value)
        {
            Name(name);
            Quoted(value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
            needsComma = true;
        }

        /// <summary>A field that is absent rather than empty: no zone yet, no vehicle.</summary>
        public void Null(string name)
        {
            Name(name);
            text.Append("null");
            needsComma = true;
        }

        public override string ToString() => text.ToString();

        private string Written(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                // Not valid JSON; written as null so one bad value does not break the payload.
                return "null";
            }

            double cut = Math.Truncate(value * Scale) / Scale;

            // Truncation can leave -0; write it as 0.
            if (cut == 0)
            {
                return "0";
            }

            return cut.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private void Name(string name)
        {
            Separate();
            Quoted(name);
            text.Append(':');
        }

        private void Separate()
        {
            if (needsComma)
            {
                text.Append(',');
            }
        }

        private void Quoted(string value)
        {
            text.Append('"');

            foreach (char character in value)
            {
                switch (character)
                {
                    case '"': text.Append("\\\""); break;
                    case '\\': text.Append("\\\\"); break;
                    case '\n': text.Append("\\n"); break;
                    case '\r': text.Append("\\r"); break;
                    case '\t': text.Append("\\t"); break;
                    case '\b': text.Append("\\b"); break;
                    case '\f': text.Append("\\f"); break;
                    default:
                        if (character < ' ')
                        {
                            // Other control characters have no short escape and are illegal unescaped.
                            text.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            text.Append(character);
                        }

                        break;
                }
            }

            text.Append('"');
        }
    }
}
