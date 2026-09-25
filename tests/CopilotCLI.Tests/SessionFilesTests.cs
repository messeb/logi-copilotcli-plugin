namespace Loupedeck.CopilotCLIPlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    using Loupedeck.CopilotCLIPlugin.Sessions;

    using Xunit;

    public class ParseTests
    {
        private const String Uuid = "abcdef0123456789abcdef0123456789";

        // Exactly what hooks/keypad-hook.sh writes.
        private const String StateJson = """{"state":"busy","since":1790332560,"ts":1790332568,"event_ms":1790332568012}""";

        private const String MetaJson = """
            {"schema":1,"warp_uuid":"abcdef0123456789abcdef0123456789","focus_url":"warp://session/abcdef0123456789abcdef0123456789","session_id":"fc96ac24","pid":3075,"cwd":"/Users/me/src/thing","project":"thing","branch":"main","prompt":"fix the parser","started":1790332500}
            """;

        [Fact]
        public void ReadsStateAndMetadata()
        {
            var session = SessionFiles.Parse(Uuid, StateJson, MetaJson);

            Assert.NotNull(session);
            Assert.Equal(SessionActivity.Busy, session.Activity);
            Assert.Equal("thing", session.Project);
            Assert.Equal("main", session.Branch);
            Assert.Equal("fix the parser", session.Prompt);
            Assert.Equal(3075, session.Pid);
            Assert.Equal(Uuid, session.SessionRef);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1790332560), session.Since);
        }

        [Theory]
        [InlineData("idle", SessionActivity.Idle)]
        [InlineData("busy", SessionActivity.Busy)]
        [InlineData("attention", SessionActivity.Attention)]
        [InlineData("done", SessionActivity.Done)]
        [InlineData("teleporting", SessionActivity.Unknown)]
        public void ReadsEveryStateTheHookCanWrite(String written, SessionActivity expected)
        {
            var session = SessionFiles.Parse(Uuid, $$"""{"state":"{{written}}","since":1,"ts":1}""", null);

            Assert.Equal(expected, session.Activity);
        }

        // The hook writes state before metadata on a brand-new session, and the plugin polls in
        // between. A tile with no name beats no tile at all.
        [Fact]
        public void SurvivesMissingMetadata()
        {
            var session = SessionFiles.Parse(Uuid, StateJson, null);

            Assert.NotNull(session);
            Assert.Equal("abcdef", session.Project);
            Assert.Equal(0, session.Pid);
        }

        // Both files are written with a temp-and-rename, but a reader can still catch a partial
        // write on some filesystems. Half a JSON object is not a session.
        [Fact]
        public void RejectsTruncatedState() =>
            Assert.Null(SessionFiles.Parse(Uuid, """{"state":"bu""", null));

        [Fact]
        public void SurvivesTruncatedMetadata()
        {
            var session = SessionFiles.Parse(Uuid, StateJson, """{"project":"thi""");

            Assert.NotNull(session);
            Assert.Equal(SessionActivity.Busy, session.Activity);
        }

        [Fact]
        public void RejectsEmptyState() => Assert.Null(SessionFiles.Parse(Uuid, "", null));

        [Theory]
        [InlineData("../../../etc/passwd")]
        [InlineData("ABCDEF0123456789ABCDEF0123456789")]
        [InlineData("abcdef")]
        [InlineData("")]
        [InlineData("zzzzzz0123456789abcdef0123456789")]
        public void RejectsAnythingThatIsNotAWarpUuid(String uuid) =>
            Assert.Null(SessionFiles.Parse(uuid, StateJson, MetaJson));

        // A hand-edited meta file must not be able to point a tile at another pane.
        [Fact]
        public void IgnoresAForgedReference()
        {
            var forged = """{"focus_url":"warp://session/ffffffffffffffffffffffffffffffff","project":"x","pid":1}""";

            var session = SessionFiles.Parse(Uuid, StateJson, forged);

            Assert.Equal(Uuid, session.SessionRef);
            Assert.Equal(TerminalKind.Warp, session.Terminal);
        }

        [Fact]
        public void MissingTimestampsDoNotThrow()
        {
            var session = SessionFiles.Parse(Uuid, """{"state":"done"}""", null);

            Assert.NotNull(session);
            Assert.Equal(DateTimeOffset.UnixEpoch, session.Since);
        }
    }

    public class RootResolutionTests
    {
        private static Func<String, String> Env(params (String Key, String Value)[] pairs)
        {
            var map = new Dictionary<String, String>();
            foreach (var (key, value) in pairs)
            {
                map[key] = value;
            }

            return name => map.TryGetValue(name, out var v) ? v : null;
        }

        [Fact]
        public void DefaultsToCopilotHomeUnderTheUsersHome() =>
            Assert.Equal(
                Path.Combine("/Users/me", ".copilot", "keypad"),
                SessionFiles.ResolveRoot(Env(), "/Users/me"));

        [Fact]
        public void HonoursCopilotHome() =>
            Assert.Equal(
                Path.Combine("/opt/copilot", "keypad"),
                SessionFiles.ResolveRoot(Env(("COPILOT_HOME", "/opt/copilot")), "/Users/me"));

        // The tests for the hook set this, so the plugin has to agree with it.
        [Fact]
        public void AnExplicitRootWinsOverEverything() =>
            Assert.Equal(
                "/tmp/kp",
                SessionFiles.ResolveRoot(Env(("COPILOT_KEYPAD_ROOT", "/tmp/kp"), ("COPILOT_HOME", "/opt/copilot")), "/Users/me"));
    }

    public class SubtitleTests
    {
        [Fact]
        public void PrefersTheBranch()
        {
            var session = new SessionSnapshot { Branch = "main", Prompt = "fix the parser" };

            Assert.Equal("main", session.Subtitle);
        }

        // Outside a repo there is no branch, and the prompt is then the only thing that tells two
        // sessions in the same directory apart.
        [Fact]
        public void FallsBackToThePrompt()
        {
            var session = new SessionSnapshot { Branch = "", Prompt = "fix the parser" };

            Assert.Equal("fix the parser", session.Subtitle);
        }
    }
}
