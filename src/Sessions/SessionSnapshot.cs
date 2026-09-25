namespace Loupedeck.CopilotCLIPlugin.Sessions
{
    using System;

    /// <summary>What a session is doing, as recorded by keypad-hook.sh.</summary>
    public enum SessionActivity
    {
        /// <summary>The state file said something this build does not know about.</summary>
        Unknown = 0,

        /// <summary>Session is up, nothing asked yet.</summary>
        Idle,

        /// <summary>The agent is working.</summary>
        Busy,

        /// <summary>The agent is asking you something: a permission prompt or an elicitation
        /// dialog. Written only for a notification the installed hook file matched, so it always
        /// means the session is blocked on you.</summary>
        Attention,

        /// <summary>The turn finished. Your move.</summary>
        Done,
    }

    /// <summary>The terminal a session is running in, which decides how it is focused.</summary>
    public enum TerminalKind
    {
        /// <summary>Recorded by a hook version this build does not know, or not recorded at all.</summary>
        Unknown = 0,

        /// <summary>Warp. Focused with a warp://session deep link.</summary>
        Warp,

        /// <summary>iTerm2. Focused by selecting the session with that id over AppleScript.</summary>
        ITerm,
    }

    /// <summary>Which of the top-level buttons a session belongs behind.</summary>
    public enum SessionBucket
    {
        /// <summary>The agent is working; nothing is expected of you.</summary>
        Active,

        /// <summary>Your move, genuinely: the agent is asking you something, or it finished a turn
        /// and is holding for your next prompt.</summary>
        Waiting,

        /// <summary>Open, but wanting nothing and doing nothing - an idle session, or a state this
        /// build does not recognise. Shows in All only.</summary>
        Other,
    }

    /// <summary>
    /// One Copilot CLI session in one Warp pane, as read off disk.
    ///
    /// Deliberately free of PluginApi types: PluginApi.dll cannot be loaded in a test host, so
    /// everything worth asserting on lives here and in <see cref="SessionClassifier"/>.
    /// </summary>
    public sealed class SessionSnapshot
    {
        /// <summary>
        /// The session's identity, normalised to 32 lowercase hex.
        ///
        /// Both terminals' identifiers reduce to this shape once dashes are dropped and letters
        /// lowercased, which is what lets one filename format serve both - and what keeps the
        /// filename derived from the identifier rather than taken from it.
        /// </summary>
        public String WarpUuid { get; set; } = "";

        /// <summary>Which terminal this session is in.</summary>
        public TerminalKind Terminal { get; set; } = TerminalKind.Unknown;

        /// <summary>
        /// The identifier in the terminal's own spelling - Warp's 32-hex pane UUID, or iTerm's
        /// dashed GUID. This is what gets handed back to the terminal to focus the session, so it
        /// is kept verbatim rather than normalised.
        /// </summary>
        public String SessionRef { get; set; } = "";

        public SessionActivity Activity { get; set; } = SessionActivity.Unknown;

        /// <summary>When the current state began. Preserved across same-state writes, so this is
        /// "how long in this state", not "how long since the last event".</summary>
        public DateTimeOffset Since { get; set; }

        /// <summary>When the hook last wrote. Used to spot a session whose terminal died.</summary>
        public DateTimeOffset Ts { get; set; }

        public String Project { get; set; } = "";
        public String Branch { get; set; } = "";

        /// <summary>The prompt that started the turn. Copilot CLI has no session slug, so this is
        /// the only thing that tells two sessions in one checkout apart.</summary>
        public String Prompt { get; set; } = "";

        public String Cwd { get; set; } = "";
        public String SessionId { get; set; } = "";
        public Int32 Pid { get; set; }
        public DateTimeOffset Started { get; set; }

        /// <summary>How long the session has been in its current state, at <paramref name="now"/>.</summary>
        public TimeSpan ElapsedIn(DateTimeOffset now)
        {
            var elapsed = now - this.Since;
            return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
        }

        /// <summary>The tile's second line: branch when there is one, else the prompt.</summary>
        public String Subtitle => !String.IsNullOrEmpty(this.Branch) ? this.Branch : this.Prompt;
    }
}
