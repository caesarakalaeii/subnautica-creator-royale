using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace Lms.Core.Match
{
    /// <summary>What the server tells one contestant a round is starting with.</summary>
    /// <remarks>
    ///     The server decides the kit; the client clears its own inventory, since only it owns
    ///     <c>Inventory.main</c>. Point-to-point, names no player; group and generation fence it.
    /// </remarks>
    public readonly struct RoundBroadcast : IEquatable<RoundBroadcast>
    {
        /// <summary>The first token of the wire form. It moves when the meaning of a field moves.</summary>
        public const string WireHeader = "lms-round/1";

        private const string GroupField = "group";
        private const string GenerationField = "gen";
        private const string HoldField = "hold";
        private const string ItemField = "item";

        /// <summary>
        ///     The starting vitals (<c>start.vitals</c>), one key per scale. Absent means the client
        ///     applies nothing.
        /// </summary>
        private const string HealthField = "health";
        private const string FoodField = "food";
        private const string WaterField = "water";

        /// <summary>
        ///     The recipes this round withholds (<c>start.lockedRecipes</c>). Only the client can
        ///     enumerate "everything", via <c>KnownTech.GetAllUnlockables()</c>.
        /// </summary>
        private const string LockedField = "locked";

        /// <summary>
        ///     The blueprint floor this round grants (<c>start.recipes</c>), on top of the vanilla
        ///     baseline the game re-grants itself; <c>all</c> means everything.
        /// </summary>
        private const string RecipeField = "recipe";

        private static readonly ReadOnlyCollection<string> Nothing =
            new ReadOnlyCollection<string>(new string[0]);

        private readonly ReadOnlyCollection<string>? kit;
        private readonly ReadOnlyCollection<string>? locked;
        private readonly ReadOnlyCollection<string>? recipes;

        private readonly RoundVitals? vitals;

        /// <param name="group">Which of the matches this is about.</param>
        /// <param name="generation">Which attempt at it, which is the fence a stale instruction fails.</param>
        /// <param name="hold">How long the contestant is held for. Zero is no hold.</param>
        /// <param name="kit">What the inventory is cleared to. Empty means empty hands.</param>
        /// <param name="locked">
        ///     Which recipes the grant withholds. Subtracts from <paramref name="recipes"/>, so it only
        ///     matters when the floor is <c>all</c>.
        /// </param>
        /// <param name="recipes">
        ///     The floor this round grants on top of vanilla. Empty means vanilla only; <c>all</c> means
        ///     every blueprint.
        /// </param>
        /// <param name="vitals">The starting vitals, or null for the client to apply nothing.</param>
        public RoundBroadcast(int group, int generation, TimeSpan hold, IReadOnlyList<string>? kit,
            IReadOnlyList<string>? locked = null, IReadOnlyList<string>? recipes = null,
            RoundVitals? vitals = null)
        {
            Group = group;
            Generation = generation;
            Hold = hold;
            this.kit = Named(kit);
            this.locked = Named(locked);
            this.recipes = Named(recipes);
            this.vitals = vitals;
        }

        /// <summary>The names, trimmed, with the blanks dropped.</summary>
        private static ReadOnlyCollection<string> Named(IReadOnlyList<string>? names)
        {
            if (names == null || names.Count == 0)
            {
                return Nothing;
            }

            List<string> named = new List<string>(names.Count);
            foreach (string name in names)
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    named.Add(name.Trim());
                }
            }

            return new ReadOnlyCollection<string>(named);
        }

        public int Group { get; }

        public int Generation { get; }

        /// <summary>How long this contestant is held before the match starts.</summary>
        public TimeSpan Hold { get; }

        /// <summary>Whether they are held at all.</summary>
        public bool Holds => Hold > TimeSpan.Zero;

        /// <summary>What the inventory is cleared to, by the game's own item names.</summary>
        public IReadOnlyList<string> Kit => kit ?? Nothing;

        /// <summary>
        ///     The vitals the round starts the contestant with, as percentages. Null means apply
        ///     nothing.
        /// </summary>
        public RoundVitals? Vitals => vitals;

        /// <summary>Which recipes the grant withholds, by the game's own names.</summary>
        public IReadOnlyList<string> Locked => locked ?? Nothing;

        /// <summary>The floor this round grants, by the game's own names. Empty is the vanilla baseline.</summary>
        public IReadOnlyList<string> Recipes => recipes ?? Nothing;

        /// <summary>The instruction as one line of text, the same shape the other messages use.</summary>
        public string Encode()
        {
            StringBuilder line = new StringBuilder(80);

            line.Append(WireHeader);
            Pair(line, GroupField, Group.ToString(CultureInfo.InvariantCulture));
            Pair(line, GenerationField, Generation.ToString(CultureInfo.InvariantCulture));
            Pair(line, HoldField,
                ((long)Hold.TotalMilliseconds).ToString(CultureInfo.InvariantCulture));

            foreach (string item in Kit)
            {
                Pair(line, ItemField, item);
            }

            foreach (string recipe in Locked)
            {
                Pair(line, LockedField, recipe);
            }

            foreach (string recipe in Recipes)
            {
                Pair(line, RecipeField, recipe);
            }
            // Vitals go last so lines without them keep the same prefix.
            if (vitals is RoundVitals starting)
            {
                Pair(line, HealthField, starting.Health.ToString(CultureInfo.InvariantCulture));
                Pair(line, FoodField, starting.Food.ToString(CultureInfo.InvariantCulture));
                Pair(line, WaterField, starting.Water.ToString(CultureInfo.InvariantCulture));
            }

            return line.ToString();
        }

        /// <summary>Whether a line is one of these, without reading it.</summary>
        public static bool IsRound(string? line) =>
            line != null && line.StartsWith(WireHeader, StringComparison.Ordinal);

        /// <summary>
        ///     Reads an instruction back, declining it when anything required is missing, so an
        ///     inventory is never emptied on a half-understood message.
        /// </summary>
        public static bool TryDecode(string? line, out RoundBroadcast round)
        {
            round = default;

            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            string[] tokens = line!.Split('\t');
            if (tokens[0] != WireHeader)
            {
                return false;
            }

            int? group = null;
            int? generation = null;
            TimeSpan? hold = null;
            int? health = null;
            int? food = null;
            int? water = null;
            List<string> kit = new List<string>();
            List<string> locked = new List<string>();
            List<string> recipes = new List<string>();

            for (int i = 1; i < tokens.Length; i++)
            {
                string token = tokens[i];
                int split = token.IndexOf('=');
                if (split < 1)
                {
                    continue;
                }

                string key = token.Substring(0, split);
                string value = token.Substring(split + 1);

                switch (key)
                {
                    case GroupField:
                        group = Whole(value);
                        break;

                    case GenerationField:
                        generation = Whole(value);
                        break;

                    case HoldField:
                        long? ms = Long(value);
                        hold = ms == null ? (TimeSpan?)null : TimeSpan.FromMilliseconds(ms.Value);
                        break;

                    case ItemField:
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            kit.Add(value);
                        }

                        break;

                    case LockedField:
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            locked.Add(value);
                        }

                        break;

                    case RecipeField:
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            recipes.Add(value);
                        }

                        break;

                    case HealthField:
                        health = Whole(value);
                        break;

                    case FoodField:
                        food = Whole(value);
                        break;

                    case WaterField:
                        water = Whole(value);
                        break;
                }
            }

            if (group == null || generation == null || hold == null || hold.Value < TimeSpan.Zero)
            {
                return false;
            }

            round = new RoundBroadcast(group.Value, generation.Value, hold.Value, kit, locked, recipes,
                health == null && food == null && water == null
                    ? null
                    : new RoundVitals(health ?? RoundVitals.Maximum, food ?? RoundVitals.Maximum,
                        water ?? RoundVitals.Maximum));

            return true;
        }

        public bool Equals(RoundBroadcast other)
        {
            if (Group != other.Group || Generation != other.Generation || Hold != other.Hold)
            {
                return false;
            }

            if (!Nullable.Equals(Vitals, other.Vitals))
            {
                return false;
            }

            return Same(Kit, other.Kit) && Same(Locked, other.Locked) && Same(Recipes, other.Recipes);
        }

        public override bool Equals(object? obj) => obj is RoundBroadcast other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Group * 397 ^ Generation;
                hash = hash * 397 ^ Hold.GetHashCode();

                foreach (string item in Kit)
                {
                    hash = hash * 397 ^ item.GetHashCode();
                }

                foreach (string recipe in Locked)
                {
                    hash = hash * 397 ^ recipe.GetHashCode();
                }

                foreach (string recipe in Recipes)
                {
                    hash = hash * 397 ^ recipe.GetHashCode();
                }

                hash = hash * 397 ^ Vitals.GetHashCode();

                return hash;
            }
        }

        public override string ToString() =>
            $"round {Group}.{Generation}: " +
            (Kit.Count == 0 ? "empty handed" : string.Join(", ", Kit)) +
            (Holds ? $", held {Hold.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)}s" : string.Empty) +
            (Recipes.Count == 0
                ? ", the vanilla recipes"
                : $", {string.Join(", ", Recipes)}") +
            (Locked.Count == 0 ? string.Empty : $", but not {string.Join(", ", Locked)}") +
            (Vitals.HasValue
                ? $", starting {Vitals.Value.Health}/{Vitals.Value.Food}/{Vitals.Value.Water}"
                : string.Empty);

        /// <summary>Ordinal list equality, so <c>RoundKit</c> can tell a resend from a new round.</summary>
        private static bool Same(IReadOnlyList<string> left, IReadOnlyList<string> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static void Pair(StringBuilder line, string key, string value)
        {
            line.Append('\t').Append(key).Append('=').Append(value);
        }

        private static int? Whole(string value) =>
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : (int?)null;

        private static long? Long(string value) =>
            long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
                ? parsed
                : (long?)null;
    }
}
