using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;

namespace Lms.Core.Match
{
    /// <summary>
    ///     Turns what an operator typed into a command. Shared by the console and the settings buttons
    ///     so they agree on the words.
    /// </summary>
    public static class AdminCommands
    {
        /// <summary>
        ///     Declared first: static initializers run in textual order, and <see cref="Squeezed"/>
        ///     needs this before anything below it is initialized.
        /// </summary>
        private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.CultureInvariant);

        private static readonly Dictionary<string, AdminCommand> Vocabulary =
            new Dictionary<string, AdminCommand>(StringComparer.OrdinalIgnoreCase)
            {
                { "status", AdminCommand.Status },
                { "start", AdminCommand.Start },
                { "pause", AdminCommand.Pause },
                { "resume", AdminCommand.Resume },
                { "skip", AdminCommand.Skip },
                { "reset", AdminCommand.Reset },
                { "next", AdminCommand.NextGroup },
                // The console takes one word, so accept "next" and "nextgroup".
                { "nextgroup", AdminCommand.NextGroup },
                { "podspawn", AdminCommand.PodSpawn },
                // Shorthand an operator reaches for under pressure.
                { "pods", AdminCommand.PodSpawn },
                { "podclear", AdminCommand.PodClear },
                { PodClearAll, AdminCommand.PodClearAll },
                { "round", AdminCommand.Round },
                { RoundForce, AdminCommand.RoundForce },
                { "ghost", AdminCommand.Ghost },
                { "unghost", AdminCommand.Unghost },
                { "look", AdminCommand.Look },
                // Shorthand a caster says out loud.
                { "cam", AdminCommand.Look },
                { "follow", AdminCommand.Follow },
                { "unfollow", AdminCommand.Unfollow },
                { "followOne", AdminCommand.FollowOne },
            };

        /// <summary>
        ///     The verbs that take a player name. The parser splits it off, the usage line shows a
        ///     placeholder, and the settings tab gives them no button.
        /// </summary>
        private static readonly HashSet<AdminCommand> Subjected = new HashSet<AdminCommand>
        {
            AdminCommand.Ghost, AdminCommand.Unghost, AdminCommand.Look, AdminCommand.Follow,
            AdminCommand.FollowOne,
        };

        /// <summary>A two-word verb; <see cref="TryParse"/> squeezes typed whitespace down to match it.</summary>
        private const string PodClearAll = "podclear all";

        /// <summary>The other two-word verb: the deliberate override of the group-size check.</summary>
        private const string RoundForce = "round force";

        /// <summary>
        ///     The verbs that have to be typed rather than pressed: each bypasses a safety refusal, and
        ///     a settings button has no confirmation.
        /// </summary>
        private static readonly HashSet<AdminCommand> Typed = new HashSet<AdminCommand>
        {
            AdminCommand.PodClearAll, AdminCommand.RoundForce,
        };

        /// <summary>
        ///     One word per command, in <see cref="AdminCommand"/> declaration order; <see cref="Word"/>
        ///     indexes it.
        /// </summary>
        private static readonly ReadOnlyCollection<string> CanonicalVerbs = new ReadOnlyCollection<string>(
            new List<string>
            {
                "status", "start", "pause", "resume", "skip", "reset", "next", "podspawn", "podclear",
                PodClearAll, "round", RoundForce, "ghost", "unghost", "look", "follow",
                "unfollow", "followOne",
            });

        /// <summary>
        ///     The verbs the settings tab puts a button on: every one except those that must be typed
        ///     and those that take a player name.
        /// </summary>
        public static IReadOnlyList<string> Buttons { get; } = new ReadOnlyCollection<string>(
            CanonicalVerbs
                // Vocabulary directly, not TryParse, which depends on fields declared below this one.
                .Where(verb => !Vocabulary.TryGetValue(verb, out AdminCommand command)
                               || (!MustBeTyped(command) && !TakesASubject(command)))
                .ToList());

        /// <summary>One word per command, in the order they come up in a match.</summary>
        public static IReadOnlyList<string> Verbs => CanonicalVerbs;

        /// <summary>
        ///     Everything that can be typed, with name placeholders, for the message an unrecognised word
        ///     gets.
        /// </summary>
        public static string Usage { get; } = string.Join(" | ", CanonicalVerbs.Select(Spelled));

        /// <summary>Whether this verb is about one named player, so a caller knows to expect a subject.</summary>
        public static bool TakesASubject(AdminCommand command) => Subjected.Contains(command);

        /// <summary>Whether this verb has to be typed rather than pressed, so it gets no button.</summary>
        public static bool MustBeTyped(AdminCommand command) => Typed.Contains(command);

        /// <summary>
        ///     The one word that names a command: the inverse of <see cref="TryParse"/>, used for button
        ///     labels and by <see cref="AdminRequest"/>.
        /// </summary>
        public static string Word(AdminCommand command)
        {
            int index = (int)command;

            if (index < 0 || index >= CanonicalVerbs.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(command), command,
                    "A command with no word is a command nothing can ask for.");
            }

            return CanonicalVerbs[index];
        }

        /// <summary>Reads one word as a command. An empty word is <see cref="AdminCommand.Status"/>.</summary>
        public static bool TryParse(string? word, out AdminCommand command) =>
            TryParse(word, out command, out _);

        /// <summary>Reads a command and, for the verbs that take one, the player it is about.</summary>
        /// <param name="word">What the operator typed, verb and all.</param>
        /// <param name="command">The command.</param>
        /// <param name="subject">
        ///     The name after the verb, or empty. Empty is not a parse failure, so the command can be
        ///     refused with a clearer message.
        /// </param>
        public static bool TryParse(string? word, out AdminCommand command, out string subject)
        {
            subject = string.Empty;

            return TryParse(word, out command, out subject, out _);
        }

        /// <summary>
        ///     Reads a command, the player it is about and, for <c>followOne</c>, the ghost it is for.
        /// </summary>
        /// <param name="word">What the operator typed, verb and all.</param>
        /// <param name="command">The command.</param>
        /// <param name="subject">The target after the verb, or empty.</param>
        /// <param name="addressee">
        ///     The ghost the instruction is for, or empty for every camera. Only
        ///     <see cref="AdminCommand.FollowOne"/> sets it.
        /// </param>
        /// <remarks>
        ///     The whole line is looked up first, so <c>podclear all</c> is a verb, not <c>podclear</c>
        ///     with a subject. A verb that takes no subject refuses one: <c>lms start now</c> fails.
        /// </remarks>
        public static bool TryParse(string? word, out AdminCommand command, out string subject,
            out string addressee)
        {
            subject = string.Empty;
            addressee = string.Empty;

            if (string.IsNullOrWhiteSpace(word))
            {
                command = AdminCommand.Status;
                return true;
            }

            string line = Squeezed(word!);

            if (Vocabulary.TryGetValue(line, out command))
            {
                return true;
            }

            int space = line.IndexOf(' ');
            if (space <= 0)
            {
                return false;
            }

            if (!Vocabulary.TryGetValue(line.Substring(0, space), out command))
            {
                command = default;
                return false;
            }

            string rest = line.Substring(space + 1);

            // followOne takes two names: the ghost (first word), then the target (the rest, which may
            // contain spaces). Other subjected verbs take the rest of the line whole.
            if (command == AdminCommand.FollowOne)
            {
                int second = rest.IndexOf(' ');
                if (second > 0)
                {
                    addressee = rest.Substring(0, second);
                    subject = rest.Substring(second + 1);
                }
                else
                {
                    addressee = rest;
                }

                return true;
            }

            if (!TakesASubject(command))
            {
                command = default;
                return false;
            }

            subject = rest;

            return true;
        }

        /// <summary>One entry of the usage line: the verb, and its placeholders if it takes a name.</summary>
        private static string Spelled(string verb)
        {
            if (!Vocabulary.TryGetValue(verb, out AdminCommand command) || !TakesASubject(command))
            {
                return verb;
            }

            // Same order the parser reads them: whose camera first, then who.
            return command == AdminCommand.FollowOne ? verb + " <ghost> <target>" : verb + " <player>";
        }

        /// <summary>
        ///     Whatever was typed, trimmed and with runs of whitespace collapsed to one space, since
        ///     Nitrox splits command lines on spaces and the mod rejoins them.
        /// </summary>
        private static string Squeezed(string word) => Whitespace.Replace(word.Trim(), " ");

    }
}
