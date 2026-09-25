namespace Loupedeck.CopilotCLIPlugin.Sessions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Decides which button a session belongs behind, in what order, and how its clock reads.
    ///
    /// Pure, and free of PluginApi types, so all of it is unit-testable.
    /// </summary>
    public static class SessionClassifier
    {
        /// <summary>
        /// How long after the last hook write a session with no usable PID is presumed gone.
        /// Generous, because the only cost of being wrong is a stale tile, while dropping a live
        /// session's tile loses the thing the plugin exists to show.
        /// </summary>
        public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(12);

        public static SessionBucket Bucket(SessionSnapshot session, DateTimeOffset now) =>
            session.Activity switch
            {
                SessionActivity.Busy => SessionBucket.Active,

                // Attention is written only for a notification whose type says the agent is asking
                // you something, and Done means a turn ended and nothing moves until you reply.
                // Both are genuinely your move.
                SessionActivity.Attention => SessionBucket.Waiting,
                SessionActivity.Done => SessionBucket.Waiting,

                // Idle wants nothing: the session is open but has never been asked anything, so
                // putting it in Waiting would pad the one list that is supposed to mean "act now".
                // Unknown is not guessed into an alarm either. Both still show in All.
                _ => SessionBucket.Other,
            };

        /// <summary>Busy sessions, longest-running first - the one that has been grinding for
        /// four minutes is the one worth looking at.</summary>
        public static IReadOnlyList<SessionSnapshot> Active(IEnumerable<SessionSnapshot> sessions, DateTimeOffset now) =>
            sessions
                .Where(s => Bucket(s, now) == SessionBucket.Active)
                .OrderByDescending(s => s.ElapsedIn(now))
                .ThenBy(s => s.Project, StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>Sessions that genuinely want something from you: blocked first, then finished
        /// turns, and within each the one that has waited longest.</summary>
        public static IReadOnlyList<SessionSnapshot> Waiting(IEnumerable<SessionSnapshot> sessions, DateTimeOffset now) =>
            sessions
                .Where(s => Bucket(s, now) == SessionBucket.Waiting)
                .OrderBy(s => Rank(s.Activity))
                .ThenByDescending(s => s.ElapsedIn(now))
                .ThenBy(s => s.Project, StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>
        /// Every session the plugin knows about, whatever it is doing - the answer to "how many
        /// Copilot sessions do I have open at all", including the ones that are neither working nor
        /// waiting. Most interesting first.
        /// </summary>
        public static IReadOnlyList<SessionSnapshot> All(IEnumerable<SessionSnapshot> sessions, DateTimeOffset now) =>
            sessions
                .OrderBy(s => Rank(s.Activity))
                .ThenByDescending(s => s.ElapsedIn(now))
                .ThenBy(s => s.Project, StringComparer.OrdinalIgnoreCase)
                .ToList();

        private static Int32 Rank(SessionActivity activity) => activity switch
        {
            SessionActivity.Attention => 0,   // blocked: nothing else moves until you answer
            SessionActivity.Busy => 1,        // working
            SessionActivity.Done => 2,        // finished a turn, ready for the next one
            SessionActivity.Idle => 3,        // up, but never asked anything
            _ => 4,
        };

        /// <summary>
        /// Whether a session's files still describe something running.
        ///
        /// A terminal closed with SIGKILL never delivers sessionEnd, so the files outlive the
        /// session; the recorded PID is what catches that. Sessions recorded before the hook
        /// existed have no PID, and for those the write age is the only signal available.
        /// </summary>
        public static Boolean IsLive(SessionSnapshot session, DateTimeOffset now, Func<Int32, Boolean> isProcessAlive)
        {
            if (session.Pid > 0)
            {
                return isProcessAlive(session.Pid);
            }

            return now - session.Ts < StaleAfter;
        }

        /// <summary>
        /// An elapsed time narrow enough for a 116 px tile: "0:07", "59:59", "1h01", "99h+".
        /// </summary>
        public static String FormatElapsed(TimeSpan elapsed)
        {
            if (elapsed < TimeSpan.Zero)
            {
                // The hook stamps with the shell's clock and the plugin reads with its own; a
                // little skew must not render "-0:01".
                elapsed = TimeSpan.Zero;
            }

            var totalHours = (Int32)elapsed.TotalHours;

            if (totalHours >= 100)
            {
                return "99h+";
            }

            return totalHours >= 1
                ? $"{totalHours}h{elapsed.Minutes:D2}"
                : $"{(Int32)elapsed.TotalMinutes}:{elapsed.Seconds:D2}";
        }
    }
}
