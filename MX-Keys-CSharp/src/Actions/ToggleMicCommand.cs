namespace Loupedeck.MxKeysGoogleMeetPlugin.Actions
{
    using System;

    using Loupedeck.MxKeysGoogleMeetPlugin.Bridge;

    public class ToggleMicCommand : PluginDynamicCommand
    {
        // Kept so OnUnload can remove the exact same delegate — `+=` with an anonymous lambda has no
        // way to be unsubscribed later, which left every reload accumulating one more subscriber on
        // MeetBridge.Instance.StateChanged for the lifetime of the process (flagged in review).
        private readonly Action<MeetState> _onStateChanged;

        public ToggleMicCommand()
            : base(displayName: "Toggle Microphone",
                   description: "Mutes or unmutes your microphone in the active Google Meet call",
                   groupName: "Google Meet")
        {
            // Without this, Options+ treats our image as a small inset icon and reserves the rest
            // of the key for its own static label compositor — this puts our image on the full
            // surface instead.
            this.SetWidget(true);
            this._onStateChanged = _ => this.ActionImageChanged();
            MeetBridge.Instance.StateChanged += this._onStateChanged;
        }

        protected override Boolean OnUnload()
        {
            MeetBridge.Instance.StateChanged -= this._onStateChanged;
            return base.OnUnload();
        }

        protected override void RunCommand(String actionParameter) => MeetBridge.Instance.Send(MeetBridge.Commands.ToggleMic);

        // An icon carries the meaning, so no label — but String.Empty makes Options+ fall back to
        // showing the declared displayName ("Toggle Microphone") instead of nothing. A zero-width
        // space is non-empty (no fallback) and renders as nothing.
        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) => "​";

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize) =>
            KeyImage.Render(imageSize, "mic", this.Color());

        private BitmapColor Color()
        {
            var state = MeetBridge.Instance.State;
            if (!state.Connected || !state.InCall)
            {
                return KeyImage.Gray;
            }
            return state.MicMuted switch { false => KeyImage.Green, true => KeyImage.Red, _ => KeyImage.Gray };
        }
    }
}
