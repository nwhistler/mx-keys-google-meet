namespace Loupedeck.MxKeysGoogleMeetPlugin
{
    using System;

    using Loupedeck.MxKeysGoogleMeetPlugin.Bridge;

    /// <summary>
    /// MX Keys - Google Meet — Logitech MX Keypad plugin.
    ///
    /// Mute/unmute, camera, raise hand, and leave-call, with live per-key icon state. The SDK has
    /// no visibility into browser tabs, so a companion Chrome/Arc/Dia/Firefox extension (see
    /// /Google Meet) reports live Meet state over a loopback WebSocket (MeetBridge) and executes
    /// the actual button clicks in the Meet tab.
    ///
    /// Commands are AUTO-DISCOVERED by the SDK — every PluginDynamicCommand subclass with a
    /// parameterless constructor is registered automatically. Load() only has to start the bridge.
    /// </summary>
    public class MxKeysGoogleMeetPlugin : Plugin
    {
        public override Boolean UsesApplicationApiOnly => true;

        public override Boolean HasNoApplication => true;

        public MxKeysGoogleMeetPlugin()
        {
            PluginLog.Init(this.Log);
            PluginResources.Init(this.Assembly);
        }

        public override void Load()
        {
            MeetBridge.Instance.Start();
            PluginLog.Info("MxKeysGoogleMeetPlugin: Loaded - actions auto-discovered; bridge listening");
        }

        public override void Unload()
        {
            MeetBridge.Instance.Stop();
            PluginLog.Info("MxKeysGoogleMeetPlugin: Unloaded");
        }
    }
}
