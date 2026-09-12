namespace Loupedeck.MxKeysGoogleMeetPlugin.Actions
{
    using System;

    using Loupedeck.MxKeysGoogleMeetPlugin.Bridge;

    public class ToggleCaptionsCommand : PluginDynamicCommand
    {
        // Kept so OnUnload can remove the exact same delegate — see ToggleMicCommand for why.
        private readonly Action<MeetState> _onStateChanged;

        public ToggleCaptionsCommand()
            : base(displayName: "Toggle Captions",
                   description: "Turns closed captions on or off in the active Google Meet call",
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

        protected override void RunCommand(String actionParameter) => MeetBridge.Instance.Send(MeetBridge.Commands.ToggleCaptions);

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) => "​";

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize) =>
            KeyImage.Render(imageSize, "captions", this.Color());

        private BitmapColor Color()
        {
            var state = MeetBridge.Instance.State;
            if (!state.Connected || !state.InCall)
            {
                return KeyImage.Gray;
            }
            return state.CaptionsOn switch { true => KeyImage.Green, _ => KeyImage.Gray };
        }
    }
}
