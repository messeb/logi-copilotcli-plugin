namespace Loupedeck.CopilotCLIPlugin.Sessions
{
    /// <summary>What the next press of the Set up key would do.</summary>
    public enum SetupAction
    {
        /// <summary>Nothing is armed; the key is just showing its state.</summary>
        None,

        /// <summary>Armed to install the hook file.</summary>
        Enable,

        /// <summary>Armed to remove it.</summary>
        Disable,
    }
}
