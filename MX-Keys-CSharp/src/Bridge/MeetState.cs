namespace Loupedeck.MxKeysGoogleMeetPlugin.Bridge
{
    using System;

    /// <summary>
    /// Sharing is a plain Boolean, not Boolean?, unlike the other toggles: those are read from an
    /// on/off-labelled button whose absence genuinely means "unknown", whereas sharing is derived
    /// from whether a "Stop presenting" control exists at all — absent means not sharing, which is
    /// a definite answer rather than a missing one.
    /// </summary>
    public sealed record MeetState(Boolean Connected, Boolean InCall, Boolean? MicMuted, Boolean? CameraOn, Boolean? HandRaised, Boolean? CaptionsOn, Boolean Sharing)
    {
        public static readonly MeetState Idle = new(false, false, null, null, null, null, false);
    }
}
