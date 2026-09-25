namespace Loupedeck.CopilotCLIPlugin.Tests
{
    using System;

    using Loupedeck.CopilotCLIPlugin.Sessions;

    using Xunit;

    public class TerminalIdentityTests
    {
        // Both observed live on this machine: ITERM_SESSION_ID was "w0t0p0:35EF942E-..." and
        // "w0t1p0:45115895-...", and each GUID matched iTerm's own `id of session`.
        [Theory]
        [InlineData("35EF942E-C843-46A5-A2D1-A567234BBE47")]
        [InlineData("45115895-A375-4515-ACA6-3E9A8446E1E3")]
        [InlineData("00000000-0000-0000-0000-000000000000")]
        public void AcceptsRealITermSessionIds(String id) =>
            Assert.True(SessionFiles.IsValidITermSessionId(id));

        [Theory]
        [InlineData("w0t0p0:35EF942E-C843-46A5-A2D1-A567234BBE47")]  // the prefix must be stripped first
        [InlineData("35EF942E-C843-46A5-A2D1-A567234BBE4")]          // too short
        [InlineData("35EF942EC84346A5A2D1A567234BBE47")]             // undashed
        [InlineData("35EF942E-C843-46A5-A2D1-A567234BBE4G")]         // not hex
        [InlineData("35EF942E-C843-46A5-A2D1+A567234BBE47")]         // wrong separator
        [InlineData("../../../etc/passwd")]
        [InlineData("")]
        [InlineData(null)]
        public void RejectsAnythingElse(String id) =>
            Assert.False(SessionFiles.IsValidITermSessionId(id));

        // Both terminals' identifiers have to reduce to the same 32-hex filename shape, or one
        // session would end up filed under two different names.
        [Fact]
        public void BothTerminalsNormaliseToTheSameShape()
        {
            Assert.Equal(
                "35ef942ec84346a5a2d1a567234bbe47",
                SessionFiles.Normalise("35EF942E-C843-46A5-A2D1-A567234BBE47"));

            Assert.Equal(
                "abcdef0123456789abcdef0123456789",
                SessionFiles.Normalise("abcdef0123456789abcdef0123456789"));
        }

        [Fact]
        public void ANormalisedITermIdIsAValidFileName() =>
            Assert.True(SessionFiles.IsValidUuid(
                SessionFiles.Normalise("35EF942E-C843-46A5-A2D1-A567234BBE47")));

        [Fact]
        public void NormalisingIsHarmlessOnEmptyInput() =>
            Assert.Equal("", SessionFiles.Normalise(null));
    }
}
