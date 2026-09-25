namespace Loupedeck.CopilotCLIPlugin.Actions
{
    using System;
    using System.Collections.Generic;

    using Loupedeck.CopilotCLIPlugin.Rendering;
    using Loupedeck.CopilotCLIPlugin.Sessions;

    /// <summary>
    /// The sessions doing work right now. Press the button to see them, press a tile to go there.
    ///
    /// Longest-running first: the session that has been grinding for four minutes is the one worth
    /// looking at, and the one that just started needs nothing from you yet.
    /// </summary>
    public class ActiveSessionsFolder : SessionsFolderBase
    {
        public ActiveSessionsFolder()
        {
            this.DisplayName = "Active sessions";
            this.GroupName = "Sessions";
        }

        protected override String EmptyLine => "Nothing running";

        protected override IReadOnlyList<SessionSnapshot> Select(DateTimeOffset now) => Store.Active(now);

        protected override BitmapImage RenderFolderButton(PluginImageSize imageSize, Int32 frame) =>
            TileRenderer.ActiveButton(Store.Active(DateTimeOffset.UtcNow).Count, imageSize);
    }
}
