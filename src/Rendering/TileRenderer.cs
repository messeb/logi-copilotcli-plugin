namespace Loupedeck.CopilotCLIPlugin.Rendering
{
    using System;

    using Loupedeck.CopilotCLIPlugin.Sessions;

    /// <summary>
    /// Draws every key this plugin puts on the device.
    ///
    /// Two deliberately different looks, so you can tell at a glance whether you are looking at a
    /// key that opens something or a key that IS something:
    ///
    /// - The three top-level keys are dark, with a coloured ring around a big count. They sit on a
    ///   home page beside other plugins' keys and should read as instruments, not as alarms.
    /// - Session tiles are filled edge to edge with their state colour. Inside a folder every key
    ///   is one of these, so colour is the fastest way to find the one that needs you.
    /// </summary>
    public static class TileRenderer
    {
        // Chosen for contrast against white text rather than for vividness. WCAG ratios against
        // #FFFFFF: busy 4.61 : done 4.83 : attention 5.89 : idle 7.15 - all clear of 4.5.
        private static readonly BitmapColor Busy = new(0x1F, 0x6F, 0xC4);
        private static readonly BitmapColor Done = new(0x3E, 0x7F, 0x4E);
        private static readonly BitmapColor Attention = new(0xB3, 0x3A, 0x32);
        private static readonly BitmapColor Idle = new(0x55, 0x58, 0x5C);
        private static readonly BitmapColor Surface = new(0x15, 0x17, 0x1B);
        private static readonly BitmapColor Ink = new(0xF2, 0xF4, 0xF7);

        /// <summary>Animation tick. The renderer stays a pure function of (session, frame), so
        /// nothing has to be remembered between paints.</summary>
        public const Int32 TickMs = 250;

        /// <summary>Frames for one full sweep of the busy bar.</summary>
        private const Int32 SweepFrames = 10;

        /// <summary>Frames per half-cycle of the attention blink: 2 -> on 500 ms, off 500 ms.</summary>
        private const Int32 BlinkFrames = 2;

        public static BitmapColor StateColor(SessionActivity activity) => activity switch
        {
            SessionActivity.Busy => Busy,
            SessionActivity.Done => Done,
            SessionActivity.Attention => Attention,
            _ => Idle,
        };

        /// <summary>
        /// One session: project name over its branch, with a status footer carrying a dot, the
        /// state in words and the time spent in it.
        /// </summary>
        public static BitmapImage Session(
            SessionSnapshot session, DateTimeOffset now, PluginImageSize size, Int32 frame)
        {
            var blocked = session.Activity == SessionActivity.Attention;
            var bg = StateColor(session.Activity);

            // Colour alone is a poor alarm: it is static, and one red tile among blue ones is easy
            // to look straight past. Dimming the whole tile on and off makes it move, and movement
            // is what peripheral vision actually picks up.
            if (blocked && (frame / BlinkFrames) % 2 == 1)
            {
                bg = Shade(Attention, 0.5);
            }

            using var b = new BitmapBuilder(size);
            var w = b.Width;
            var h = b.Height;

            b.Clear(bg);
            Sheen(b);

            var footerTop = (Int32)(h * 0.72);

            // The name gets the space and the weight. It shrinks to fit before it truncates, so a
            // long project name stays whole where it can and loses its middle only when it cannot.
            var nameBox = w - 10;
            var project = String.IsNullOrEmpty(session.Project) ? "—" : session.Project;
            var nameSize = TextMetrics.FitFontSize(project, nameBox, Math.Max(10, (Int32)(h * 0.125)), (Int32)(h * 0.23));
            Draw(b, TextMetrics.Fit(project, nameBox, nameSize), 5, (Int32)(h * 0.10), nameBox, (Int32)(h * 0.34), Ink, nameSize);

            var subtitle = session.Subtitle;
            if (!String.IsNullOrWhiteSpace(subtitle))
            {
                var subSize = Math.Max(9, (Int32)(h * 0.105));
                Draw(b, TextMetrics.FitEnd(subtitle, nameBox, subSize), 5, (Int32)(h * 0.47), nameBox,
                    (Int32)(h * 0.20), new BitmapColor(Ink, 165), subSize);
            }

            // Footer: a scrim rather than a solid bar, so the state colour still shows through and
            // the tile reads as one object instead of two stacked ones.
            b.FillRectangle(0, footerTop, w, h - footerTop, new BitmapColor(0, 0, 0, 92));

            var footerH = h - footerTop;
            var dotR = Math.Max(2.5f, h * 0.030f);
            var dotY = footerTop + (footerH / 2f) - 1;
            b.FillCircle(6 + dotR, dotY, dotR, blocked ? Ink : new BitmapColor(Tint(bg, 0.75), 235));

            // The clock is laid out first and the caption gets whatever is left. DrawText centres
            // within the rectangle it is handed and does not clip, so giving each its exact width
            // is what keeps "your turn" from printing on top of "0:09".
            var footSize = Math.Max(9, (Int32)(h * 0.10));
            var clock = SessionClassifier.FormatElapsed(session.ElapsedIn(now));
            var clockW = TextMetrics.TextWidth(clock, footSize);
            var captionX = (Int32)(9 + (dotR * 2));
            var captionW = w - captionX - clockW - 8;

            if (captionW > footSize)
            {
                Draw(b, TextMetrics.FitEnd(Caption(session.Activity), captionW, footSize),
                    captionX, footerTop, captionW, footerH, new BitmapColor(Ink, 225), footSize);
            }

            Draw(b, clock, w - clockW - 5, footerTop, clockW, footerH, new BitmapColor(Ink, 175), footSize);

            if (session.Activity == SessionActivity.Busy)
            {
                Sweep(b, w, h, bg, frame);
            }

            return b.ToImage();
        }

        // One word each, deliberately. DrawText wraps at spaces when a string is close to its
        // box, and in a footer only a few pixels taller than one line the second line lands on top
        // of the first - "your turn" printed as "your" over "turn". A single word cannot wrap.
        private static String Caption(SessionActivity activity) => activity switch
        {
            SessionActivity.Busy => "working",
            SessionActivity.Attention => "blocked",
            SessionActivity.Done => "ready",
            SessionActivity.Idle => "idle",
            _ => "?",
        };

        /// <summary>The Active key: how many sessions are working.</summary>
        public static BitmapImage ActiveButton(Int32 count, PluginImageSize size) =>
            CountKey(count, "ACTIVE", Busy, size, false, 0);

        /// <summary>
        /// The Waiting key. Red and blinking while anything is actually blocked, green while the
        /// only thing waiting is a finished turn - the difference between "go now" and "when you
        /// get a moment", which is worth more than a number alone.
        /// </summary>
        public static BitmapImage WaitingButton(Int32 count, Int32 blocked, PluginImageSize size, Int32 frame) =>
            CountKey(count, "WAITING", blocked > 0 ? Attention : Done, size, blocked > 0, frame);

        /// <summary>
        /// The Sessions key: how many are open at all. Deliberately the neutral grey - this one
        /// answers "how much is going on", and a status hue would make it compete with the two keys
        /// that mean something needs doing.
        /// </summary>
        public static BitmapImage AllButton(Int32 count, PluginImageSize size) =>
            CountKey(count, "SESSIONS", Idle, size, false, 0);

        /// <summary>
        /// A count on a dark key, inside a coloured ring.
        ///
        /// The ring is what makes these read as one family and keeps them calm next to other
        /// plugins' keys, while still carrying the state colour. An empty count dims the whole
        /// thing rather than drawing a dash, which at this size looked like a broken glyph.
        /// </summary>
        private static BitmapImage CountKey(
            Int32 count, String label, BitmapColor accent, PluginImageSize size, Boolean blink, Int32 frame)
        {
            var empty = count <= 0;
            var ring = empty ? new BitmapColor(Idle, 120) : accent;

            if (blink && (frame / BlinkFrames) % 2 == 1)
            {
                ring = Shade(accent, 0.55);
            }

            using var b = new BitmapBuilder(size);
            var w = b.Width;
            var h = b.Height;

            b.Clear(Surface);

            // One geometry for all three keys, so Active, Waiting and Sessions are the same size
            // whatever state they are in.
            var centreX = w / 2f;
            var centreY = h * 0.44f;
            var radius = h * 0.30f;
            var stroke = Math.Max(3f, h * 0.035f);

            // A wash of the accent behind the ring: barely visible on its own, but it stops the key
            // reading as pure black and ties the number to its colour.
            //
            // It has to sit strictly INSIDE the ring. At a larger radius it spilled past the ring,
            // and because the wash is drawn stronger while something is blocked, that made the
            // Waiting key look like a bigger circle than the other two exactly when it mattered.
            // Skipped for the neutral key: a grey wash inside a grey ring has too little contrast
            // to separate them, and the two merge into one soft blob that reads as a heavier ring
            // than the coloured keys have.
            var washed = !empty && !Same(accent, Idle);
            if (washed)
            {
                b.FillCircle(centreX, centreY, radius - (stroke / 2f) - 1f,
                    new BitmapColor(accent, blink ? 70 : 42));
            }

            // An arc with a full sweep: DrawCircle is a filled disc, and a ring is what gives the
            // key its frame.
            b.DrawArc((Int32)centreX, (Int32)centreY, (Int32)radius, 0, 360, ring, stroke);

            // Sized explicitly: the default is small enough to look like a mistake inside a ring,
            // and a three-digit count still has to fit, so the size falls back as digits are added.
            var digits = count.ToString();
            var numberSize = TextMetrics.FitFontSize(digits, (Int32)(h * 0.44), (Int32)(h * 0.22), (Int32)(h * 0.42));
            // Centred on the ring, not on the rectangle: the ring's centre is at 0.44h, so the
            // number's box is placed symmetrically about it and the ink correction does the rest.
            Draw(b, digits, (Int32)(w * 0.20), (Int32)(h * 0.44) - (Int32)(h * 0.23),
                (Int32)(w * 0.60), (Int32)(h * 0.46), empty ? new BitmapColor(Ink, 110) : Ink, numberSize);

            var labelSize = Math.Max(9, (Int32)(h * 0.10));
            Draw(b, TextMetrics.FitEnd(label, w - 6, labelSize), 3, (Int32)(h * 0.80), w - 6,
                (Int32)(h * 0.16), new BitmapColor(Ink, empty ? 105 : 190), labelSize);

            return b.ToImage();
        }

        /// <summary>The key before the plugin is connected to Copilot CLI, and the confirmation
        /// that connects it.</summary>
        public static BitmapImage Setup(SetupAction armed, PluginImageSize size)
        {
            using var b = new BitmapBuilder(size);
            var w = b.Width;
            var h = b.Height;

            var accent = armed switch
            {
                SetupAction.Enable => Busy,
                SetupAction.Disable => Attention,
                _ => Idle,
            };

            // Two lines rather than one: "Set up" alone does not say set up what. The armed state
            // changes the colour as well as the words, because the difference between "nothing has
            // happened yet" and "the next press writes a file" is too important to rest on reading
            // two small words.
            var (line, note) = armed switch
            {
                SetupAction.Enable => ("Press again", "to connect"),
                SetupAction.Disable => ("Press again", "to disconnect"),
                _ => ("Set up", "Copilot CLI"),
            };

            b.Clear(Surface);
            b.FillRectangle(0, 0, w, Math.Max(3, h / 26), accent);

            var lineSize = TextMetrics.FitFontSize(line, w - 8, Math.Max(10, (Int32)(h * 0.11)), (Int32)(h * 0.16));
            var noteSize = Math.Max(9, (Int32)(h * 0.10));
            Draw(b, line, 4, (Int32)(h * 0.20), w - 8, (Int32)(h * 0.28), Ink, lineSize);
            Draw(b, TextMetrics.FitEnd(note, w - 8, noteSize), 4, (Int32)(h * 0.54), w - 8, (Int32)(h * 0.22),
                new BitmapColor(Ink, 150), noteSize);

            return b.ToImage();
        }

        /// <summary>Shown instead of a blank page when a folder has nothing in it. A page of empty
        /// tiles is a convincing impression of a broken plugin.</summary>
        public static BitmapImage Nothing(String line, PluginImageSize size)
        {
            using var b = new BitmapBuilder(size);
            var w = b.Width;
            var h = b.Height;

            b.Clear(Surface);
            b.DrawArc((Int32)(w / 2f), (Int32)(h * 0.40f), (Int32)(h * 0.18f), 0, 360,
                new BitmapColor(Idle, 110), Math.Max(2f, h * 0.022f));
            var emptySize = Math.Max(9, (Int32)(h * 0.10));
            Draw(b, TextMetrics.FitEnd(line, w - 8, emptySize), 4, (Int32)(h * 0.64), w - 8, (Int32)(h * 0.24),
                new BitmapColor(Ink, 120), emptySize);

            return b.ToImage();
        }

        public static BitmapImage Blank(PluginImageSize size)
        {
            using var b = new BitmapBuilder(size);
            b.Clear(Surface);
            return b.ToImage();
        }

        // A light-from-above wash: a highlight over the top half and a shadow under the bottom.
        // Drawn in bands rather than per row - at this size the banding is invisible and it is a
        // third of the fill calls.
        private static void Sheen(BitmapBuilder b)
        {
            var w = b.Width;
            var h = b.Height;
            const Int32 Band = 3;

            for (var y = 0; y < h; y += Band)
            {
                var t = y / (Double)h;

                if (t < 0.5)
                {
                    var alpha = (Int32)(26 * (1 - (t / 0.5)));
                    if (alpha > 0)
                    {
                        b.FillRectangle(0, y, w, Band, new BitmapColor(255, 255, 255, alpha));
                    }
                }
                else
                {
                    var alpha = (Int32)(60 * ((t - 0.5) / 0.5));
                    if (alpha > 0)
                    {
                        b.FillRectangle(0, y, w, Band, new BitmapColor(0, 0, 0, alpha));
                    }
                }
            }
        }

        // An indeterminate progress bar across the bottom edge: a segment that slides in from the
        // left, crosses, and slides out to the right. Legible across a room, which a row of
        // cycling ASCII dots at this size is not.
        private static void Sweep(BitmapBuilder b, Int32 w, Int32 h, BitmapColor bg, Int32 frame)
        {
            var barH = Math.Max(3, (Int32)(h * 0.045));
            var y = h - barH;

            b.FillRectangle(0, y, w, barH, new BitmapColor(0, 0, 0, 110));

            var segW = Math.Max(12, (Int32)(w * 0.32));
            var travel = w + segW;
            var x = (Int32)(((frame % SweepFrames) / (Double)SweepFrames) * travel) - segW;

            // Clamped rather than relying on the canvas to clip a negative origin.
            var x0 = Math.Max(0, x);
            var x1 = Math.Min(w, x + segW);
            if (x1 > x0)
            {
                b.FillRectangle(x0, y, x1 - x0, barH, new BitmapColor(Tint(bg, 0.8), 235));
            }
        }

        /// <summary>
        /// DrawText, with the glyphs actually centred in the box rather than sitting high in it.
        /// Every piece of text on every key goes through here.
        /// </summary>
        private static void Draw(
            BitmapBuilder b, String text, Int32 x, Int32 y, Int32 w, Int32 h, BitmapColor color, Int32 fontSize) =>
            b.DrawText(text, x, y + TextMetrics.InkOffset(fontSize), w, h, color, fontSize);

        private static Boolean Same(BitmapColor a, BitmapColor b) =>
            a.R == b.R && a.G == b.G && a.B == b.B;

        // A lighter wash of a colour, for accents that should read as the same hue.
        private static BitmapColor Tint(BitmapColor c, Double amount) => new(
            (Byte)(c.R + ((255 - c.R) * amount)),
            (Byte)(c.G + ((255 - c.G) * amount)),
            (Byte)(c.B + ((255 - c.B) * amount)));

        // The opposite of Tint: toward black, for the blink's dark phase.
        private static BitmapColor Shade(BitmapColor c, Double amount) => new(
            (Byte)(c.R * (1 - amount)),
            (Byte)(c.G * (1 - amount)),
            (Byte)(c.B * (1 - amount)));
    }
}
