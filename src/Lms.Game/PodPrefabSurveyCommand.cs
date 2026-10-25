using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Lms.Core.Diagnostics;
using Nitrox.Model.Logger;
using UnityEngine;
using UWE;

namespace Lms.Game
{
    /// <summary>
    ///     `lmspods`: finds every prefab that could stand in for another player's escape pod, and
    ///     writes what each one is made of.
    /// </summary>
    /// <remarks>
    ///     Nitrox reuses the single <c>EscapePod.main</c> for every pod, so other players' pods need a
    ///     prop prefab. This lists candidates with their renderer count and size: one with renderers
    ///     and no <c>EscapePod</c> component is ideal.
    /// </remarks>
    internal sealed class PodPrefabSurveyCommand : MonoBehaviour
    {
        internal const string CommandName = "lmspods";

        private const string FileName = "lms-pod-prefabs.tsv";

        /// <summary>
        ///     Path fragments that might name a pod. Deliberately loose; a human picks from the output.
        /// </summary>
        private static readonly string[] Wanted = { "escapepod", "lifepod", "life_pod", "pod" };

        private bool running;
        private bool registered;

        private void Awake()
        {
            DevConsole.RegisterConsoleCommand(CommandName, OnCommand);
            registered = true;
            Log.Info($"{LmsMod.LogPrefix} '{CommandName}' will look for prefabs that could stand in " +
                     $"for another player's pod, into {FileName}");
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
                ErrorMessage.AddMessage("LMS: already looking at pod prefabs.");
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

            List<KeyValuePair<string, string>> candidates = Candidates();
            ErrorMessage.AddMessage($"LMS: looking at {candidates.Count} pod-ish prefabs, this takes a moment.");
            Log.Info($"{LmsMod.LogPrefix} looking at {candidates.Count} pod-ish prefabs of " +
                     $"{PrefabDatabase.prefabFiles.Count} in the database");

            List<string> rows = new List<string>();
            int drawable = 0;

            foreach (KeyValuePair<string, string> candidate in candidates)
            {
                IPrefabRequest request = PrefabDatabase.GetPrefabAsync(candidate.Key);
                yield return request;

                string row = Describe(request, candidate.Key, candidate.Value, out bool draws);
                rows.Add(row);

                if (draws)
                {
                    drawable++;
                }

                Log.Info($"{LmsMod.LogPrefix} {row}");
            }

            string summary = $"{candidates.Count} pod-ish prefabs, {drawable} with something to draw";
            Log.Info($"{LmsMod.LogPrefix} {summary}");
            ErrorMessage.AddMessage($"LMS: {summary}");
            Write(summary, rows);

            running = false;
        }

        /// <remarks>
        ///     A prefab with no renderers draws nothing, so it is unusable as a stand-in.
        /// </remarks>
        private static string Describe(IPrefabRequest request, string classId, string path, out bool draws)
        {
            draws = false;

            if (!request.TryGetPrefab(out GameObject prefab))
            {
                return $"{path}\t{classId}\t-\t-\t-\tdid not load";
            }

            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            bool isTheRealPod = prefab.GetComponent<EscapePod>() != null;
            draws = renderers.Length > 0;

            string size = "-";
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                foreach (Renderer renderer in renderers)
                {
                    bounds.Encapsulate(renderer.bounds);
                }

                // Invariant formatting via Readable.
                size = Readable.Line($"{bounds.size.x:0.#}x{bounds.size.y:0.#}x{bounds.size.z:0.#}");
            }

            return $"{path}\t{classId}\t{renderers.Length}\t{size}\t{isTheRealPod}\t" +
                   $"{(draws ? "usable" : "nothing to draw")}";
        }

        /// <remarks>
        ///     Sorted by path so runs are reproducible and a game update shows up as a diff.
        /// </remarks>
        private static List<KeyValuePair<string, string>> Candidates()
        {
            List<KeyValuePair<string, string>> found = new List<KeyValuePair<string, string>>();

            foreach (KeyValuePair<string, string> entry in PrefabDatabase.prefabFiles)
            {
                if (entry.Value == null)
                {
                    continue;
                }

                foreach (string wanted in Wanted)
                {
                    if (entry.Value.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        found.Add(entry);
                        break;
                    }
                }
            }

            found.Sort((left, right) => string.CompareOrdinal(left.Value, right.Value));

            return found;
        }

        private static void Write(string summary, IReadOnlyList<string> rows)
        {
            string directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
            string path = Path.Combine(directory, FileName);

            try
            {
                using (StreamWriter writer = new StreamWriter(path, append: false))
                {
                    writer.WriteLine($"# {summary}");
                    writer.WriteLine($"# read by mod {typeof(PodPrefabSurveyCommand).Assembly.GetName().Version} " +
                                     $"at {DateTime.UtcNow:O}");
                    writer.WriteLine("prefabPath\tclassId\trenderers\tsize\tisEscapePod\toutcome");

                    foreach (string row in rows)
                    {
                        writer.WriteLine(row);
                    }
                }

                Log.Info($"{LmsMod.LogPrefix} wrote '{path}'");
                ErrorMessage.AddMessage($"LMS: written to {FileName}");
            }
            catch (Exception ex)
            {
                // The rows are already in the log.
                Log.Error(ex, $"{LmsMod.LogPrefix} cannot write '{path}'; the rows are in this log instead.");
            }
        }
    }
}
