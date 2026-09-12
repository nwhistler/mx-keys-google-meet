namespace Loupedeck.MxKeysGoogleMeetPlugin.Actions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Loupedeck.MxKeysGoogleMeetPlugin.Bridge;

    /// <summary>
    /// Pressing this key expands the keypad into Meet's own reaction set as its own row of keys;
    /// pressing one sends that reaction. `actionParameter` IS the emoji character itself —
    /// CreateCommandName/GetButtonPressActionNames round-trips it through the SDK's naming scheme,
    /// and RunCommand gets the same raw character back (confirmed via logging: the correct emoji
    /// arrives at GetCommandDisplayName every time).
    ///
    /// Meet's own reaction picker auto-closes itself right after each pick — fine for a human
    /// clicking it once, but on the keypad it read as broken (confirmed live, 2026-09: watching it
    /// snap shut after every single press, even though the reaction was actually sent every time).
    /// content.js's sendReaction() now reopens the picker after each send so it stays visibly open
    /// across repeated presses; Deactivate() below closes it back up once the user actually backs
    /// out of this folder on the device, via a new close-reactions command.
    ///
    /// PluginDynamicFolder has no SetWidget equivalent, so these sub-keys stay in Options+'s default
    /// small-boxed-with-caption layout. Originally the caption itself was the raw emoji character,
    /// betting on Options+'s native text renderer supporting color emoji glyphs — confirmed live
    /// (2026-09) that it does NOT: every sub-key rendered as a bare "0" despite the correct emoji
    /// value arriving at GetCommandDisplayName. Fixed by rendering each reaction as a pre-baked
    /// bitmap (src/Resources/icons/reactions/*.png, generated from Apple Color Emoji) via the same
    /// KeyImage.Render path every other action uses, with the caption blanked via the zero-width-
    /// space trick.
    /// </summary>
    public class EmojiReactionsDynamicFolder : PluginDynamicFolder
    {
        // Meet's own reaction picker, confirmed live against the real UI. Icon names have no
        // "reactions/" prefix even though the source PNGs live in icons/reactions/ — the csproj's
        // EmbeddedResource Link flattens every icon to Resources/icons/%(Filename)%(Extension)
        // regardless of source subfolder, so the embedded logical name is just "icons.heart.png".
        private static readonly (String Emoji, String Icon)[] Reactions =
        {
            ("💖", "heart"),
            ("👍", "thumbsup"),
            ("🎉", "party"),
            ("👏", "clap"),
            ("😂", "laugh"),
            ("😮", "surprised"),
            ("😢", "sad"),
            ("🤔", "thinking"),
            ("👎", "thumbsdown"),
        };

        public EmojiReactionsDynamicFolder()
        {
            this.DisplayName = "Reactions";
            this.GroupName = "Google Meet";
            this.Description = "Expands into Google Meet's reaction emoji so you can send one directly from the keypad";
        }

        public override BitmapImage GetButtonImage(PluginImageSize imageSize) =>
            KeyImage.Render(imageSize, "smiley", KeyImage.Gray);

        public override IEnumerable<String> GetButtonPressActionNames(DeviceType deviceType) =>
            Reactions.Select(r => this.CreateCommandName(r.Emoji));

        public override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) => "​";

        public override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var icon = Reactions.FirstOrDefault(r => r.Emoji == actionParameter).Icon ?? "smiley";
            // No SetWidget equivalent on PluginDynamicFolder means these sub-keys can't go true
            // full-bleed like the top-level toggles — Options+ still insets them into its own
            // small icon frame. Pushing the fill ratio well past the 0.72 the full-bleed buttons
            // use is the only lever available: it makes the glyph fill as much of that fixed inset
            // frame as possible, short of actual edge-to-edge rendering.
            return KeyImage.Render(imageSize, icon, KeyImage.LightGray, fillRatio: 0.95);
        }

        public override void RunCommand(String actionParameter)
        {
            MeetBridge.Instance.Send(MeetBridge.Commands.SendReaction, actionParameter);
        }

        /// <summary>Fires when the user backs out of this folder on the device (confirmed via
        /// reflection against PluginApi.dll — this is the folder-level counterpart to
        /// PluginDynamicCommand's OnUnload). Closes Meet's reaction picker, since sendReaction()
        /// deliberately leaves it open across repeated presses instead of letting it auto-close.</summary>
        public override Boolean Deactivate()
        {
            MeetBridge.Instance.Send(MeetBridge.Commands.CloseReactions);
            return base.Deactivate();
        }
    }
}
