namespace Odeon.Core.Helpers
{
    /// <summary>
    /// Single source of truth for subtitle styling. Single source of truth for subtitle styling in libmpv / libass.
    /// </summary>
    public static class SubtitleStyle
    {
        /// <summary>
        /// Canonical subtitle font family name. Must equal the internal family name
        /// of the embedded TTF (<c>Assets/Fonts/FuturaCyrillicMedium.ttf</c>).
        /// Verified via System.Drawing.Text.PrivateFontCollection: the GDI Win32FamilyName
        /// is "Futura Cyrillic Medium". libass matches the ASS [Styles] Fontname field AND
        /// the [Fonts] fontname: header against this exact string.
        /// </summary>
        public const string FontFamily = "Futura Cyrillic Medium";

        public const int FontSize = 50;
        public const int OutlineThickness = 1;
        public const int ShadowDepth = 0;

        /// <summary>
        /// Vertical bottom margin as a percentage of the video height.
        /// </summary>
        public const int VerticalMarginPercent = 6;

        public const string FontAssetUri = "ms-appx:///Assets/Fonts/FuturaCyrillicMedium.ttf";

        /// <summary>
        /// A ready-to-use <c>--sub-ass-force-style</c> option string that forces the same
        /// look on ASS/SSA subtitles. Built from the constants above so it can never drift.
        /// </summary>
        /// <remarks>
        /// Colors: white text, black outline, 50%-alpha back.
        /// <c>MarginV</c> is intentionally omitted here; it is applied by the file rewriter (scaled to
        /// <c>PlayResY</c>) since it is resolution-dependent.
        /// </remarks>
        public static string ForceStyleOption
        {
            get
            {
                // BorderStyle=1: standard text with shadow.
                string value = "Fontname=" + FontFamily +
                               ",Fontsize=" + FontSize +
                               ",PrimaryColour=&H00FFFFFF" +
                               ",SecondaryColour=&H00FFFFFF" +
                               ",OutlineColour=&H00000000" +
                               ",BackColour=&H80000000" +
                               // Bold=0: Medium is now the only registered font in PrivateFontRegistration.
                               ",Bold=0,Italic=0,BorderStyle=1" +
                               ",Outline=" + OutlineThickness +
                               ",Shadow=" + ShadowDepth;
                return "--sub-ass-force-style=" + value;
            }
        }
    }
}
