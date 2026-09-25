namespace Loupedeck.CopilotCLIPlugin.Tests
{
    using System;

    using Loupedeck.CopilotCLIPlugin.Rendering;

    using Xunit;

    public class TextFittingTests
    {
        // The whole point: BitmapBuilder's DrawText does not clip, so anything these say fits has
        // to actually fit. A key is 90 px wide with ~5 px of padding each side.
        private const Int32 KeyBox = 80;

        [Theory]
        [InlineData("copilot-warp")]
        [InlineData("acme-api")]
        [InlineData("website")]
        [InlineData("notes")]
        public void OrdinaryProjectNamesSurviveWhole(String name)
        {
            var size = TextMetrics.FitFontSize(name, KeyBox, 10, 20);

            Assert.Equal(name, TextMetrics.Fit(name, KeyBox, size));
        }

        [Fact]
        public void AnOverlongNameLosesItsMiddleRatherThanItsTail()
        {
            var fitted = TextMetrics.Fit("a-very-long-project-name-here", KeyBox, 14);

            Assert.Contains("…", fitted);
            Assert.StartsWith("a-", fitted);
            Assert.EndsWith("here", fitted);   // acme-ios vs acme-web must stay distinguishable
            Assert.True(TextMetrics.TextWidth(fitted, 14) <= KeyBox);
        }

        [Fact]
        public void SentenceLikeTextLosesItsTail()
        {
            var fitted = TextMetrics.FitEnd("why is the build failing on main again", KeyBox, 9);

            Assert.StartsWith("why is", fitted);
            Assert.EndsWith("…", fitted);
            Assert.True(TextMetrics.TextWidth(fitted, 9) <= KeyBox);
        }

        [Theory]
        [InlineData("working")]
        [InlineData("blocked")]
        [InlineData("ready")]
        [InlineData("idle")]
        public void FooterCaptionsFitBesideAClock(String caption)
        {
            // Worst case: a wide clock and the status dot both eating into the footer.
            var clockWidth = TextMetrics.TextWidth("12h34", 9);
            var available = 90 - 14 - clockWidth - 8;

            Assert.Equal(caption, TextMetrics.FitEnd(caption, available, 9));
        }

        [Theory]
        [InlineData("ACTIVE")]
        [InlineData("WAITING")]
        [InlineData("SESSIONS")]
        public void UppercaseLabelsFitTheKey(String label) =>
            Assert.Equal(label, TextMetrics.FitEnd(label, 84, 9));

        // Uppercase and digits are the widest glyphs; if they were measured as narrow, the labels
        // would overflow silently.
        [Fact]
        public void UppercaseIsMeasuredWiderThanLowercase() =>
            Assert.True(TextMetrics.TextWidth("WWWW", 12) > TextMetrics.TextWidth("wwww", 12));

        [Fact]
        public void NarrowGlyphsAreMeasuredNarrower() =>
            Assert.True(TextMetrics.TextWidth("llll", 12) < TextMetrics.TextWidth("oooo", 12));

        [Fact]
        public void WidthScalesWithFontSize() =>
            Assert.True(TextMetrics.TextWidth("session", 20) > TextMetrics.TextWidth("session", 10));

        [Fact]
        public void FitFontSizeStaysInsideItsRange()
        {
            Assert.InRange(TextMetrics.FitFontSize("a", KeyBox, 10, 20), 10, 20);
            Assert.InRange(TextMetrics.FitFontSize(new String('m', 60), KeyBox, 10, 20), 10, 20);
        }

        [Fact]
        public void ALongerNameNeverGetsABiggerSize() =>
            Assert.True(
                TextMetrics.FitFontSize("a-very-long-project-name", KeyBox, 10, 20)
                <= TextMetrics.FitFontSize("api", KeyBox, 10, 20));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void EmptyInputIsHarmless(String text)
        {
            Assert.Equal(0, TextMetrics.TextWidth(text, 12));
            Assert.Equal("", TextMetrics.Fit(text, KeyBox, 12));
            Assert.Equal("", TextMetrics.FitEnd(text, KeyBox, 12));
        }

        // DrawText centres the text's line box, not its ink, so glyphs sit high by an amount that
        // grows with the font size. These are the measured offsets: digits drawn into a known
        // rectangle, ink centre compared with rectangle centre, on the shipped font.
        [Theory]
        [InlineData(12, 2)]
        [InlineData(16, 4)]
        [InlineData(20, 5)]
        [InlineData(24, 7)]
        [InlineData(28, 8)]
        [InlineData(32, 10)]
        [InlineData(36, 11)]
        [InlineData(40, 13)]
        public void InkOffsetMatchesTheMeasuredFont(Int32 fontSize, Int32 expected) =>
            Assert.Equal(expected, TextMetrics.InkOffset(fontSize));

        [Fact]
        public void InkOffsetGrowsWithFontSize() =>
            Assert.True(TextMetrics.InkOffset(40) > TextMetrics.InkOffset(12));

        // Small text needs almost no correction; a negative shift would push it out of its box.
        [Fact]
        public void TinyTextIsNotShiftedUpwards() =>
            Assert.True(TextMetrics.InkOffset(9) >= 0);

        // A box too small for even one character must still return something drawable, not throw.
        [Fact]
        public void AnImpossiblyNarrowBoxDoesNotThrow()
        {
            Assert.NotNull(TextMetrics.Fit("copilot-warp", 4, 14));
            Assert.NotNull(TextMetrics.FitEnd("copilot-warp", 4, 14));
        }
    }
}
