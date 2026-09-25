namespace Loupedeck.CopilotCLIPlugin.Tests
{
    using System;
    using System.Linq;

    using Loupedeck.CopilotCLIPlugin.Sessions;

    using Xunit;

    public class BucketTests
    {
        private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

        private static SessionSnapshot Session(SessionActivity activity, Double secondsInState = 0) =>
            new()
            {
                WarpUuid = new String('a', 32),
                Activity = activity,
                Since = Now.AddSeconds(-secondsInState),
                Ts = Now,
            };

        [Fact]
        public void BusyIsActive() =>
            Assert.Equal(SessionBucket.Active, SessionClassifier.Bucket(Session(SessionActivity.Busy), Now));

        [Fact]
        public void DoneIsWaiting() =>
            Assert.Equal(SessionBucket.Waiting, SessionClassifier.Bucket(Session(SessionActivity.Done), Now));

        // Waiting means the session actually wants something from you. A session that is up but has
        // never been asked anything wants nothing - it belongs in All, not in the alarm list.
        [Fact]
        public void IdleIsNeither() =>
            Assert.Equal(SessionBucket.Other, SessionClassifier.Bucket(Session(SessionActivity.Idle), Now));

        // Attention is written only for a notification the hook file matched - permission_prompt
        // or elicitation_dialog - so it means the agent is asking you something, whatever its age.
        // An earlier design debounced it, because permissionRequest was being used as the signal;
        // that was wrong, since the gap from permissionRequest to postToolUse is the tool's own
        // duration (78 ms for `echo`, 4 s for `sleep 4`) and no delay can separate the two cases.
        [Theory]
        [InlineData(0.1)]
        [InlineData(2)]
        [InlineData(600)]
        public void AttentionIsAlwaysWaiting(Double secondsInState) =>
            Assert.Equal(
                SessionBucket.Waiting,
                SessionClassifier.Bucket(Session(SessionActivity.Attention, secondsInState), Now));

        // A state this build does not recognise must not be guessed into the waiting alarm. It
        // still surfaces, in All, because a session you cannot see is worse than one labelled oddly.
        [Fact]
        public void UnknownIsNeither() =>
            Assert.Equal(SessionBucket.Other, SessionClassifier.Bucket(Session(SessionActivity.Unknown), Now));
    }

    public class OrderingTests
    {
        private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

        private static SessionSnapshot Session(String id, SessionActivity activity, Double secondsInState) =>
            new()
            {
                WarpUuid = id.PadRight(32, '0'),
                Project = id,
                Activity = activity,
                Since = Now.AddSeconds(-secondsInState),
                Ts = Now,
            };

        [Fact]
        public void WaitingPutsBlockedSessionsFirst()
        {
            var sessions = new[]
            {
                Session("done", SessionActivity.Done, 100),
                Session("blocked", SessionActivity.Attention, 10),
            };

            var order = SessionClassifier.Waiting(sessions, Now).Select(s => s.Project).ToArray();

            Assert.Equal(new[] { "blocked", "done" }, order);
        }

        // The count on the Waiting key is only worth glancing at if a number there always means
        // something needs doing. Sessions that are merely running must leave it empty.
        [Fact]
        public void OnlyRunningSessionsMeansNothingIsWaiting()
        {
            var sessions = new[]
            {
                Session("one", SessionActivity.Busy, 5),
                Session("two", SessionActivity.Busy, 90),
                Session("three", SessionActivity.Busy, 600),
            };

            Assert.Empty(SessionClassifier.Waiting(sessions, Now));
            Assert.Equal(3, SessionClassifier.Active(sessions, Now).Count);
            Assert.Equal(3, SessionClassifier.All(sessions, Now).Count);
        }

        // Nor may idle sessions put a number on it: an open session that was never asked anything
        // is not waiting on you in any sense you would want to be alerted about.
        [Fact]
        public void RunningAndIdleTogetherStillLeaveWaitingEmpty()
        {
            var sessions = new[]
            {
                Session("running", SessionActivity.Busy, 5),
                Session("never-asked", SessionActivity.Idle, 600),
            };

            Assert.Empty(SessionClassifier.Waiting(sessions, Now));
            Assert.Equal(2, SessionClassifier.All(sessions, Now).Count);
        }

        [Fact]
        public void WaitingHoldsOnlySessionsThatWantSomething()
        {
            var sessions = new[]
            {
                Session("idle", SessionActivity.Idle, 100),
                Session("working", SessionActivity.Busy, 100),
                Session("strange", SessionActivity.Unknown, 100),
                Session("done", SessionActivity.Done, 100),
                Session("blocked", SessionActivity.Attention, 100),
            };

            var waiting = SessionClassifier.Waiting(sessions, Now).Select(s => s.Project).ToArray();

            Assert.Equal(new[] { "blocked", "done" }, waiting);
        }

        [Fact]
        public void WaitingBreaksTiesByLongestWait()
        {
            var sessions = new[]
            {
                Session("recent", SessionActivity.Done, 5),
                Session("stale", SessionActivity.Done, 500),
            };

            var order = SessionClassifier.Waiting(sessions, Now).Select(s => s.Project).ToArray();

            Assert.Equal(new[] { "stale", "recent" }, order);
        }

        [Fact]
        public void ActiveListsLongestRunningFirst()
        {
            var sessions = new[]
            {
                Session("quick", SessionActivity.Busy, 3),
                Session("grinding", SessionActivity.Busy, 300),
            };

            var order = SessionClassifier.Active(sessions, Now).Select(s => s.Project).ToArray();

            Assert.Equal(new[] { "grinding", "quick" }, order);
        }

        private static SessionSnapshot[] OneOfEach() => new[]
        {
            Session("working", SessionActivity.Busy, 1),
            Session("done", SessionActivity.Done, 1),
            Session("blocked", SessionActivity.Attention, 30),
            Session("idle", SessionActivity.Idle, 1),
            Session("strange", SessionActivity.Unknown, 1),
        };

        [Fact]
        public void ActiveAndWaitingNeverOverlap()
        {
            var active = SessionClassifier.Active(OneOfEach(), Now).Select(s => s.Project);
            var waiting = SessionClassifier.Waiting(OneOfEach(), Now).Select(s => s.Project);

            Assert.Empty(active.Intersect(waiting));
        }

        // All is the answer to "how many sessions do I have open at all", so nothing may be
        // filtered out of it - including the idle and unrecognised ones the other two drop.
        [Fact]
        public void AllHoldsEverySession()
        {
            var all = SessionClassifier.All(OneOfEach(), Now).Select(s => s.Project).ToArray();

            Assert.Equal(5, all.Length);
            Assert.Contains("idle", all);
            Assert.Contains("strange", all);
        }

        [Fact]
        public void AllIsASupersetOfTheOtherTwo()
        {
            var all = SessionClassifier.All(OneOfEach(), Now).Select(s => s.Project).ToHashSet();
            var active = SessionClassifier.Active(OneOfEach(), Now).Select(s => s.Project);
            var waiting = SessionClassifier.Waiting(OneOfEach(), Now).Select(s => s.Project);

            Assert.True(all.IsSupersetOf(active));
            Assert.True(all.IsSupersetOf(waiting));
        }

        // Most interesting first: blocked, then working, then finished, then the quiet ones.
        [Fact]
        public void AllRanksTheMostInterestingFirst()
        {
            var order = SessionClassifier.All(OneOfEach(), Now).Select(s => s.Project).ToArray();

            Assert.Equal(new[] { "blocked", "working", "done", "idle", "strange" }, order);
        }
    }

    public class ElapsedFormattingTests
    {
        [Theory]
        [InlineData(0, "0:00")]
        [InlineData(7, "0:07")]
        [InlineData(67, "1:07")]
        [InlineData(599, "9:59")]
        [InlineData(3599, "59:59")]
        [InlineData(3600, "1h00")]
        [InlineData(3660, "1h01")]
        [InlineData(86399, "23h59")]
        public void FormatsForATinyTile(Int32 seconds, String expected) =>
            Assert.Equal(expected, SessionClassifier.FormatElapsed(TimeSpan.FromSeconds(seconds)));

        // Clock skew between the shell's `date +%s` and the plugin's clock must not render "-0:01".
        [Fact]
        public void NegativeElapsedClampsToZero() =>
            Assert.Equal("0:00", SessionClassifier.FormatElapsed(TimeSpan.FromSeconds(-5)));

        [Fact]
        public void LongRunsDoNotOverflowTheTile() =>
            Assert.Equal("99h+", SessionClassifier.FormatElapsed(TimeSpan.FromHours(120)));
    }

    public class LivenessTests
    {
        private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

        private static SessionSnapshot Aged(Double secondsSinceWrite, Int32 pid = 4242) =>
            new()
            {
                WarpUuid = new String('a', 32),
                Activity = SessionActivity.Busy,
                Since = Now.AddSeconds(-secondsSinceWrite),
                Ts = Now.AddSeconds(-secondsSinceWrite),
                Pid = pid,
            };

        [Fact]
        public void ALiveProcessKeepsItsTile() =>
            Assert.True(SessionClassifier.IsLive(Aged(10), Now, _ => true));

        // A terminal closed with SIGKILL never delivers sessionEnd, so the files outlive the
        // session. The PID is what catches that.
        [Fact]
        public void ADeadProcessDropsItsTile() =>
            Assert.False(SessionClassifier.IsLive(Aged(10), Now, _ => false));

        // A session that predates the hook has pid 0: there is nothing to check, so fall back to
        // the write age rather than dropping a tile that may well be live.
        [Fact]
        public void WithoutAPidARecentWriteCounts() =>
            Assert.True(SessionClassifier.IsLive(Aged(10, pid: 0), Now, _ => false));

        [Fact]
        public void WithoutAPidAStaleWriteDoesNot() =>
            Assert.False(SessionClassifier.IsLive(Aged(SessionClassifier.StaleAfter.TotalSeconds + 1, pid: 0), Now, _ => false));
    }
}
