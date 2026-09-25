namespace Loupedeck.CopilotCLIPlugin.Rendering
{
    using System;

    /// <summary>
    /// Text measurement for laying out a 90-116 px key.
    ///
    /// Free of PluginApi types on purpose: BitmapBuilder exposes no way to measure a string, so
    /// these estimates are the only thing standing between a long branch name and text printed off
    /// the edge of the key - which makes them worth unit tests rather than trust.
    /// </summary>
    public static class TextMetrics
    {
        /// <summary>
        /// Width of a string, in pixels, near enough for laying out a 90 px key.
        ///
        /// BitmapBuilder exposes no text measurement and, importantly, DrawText does NOT clip to
        /// the rectangle it is given - it centres the text and lets it overflow, which is what put
        /// branch names off the edge of the key.
        ///
        /// Measured on the shipped font at several sizes: advance scales linearly with font size,
        /// and the per-character share depends almost entirely on the character class. A single
        /// flat factor has to be set to the widest case, which then truncates ordinary lowercase
        /// names that would have fitted - "copilot-warp" came out as "copil…-warp". Three classes
        /// track the measurements to within a few percent while still erring wide.
        /// </summary>
        /// <summary>
        /// Slack on every width estimate.
        ///
        /// DrawText decides for itself when to wrap, and it does so a little sooner than the
        /// per-character estimate predicts. Without headroom a string that "just fits" wraps onto a
        /// second line, which in a one-line footer prints on top of the first. Reserving a tenth of
        /// the width costs a character or two of truncation and removes the failure.
        /// </summary>
        public const Double Headroom = 1.12;

        /// <summary>
        /// How far down to shift a draw rectangle so the glyphs end up optically centred in it.
        ///
        /// DrawText centres the text's line box, not its ink, so a glyph sits high by an amount
        /// that grows with the font size - a big number inside a ring ends up noticeably above the
        /// ring's centre. Measured on the shipped font by drawing digits into a known rectangle and
        /// comparing the ink's centre with the rectangle's:
        ///
        ///   size 12 16 20 24 28 32 36 40
        ///   off  -2.0 -3.5 -5.0 -6.5 -8.0 -9.5 -10.5 -12.0
        ///
        /// which is 2.5 - 0.375 * size to within half a pixel across the whole range. Horizontal
        /// centring needs no correction; it was measured at zero.
        /// </summary>
        public static Int32 InkOffset(Int32 fontSize) =>
            (Int32)Math.Round((0.375 * fontSize) - 2.5, MidpointRounding.AwayFromZero);

        public static Double Advance(Char c)
        {
            if (c is 'i' or 'l' or 'j' or 't' or 'f' or 'r' or 'I' or '.' or ',' or ':' or ';'
                or '-' or '/' or '\'' or '`' or ' ' or '|' or '!' or '(' or ')' or '[' or ']')
            {
                return 0.34;
            }

            return Char.IsUpper(c) || Char.IsDigit(c) ? 0.64 : 0.52;
        }

        public static Int32 TextWidth(String text, Int32 fontSize)
        {
            if (String.IsNullOrEmpty(text))
            {
                return 0;
            }

            var units = 0.0;
            foreach (var c in text)
            {
                units += Advance(c);
            }

            return (Int32)Math.Ceiling(units * fontSize * Headroom);
        }

        /// <summary>The largest size in the range at which the text fits the width on one line.</summary>
        public static Int32 FitFontSize(String text, Int32 boxWidth, Int32 min, Int32 max)
        {
            if (String.IsNullOrEmpty(text))
            {
                return min;
            }

            var units = 0.0;
            foreach (var c in text)
            {
                units += Advance(c);
            }

            return Math.Clamp((Int32)(boxWidth / Math.Max(units * Headroom, 0.001)), min, max);
        }

        /// <summary>How many leading characters fit, by accumulating real advances.</summary>
        public static Int32 CharsThatFit(String text, Int32 boxWidth, Int32 fontSize)
        {
            var used = 0.0;
            var limit = boxWidth / (Double)Math.Max(fontSize, 1) / Headroom;

            for (var i = 0; i < text.Length; i++)
            {
                used += Advance(text[i]);
                if (used > limit)
                {
                    return i;
                }
            }

            return text.Length;
        }

        /// <summary>
        /// Middle-truncates to fit a width. Middle rather than tail because names that differ only
        /// at the end - acme-ios against acme-web - render identical when clipped from the right.
        /// </summary>
        public static String Fit(String text, Int32 boxWidth, Int32 fontSize)
        {
            if (String.IsNullOrEmpty(text) || TextWidth(text, fontSize) <= boxWidth)
            {
                return text ?? "";
            }

            var maxChars = Math.Max(1, CharsThatFit(text, boxWidth, fontSize));
            if (text.Length <= maxChars)
            {
                return text;
            }

            if (maxChars <= 2)
            {
                return text.Substring(0, maxChars);
            }

            var keep = maxChars - 1;
            var head = (keep + 1) / 2;
            return text.Substring(0, head) + "…" + text.Substring(text.Length - (keep - head));
        }

        /// <summary>Tail-truncates: for anything sentence-like the meaning is front-loaded.</summary>
        public static String FitEnd(String text, Int32 boxWidth, Int32 fontSize)
        {
            if (String.IsNullOrEmpty(text) || TextWidth(text, fontSize) <= boxWidth)
            {
                return text ?? "";
            }

            var maxChars = Math.Max(1, CharsThatFit(text, boxWidth, fontSize) - 1);
            return text.Length <= maxChars ? text : text.Substring(0, maxChars).TrimEnd() + "…";
        }
    }
}
