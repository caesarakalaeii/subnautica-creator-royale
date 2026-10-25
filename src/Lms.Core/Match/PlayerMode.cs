namespace Lms.Core.Match
{
    /// <summary>What game mode one connected player is actually in.</summary>
    /// <remarks>
    ///     The mode is the server's own spelling as a string, since Core cannot reference
    ///     <c>NitroxGameMode</c>; an unfamiliar mode still reads correctly in the audit log.
    /// </remarks>
    public readonly struct PlayerMode
    {
        public PlayerMode(ushort id, string name, string mode)
        {
            Id = id;
            Name = name ?? string.Empty;
            Mode = mode ?? string.Empty;
        }

        /// <summary>The server's own player id, which is how a correction is addressed.</summary>
        public ushort Id { get; }

        /// <summary>The name the tournament knows them by.</summary>
        public string Name { get; }

        /// <summary>The mode the server holds for them, in the server's own spelling.</summary>
        public string Mode { get; }

        public override string ToString() => $"{Name} in {Mode}";
    }
}
