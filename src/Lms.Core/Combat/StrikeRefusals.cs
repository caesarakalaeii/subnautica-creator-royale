using System;

namespace Lms.Core.Combat
{
    /// <summary>
    ///     The wire word and the on-screen sentence for each <see cref="StrikeRefusal"/>.
    /// </summary>
    /// <remarks>
    ///     Sentences live in Core so the attacker's screen and the audit log agree. They carry no
    ///     numbers, since reach and interval are the server's.
    /// </remarks>
    public static class StrikeRefusals
    {
        /// <summary>The token that goes on the wire. Renaming one is a wire change.</summary>
        public static string Word(StrikeRefusal refusal)
        {
            switch (refusal)
            {
                case StrikeRefusal.None:
                    return "none";

                case StrikeRefusal.PrawnsAreHarmless:
                    return "harmless";

                case StrikeRefusal.BladesAreHarmless:
                    return "blades-harmless";

                case StrikeRefusal.ArmIsDisabled:
                    return "arm-off";

                case StrikeRefusal.BladeIsDisabled:
                    return "blade-off";

                case StrikeRefusal.MatchIsNotRunning:
                    return "not-running";

                case StrikeRefusal.YouAreAGhost:
                    return "ghost";

                case StrikeRefusal.TheRingHasYou:
                    return "held";

                case StrikeRefusal.NotInAPrawn:
                    return "no-prawn";

                case StrikeRefusal.NotHoldingThatBlade:
                    return "no-blade";

                case StrikeRefusal.YouAreInAVehicle:
                    return "in-a-vehicle";

                case StrikeRefusal.TheyAreAlreadyOut:
                    return "already-out";

                case StrikeRefusal.OutOfRange:
                    return "out-of-range";

                case StrikeRefusal.TooSoon:
                    return "too-soon";

                case StrikeRefusal.TheBuildsDiffer:
                    return "stale-build";

                default:
                    throw new ArgumentOutOfRangeException(nameof(refusal), refusal,
                        "No word for that refusal.");
            }
        }

        /// <summary>Reads a word back, or declines one this build does not know.</summary>
        public static bool TryParse(string? word, out StrikeRefusal refusal)
        {
            refusal = StrikeRefusal.None;

            if (word == null)
            {
                return false;
            }

            foreach (StrikeRefusal candidate in Enum.GetValues(typeof(StrikeRefusal)))
            {
                if (string.Equals(Word(candidate), word.Trim(), StringComparison.Ordinal))
                {
                    refusal = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        ///     What the attacker is told, in one line.
        /// </summary>
        public static string Sentence(StrikeRefusal refusal)
        {
            switch (refusal)
            {
                case StrikeRefusal.None:
                    return "that landed";

                case StrikeRefusal.PrawnsAreHarmless:
                    return "prawn arms do not hurt players in this tournament";

                case StrikeRefusal.BladesAreHarmless:
                    return "blades do not hurt players in this tournament";

                case StrikeRefusal.ArmIsDisabled:
                    return "that arm does nothing in this tournament";

                case StrikeRefusal.BladeIsDisabled:
                    return "that blade does nothing in this tournament";

                case StrikeRefusal.MatchIsNotRunning:
                    return "no match is running, so nobody is being hurt";

                case StrikeRefusal.YouAreAGhost:
                    return "you are a caster: the match cannot see you, so it cannot see your swings";

                case StrikeRefusal.TheRingHasYou:
                    return "the ring has you -- you are not swinging anything";

                case StrikeRefusal.NotInAPrawn:
                    return "you are not in a prawn suit";

                case StrikeRefusal.NotHoldingThatBlade:
                    return "you are not holding that";

                case StrikeRefusal.YouAreInAVehicle:
                    return "you cannot use a blade from in here";

                case StrikeRefusal.TheyAreAlreadyOut:
                    return "they are already out of the match";

                case StrikeRefusal.OutOfRange:
                    return "too far away -- the server did not believe that reached";

                case StrikeRefusal.TooSoon:
                    return "too soon after the last one";

                case StrikeRefusal.TheBuildsDiffer:
                    return "this server could not read that swing -- it is not running your build";

                default:
                    throw new ArgumentOutOfRangeException(nameof(refusal), refusal,
                        "No sentence for that refusal.");
            }
        }
    }
}
