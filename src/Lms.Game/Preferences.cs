using System;
using Lms.Core.Diagnostics;
using Lms.Core.Match;
using Lms.Core.Settings;
using Nitrox.Model.Logger;
using Nitrox.Model.Subnautica.Tournament;
using UnityEngine;

namespace Lms.Game
{
    /// <summary>
    ///     This player's local, cosmetic preferences, and the options tab they are set in.
    /// </summary>
    /// <remarks>
    ///     <see cref="Declare"/> asks the fork for the rows, since the settings framework is
    ///     NitroxClient's; the readers go straight to <c>PlayerPrefs</c>, where the fork stores them.
    ///     Read per call rather than cached: <c>PlayerPrefs</c> is in memory and has no change event here.
    /// </remarks>
    internal static class Preferences
    {
        /// <summary>Whether to draw the match status box, the bearing and the place-and-depth line.</summary>
        internal static bool ShowStatusBox => Toggle(ClientPreferences.StatusBoxKey);

        /// <summary>Whether to draw the minimap and its rings.</summary>
        internal static bool ShowMinimap => Toggle(ClientPreferences.MinimapKey);

        /// <summary>Whether to draw the developer biome readout.</summary>
        internal static bool ShowBiomeReadout => Toggle(ClientPreferences.BiomeReadoutKey);

        /// <summary>How loud the phase countdown is; zero is silent.</summary>
        internal static float WarningVolume => Slider(ClientPreferences.WarningVolumeKey);

        /// <summary>
        ///     Asks the fork for a row per preference and a button per admin verb.
        /// </summary>
        /// <remarks>
        ///     Called once at load. Cleared first so a reload cannot duplicate rows.
        /// </remarks>
        internal static void Declare()
        {
            LmsPreferences.Clear();

            foreach (ClientPreference preference in ClientPreferences.All)
            {
                LmsPreferences.Declare(new LmsPreference
                {
                    Heading = ClientPreferences.Heading,
                    Key = preference.Key,
                    Label = preference.Label,
                    IsSlider = preference.Kind == ClientPreferenceKind.Slider,
                    Default = preference.Default,
                    Minimum = preference.Minimum,
                    Maximum = preference.Maximum,
                    Step = preference.Step,
                    Changed = value => Log.Info(Readable.Line(
                        $"{LmsMod.LogPrefix} {preference.Label} is now {value:0.##}.")),
                });
            }

            foreach (string verb in ClientPreferences.AdminButtons)
            {
                string word = verb;

                LmsPreferences.Declare(new LmsPreferenceButton
                {
                    Heading = ClientPreferences.AdminHeading,
                    Label = word,
                    Pressed = () => Ask(word),
                });
            }

            Log.Info($"{LmsMod.LogPrefix} settings tab: {ClientPreferences.All.Count} preference(s) " +
                     $"and {ClientPreferences.AdminButtons.Count} match-control button(s). " +
                     "A button asks the server; only an admin's is obeyed.");
        }

        /// <summary>
        ///     Asks the server to run a verb, over the report packet.
        /// </summary>
        /// <remarks>
        ///     The server checks Nitrox's admin permission and silently refuses anybody else; the
        ///     reply is the match changing.
        /// </remarks>
        private static void Ask(string verb)
        {
            if (!AdminCommands.TryParse(verb, out AdminCommand command))
            {
                // Unreachable while the buttons come from AdminCommands.Verbs.
                Log.Warn($"{LmsMod.LogPrefix} no such tournament verb: '{verb}'.");
                return;
            }

            Log.Info($"{LmsMod.LogPrefix} asking the server for 'lms {verb}'.");
            LmsReporting.Report(AdminRequest.Encode(command));
        }

        /// <remarks>
        ///     The default comes from the catalogue, so an untouched preference reads what the menu shows.
        /// </remarks>
        private static bool Toggle(string key)
        {
            try
            {
                return PlayerPrefs.GetInt(key, Stored(key) != 0f ? 1 : 0) != 0;
            }
            catch (Exception)
            {
                // An unreadable preference must not stop the HUD drawing.
                return true;
            }
        }

        private static float Slider(string key)
        {
            try
            {
                return PlayerPrefs.GetFloat(key, Stored(key));
            }
            catch (Exception)
            {
                return Stored(key);
            }
        }

        private static float Stored(string key)
        {
            foreach (ClientPreference preference in ClientPreferences.All)
            {
                if (string.Equals(preference.Key, key, StringComparison.Ordinal))
                {
                    return preference.Default;
                }
            }

            return 0f;
        }
    }
}
