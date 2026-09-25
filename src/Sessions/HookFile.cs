namespace Loupedeck.CopilotCLIPlugin.Sessions
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.Json;

    /// <summary>
    /// Builds the hook registration this plugin installs into Copilot CLI.
    ///
    /// Pure (path in, JSON out) and free of PluginApi types, so the file that gets written into a
    /// user's Copilot configuration is unit-tested rather than trusted.
    /// </summary>
    public static class HookFile
    {
        /// <summary>
        /// The events the tiles are built from.
        ///
        /// camelCase, which is Copilot CLI's own spelling. The PascalCase aliases exist for VS Code
        /// compatibility and carry Claude-format matcher semantics - a translation layer there is
        /// no reason to stand on when writing a file from scratch.
        /// </summary>
        public static readonly IReadOnlyList<String> Events = new[]
        {
            "sessionStart",
            "userPromptSubmitted",
            "preToolUse",
            "postToolUse",
            "permissionRequest",
            "notification",
            "agentStop",
            "sessionEnd",
        };

        // No matchers, deliberately.
        //
        // notification does need narrowing to permission_prompt and elicitation_dialog, but that
        // happens in keypad-hook.sh, on the payload. A matcher is one untestable string, and if
        // Copilot's matcher semantics differ in any way from what was assumed here, every
        // notification is silently dropped - a blocked session would then never reach the waiting
        // tile, with no symptom other than the alarm quietly never firing. The script's own filter
        // is covered by tests.
        //
        // Nothing else may be narrowed either: every tool call and every session event is wanted.

        /// <summary>
        /// Five seconds is generous for a script whose slow path is one `git rev-parse`, and it
        /// bounds the damage if the state directory ever lives on a stalled network mount: Copilot
        /// waits for hooks, so a hook that hangs would hang the session it is watching.
        /// </summary>
        private const Int32 TimeoutSec = 5;

        public static String Build(String scriptPath)
        {
            if (String.IsNullOrWhiteSpace(scriptPath))
            {
                throw new ArgumentException("a hook script path is required", nameof(scriptPath));
            }

            var hooks = new Dictionary<String, Object>();

            foreach (var name in Events)
            {
                hooks[name] = new[]
                {
                    new Dictionary<String, Object>
                    {
                        // `sh <path> <event>` rather than executing the script directly: the file
                        // mode can be lost by a copy or a restore, and `sh` does not care.
                        ["type"] = "command",
                        ["command"] = $"sh {Quote(scriptPath)} {name}",
                        ["timeoutSec"] = TimeoutSec,
                    },
                };
            }

            var document = new Dictionary<String, Object>
            {
                ["version"] = 1,
                ["//"] = "Installed by the CopilotCLI keypad plugin. Delete this file to disconnect it.",
                ["hooks"] = hooks,
            };

            // Serialised rather than built by string concatenation: a project path containing a
            // quote or a backslash would otherwise write a broken file into a user's Copilot
            // configuration, and a broken hooks file is not a failure they would connect to us.
            return JsonSerializer.Serialize(document, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }) + "\n";
        }

        /// <summary>Wraps a path for the shell Copilot runs the command with.</summary>
        private static String Quote(String path)
        {
            var quoted = new StringBuilder("\"");

            foreach (var c in path)
            {
                // Inside double quotes a shell still expands these, so each is escaped rather than
                // relying on the quotes alone.
                if (c is '"' or '\\' or '$' or '`')
                {
                    quoted.Append('\\');
                }

                quoted.Append(c);
            }

            return quoted.Append('"').ToString();
        }
    }
}
