using System;

namespace Lms.Core.Settings
{
    /// <summary>
    ///     One local, per-machine setting a player can change in Nitrox's settings tab.
    /// </summary>
    /// <remarks>
    ///     <see cref="Key"/> is a Unity <c>PlayerPrefs</c> key: the fork writes it and the mod reads it,
    ///     without either assembly referencing the other. Tournament rules never live here.
    /// </remarks>
    public sealed class ClientPreference
    {
        private ClientPreference(string key, string label, ClientPreferenceKind kind,
                                 float @default, float minimum, float maximum, float step)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("A preference with no key has nowhere to be stored.", nameof(key));
            }

            if (string.IsNullOrWhiteSpace(label))
            {
                throw new ArgumentException("A preference with no label is a blank row in the menu.", nameof(label));
            }

            Key = key;
            Label = label;
            Kind = kind;
            Default = @default;
            Minimum = minimum;
            Maximum = maximum;
            Step = step;
        }

        /// <summary>The <c>PlayerPrefs</c> key the value is stored under. Prefixed, because that store is shared.</summary>
        public string Key { get; }

        /// <summary>What the menu row says.</summary>
        public string Label { get; }

        /// <summary>Which control to draw.</summary>
        public ClientPreferenceKind Kind { get; }

        /// <summary>The initial value. For a toggle, non-zero is on.</summary>
        public float Default { get; }

        /// <summary>Slider only.</summary>
        public float Minimum { get; }

        /// <summary>Slider only.</summary>
        public float Maximum { get; }

        /// <summary>Slider only.</summary>
        public float Step { get; }

        /// <summary>Whether this is a toggle that starts on.</summary>
        public bool IsOnByDefault => Kind == ClientPreferenceKind.Toggle && Default != 0f;

        /// <summary>An on/off preference.</summary>
        public static ClientPreference Toggle(string key, string label, bool onByDefault = true) =>
            new ClientPreference(key, label, ClientPreferenceKind.Toggle, onByDefault ? 1f : 0f, 0f, 1f, 1f);

        /// <summary>A numeric preference between two bounds.</summary>
        public static ClientPreference Slider(string key, string label,
                                              float minimum, float maximum, float @default, float step)
        {
            if (minimum >= maximum)
            {
                throw new ArgumentOutOfRangeException(nameof(maximum), maximum,
                    "A slider whose bounds meet cannot be moved.");
            }

            if (step <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(step), step, "A slider needs a step it can take.");
            }

            if (@default < minimum || @default > maximum)
            {
                throw new ArgumentOutOfRangeException(nameof(@default), @default,
                    "A default outside the bounds is a control that moves the moment it is drawn.");
            }

            return new ClientPreference(key, label, ClientPreferenceKind.Slider, @default, minimum, maximum, step);
        }
    }
}
