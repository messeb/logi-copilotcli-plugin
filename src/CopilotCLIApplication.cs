namespace Loupedeck.CopilotCLIPlugin
{
    using System;

    /// <summary>
    /// The host requires a <see cref="ClientApplication"/> type in the assembly even for a plugin
    /// that is bound to no application: without one the service refuses the assembly outright with
    /// "Cannot load plugin from '&lt;path&gt;.dll'", which reads like a broken build rather than a
    /// missing class.
    ///
    /// So this exists and deliberately claims nothing. Returning empty names is what keeps it from
    /// registering an application entry, alongside <see cref="Plugin.HasNoApplication"/> and the
    /// HasNoApplication capability in the manifest.
    /// </summary>
    public class CopilotCLIApplication : ClientApplication
    {
        public CopilotCLIApplication()
        {
        }

        protected override String GetProcessName() => "";

        protected override String GetBundleName() => "";

        public override ClientApplicationStatus GetApplicationStatus() => ClientApplicationStatus.Unknown;
    }
}
