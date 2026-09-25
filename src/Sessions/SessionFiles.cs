namespace Loupedeck.CopilotCLIPlugin.Sessions
{
    using System;
    using System.Text.Json;

    /// <summary>
    /// Turns the two JSON files keypad-hook.sh writes into a <see cref="SessionSnapshot"/>.
    ///
    /// Pure (string in, snapshot out) and free of PluginApi types, so the parsing rules are
    /// unit-testable. Everything here tolerates absent, truncated and malformed input: the files
    /// are written by a shell script on a session's hot path and may be read mid-rename.
    /// </summary>
    public static class SessionFiles
    {
        public const String StateSuffix = ".state.json";
        public const String MetaSuffix = ".meta.json";

        /// <summary>
        /// Where the hook writes, mirroring its own resolution order:
        /// <c>$COPILOT_KEYPAD_ROOT</c>, else <c>$COPILOT_HOME/keypad</c>, else
        /// <c>~/.copilot/keypad</c>.
        /// </summary>
        public static String ResolveRoot(Func<String, String> getEnv, String home)
        {
            var explicitRoot = getEnv("COPILOT_KEYPAD_ROOT");
            if (!String.IsNullOrEmpty(explicitRoot))
            {
                return explicitRoot;
            }

            var copilotHome = getEnv("COPILOT_HOME");
            var baseDir = String.IsNullOrEmpty(copilotHome)
                ? System.IO.Path.Combine(home, ".copilot")
                : copilotHome;

            return System.IO.Path.Combine(baseDir, "keypad");
        }

        /// <summary>
        /// A dashed GUID as iTerm2 spells a session id: 8-4-4-4-12 hex.
        ///
        /// Checked before the value is handed to osascript. It goes across as an argument rather
        /// than spliced into the script, so this is a second line of defence rather than the only
        /// one - but an identifier that is not an identifier has no business being passed on.
        /// </summary>
        public static Boolean IsValidITermSessionId(String id)
        {
            if (id is not { Length: 36 })
            {
                return false;
            }

            for (var i = 0; i < id.Length; i++)
            {
                var c = id[i];
                var expectDash = i is 8 or 13 or 18 or 23;

                if (expectDash)
                {
                    if (c != '-')
                    {
                        return false;
                    }
                }
                else if (!Uri.IsHexDigit(c))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>A session key as the hook is willing to write one: 32 lowercase hex.</summary>
        public static Boolean IsValidUuid(String uuid)
        {
            if (uuid is not { Length: 32 })
            {
                return false;
            }

            foreach (var c in uuid)
            {
                var isHex = c is >= '0' and <= '9' or >= 'a' and <= 'f';
                if (!isHex)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Builds a snapshot from the state file (required) and the meta file (optional - a
        /// session writes its state before its metadata exists, and heals on the next event).
        /// Returns null when the pair cannot describe a session worth showing.
        /// </summary>
        public static SessionSnapshot Parse(String uuid, String stateJson, String metaJson)
        {
            if (!IsValidUuid(uuid) || String.IsNullOrWhiteSpace(stateJson))
            {
                return null;
            }

            var state = TryParse(stateJson);
            if (state is null)
            {
                return null;
            }

            var session = new SessionSnapshot
            {
                WarpUuid = uuid,
                Activity = ParseActivity(GetString(state.Value, "state")),
                Since = FromUnixSeconds(GetInt64(state.Value, "since")),
                Ts = FromUnixSeconds(GetInt64(state.Value, "ts")),

                // Assumed until the metadata says otherwise: schema 1 files predate iTerm support
                // and only ever described Warp panes.
                Terminal = TerminalKind.Warp,
                SessionRef = uuid,
            };

            var meta = String.IsNullOrWhiteSpace(metaJson) ? null : TryParse(metaJson);
            if (meta is not null)
            {
                session.Project = GetString(meta.Value, "project") ?? "";
                session.Branch = GetString(meta.Value, "branch") ?? "";
                session.Prompt = GetString(meta.Value, "prompt") ?? "";
                session.Cwd = GetString(meta.Value, "cwd") ?? "";
                session.SessionId = GetString(meta.Value, "session_id") ?? "";
                session.Pid = (Int32)GetInt64(meta.Value, "pid");
                session.Started = FromUnixSeconds(GetInt64(meta.Value, "started"));

                // The terminal and its own spelling of the session id, when the hook recorded
                // them. A reference that does not normalise back to this file's own name is
                // rejected: a tile must never be able to focus a different session because of a
                // hand-edited file.
                var terminal = GetString(meta.Value, "terminal");
                var reference = GetString(meta.Value, "session_ref");

                if (!String.IsNullOrEmpty(reference) && Normalise(reference) == uuid)
                {
                    session.SessionRef = reference;
                    session.Terminal = terminal switch
                    {
                        "warp" => TerminalKind.Warp,
                        "iterm" => TerminalKind.ITerm,
                        _ => TerminalKind.Unknown,
                    };
                }
            }

            if (String.IsNullOrEmpty(session.Project))
            {
                // Better a short hash than a blank tile.
                session.Project = uuid.Substring(0, 6);
            }

            return session;
        }

        /// <summary>Both terminals' identifiers reduced to the 32-hex form used for filenames.</summary>
        public static String Normalise(String reference) =>
            String.IsNullOrEmpty(reference) ? "" : reference.Replace("-", "").ToLowerInvariant();

        private static JsonElement? TryParse(String json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                // Read mid-rename, or written by a future hook version. Either way: not a session.
                return null;
            }
        }

        private static SessionActivity ParseActivity(String value) => value switch
        {
            "idle" => SessionActivity.Idle,
            "busy" => SessionActivity.Busy,
            "attention" => SessionActivity.Attention,
            "done" => SessionActivity.Done,
            _ => SessionActivity.Unknown,
        };

        private static String GetString(JsonElement root, String name) =>
            root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        private static Int64 GetInt64(JsonElement root, String name) =>
            root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var number)
                ? number
                : 0;

        private static DateTimeOffset FromUnixSeconds(Int64 seconds) =>
            seconds <= 0 ? DateTimeOffset.UnixEpoch : DateTimeOffset.FromUnixTimeSeconds(seconds);
    }
}
