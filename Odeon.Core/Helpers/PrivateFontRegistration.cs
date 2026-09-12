#nullable enable

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Storage;

namespace Odeon.Core.Helpers
{
    /// <summary>
    /// Registers a bundled font file with Windows so that DirectWrite (and by extension
    /// mpv's libass text renderer) can resolve the font by family name.
    /// 
    /// Uses <c>AddFontResourceEx</c> with <c>FR_PRIVATE</c> (0x10) so the font is only
    /// visible to this process — no admin rights, no permanent installation, no cleanup needed
    /// (Windows automatically unregisters when the process exits).
    /// </summary>
    public static class PrivateFontRegistration
    {
        private const uint FR_PRIVATE = 0x10;

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int AddFontResourceExW(string lpszFilename, uint fl, IntPtr pdv);

        private static bool _registered;

        /// <summary>
        /// The resolved absolute path to the font TTF on disk, set by <see cref="EnsureRegisteredAsync"/>.
        /// Passed to mpv so it loads the font file directly.
        /// </summary>
        internal static string? FontFilePath { get; private set; }

        /// <summary>
        /// Copies the bundled font from the app package to LocalState (where file access
        /// is guaranteed even for packaged/UWP apps) and registers it process-private.
        /// Safe to call multiple times — only registers once.
        /// </summary>
        /// <returns>The font family name if registration succeeded, null otherwise.</returns>
        public static Task<string?> EnsureRegisteredAsync()
        {
            if (_registered)
                return Task.FromResult<string?>(SubtitleStyle.FontFamily);

            try
            {
                string[] fontFiles = new[]
                {
                    "FuturaCyrillicMedium.ttf"
                };

                string packagePath = Windows.ApplicationModel.Package.Current.InstalledLocation.Path;
                // LocalCacheFolder is a plain Win32 path accessible by native code (libfreetype).
                // The ms-appx package path may not be readable by unpackaged native DLLs.
                string cacheDir = Windows.Storage.ApplicationData.Current.LocalCacheFolder.Path;

                foreach (var fontFile in fontFiles)
                {
                    string sourcePath = Path.Combine(packagePath, "Assets", "Fonts", fontFile);
                    string destPath   = Path.Combine(cacheDir, fontFile);

                    if (File.Exists(sourcePath))
                    {
                        // Copy to cache dir only if not already there (idempotent)
                        if (!File.Exists(destPath))
                            File.Copy(sourcePath, destPath);

                        int result = AddFontResourceExW(destPath, FR_PRIVATE, IntPtr.Zero);
                        System.Diagnostics.Debug.WriteLine(
                            $"[PrivateFontRegistration] AddFontResourceExW(\"{destPath}\") returned {result}");

                        // Store the Win32 path so PlayerService can pass it to --freetype-font
                        if (FontFilePath == null)
                            FontFilePath = destPath;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[PrivateFontRegistration] Font file not found in package: {sourcePath}");
                    }
                }

                _registered = true;
                return Task.FromResult<string?>(SubtitleStyle.FontFamily);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[PrivateFontRegistration] Exception: {ex}");
                return Task.FromResult<string?>(null);
            }
        }
    }
}
