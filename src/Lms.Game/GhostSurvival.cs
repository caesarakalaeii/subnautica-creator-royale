using Nitrox.Model.Logger;
using UnityEngine;

namespace Lms.Game
{
    /// <summary>
    ///     Keeps a ghost alive: invincible, with air, food and water topped up.
    /// </summary>
    /// <remarks>
    ///     Not the game's creative mode: Nitrox persists game mode per player, so a ghost who later
    ///     contests would keep a creative body. Invincibility is cleared again when the role ends.
    /// </remarks>
    internal sealed class GhostSurvival : MonoBehaviour
    {
        /// <summary>How long between top-ups. Vitals drain in seconds, not frames.</summary>
        private const float Interval = 1f;

        /// <summary>
        ///     Seconds of air added per top-up. More than the interval so the tank fills; the oxygen
        ///     manager clamps to capacity.
        /// </summary>
        private const float Added = 10f;

        /// <summary>How much food and water a top-up restores, in the game's own points.</summary>
        private const int Eaten = 100;

        private LmsRuntime? runtime;
        private float nextTopUp;
        private bool saidSo;
        private bool madeInvincible;

        private void Awake()
        {
            runtime = GetComponent<LmsRuntime>();

            if (runtime == null)
            {
                enabled = false;
            }
        }

        private void Update()
        {
            if (runtime == null || Player.main == null)
            {
                return;
            }

            if (!runtime.IsGhost)
            {
                // Not `enabled = false`: `lms ghost` can make a player a ghost mid-session.
                Restore();
                return;
            }

            // Starvation is not damage, so invincibility alone is not enough; vitals are topped up below.
            LiveMixin? live = Player.main.liveMixin;
            if (live != null && !live.invincible)
            {
                live.invincible = true;
                madeInvincible = true;
            }

            if (Time.time < nextTopUp)
            {
                return;
            }

            nextTopUp = Time.time + Interval;

            Player.main.oxygenMgr?.AddOxygen(Added);

            Survival? survival = Player.main.GetComponent<Survival>();
            if (survival != null)
            {
                survival.food = Mathf.Max(survival.food, Eaten);
                survival.water = Mathf.Max(survival.water, Eaten);
            }

            if (!saidSo)
            {
                saidSo = true;
                Log.Info($"{LmsMod.LogPrefix} you are a ghost, so your health, air, food and " +
                         "water are being kept whole");
            }
        }

        /// <summary>Puts the body back the way survival found it, on the tick the role ends.</summary>
        private void Restore()
        {
            if (!madeInvincible)
            {
                return;
            }

            Player.main.liveMixin.invincible = false;
            madeInvincible = false;
        }
    }
}
