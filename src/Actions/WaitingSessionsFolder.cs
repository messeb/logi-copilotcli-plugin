namespace Loupedeck.CopilotCLIPlugin.Actions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Loupedeck.CopilotCLIPlugin.Rendering;
    using Loupedeck.CopilotCLIPlugin.Sessions;

    /// <summary>
    /// The sessions wanting something from you: blocked on a permission prompt, finished a turn, or
    /// up but never asked anything.
    ///
    /// Blocked sessions sort first and make the button blink, because they are the only ones where
    /// nothing at all moves until you act.
    /// </summary>
    public class WaitingSessionsFolder : SessionsFolderBase
    {
        public WaitingSessionsFolder()
        {
            this.DisplayName = "Waiting sessions";
            this.GroupName = "Sessions";
        }

        protected override String EmptyLine => "Nothing waiting";

        protected override IReadOnlyList<SessionSnapshot> Select(DateTimeOffset now) => Store.Waiting(now);

        protected override BitmapImage RenderFolderButton(PluginImageSize imageSize, Int32 frame)
        {
            var waiting = Store.Waiting(DateTimeOffset.UtcNow);
            var blocked = waiting.Count(s => s.Activity == SessionActivity.Attention);

            return TileRenderer.WaitingButton(waiting.Count, blocked, imageSize, frame);
        }
    }
}
