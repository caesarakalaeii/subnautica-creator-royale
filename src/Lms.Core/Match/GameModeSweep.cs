using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Lms.Core.Match
{
    /// <summary>
    ///     Who a round has to put back into survival, and who it could not see. Two lists, so "nothing
    ///     to correct" is never confused with "could not tell".
    /// </summary>
    public sealed class GameModeSweep
    {
        private static readonly ReadOnlyCollection<PlayerMode> NobodyWrong =
            new ReadOnlyCollection<PlayerMode>(new PlayerMode[0]);

        private static readonly ReadOnlyCollection<string> NobodyUnseen =
            new ReadOnlyCollection<string>(new string[0]);

        internal GameModeSweep(IReadOnlyList<PlayerMode>? wrong, IReadOnlyList<string>? unseen)
        {
            Wrong = wrong == null || wrong.Count == 0
                ? NobodyWrong
                : new ReadOnlyCollection<PlayerMode>(new List<PlayerMode>(wrong));

            Unseen = unseen == null || unseen.Count == 0
                ? NobodyUnseen
                : new ReadOnlyCollection<string>(new List<string>(unseen));
        }

        /// <summary>Contestants holding a mode that is not survival, in name order.</summary>
        public IReadOnlyList<PlayerMode> Wrong { get; }

        /// <summary>Contestants the server returned no mode for, in name order.</summary>
        public IReadOnlyList<string> Unseen { get; }

        /// <summary>Nothing to correct and nobody unaccounted for.</summary>
        public bool IsClean => Wrong.Count == 0 && Unseen.Count == 0;

        /// <summary>Why a round must not start on this reading, or null when it may.</summary>
        /// <remarks>
        ///     A wrong mode is corrected, but an unreadable one refuses the round: it may be creative,
        ///     which takes no damage while looking normal.
        /// </remarks>
        public string? Refusal =>
            Unseen.Count == 0
                ? null
                : $"could not read the game mode of {string.Join(", ", Unseen)}. A mode the seam " +
                  "cannot read may be a creative one, and a creative body takes no damage from the " +
                  "wall";

        /// <summary>One line for whoever typed the verb, naming who and what they were in.</summary>
        public string Message
        {
            get
            {
                if (IsClean)
                {
                    return "every contestant was already in survival.";
                }

                List<string> parts = new List<string>(2);

                if (Wrong.Count > 0)
                {
                    List<string> named = new List<string>(Wrong.Count);
                    foreach (PlayerMode player in Wrong)
                    {
                        named.Add($"{player.Name} was in {player.Mode}");
                    }

                    parts.Add($"put {Wrong.Count} contestant(s) back into survival -- " +
                              string.Join(", ", named));
                }

                if (Unseen.Count > 0)
                {
                    parts.Add($"could not read the mode of {string.Join(", ", Unseen)}");
                }

                return string.Join("; ", parts) + ".";
            }
        }

        public override string ToString() => Message;
    }

    /// <summary>Decides which contestants a round puts back into survival before it starts.</summary>
    /// <remarks>
    ///     Nitrox persists game mode per player, so a SURVIVAL server can still hand a returning player
    ///     a creative body, which <c>LiveMixin.TakeDamage</c> ignores. Ghosts are never touched.
    /// </remarks>
    public static class GameModeGuard
    {
        /// <summary>The one mode a tournament is played in.</summary>
        public const string Survival = "SURVIVAL";

        /// <summary>Works out who needs correcting. Pure: it changes nothing and asks nothing of a server.</summary>
        /// <param name="contestants">Who is in the round: connected minus ghosts.</param>
        /// <param name="modes">What the server says each connected player's mode is.</param>
        public static GameModeSweep Plan(IReadOnlyList<string> contestants,
            IReadOnlyList<PlayerMode> modes)
        {
            if (contestants == null || contestants.Count == 0)
            {
                return new GameModeSweep(null, null);
            }

            Dictionary<string, PlayerMode> byName =
                new Dictionary<string, PlayerMode>(StringComparer.OrdinalIgnoreCase);

            if (modes != null)
            {
                foreach (PlayerMode player in modes)
                {
                    // First reading for a name wins, so nobody is corrected twice.
                    if (!byName.ContainsKey(player.Name))
                    {
                        byName[player.Name] = player;
                    }
                }
            }

            List<PlayerMode> wrong = new List<PlayerMode>();
            List<string> unseen = new List<string>();

            foreach (string name in contestants)
            {
                if (!byName.TryGetValue(name ?? string.Empty, out PlayerMode player))
                {
                    unseen.Add(name ?? string.Empty);
                    continue;
                }

                if (!IsSurvival(player.Mode))
                {
                    wrong.Add(player);
                }
            }

            // Ordinal sort so the audit record is stable between runs.
            wrong.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
            unseen.Sort(StringComparer.Ordinal);

            return new GameModeSweep(wrong, unseen);
        }

        /// <summary>
        ///     Whether a mode is the one a tournament is played in. Only SURVIVAL counts: FREEDOM has no
        ///     hunger and HARDCORE deletes the save on death.
        /// </summary>
        public static bool IsSurvival(string? mode) =>
            mode != null && mode.Trim().Equals(Survival, StringComparison.OrdinalIgnoreCase);
    }
}
