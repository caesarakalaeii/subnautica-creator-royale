using System;
using Lms.Core.Biomes;
using Lms.Core.Diagnostics;
using Nitrox.Model.Logger;
using UnityEngine;

namespace Lms.Game
{
    /// <summary>
    ///     Samples the biome under the local player, asks <c>Lms.Core</c> what it is, and puts the
    ///     answer on screen. A biome is only a label; it decides nothing.
    /// </summary>
    internal sealed class BiomeSampler : MonoBehaviour
    {
        // The biome only changes across a batch seam, so per-frame sampling buys nothing.
        private const float SampleIntervalSeconds = 0.25f;

        /// <summary>Which strings have already been reported, so a new one is said once.</summary>
        private readonly BiomeSightingLog sightings = new BiomeSightingLog();

        private BiomeResolution? current;

        /// <summary>
        ///     The most recent sample, shared so <c>GetBiome</c> runs once per interval.
        /// </summary>
        internal BiomeResolution? Current => current;

        /// <summary>
        ///     What to call the place: the tournament's name, else the game's raw string, else null
        ///     before the first sample.
        /// </summary>
        internal string? Named => current == null ? null : current.Biome?.Identifier ?? current.RawValue;

        private float nextSampleTime;
        private GUIStyle? readoutStyle;

        private void Awake()
        {
            Log.Info(Readable.Line(
                $"{LmsMod.LogPrefix} sampling biomes every {SampleIntervalSeconds:0.##}s, ",
                $"naming {KnownBiomes.Playable.Count} playable biomes"));
        }

        private void Update()
        {
            if (Time.time < nextSampleTime)
            {
                return;
            }
            nextSampleTime = Time.time + SampleIntervalSeconds;

            if (Player.main == null || LargeWorld.main == null)
            {
                current = null;
                return;
            }

            // A per-batch override, a biomes.csv name, or null when the world is not mounted.
            string rawBiome = LargeWorld.main.GetBiome(Player.main.transform.position);
            current = BiomeResolver.Resolve(rawBiome);

            BiomeSighting? firstSighting = sightings.Record(current);
            if (firstSighting != null)
            {
                Report(firstSighting);
            }
        }

        private void Report(BiomeSighting sighting)
        {
            if (sighting.IsResolved)
            {
                Log.Info($"{LmsMod.LogPrefix} new biome string '{sighting.RawValue}' -> " +
                         $"{sighting.Biome} ({(sighting.ResolvedByPrefix ? "by prefix" : "exact")}, {sighting.Kind})");
            }
            else
            {
                // Logged and shown in game so whoever is there can say where it happened.
                Log.Warn($"{LmsMod.LogPrefix} UNRESOLVED biome string '{sighting.RawValue}' ({sighting.Failure})");
                Log.InGame($"LMS: unresolved biome '{sighting.RawValue}'");
            }
        }

        private void OnGUI()
        {
            // A preference: the developer readout is noise on a creator's stream.
            if (current == null || !Preferences.ShowBiomeReadout)
            {
                return;
            }

            readoutStyle ??= new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(10, 10, 8, 8)
            };

            GUI.Box(new Rect(12f, 12f, 460f, 62f), Readout(current), readoutStyle);
        }

        private string Readout(BiomeResolution resolution)
        {
            string biomeLine;
            if (resolution.IsResolved)
            {
                biomeLine = $"{resolution.Biome!.Identifier}  [{resolution.Kind}]";
                if (!string.Equals(resolution.RawValue?.Trim(), resolution.Biome.Identifier, StringComparison.Ordinal))
                {
                    biomeLine += $"   from '{resolution.RawValue}'";
                }
            }
            else
            {
                biomeLine = $"unresolved  '{resolution.RawValue ?? "<nothing reported>"}'  ({resolution.Failure})";
            }

            return $"LMS biome readout\n{biomeLine}\n" +
                   $"{sightings.Sightings.Count} distinct strings seen, {sightings.UnresolvedCount} unresolved";
        }
    }
}
