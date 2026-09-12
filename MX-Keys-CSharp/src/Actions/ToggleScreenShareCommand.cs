namespace Loupedeck.MxKeysGoogleMeetPlugin.Actions
{
    using System;

    using Loupedeck.MxKeysGoogleMeetPlugin.Bridge;

    /// <summary>
    /// Single toggle: pressing it while not sharing clicks Meet's "Share screen" button and stops
    /// there — the browser's own tab/window/screen picker is a native OS dialog no content script
    /// can drive, so the user finishes that step by hand. Pressing it while already sharing clicks
    /// "Stop sharing", which IS a plain in-page button, so that direction is fully automatic. No
    /// live on/off state is tracked (the extension doesn't report one), so the icon stays a flat
    /// static colour rather than reflecting sharing state the way the other toggles do.
    /// </summary>
    public class ToggleScreenShareCommand : PluginDynamicCommand
    {
        public ToggleScreenShareCommand()
            : base(displayName: "Toggle Screen Share",
                   description: "Starts (or stops) sharing your screen in the active Google Meet call",
                   groupName: "Google Meet")
        {
            this.SetWidget(true);
        }

        protected override void RunCommand(String actionParameter) => MeetBridge.Instance.Send(MeetBridge.Commands.ToggleScreenShare);

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) => "​";

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize) =>
            KeyImage.Render(imageSize, "share_screen", KeyImage.Blue);
    }
}
