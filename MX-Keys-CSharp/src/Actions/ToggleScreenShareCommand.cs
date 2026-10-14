namespace Loupedeck.MxKeysGoogleMeetPlugin.Actions
{
    using System;

    using Loupedeck.MxKeysGoogleMeetPlugin.Bridge;

    /// <summary>
    /// Single toggle: pressing it while not sharing clicks Meet's "Share screen" button and stops
    /// there — the browser's own tab/window/screen picker is a native OS dialog no content script
    /// can drive, so the user finishes that step by hand. Pressing it while already sharing clicks
    /// "Stop sharing", which IS a plain in-page button, so that direction is fully automatic.
    ///
    /// The icon reflects live sharing state (green while sharing, blue when idle and able to
    /// start, gray when there's no call to share into). Note the asymmetry this creates: because
    /// starting a share needs the user to finish the native picker, the key can sit on blue for a
    /// few seconds after a press and only flip to green once they've actually chosen a surface —
    /// that delay is the picker, not a stale state read.
    /// </summary>
    public class ToggleScreenShareCommand : PluginDynamicCommand
    {
        // Kept so OnUnload can remove the exact same delegate — see ToggleMicCommand for why.
        private readonly Action<MeetState> _onStateChanged;

        public ToggleScreenShareCommand()
            : base(displayName: "Toggle Screen Share",
                   description: "Starts (or stops) sharing your screen in the active Google Meet call",
                   groupName: "Google Meet")
        {
            this.SetWidget(true);
            this._onStateChanged = _ => this.ActionImageChanged();
            MeetBridge.Instance.StateChanged += this._onStateChanged;
        }

        protected override Boolean OnUnload()
        {
            MeetBridge.Instance.StateChanged -= this._onStateChanged;
            return base.OnUnload();
        }

        protected override void RunCommand(String actionParameter) => MeetBridge.Instance.Send(MeetBridge.Commands.ToggleScreenShare);

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) => "​";

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize) =>
            KeyImage.Render(imageSize, "share_screen", this.Color());

        private BitmapColor Color()
        {
            var state = MeetBridge.Instance.State;
            if (!state.Connected || !state.InCall)
            {
                return KeyImage.Gray;
            }
            // Blue rather than Red for "not sharing": unlike mic/camera, not sharing isn't an
            // alarming state to be in — it's the normal one — so Blue reads as "available to
            // start" while Green marks the state you'd want to notice and stop.
            return state.Sharing ? KeyImage.Green : KeyImage.Blue;
        }
    }
}
