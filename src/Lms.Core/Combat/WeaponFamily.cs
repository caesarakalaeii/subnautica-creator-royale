namespace Lms.Core.Combat
{
    /// <summary>
    ///     Which <see cref="StrikePolicy"/> a weapon is judged against; each family has its own config
    ///     section.
    /// </summary>
    public enum WeaponFamily
    {
        /// <summary>A prawn suit's arms. <c>prawn:</c> in the config.</summary>
        PrawnArm,

        /// <summary>The knife and the heat blade. <c>blade:</c> in the config.</summary>
        Blade,
    }
}
