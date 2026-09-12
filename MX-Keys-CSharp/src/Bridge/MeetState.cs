namespace Loupedeck.MxKeysGoogleMeetPlugin.Bridge
{
    using System;

    public sealed record MeetState(Boolean Connected, Boolean InCall, Boolean? MicMuted, Boolean? CameraOn, Boolean? HandRaised, Boolean? CaptionsOn)
    {
        public static readonly MeetState Idle = new(false, false, null, null, null, null);
    }
}
