namespace Loupedeck.MxKeysGoogleMeetPlugin.Actions
{
    using System;

    using Loupedeck.MxKeysGoogleMeetPlugin.Bridge;

    public class ToggleHandCommand : PluginDynamicCommand
    {
        // Kept so OnUnload can remove the exact same delegate — see ToggleMicCommand for why.
        private readonly Action<MeetState> _onStateChanged;

        public ToggleHandCommand()
            : base(displayName: "Raise/Lower Hand",
                   description: "Raises or lowers your hand in the active Google Meet call",
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

        protected override void RunCommand(String actionParameter) => MeetBridge.Instance.Send(MeetBridge.Commands.ToggleHand);

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) => "​";

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize) =>
            KeyImage.Render(imageSize, "hand", this.Color());

        private BitmapColor Color()
        {
            var state = MeetBridge.Instance.State;
            if (!state.Connected || !state.InCall)
            {
                return KeyImage.Gray;
            }
            return state.HandRaised switch { true => KeyImage.Amber, _ => KeyImage.Gray };
        }
    }
}
