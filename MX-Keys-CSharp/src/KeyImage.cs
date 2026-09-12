namespace Loupedeck.MxKeysGoogleMeetPlugin
{
    using System;

    /// <summary>
    /// Draws each key's live face: a solid state colour filling the ENTIRE physical key, with a
    /// centred icon and no text. Two things had to be true for this to actually happen — see each
    /// action's constructor/GetCommandDisplayName for the other half:
    ///   1. `new BitmapBuilder(imageSize)` gives an inset canvas sized for Options+'s default
    ///      80x80-in-90x90 layout, which reserves the surrounding strip for its own text label —
    ///      that's the small boxed-in look with a caption underneath. Building against the full
    ///      button dimensions instead (ButtonCanvas) fills the whole key ourselves.
    ///   2. Each action must call `this.SetWidget(true)` — without it, Options+ still treats the
    ///      image as the small inset icon regardless of canvas size used to build it.
    /// </summary>
    internal static class KeyImage
    {
        public static readonly BitmapColor Green = new(0x4F, 0xA9, 0x75);   // live / on
        public static readonly BitmapColor Red = new(0xDA, 0x3D, 0x29);    // muted / off / leave call
        public static readonly BitmapColor Amber = new(0xE2, 0x9D, 0x37);  // hand raised
        public static readonly BitmapColor Gray = new(0x5A, 0x5A, 0x60);   // neutral / not connected
        // Noticeably lighter than Gray on purpose: folder sub-keys (no SetWidget) get shrunk into
        // Options+'s own small inset frame surrounded by its black chrome, so Gray's darker tone
        // read as barely-distinguishable from that surrounding black in practice (confirmed live,
        // 2026-09). This one is deliberately light enough to read as "gray" against that black.
        public static readonly BitmapColor LightGray = new(0x8C, 0x8C, 0x94);
        public static readonly BitmapColor Blue = new(0x3E, 0x7C, 0xB1);   // screen share (no live on/off state to reflect)

        public static BitmapImage Render(PluginImageSize imageSize, String icon, BitmapColor background, Double fillRatio = 0.72)
        {
            using var bitmap = ButtonCanvas(imageSize);
            bitmap.Clear(background);

            try
            {
                var image = PluginResources.ReadImage("icons." + icon + ".png");
                var w = bitmap.Width;
                var h = bitmap.Height;
                var size = (Int32)(Math.Min(w, h) * fillRatio);
                bitmap.DrawImage(image, (w - size) / 2, (h - size) / 2, size, size);
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, $"KeyImage: icon '{icon}' failed to load");
            }

            return bitmap.ToImage();
        }

        private static BitmapBuilder ButtonCanvas(PluginImageSize imageSize)
        {
            var width = imageSize.GetButtonWidth();
            var height = imageSize.GetButtonHeight();
            return width > 0 && height > 0
                ? new BitmapBuilder(width, height)
                : new BitmapBuilder(imageSize);
        }
    }
}
