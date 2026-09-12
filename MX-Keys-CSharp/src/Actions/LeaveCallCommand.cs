namespace Loupedeck.MxKeysGoogleMeetPlugin.Actions
{
    using System;

    using Loupedeck.MxKeysGoogleMeetPlugin.Bridge;

    public class LeaveCallCommand : PluginDynamicCommand
    {
        public LeaveCallCommand()
            : base(displayName: "Leave Call",
                   description: "Leaves the active Google Meet call",
                   groupName: "Google Meet")
        {
            this.SetWidget(true);
        }

        protected override void RunCommand(String actionParameter) => MeetBridge.Instance.Send(MeetBridge.Commands.LeaveCall);

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) => "​";

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize) =>
            KeyImage.Render(imageSize, "leave_call", KeyImage.Red);
    }
}
