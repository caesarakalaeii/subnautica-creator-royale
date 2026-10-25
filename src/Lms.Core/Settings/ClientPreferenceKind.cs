namespace Lms.Core.Settings
{
    /// <summary>
    ///     The control a preference gets in the settings tab. Admin buttons are listed separately in
    ///     <see cref="ClientPreferences.AdminButtons"/>.
    /// </summary>
    public enum ClientPreferenceKind
    {
        /// <summary>On or off.</summary>
        Toggle,

        /// <summary>A number between two bounds.</summary>
        Slider,
    }
}
