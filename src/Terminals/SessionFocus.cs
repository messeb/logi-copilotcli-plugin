namespace Loupedeck.CopilotCLIPlugin.Terminals
{
    using System;
    using System.Diagnostics;

    using Loupedeck.CopilotCLIPlugin.Sessions;

    /// <summary>
    /// Jumps to the terminal pane a session is running in.
    ///
    /// Two terminals, two mechanisms, and the difference is worth knowing:
    ///
    /// - Warp answers <c>warp://session/&lt;uuid&gt;</c>, which activates the app, switches tab and
    ///   focuses the pane in one call, through the ordinary URL opener.
    /// - iTerm2 has no such link, but its session ids are addressable over AppleScript: the GUID in
    ///   <c>ITERM_SESSION_ID</c> is the same value as <c>id of session</c>, so the pane can be found
    ///   and selected. That costs an Automation permission the deep link does not - see
    ///   <see cref="AutomationDenied"/>.
    /// </summary>
    public static class SessionFocus
    {
        /// <summary>
        /// Raised when macOS refused the AppleScript that focuses an iTerm session. The user has to
        /// grant Logi Plugin Service permission to control iTerm before the tile can work, and
        /// nothing in the plugin can do that for them.
        /// </summary>
        public static event EventHandler AutomationDenied;

        /// <summary>
        /// Finds the session by id and selects it, its tab and its window.
        ///
        /// Written as a search rather than by remembering window and tab indices, because
        /// ITERM_SESSION_ID's "w0t0p0" prefix is the pane's POSITION: move a pane to another tab
        /// and the prefix changes while the GUID does not.
        /// </summary>
        private const String ITermScript = @"
on run argv
    set targetId to item 1 of argv
    tell application ""iTerm2""
        repeat with w in windows
            repeat with t in tabs of w
                repeat with s in sessions of t
                    if id of s is targetId then
                        select w
                        select t
                        select s
                        activate
                        return ""ok""
                    end if
                end repeat
            end repeat
        end repeat
    end tell
    return ""not found""
end run";

        /// <summary>Focuses the pane a session is in. False if it could not be done.</summary>
        public static Boolean Focus(SessionSnapshot session)
        {
            if (session is null)
            {
                return false;
            }

            return session.Terminal switch
            {
                TerminalKind.ITerm => FocusITerm(session.SessionRef),

                // Unknown covers a session recorded by a newer hook than this build understands.
                // Warp is the older behaviour and the safer guess: its deep link simply does
                // nothing if the pane is gone, where a wrong guess elsewhere could steal focus.
                _ => FocusWarp(session.SessionRef),
            };
        }

        private static Boolean FocusWarp(String uuid)
        {
            // Re-checked here rather than trusted from the caller, because this hands a string to
            // the shell's URL opener and that is not a place to rely on someone else's care.
            if (!SessionFiles.IsValidUuid(uuid))
            {
                PluginLog.Warning("refusing to focus a Warp pane with a malformed UUID");
                return false;
            }

            return Run("/usr/bin/open", $"\"warp://session/{uuid}\"") is not null;
        }

        private static Boolean FocusITerm(String sessionId)
        {
            if (!SessionFiles.IsValidITermSessionId(sessionId))
            {
                PluginLog.Warning("refusing to focus an iTerm session with a malformed id");
                return false;
            }

            try
            {
                var start = new ProcessStartInfo
                {
                    FileName = "/usr/bin/osascript",
                    // The script is read from stdin and the id is passed as an argument, so the
                    // value is never spliced into the source text.
                    ArgumentList = { "-", sessionId },
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using var process = Process.Start(start);
                if (process is null)
                {
                    return false;
                }

                process.StandardInput.Write(ITermScript);
                process.StandardInput.Close();

                var output = process.StandardOutput.ReadToEnd().Trim();
                var error = process.StandardError.ReadToEnd().Trim();

                // Bounded: a hung osascript must not hold a folder press open forever.
                if (!process.WaitForExit(5000))
                {
                    PluginLog.Warning("focusing an iTerm session timed out");
                    return false;
                }

                if (error.Length > 0)
                {
                    PluginLog.Warning($"could not focus the iTerm session: {error}");

                    // -1743 is the TCC refusal. It is not a bug to fix but a permission to grant,
                    // so it is surfaced rather than logged and forgotten.
                    if (error.Contains("-1743") || error.Contains("Not authorized"))
                    {
                        AutomationDenied?.Invoke(null, EventArgs.Empty);
                    }

                    return false;
                }

                if (output != "ok")
                {
                    // The session's pane is gone, or iTerm has been restarted since it was
                    // recorded. Nothing to focus, and nothing worth alarming the user about.
                    PluginLog.Verbose($"iTerm did not find session {sessionId}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "could not focus the iTerm session");
                return false;
            }
        }

        private static Process Run(String fileName, String arguments)
        {
            try
            {
                return Process.Start(new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "could not focus the terminal");
                return null;
            }
        }
    }
}
