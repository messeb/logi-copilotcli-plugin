namespace Loupedeck.CopilotCLIPlugin.Sessions
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Threading;

    /// <summary>Raised when the set of sessions changed, as opposed to only their states.</summary>
    public sealed class SessionsChangedEventArgs : EventArgs
    {
    }

    /// <summary>
    /// Watches the directory keypad-hook.sh writes into and keeps a current list of sessions.
    ///
    /// Polls rather than watching with FileSystemWatcher: the hook writes with a temp-and-rename on
    /// a session's hot path, which produces a burst of create/rename/delete events per tool call,
    /// and a poll at the refresh rate the tiles are redrawn at is both simpler and less work.
    /// </summary>
    public sealed class SessionStore : IDisposable
    {
        private const Int32 PollMs = 1000;

        private static readonly Lazy<SessionStore> Singleton = new(() => new SessionStore());

        private readonly Timer _poll;
        private readonly Object _gate = new();

        private IReadOnlyList<SessionSnapshot> _sessions = Array.Empty<SessionSnapshot>();
        private String _membershipKey = "";

        private SessionStore()
        {
            this.Root = SessionFiles.ResolveRoot(
                Environment.GetEnvironmentVariable,
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

            this.SessionsDirectory = Path.Combine(this.Root, "sessions");

            this._poll = new Timer(_ => this.Refresh(), null, 0, PollMs);
        }

        public static SessionStore Instance => Singleton.Value;

        public String Root { get; }

        public String SessionsDirectory { get; }

        /// <summary>Fires on every poll that changed anything; the argument is a
        /// <see cref="SessionsChangedEventArgs"/> only when the set of sessions itself changed.</summary>
        public event EventHandler Changed;

        public IReadOnlyList<SessionSnapshot> Sessions
        {
            get
            {
                lock (this._gate)
                {
                    return this._sessions;
                }
            }
        }

        public IReadOnlyList<SessionSnapshot> Active(DateTimeOffset now) =>
            SessionClassifier.Active(this.Sessions, now);

        public IReadOnlyList<SessionSnapshot> Waiting(DateTimeOffset now) =>
            SessionClassifier.Waiting(this.Sessions, now);

        public IReadOnlyList<SessionSnapshot> All(DateTimeOffset now) =>
            SessionClassifier.All(this.Sessions, now);

        public static void Shutdown()
        {
            if (Singleton.IsValueCreated)
            {
                Singleton.Value.Dispose();
            }
        }

        public void Dispose() => this._poll?.Dispose();

        private void Refresh()
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                var fresh = this.ReadAll(now);

                // Which tiles exist changes the folder's pages; what a tile says does not. Telling
                // the two apart keeps a state change from rebuilding the page list, which the host
                // renders as visible churn.
                var key = String.Join("|", fresh.Select(s => s.WarpUuid).OrderBy(u => u, StringComparer.Ordinal));

                Boolean membershipChanged;
                lock (this._gate)
                {
                    membershipChanged = key != this._membershipKey;
                    this._sessions = fresh;
                    this._membershipKey = key;
                }

                this.Changed?.Invoke(this, membershipChanged ? new SessionsChangedEventArgs() : EventArgs.Empty);
            }
            catch (Exception ex)
            {
                // A poll that throws must not kill the timer, or the tiles freeze silently.
                PluginLog.Warning(ex, "session poll failed");
            }
        }

        private IReadOnlyList<SessionSnapshot> ReadAll(DateTimeOffset now)
        {
            if (!Directory.Exists(this.SessionsDirectory))
            {
                // Normal before the hook has ever run.
                return Array.Empty<SessionSnapshot>();
            }

            var sessions = new List<SessionSnapshot>();

            foreach (var stateFile in Directory.EnumerateFiles(this.SessionsDirectory, "*" + SessionFiles.StateSuffix))
            {
                var name = Path.GetFileName(stateFile);
                var uuid = name.Substring(0, name.Length - SessionFiles.StateSuffix.Length);

                if (!SessionFiles.IsValidUuid(uuid))
                {
                    continue;
                }

                var metaFile = Path.Combine(this.SessionsDirectory, uuid + SessionFiles.MetaSuffix);
                var session = SessionFiles.Parse(uuid, ReadOrNull(stateFile), ReadOrNull(metaFile));

                if (session is null)
                {
                    continue;
                }

                if (!SessionClassifier.IsLive(session, now, IsProcessAlive))
                {
                    // A terminal closed with SIGKILL never delivers sessionEnd, so its files outlive
                    // it. Sweeping them here is what stops dead tiles accumulating forever.
                    TryDelete(stateFile);
                    TryDelete(metaFile);
                    continue;
                }

                sessions.Add(session);
            }

            return sessions;
        }

        private static String ReadOrNull(String path)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (IOException)
            {
                // Caught mid-rename. The next poll is 1 s away.
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static void TryDelete(String path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex)
            {
                PluginLog.Verbose($"could not remove a dead session's file: {ex.Message}");
            }
        }

        private static Boolean IsProcessAlive(Int32 pid)
        {
            try
            {
                using var process = Process.GetProcessById(pid);

                // A PID can be recycled onto something else entirely, and focusing a Warp pane that
                // now belongs to a different program would be worse than dropping the tile.
                return process.ProcessName.Contains("copilot", StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException)
            {
                // No such process.
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }
}
