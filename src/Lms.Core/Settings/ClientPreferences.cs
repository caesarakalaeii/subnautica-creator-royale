using System.Collections.Generic;
using System.Collections.ObjectModel;
using Lms.Core.Match;

namespace Lms.Core.Settings
{
    /// <summary>
    ///     Everything the tournament puts in the game's options panel: local per-player preferences, and
    ///     admin buttons that only send a request the server authorises (<see cref="AdminRequest"/>).
    /// </summary>
    public static class ClientPreferences
    {
        /// <summary>The heading the per-player rows go under.</summary>
        public const string Heading = "Last Man Standing";

        /// <summary>The heading the match-control buttons go under, kept separate from player preferences.</summary>
        public const string AdminHeading = "Last Man Standing — match control (admin)";

        /// <summary>The match status box, the bearing and the place-and-depth line.</summary>
        public const string StatusBoxKey = "Lms.hud.statusBox";

        /// <summary>The minimap, its two rings and the look cone.</summary>
        public const string MinimapKey = "Lms.hud.minimap";

        /// <summary>The developer biome readout.</summary>
        public const string BiomeReadoutKey = "Lms.hud.biomeReadout";

        /// <summary>Phase countdown volume; zero is silent.</summary>
        public const string WarningVolumeKey = "Lms.warning.volume";

        private static readonly ReadOnlyCollection<ClientPreference> Catalogue =
            new ReadOnlyCollection<ClientPreference>(new List<ClientPreference>
            {
                // Every toggle ships on, so a player who never opens the tab sees no change.
                ClientPreference.Toggle(StatusBoxKey, "Match status box"),
                ClientPreference.Toggle(MinimapKey, "Minimap"),
                ClientPreference.Toggle(BiomeReadoutKey, "Biome readout"),

                ClientPreference.Slider(WarningVolumeKey, "Phase warning volume", 0f, 1f, 1f, 0.05f),
            });

        /// <summary>Every per-player preference, in the order the menu shows them.</summary>
        public static IReadOnlyList<ClientPreference> All => Catalogue;

        /// <summary>
        ///     The admin verbs that get a button, taken from <see cref="AdminCommands.Buttons"/> so the tab
        ///     cannot drift from the console. Destructive verbs such as <c>podclear all</c> are excluded there.
        /// </summary>
        public static IReadOnlyList<string> AdminButtons => AdminCommands.Buttons;
    }
}
