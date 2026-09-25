namespace Loupedeck.CopilotCLIPlugin.Actions
{
    using System;
    using System.Collections.Generic;

    using Loupedeck.CopilotCLIPlugin.Rendering;
    using Loupedeck.CopilotCLIPlugin.Sessions;

    /// <summary>
    /// Every Copilot CLI session that is open, whatever it is doing.
    ///
    /// Active and Waiting are both deliberately narrow - one means "working", the other means "this
    /// wants you now" - which leaves sessions that are merely open, idle, or in a state this build
    /// does not recognise with nowhere to appear. This is where they appear, and it is the only key
    /// that answers "how many sessions have I got going at all".
    /// </summary>
    public class AllSessionsFolder : SessionsFolderBase
    {
        public AllSessionsFolder()
        {
            this.DisplayName = "All sessions";
            this.GroupName = "Sessions";
        }

        protected override String EmptyLine => "No sessions";

        protected override IReadOnlyList<SessionSnapshot> Select(DateTimeOffset now) => Store.All(now);

        protected override BitmapImage RenderFolderButton(PluginImageSize imageSize, Int32 frame) =>
            TileRenderer.AllButton(Store.All(DateTimeOffset.UtcNow).Count, imageSize);
    }
}
