namespace Loupedeck.CopilotCLIPlugin.Actions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;

    using Loupedeck.CopilotCLIPlugin.Rendering;
    using Loupedeck.CopilotCLIPlugin.Sessions;
    using Loupedeck.CopilotCLIPlugin.Terminals;

    /// <summary>
    /// A folder listing one bucket of sessions, one tile each, newest page first.
    ///
    /// The host chunks a dynamic folder's action list into pages and takes one of the nine keys for
    /// its own Back button, leaving eight usable tiles per page. That is measured on an MX Creative
    /// Keypad, not assumed: a folder returning 27 names produced pages of 8/8/8/3. Paging itself is
    /// left to the host - there is nothing to gain from doing it by hand when every page holds the
    /// same kind of tile.
    /// </summary>
    public abstract class SessionsFolderBase : PluginDynamicFolder
    {
        private const String SetupParameter = "setup";
        private const String EmptyParameter = "empty";
        private const String SessionPrefix = "s:";

        /// <summary>How a profile addresses the key that opens a dynamic folder: the middle token
        /// of "$&lt;plugin&gt;___#DynamicFolder___DynamicFolder#&lt;type&gt;".</summary>
        private const String FolderActionName = "#DynamicFolder";

        private readonly Timer _tick;
        private volatile Boolean _open;
        private volatile Int32 _frame;

        protected SessionsFolderBase()
        {
            // Animation only has to run while someone is looking at it.
            this._tick = new Timer(_ => this.OnTick(), null, Timeout.Infinite, Timeout.Infinite);
        }

        protected static SessionStore Store => SessionStore.Instance;

        /// <summary>The line shown when this folder has nothing to list.</summary>
        protected abstract String EmptyLine { get; }

        /// <summary>The sessions this folder lists, already in display order.</summary>
        protected abstract IReadOnlyList<SessionSnapshot> Select(DateTimeOffset now);

        /// <summary>This folder's own button face, drawn from the current counts.</summary>
        protected abstract BitmapImage RenderFolderButton(PluginImageSize imageSize, Int32 frame);

        public override PluginDynamicFolderNavigation GetNavigationArea(DeviceType deviceType) =>
            PluginDynamicFolderNavigation.ButtonArea;

        /// <summary>
        /// Subscribing happens here, for the plugin's whole life, and NOT in Activate.
        ///
        /// Activate runs when the folder is opened, which is exactly when this folder's own button
        /// is not on screen. Subscribing there meant that while the button WAS visible - the rest of
        /// the time - nothing ever told the host the counts had moved, so it kept redrawing whatever
        /// it had last cached: a green Waiting key sitting above a session that had gone red.
        /// </summary>
        public override Boolean Load()
        {
            Store.Changed += this.OnSessionsChanged;
            HookWiring.Changed += this.OnWiringChanged;
            return base.Load();
        }

        public override Boolean Unload()
        {
            Store.Changed -= this.OnSessionsChanged;
            HookWiring.Changed -= this.OnWiringChanged;
            this._tick.Change(Timeout.Infinite, Timeout.Infinite);
            return base.Unload();
        }

        public override Boolean Activate()
        {
            this._open = true;

            // The animation only has to run while someone is looking at it; the button's own state
            // is carried by colour, which does not need a frame clock.
            this._tick.Change(TileRenderer.TickMs, TileRenderer.TickMs);
            return base.Activate();
        }

        public override Boolean Deactivate()
        {
            this._open = false;
            this._tick.Change(Timeout.Infinite, Timeout.Infinite);

            // Park the blink on its bright phase. The frame clock stops with the folder, so whatever
            // value it held is the one the closed button would keep drawing with - and freezing on
            // the dim phase would leave a muted alarm sitting on the home page indefinitely.
            this._frame = 0;
            return base.Deactivate();
        }

        public override IEnumerable<String> GetButtonPressActionNames(DeviceType deviceType) =>
            this.BuildParameters().Select(this.CreateCommandName);

        /// <summary>
        /// Empty, not null.
        ///
        /// The SDK defines null as "no display name is available", and the host answers that by
        /// falling back to the action's own name and reserving a strip of the key to draw it in -
        /// which is what stops the tile's background filling the key. An empty string is a name,
        /// and it occupies nothing, so the image gets the whole face.
        /// </summary>
        public override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) => "";

        /// <summary>Same reasoning as <see cref="GetCommandDisplayName"/>, for the key that opens
        /// this folder: the count image carries its own label and wants the full face.</summary>
        public override String GetButtonDisplayName(PluginImageSize imageSize) => "";

        public override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            try
            {
                if (actionParameter == SetupParameter)
                {
                    return TileRenderer.Setup(HookWiring.Armed, imageSize);
                }

                if (actionParameter == EmptyParameter)
                {
                    return TileRenderer.Nothing(this.EmptyLine, imageSize);
                }

                var now = DateTimeOffset.UtcNow;
                var session = this.Find(actionParameter, now);

                // The session ended between the page being built and this tile being drawn.
                return session is null
                    ? TileRenderer.Blank(imageSize)
                    : TileRenderer.Session(session, now, imageSize, this._frame);
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "could not draw a tile");
                return TileRenderer.Blank(imageSize);
            }
        }

        public override void RunCommand(String actionParameter)
        {
            if (actionParameter == SetupParameter)
            {
                if (HookWiring.PressSetup())
                {
                    this.ButtonActionNamesChanged();
                }

                this.RepaintVisible();
                return;
            }

            if (actionParameter == EmptyParameter)
            {
                return;
            }

            var session = this.Find(actionParameter, DateTimeOffset.UtcNow);
            if (session is null)
            {
                return;
            }

            if (SessionFocus.Focus(session))
            {
                // Pressing a session tile means "take me there", and the folder is not where you
                // are going. Closing it is what makes the press a single gesture.
                this.Close();
            }
        }

        /// <summary>This folder's own button, on the page that opens it.
        ///
        /// Public, not protected: the SDK documentation's example declares it protected, but the
        /// shipped PluginApi declares it public and C# will not narrow an override.</summary>
        public override BitmapImage GetButtonImage(PluginImageSize imageSize)
        {
            try
            {
                return this.RenderFolderButton(imageSize, this._frame);
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "could not draw the folder button");
                return null;
            }
        }

        private SessionSnapshot Find(String actionParameter, DateTimeOffset now)
        {
            if (actionParameter is null || !actionParameter.StartsWith(SessionPrefix, StringComparison.Ordinal))
            {
                return null;
            }

            // The parameter is "s:<uuid>:<state>"; only the UUID identifies the session. The state
            // is carried in the name on purpose - see BuildParameters - so the tail is ignored here.
            var rest = actionParameter.Substring(SessionPrefix.Length);
            var separator = rest.IndexOf(':');
            var uuid = separator < 0 ? rest : rest.Substring(0, separator);

            // Looked up in the whole store rather than in this folder's selection: a session that
            // moved buckets between the press and the lookup should still be focusable. Its tile is
            // about to disappear from this folder either way.
            return Store.Sessions.FirstOrDefault(s => s.WarpUuid == uuid);
        }

        /// <summary>
        /// The folder's page layout, as raw action parameters.
        ///
        /// Each session's state is baked into its parameter ("s:&lt;uuid&gt;:&lt;state&gt;") purely so
        /// that the list CHANGES when a session changes state. The host re-queries a folder's own
        /// button image only when the action names actually differ - measured: with a constant name
        /// list, ButtonActionNamesChanged fires and GetButtonImage is never called again, which is
        /// what left a green Waiting key above a session that had gone red.
        ///
        /// It costs nothing in churn: a state is a handful of transitions per turn, and the run of
        /// tool calls that makes up most of a turn all map to "busy", so the names hold still
        /// exactly when nothing has visibly changed.
        /// </summary>
        private List<String> BuildParameters()
        {
            // Unwired, there are no sessions and never will be, so a page of blank tiles would be a
            // convincing impression of a broken plugin. One tile that says what to do instead.
            if (!HookWiring.IsWired || HookWiring.Armed != SetupAction.None)
            {
                return new List<String> { SetupParameter };
            }

            var sessions = this.Select(DateTimeOffset.UtcNow);

            return sessions.Count == 0
                ? new List<String> { EmptyParameter }
                : sessions.Select(s => $"{SessionPrefix}{s.WarpUuid}:{s.Activity}").ToList();
        }

        /// <summary>
        /// Asks the host to re-read this folder's own button image.
        ///
        /// PluginDynamicFolder has no ButtonImageChanged - enumerated from the shipped PluginApi,
        /// and the class has no base type beyond Object, so nothing is inherited either. The
        /// plugin-level equivalent is addressed the way a profile addresses the key:
        /// "$&lt;plugin&gt;___#DynamicFolder___&lt;this.Name&gt;", whose middle token is the action
        /// name and whose last is the parameter.
        ///
        /// Best-effort and wrapped, because it is the one part of this that could not be confirmed
        /// on the device - the keypad was not rendering the relevant page during testing, so the
        /// measurement was inconclusive rather than negative. It costs nothing if the host ignores
        /// it, and the subscription and action-name fixes stand on their own.
        /// </summary>
        private void FolderButtonChanged()
        {
            try
            {
                this.Plugin?.OnActionImageChanged(FolderActionName, this.Name, true);
            }
            catch (Exception ex)
            {
                PluginLog.Verbose($"could not invalidate the folder button: {ex.Message}");
            }
        }

        private void OnWiringChanged(Object sender, EventArgs e)
        {
            this.ButtonActionNamesChanged();
            this.FolderButtonChanged();
            this.RepaintVisible();
        }

        private void OnSessionsChanged(Object sender, EventArgs e)
        {
            // Fires whether or not the folder is open. ButtonActionNamesChanged is what makes the
            // host come back and ask for this folder's own button image again, so it is the only
            // thing keeping the counts on a closed folder's key honest.
            //
            // Every folder rebuilds on any change, not only on changes to its own bucket, because a
            // session moving from Active to Waiting alters both lists without the set of session
            // files changing at all.
            this.FolderButtonChanged();

            this.ButtonActionNamesChanged();
            this.RepaintVisible();
        }

        // Advances the busy sweep and the attention blink.
        //
        // Only the tiles that are actually moving are repainted. Redrawing the whole page four
        // times a second would push eight images per tick for the sake of one that changed, and an
        // all-idle deck would animate nothing yet still cost the traffic.
        private void OnTick()
        {
            // The timer only runs between Activate and Deactivate, but a callback already in flight
            // can still arrive after the folder closed.
            if (!this._open)
            {
                return;
            }

            try
            {
                var now = DateTimeOffset.UtcNow;
                var moving = this.Select(now)
                    .Where(s => s.Activity is SessionActivity.Busy or SessionActivity.Attention)
                    .Select(s => SessionPrefix + s.WarpUuid)
                    .ToList();

                if (moving.Count == 0)
                {
                    return;
                }

                this._frame++;

                foreach (var name in moving)
                {
                    this.CommandImageChanged(name);
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "animation tick failed");
            }
        }

        private void RepaintVisible()
        {
            if (!this._open)
            {
                return;
            }

            foreach (var name in this.BuildParameters())
            {
                this.CommandImageChanged(name);
            }
        }
    }
}
