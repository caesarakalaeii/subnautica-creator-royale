using Lms.Core.Match;

namespace Lms.Core.Telemetry
{
    /// <summary>
    ///     Serialises the console channel document, which is separate from the telemetry contract.
    /// </summary>
    /// <remarks>
    ///     Hand-written like <see cref="TelemetryJson"/>: <c>Lms.Core</c> targets <c>netstandard2.0</c>
    ///     under Unity's Mono, where <c>System.Text.Json</c> would pull in extra assemblies.
    /// </remarks>
    public static class ConsoleJson
    {
        /// <summary>
        ///     The version of this document, so a booth can refuse one it does not speak. Independent of
        ///     the telemetry contract version.
        /// </summary>
        public const int Version = 1;

        public static string Write(ConsoleReport report)
        {
            JsonText json = new JsonText();

            json.OpenObject();
            json.Number("v", Version);
            json.Time("at", report.At);
            json.Flag("acceptsCommands", report.AcceptsCommands);
            json.Number("ran", report.Ran);
            json.Number("rulesRevision", report.RulesRevision);

            // The running config verbatim; the console's editor prefills with it.
            json.Text("config", report.Running);

            json.OpenArray("connected");
            foreach (ConsolePresence presence in report.Connected)
            {
                json.OpenObject();
                json.Text("name", presence.Name);
                json.Flag("inMatch", presence.InMatch);
                json.CloseObject();
            }

            json.CloseArray();

            json.OpenArray("verbs");
            foreach (ConsoleVerb verb in report.Verbs)
            {
                json.OpenObject();
                json.Text("verb", verb.Verb);
                json.Flag("takesSubject", verb.TakesASubject);
                json.Flag("mustBeTyped", verb.MustBeTyped);
                json.CloseObject();
            }

            json.CloseArray();

            json.OpenArray("answers");
            foreach (BoothCommandAnswer answer in report.Answers)
            {
                json.OpenObject();
                json.Number("id", answer.Id);
                json.Text("verb", answer.Verb);

                // Verbatim: replies are written for the operator.
                json.Text("answer", answer.Answer);
                json.CloseObject();
            }

            json.CloseArray();

            json.OpenArray("rules");
            foreach (BoothRulesOutcome outcome in report.Rules)
            {
                json.OpenObject();
                json.Number("revision", outcome.Revision);
                json.Flag("adopted", outcome.Adopted);
                json.Flag("final", outcome.Final);
                json.Text("message", outcome.Message);

                // The loader's field-path problems verbatim; the booth does no validation of its own.
                json.OpenArray("problems");
                foreach (string problem in outcome.Problems)
                {
                    json.OpenObject();
                    json.Text("problem", problem);
                    json.CloseObject();
                }

                json.CloseArray();
                json.CloseObject();
            }

            json.CloseArray();
            json.CloseObject();

            return json.ToString();
        }

        /// <summary>
        ///     Every admin verb, in match order, built from <see cref="AdminCommands"/> so the console
        ///     cannot drift from it.
        /// </summary>
        public static ConsoleVerb[] Vocabulary()
        {
            System.Collections.Generic.IReadOnlyList<string> verbs = AdminCommands.Verbs;
            ConsoleVerb[] described = new ConsoleVerb[verbs.Count];

            for (int i = 0; i < verbs.Count; i++)
            {
                string word = verbs[i];

                if (!AdminCommands.TryParse(word, out AdminCommand command))
                {
                    // Unreachable for the vocabulary's own words; still offered rather than withheld.
                    described[i] = new ConsoleVerb(word, false, false);
                    continue;
                }

                described[i] = new ConsoleVerb(word,
                    AdminCommands.TakesASubject(command), AdminCommands.MustBeTyped(command));
            }

            return described;
        }
    }
}
