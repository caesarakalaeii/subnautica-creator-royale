using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Lms.Core.Biomes;
using Nitrox.Model.Logger;
using UnityEngine;
using UWE;

namespace Lms.Game
{
    /// <summary>
    ///     `lmsbiomes`: reads the biome string off every atmosphere prefab and writes the lot to a
    ///     file.
    /// </summary>
    /// <remarks>
    ///     A batch's <c>overrideBiome</c> comes from an <c>AtmosphereVolume</c> inside an addressable
    ///     bundle that offline extraction cannot read, so this asks the running game, using the same
    ///     <c>PrefabDatabase</c> lookup and root <c>GetComponent</c> as <c>PullSettingsFromPrefab</c>.
    /// </remarks>
    internal sealed class AtmosphereSurveyCommand : MonoBehaviour
    {
        internal const string CommandName = "lmsbiomes";

        private const string AtmosphereFolder = "WorldEntities/Atmosphere/";
        private const string FileName = "lms-atmosphere-biomes.tsv";

        private bool running;
        private bool registered;

        private void Awake()
        {
            DevConsole.RegisterConsoleCommand(CommandName, OnCommand);
            registered = true;
            Log.Info($"{LmsMod.LogPrefix} '{CommandName}' will read every atmosphere prefab's biome into {FileName}");
        }

        private void OnDestroy()
        {
            if (registered)
            {
                DevConsole.UnregisterConsoleCommand(CommandName, OnCommand);
            }
        }

        private void OnCommand(NotificationCenter.Notification notification)
        {
            if (running)
            {
                ErrorMessage.AddMessage("LMS: already reading the atmosphere prefabs.");
                return;
            }

            // Console dispatch is synchronous and each prefab load yields, hence a coroutine.
            StartCoroutine(Survey());
        }

        private IEnumerator Survey()
        {
            running = true;

            if (PrefabDatabase.prefabFiles.Count == 0)
            {
                // Loaded lazily by the game; from the main menu it would otherwise be empty.
                Log.Info($"{LmsMod.LogPrefix} prefab database is empty; loading it first");
                PrefabDatabase.LoadPrefabDatabase(SNUtils.prefabDatabaseFilename);
            }

            List<KeyValuePair<string, string>> prefabs = AtmospherePrefabs();
            ErrorMessage.AddMessage($"LMS: reading {prefabs.Count} atmosphere prefabs, this takes a moment.");
            Log.Info($"{LmsMod.LogPrefix} reading {prefabs.Count} atmosphere prefabs");

            AtmospherePrefabSurvey survey = new AtmospherePrefabSurvey();
            foreach (KeyValuePair<string, string> prefab in prefabs)
            {
                IPrefabRequest request = PrefabDatabase.GetPrefabAsync(prefab.Key);
                yield return request;

                survey.Record(Read(request, prefab.Key, prefab.Value));
            }

            Log.Info($"{LmsMod.LogPrefix} {survey.Summary}");
            ErrorMessage.AddMessage($"LMS: {survey.Summary}");
            Write(survey);

            running = false;
        }

        private static AtmospherePrefabBiome Read(IPrefabRequest request, string classId, string path)
        {
            if (!request.TryGetPrefab(out GameObject prefab))
            {
                return AtmospherePrefabBiome.Missing(classId, path);
            }

            // Root only: PullSettingsFromPrefab uses GetComponent, not GetComponentInChildren.
            AtmosphereVolume volume = prefab.GetComponent<AtmosphereVolume>();
            return volume == null
                ? AtmospherePrefabBiome.WithoutVolume(classId, path)
                : AtmospherePrefabBiome.From(classId, path, volume.overrideBiome);
        }

        /// <remarks>
        ///     Sorted by path so runs are reproducible and a game update shows up as a diff.
        /// </remarks>
        private static List<KeyValuePair<string, string>> AtmospherePrefabs()
        {
            List<KeyValuePair<string, string>> found = new List<KeyValuePair<string, string>>();
            foreach (KeyValuePair<string, string> entry in PrefabDatabase.prefabFiles)
            {
                if (entry.Value != null && entry.Value.IndexOf(AtmosphereFolder, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    found.Add(entry);
                }
            }

            found.Sort((left, right) => string.CompareOrdinal(left.Value, right.Value));
            return found;
        }

        private static void Write(AtmospherePrefabSurvey survey)
        {
            string directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
            string path = Path.Combine(directory, FileName);

            try
            {
                // Overwritten: one file is one complete reading of one build.
                using (StreamWriter writer = new StreamWriter(path, append: false))
                {
                    writer.WriteLine($"# {survey.Summary}");
                    writer.WriteLine($"# read by mod {typeof(AtmosphereSurveyCommand).Assembly.GetName().Version} " +
                                     $"core {typeof(AtmospherePrefabSurvey).Assembly.GetName().Version} at {DateTime.UtcNow:O}");
                    writer.WriteLine("prefabPath\tclassId\toverrideBiome\tresolved\tkind\toutcome");
                    foreach (AtmospherePrefabBiome record in survey.Records)
                    {
                        writer.WriteLine(record.ToString());
                    }
                }

                Log.Info($"{LmsMod.LogPrefix} wrote '{path}'");
                ErrorMessage.AddMessage($"LMS: written to {FileName}");
            }
            catch (Exception ex)
            {
                // The rows go to the log instead.
                Log.Error(ex, $"{LmsMod.LogPrefix} cannot write '{path}'; the rows are in this log instead:");
                foreach (AtmospherePrefabBiome record in survey.Records)
                {
                    Log.Info($"{LmsMod.LogPrefix} {record}");
                }
            }
        }
    }
}
