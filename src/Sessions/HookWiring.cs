namespace Loupedeck.CopilotCLIPlugin.Sessions
{
    using System;
    using System.IO;
    using System.Threading;

    /// <summary>
    /// Connects the plugin to Copilot CLI by installing one hook file, and disconnects it by
    /// deleting that file.
    ///
    /// Copilot CLI loads every <c>*.json</c> under <c>$COPILOT_HOME/hooks/</c> independently, so
    /// this plugin owns exactly one file and never edits anything a user or another tool wrote.
    /// That is worth having: an agent CLI that keeps its hooks in one shared settings file forces
    /// a plugin to merge entries in and unpick them again on uninstall, and to get that right while
    /// other tools are editing the same file.
    ///
    /// Installing is not done at load: writing into a user's Copilot configuration is a change they
    /// should ask for, so it takes a confirmed double press on the Set up key.
    /// </summary>
    public static class HookWiring
    {
        /// <summary>The file this plugin owns. Named so it is obvious where it came from.</summary>
        public const String HookFileName = "copilot-keypad.json";

        private const String ScriptName = "keypad-hook.sh";
        private const String ScriptResource = "Loupedeck.CopilotCLIPlugin.keypad-hook.sh";

        /// <summary>How long an armed Set up key stays armed before disarming itself.</summary>
        private static readonly TimeSpan ArmWindow = TimeSpan.FromSeconds(5);

        private static readonly Object Gate = new();
        private static Timer _disarm;
        private static SetupAction _armed = SetupAction.None;

        public static event EventHandler Changed;

        public static String Root => SessionStore.Instance.Root;

        /// <summary>Where the extracted hook script lives. Inside the plugin's own directory, so
        /// uninstalling the plugin and deleting the hook file leaves nothing behind.</summary>
        public static String ScriptPath => Path.Combine(Root, ScriptName);

        public static String HooksDirectory =>
            Path.Combine(CopilotHome(), "hooks");

        public static String HookFilePath => Path.Combine(HooksDirectory, HookFileName);

        public static SetupAction Armed
        {
            get
            {
                lock (Gate)
                {
                    return _armed;
                }
            }
        }

        /// <summary>Whether Copilot CLI is currently configured to feed this plugin.</summary>
        public static Boolean IsWired
        {
            get
            {
                try
                {
                    return File.Exists(HookFilePath) && File.Exists(ScriptPath);
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Writes the hook script out of the assembly. The script travels inside the DLL because a
        /// Marketplace user never has this repository, and the package directory is removed on
        /// uninstall - the assembly is the only place it can live and still be extractable.
        /// </summary>
        public static void ExtractScript()
        {
            try
            {
                Directory.CreateDirectory(Root);

                // CreateDirectory uses the process umask, which leaves the directory world
                // readable. keypad-hook.sh tightens it to 700, but only the first time it runs -
                // so between the plugin loading and a session firing its first event, the
                // directory stood open. PRIVACY.md states 700, and it should be true from the
                // moment the directory exists.
                MakePrivate(Root);

                var current = PluginResources.ReadTextFile(ScriptResource);
                var onDisk = File.Exists(ScriptPath) ? File.ReadAllText(ScriptPath) : null;

                if (current != onDisk)
                {
                    // Rewritten on every load so a plugin update ships a fixed hook without the user
                    // having to re-run anything.
                    File.WriteAllText(ScriptPath, current);
                }

                MakeExecutable(ScriptPath);
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "could not extract the hook script");
            }
        }

        /// <summary>
        /// The press handler for the Set up key: the first press arms, the second within
        /// <see cref="ArmWindow"/> acts. Returns true when something was actually changed.
        /// </summary>
        public static Boolean PressSetup()
        {
            SetupAction pending;

            lock (Gate)
            {
                pending = _armed;
            }

            if (pending == SetupAction.None)
            {
                Arm(IsWired ? SetupAction.Disable : SetupAction.Enable);
                return false;
            }

            Disarm();

            return pending == SetupAction.Enable ? Install() : Uninstall();
        }

        public static Boolean Install()
        {
            try
            {
                ExtractScript();
                Directory.CreateDirectory(HooksDirectory);

                File.WriteAllText(HookFilePath, HookFile.Build(ScriptPath));

                PluginLog.Info($"wrote {HookFilePath}");
                Changed?.Invoke(null, EventArgs.Empty);
                return true;
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "could not install the Copilot CLI hook");
                return false;
            }
        }

        public static Boolean Uninstall()
        {
            try
            {
                // Only ever this plugin's own file.
                if (File.Exists(HookFilePath))
                {
                    File.Delete(HookFilePath);
                    PluginLog.Info($"removed {HookFilePath}");
                }

                Changed?.Invoke(null, EventArgs.Empty);
                return true;
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "could not remove the Copilot CLI hook");
                return false;
            }
        }

        public static void Shutdown()
        {
            lock (Gate)
            {
                _disarm?.Dispose();
                _disarm = null;
                _armed = SetupAction.None;
            }
        }

        private static String CopilotHome()
        {
            var home = Environment.GetEnvironmentVariable("COPILOT_HOME");

            return String.IsNullOrEmpty(home)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".copilot")
                : home;
        }

        private static void Arm(SetupAction action)
        {
            lock (Gate)
            {
                _armed = action;
                _disarm?.Dispose();

                // An armed key that stays armed is a trap: come back in ten minutes, press it once
                // to see what it says, and it fires.
                _disarm = new Timer(_ => Disarm(), null, (Int32)ArmWindow.TotalMilliseconds, Timeout.Infinite);
            }

            Changed?.Invoke(null, EventArgs.Empty);
        }

        private static void Disarm()
        {
            lock (Gate)
            {
                if (_armed == SetupAction.None)
                {
                    return;
                }

                _armed = SetupAction.None;
                _disarm?.Dispose();
                _disarm = null;
            }

            Changed?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>Owner-only on the plugin's own directory, matching what the hook script sets
        /// and what PRIVACY.md states.</summary>
        private static void MakePrivate(String directory)
        {
            try
            {
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(
                        directory,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
            }
            catch (Exception ex)
            {
                PluginLog.Verbose($"could not restrict the state directory: {ex.Message}");
            }
        }

        private static void MakeExecutable(String path)
        {
            try
            {
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(
                        path,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
            }
            catch (Exception ex)
            {
                // The hook file invokes it as `sh <path>`, so this is a convenience rather than a
                // requirement.
                PluginLog.Verbose($"could not mark the hook script executable: {ex.Message}");
            }
        }
    }
}
