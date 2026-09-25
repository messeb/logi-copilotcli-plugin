namespace Loupedeck.CopilotCLIPlugin
{
    using System;
    using System.Threading.Tasks;

    using Loupedeck.CopilotCLIPlugin.Sessions;
    using Loupedeck.CopilotCLIPlugin.Terminals;

    public class CopilotCLIPlugin : Plugin
    {
        public override Boolean UsesApplicationApiOnly => true;

        // A deck of session tiles is for watching sessions WHILE working somewhere else, so it must
        // not be bound to an application: an application profile would switch away the moment you
        // focused Warp, which is the opposite of what a status display is for.
        public override Boolean HasNoApplication => true;

        public CopilotCLIPlugin()
        {
            PluginLog.Init(this.Log);
            PluginResources.Init(this.Assembly);
        }

        public override void Load()
        {
            // Focusing an iTerm session goes through AppleScript, which macOS gates behind an
            // Automation permission. Reported when it is actually refused rather than probed at
            // load: probing would raise the system prompt before the user has pressed anything
            // that needs it. Warp's deep link needs no permission, so this never fires for Warp.
            SessionFocus.AutomationDenied += (_, _) => this.OnPluginStatusChanged(
                Loupedeck.PluginStatus.Error,
                "macOS blocked the plugin from controlling iTerm. Allow Logi Plugin Service under "
                + "System Settings > Privacy & Security > Automation, then press the tile again.",
                "https://github.com/messeb/copilotcli-keypad-mx#known-limits",
                "How to fix this");

            // Everything written here stays inside the plugin's own directory. It deliberately does
            // NOT install the Copilot CLI hook: writing into a user's Copilot configuration is a
            // change they should ask for, and it takes a confirmed press on the Set up key.
            HookWiring.ExtractScript();

            if (!HookWiring.IsWired)
            {
                PluginLog.Info("Copilot CLI hooks are not installed yet; press a Set up key to connect.");

                this.OnPluginStatusChanged(
                    Loupedeck.PluginStatus.Warning,
                    "Not connected to Copilot CLI yet. Open either folder and press Set up twice.",
                    "https://github.com/messeb/copilotcli-keypad-mx#setup",
                    "How to connect");
            }
            else
            {
                PluginLog.Info($"connected: {HookWiring.HookFilePath}");
            }

            // Warmed off this thread so the first folder open is already populated. Doing it
            // synchronously is what overruns the host's 10 second Load budget - and a plugin that
            // overruns it is dropped entirely, which looks exactly like a plugin that crashed.
            Task.Run(() =>
            {
                try
                {
                    _ = SessionStore.Instance.Sessions;
                }
                catch (Exception ex)
                {
                    PluginLog.Warning(ex, "could not warm the session store");
                }
            });
        }

        public override void Unload()
        {
            // These are timers on static fields, and statics live per load context rather than per
            // process. Without this, every reload leaves its predecessor's timers running.
            HookWiring.Shutdown();
            SessionStore.Shutdown();
        }
    }
}
