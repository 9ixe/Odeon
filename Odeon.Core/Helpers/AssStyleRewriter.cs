using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace Odeon.Core.Helpers
{
    public static class AssStyleRewriter
    {
        // Cached UU-encoded representation of the font bytes.
        // The 120 KB TTF encodes to ~160 KB of UU text — identical for every subtitle file.
        // Computing it once eliminates the per-file StringBuilder loop (~50–300 ms each).
        private static string? _cachedFontUuEncoded;
        private static readonly object _fontUuLock = new object();
        public static string RewriteAssStyles(string input, int videoHeight, byte[]? fontBytes = null)
        {
            // ── First pass (span-based, zero-alloc) ─────────────────────────────────────
            // Locate PlayResY in [Script Info] without allocating a line array.
            int playResY = videoHeight > 0 ? videoHeight : 1080;
            ReadOnlySpan<char> scan = input.AsSpan();
            int scanPos = 0;
            while (scanPos < scan.Length)
            {
                int eol = scan.Slice(scanPos).IndexOfAny('\r', '\n');
                ReadOnlySpan<char> sl = eol < 0 ? scan.Slice(scanPos) : scan.Slice(scanPos, eol);
                sl = sl.Trim();
                if (sl.StartsWith("PlayResY:".AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    ReadOnlySpan<char> val = sl.Slice(9).Trim();
                    if (int.TryParse(val.ToString(), out int parsedResY) && parsedResY > 0)
                        playResY = parsedResY;
                    break;
                }
                if (sl.StartsWith("[V4".AsSpan(), StringComparison.OrdinalIgnoreCase)) break;
                if (eol < 0) break;
                scanPos += eol + 1;
                if (scanPos < scan.Length && scan[scanPos] == '\n' && scan[scanPos - 1] == '\r') scanPos++;
            }

            // ── Pre-compute scaled style values once (not per Style: line) ──────────────
            double scale = (double)playResY / 1080.0;
            string svFontSize   = (SubtitleStyle.FontSize * scale).ToString(System.Globalization.CultureInfo.InvariantCulture);
            string svOutline    = (SubtitleStyle.OutlineThickness * scale).ToString(System.Globalization.CultureInfo.InvariantCulture);
            string svShadow     = (SubtitleStyle.ShadowDepth * scale).ToString(System.Globalization.CultureInfo.InvariantCulture);
            string svMarginV    = ((int)(SubtitleStyle.VerticalMarginPercent / 100.0 * playResY)).ToString(System.Globalization.CultureInfo.InvariantCulture);

            // ── Second pass: rewrite styles + strip font override tags ───────────────────
            // Use a StringBuilder line-by-line to avoid the Split→Join round-trip that
            // allocates a string[] and a second concatenated string for the whole file.
            var sb = new StringBuilder(input.Length + 64);
            Dictionary<string, int>? styleFields = null;
            bool inStylesSection = false;
            int lineStart = 0;

            while (lineStart <= input.Length)
            {
                // Find end of line
                int crIdx = input.IndexOfAny(new[] { '\r', '\n' }, lineStart);
                string line;
                int nextLineStart;
                if (crIdx < 0)
                {
                    line = input.Substring(lineStart);
                    nextLineStart = input.Length + 1; // sentinel to stop outer loop
                }
                else
                {
                    line = input.Substring(lineStart, crIdx - lineStart);
                    nextLineStart = crIdx + 1;
                    // Handle \r\n as a single separator
                    if (input[crIdx] == '\r' && nextLineStart < input.Length && input[nextLineStart] == '\n')
                        nextLineStart++;
                }

                // ── Section detection ────────────────────────────────────────────────────
                if (line.StartsWith("[", StringComparison.OrdinalIgnoreCase))
                {
                    inStylesSection = line.StartsWith("[V4+ Styles]", StringComparison.OrdinalIgnoreCase) ||
                                      line.StartsWith("[V4 Styles]", StringComparison.OrdinalIgnoreCase);
                    styleFields = null;
                    sb.Append(line).Append("\r\n");
                    lineStart = nextLineStart;
                    continue;
                }

                if (inStylesSection)
                {
                    if (line.StartsWith("Format:", StringComparison.OrdinalIgnoreCase))
                    {
                        string[] fmt = line.Substring(7).Split(',');
                        styleFields = new Dictionary<string, int>(fmt.Length, StringComparer.OrdinalIgnoreCase);
                        for (int k = 0; k < fmt.Length; k++)
                            styleFields[fmt[k].Trim()] = k;
                        sb.Append(line).Append("\r\n");
                        lineStart = nextLineStart;
                        continue;
                    }

                    if (line.StartsWith("Style:", StringComparison.OrdinalIgnoreCase) && styleFields != null)
                    {
                        string[] parts = line.Split(',');
                        if (parts.Length > 1)
                        {
                            SetStyleField(parts, styleFields, "Fontname",       SubtitleStyle.FontFamily);
                            SetStyleField(parts, styleFields, "Fontsize",       svFontSize);
                            SetStyleField(parts, styleFields, "PrimaryColour",  "&H00FFFFFF");
                            SetStyleField(parts, styleFields, "SecondaryColour","&H00FFFFFF");
                            SetStyleField(parts, styleFields, "OutlineColour",  "&H00000000");
                            SetStyleField(parts, styleFields, "BackColour",     "&H80000000");
                            SetStyleField(parts, styleFields, "BorderStyle",    "1");
                            SetStyleField(parts, styleFields, "Outline",        svOutline);
                            SetStyleField(parts, styleFields, "Shadow",         svShadow);
                            SetStyleField(parts, styleFields, "Bold",           "0");
                            SetStyleField(parts, styleFields, "Italic",         "0");
                            SetStyleField(parts, styleFields, "Underline",      "0");
                            SetStyleField(parts, styleFields, "StrikeOut",      "0");
                            SetStyleField(parts, styleFields, "ScaleX",         "100");
                            SetStyleField(parts, styleFields, "ScaleY",         "100");
                            SetStyleField(parts, styleFields, "Spacing",        "0");
                            SetStyleField(parts, styleFields, "Angle",          "0");
                            SetStyleField(parts, styleFields, "Alignment",      "2");
                            SetStyleField(parts, styleFields, "MarginV",        svMarginV);
                            sb.AppendJoin(',', parts).Append("\r\n");
                        }
                        else
                        {
                            sb.Append(line).Append("\r\n");
                        }
                        lineStart = nextLineStart;
                        continue;
                    }

                    sb.Append(line).Append("\r\n");
                    lineStart = nextLineStart;
                    continue;
                }

                // ── Strip inline font override tags from Dialogue/Comment events ──────────
                if (line.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Comment:",  StringComparison.OrdinalIgnoreCase))
                {
                    sb.Append(StripFontOverrideTags(line)).Append("\r\n");
                }
                else
                {
                    sb.Append(line).Append("\r\n");
                }

                lineStart = nextLineStart;
            }

            // Trim trailing \r\n added after the last line
            if (sb.Length >= 2 && sb[sb.Length - 1] == '\n' && sb[sb.Length - 2] == '\r')
                sb.Length -= 2;

            string output = sb.ToString();

            if (fontBytes != null && fontBytes.Length > 0)
                output = EmbedFontToAss(output, "FuturaCyrillicMedium.ttf", fontBytes);

            return output;
        }



        /// <summary>
        /// Strips font-related inline override tags from an ASS Dialogue or Comment line,
        /// preserving all non-font ASS functionality (positioning, karaoke, animations, timing, drawing).
        /// </summary>
        /// <remarks>
        /// Uses a single-pass state machine parser for performance (no regex).
        /// Anime subtitles can have 2000+ events with heavy inline tagging.
        /// </remarks>
        private static void SetStyleField(string[] parts, Dictionary<string, int> fields, string name, string value)
        {
            if (fields.TryGetValue(name, out int idx) && idx < parts.Length)
                parts[idx] = value;
        }

        internal static string StripFontOverrideTags(string line)
        {
            // Fast path: no override blocks at all
            int firstBrace = line.IndexOf('{');
            if (firstBrace < 0) return line;

            var sb = new StringBuilder(line.Length);

            int pos = 0;
            while (pos < line.Length)
            {
                if (line[pos] == '{')
                {
                    int closeIdx = line.IndexOf('}', pos + 1);
                    if (closeIdx < 0)
                    {
                        // Malformed: no closing brace, copy remainder as-is
                        sb.Append(line, pos, line.Length - pos);
                        break;
                    }

                    // Extract content between { and }
                    string blockContent = line.Substring(pos + 1, closeIdx - pos - 1);
                    string filtered = FilterOverrideBlock(blockContent);

                    if (filtered.Length > 0)
                    {
                        sb.Append('{');
                        sb.Append(filtered);
                        sb.Append('}');
                    }
                    // else: entire block was font-related, omit the empty {}

                    pos = closeIdx + 1;
                }
                else
                {
                    // Copy non-override text directly
                    int nextBrace = line.IndexOf('{', pos);
                    if (nextBrace < 0) nextBrace = line.Length;
                    sb.Append(line, pos, nextBrace - pos);
                    pos = nextBrace;
                }
            }

            return sb.ToString();
        }



        /// <summary>
        /// Filters the content of a single override block (the text between { and }).
        /// Removes font-related tags, preserves non-font tags.
        /// Handles nested \t() animation blocks with recursive font-tag stripping.
        /// </summary>
        private static string FilterOverrideBlock(string block)
        {
            var sb = new StringBuilder(block.Length);
            int i = 0;

            while (i < block.Length)
            {
                if (block[i] != '\\')
                {
                    // Non-tag character (can occur at the start of some blocks)
                    sb.Append(block[i]);
                    i++;
                    continue;
                }

                // We're at a backslash — identify the tag
                int tagStart = i;

                // Special case: \t(...) animation transform — needs recursive processing
                if (MatchAt(block, i, "\\t("))
                {
                    // Find matching closing paren, accounting for nested parens
                    int parenStart = i + 2; // position of '('
                    int depth = 1;
                    int j = parenStart + 1;
                    while (j < block.Length && depth > 0)
                    {
                        if (block[j] == '(') depth++;
                        else if (block[j] == ')') depth--;
                        j++;
                    }
                    // j is now past the closing paren
                    string tContent = block.Substring(parenStart + 1, j - parenStart - 2);

                    // \t() can have timing params before tags: \t(t1,t2,accel,tags) or \t(tags)
                    // Timing params are numeric and comma-separated before the first backslash
                    string filteredTags = FilterTransformContent(tContent);

                    if (filteredTags.Length > 0)
                    {
                        sb.Append("\\t(");
                        sb.Append(filteredTags);
                        sb.Append(')');
                    }
                    // else: all tags inside \t() were font-related, drop the entire \t()

                    i = j;
                    continue;
                }

                // Check if this tag is font-related (should be stripped)
                if (IsFontTag(block, i, out int tagEnd))
                {
                    // Skip this tag entirely
                    i = tagEnd;
                    continue;
                }

                // Non-font tag — preserve it
                // Advance to the next backslash or end of block to copy the entire tag
                int nextTag = FindNextTagStart(block, i + 1);
                sb.Append(block, tagStart, nextTag - tagStart);
                i = nextTag;
            }

            return sb.ToString();
        }



        /// <summary>
        /// Filters the content inside a \t() animation block.
        /// \t() can have timing parameters: \t(t1,t2,tags) or \t(t1,t2,accel,tags)
        /// Timing params appear before the first backslash and are always preserved.
        /// </summary>
        private static string FilterTransformContent(string content)
        {
            // Find where timing parameters end and tags begin
            int firstBackslash = content.IndexOf('\\');
            if (firstBackslash < 0)
            {
                // No tags at all, just timing params (unusual but possible)
                return content;
            }

            string timingPrefix = content.Substring(0, firstBackslash);
            string tagsSection = content.Substring(firstBackslash);

            // Filter the tags portion using the same logic
            var sb = new StringBuilder(tagsSection.Length);
            int i = 0;
            while (i < tagsSection.Length)
            {
                if (tagsSection[i] != '\\')
                {
                    sb.Append(tagsSection[i]);
                    i++;
                    continue;
                }

                if (IsFontTag(tagsSection, i, out int tagEnd))
                {
                    i = tagEnd;
                    continue;
                }

                int nextTag = FindNextTagStart(tagsSection, i + 1);
                sb.Append(tagsSection, i, nextTag - i);
                i = nextTag;
            }

            if (sb.Length == 0)
            {
                // All tags were font-related — drop the entire \t() block
                return string.Empty;
            }

            return timingPrefix + sb.ToString();
        }



        /// <summary>
        /// Determines whether the tag starting at position <paramref name="pos"/> (at a backslash)
        /// is a font-related tag that should be stripped.
        /// If so, sets <paramref name="tagEnd"/> to the position after the tag's value.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsFontTag(string s, int pos, out int tagEnd)
        {
            tagEnd = pos;

            // Must start with backslash
            if (pos >= s.Length || s[pos] != '\\') return false;

            int remaining = s.Length - pos;

            // Order tags by frequency in anime subtitles for early-match performance.

            // \fn — font name (value extends to next backslash or end)
            if (remaining >= 3 && s[pos + 1] == 'f' && s[pos + 2] == 'n')
            {
                tagEnd = FindNextTagStart(s, pos + 3);
                return true;
            }

            // \fscx, \fscy — font scale X/Y (numeric value)
            if (remaining >= 5 && s[pos + 1] == 'f' && s[pos + 2] == 's' && s[pos + 3] == 'c')
            {
                if (s[pos + 4] == 'x' || s[pos + 4] == 'y')
                {
                    tagEnd = SkipNumericValue(s, pos + 5);
                    return true;
                }
            }

            // \fsp — letter spacing (numeric value)
            if (remaining >= 4 && s[pos + 1] == 'f' && s[pos + 2] == 's' && s[pos + 3] == 'p')
            {
                tagEnd = SkipNumericValue(s, pos + 4);
                return true;
            }

            // \fs — font size (numeric value) — must check after \fsp and \fscx/\fscy
            if (remaining >= 3 && s[pos + 1] == 'f' && s[pos + 2] == 's')
            {
                // Make sure it's not \fsp, \fscx, \fscy (already handled above)
                if (remaining >= 4 && (s[pos + 3] == 'p' || s[pos + 3] == 'c'))
                {
                    // Not \fs, it's \fsp or \fsc — already handled
                    return false;
                }
                tagEnd = SkipNumericValue(s, pos + 3);
                return true;
            }

            // \fe — font encoding (numeric value)
            if (remaining >= 3 && s[pos + 1] == 'f' && s[pos + 2] == 'e')
            {
                tagEnd = SkipNumericValue(s, pos + 3);
                return true;
            }

            // \b — bold (numeric value: 0, 1, or font weight like 700)
            if (remaining >= 2 && s[pos + 1] == 'b')
            {
                // Distinguish from \be, \blur, \bord
                if (remaining >= 3 && (s[pos + 2] == 'e' || s[pos + 2] == 'l' || s[pos + 2] == 'o'))
                {
                    // Could be \be, \blur, \bord — handle separately below
                }
                else
                {
                    tagEnd = SkipNumericValue(s, pos + 2);
                    return true;
                }
            }

            // \i — italic (0 or 1)
            if (remaining >= 2 && s[pos + 1] == 'i')
            {
                // Distinguish from \iclip
                if (remaining >= 6 && s[pos + 2] == 'c' && s[pos + 3] == 'l' && s[pos + 4] == 'i' && s[pos + 5] == 'p')
                {
                    // \iclip — NOT a font tag
                    return false;
                }
                tagEnd = SkipNumericValue(s, pos + 2);
                return true;
            }

            // \u — underline (0 or 1)
            if (remaining >= 2 && s[pos + 1] == 'u')
            {
                tagEnd = SkipNumericValue(s, pos + 2);
                return true;
            }

            // \s — strikeout (0 or 1)
            if (remaining >= 2 && s[pos + 1] == 's')
            {
                // Distinguish from \shad
                if (remaining >= 5 && s[pos + 2] == 'h' && s[pos + 3] == 'a' && s[pos + 4] == 'd')
                {
                    // \shad — handle below
                }
                else
                {
                    tagEnd = SkipNumericValue(s, pos + 2);
                    return true;
                }
            }

            // \q — wrap style (0-3)
            if (remaining >= 2 && s[pos + 1] == 'q')
            {
                tagEnd = SkipNumericValue(s, pos + 2);
                return true;
            }

            // \bord — border/outline thickness
            if (remaining >= 5 && s[pos + 1] == 'b' && s[pos + 2] == 'o' && s[pos + 3] == 'r' && s[pos + 4] == 'd')
            {
                tagEnd = SkipNumericValue(s, pos + 5);
                return true;
            }

            // \shad — shadow distance
            if (remaining >= 5 && s[pos + 1] == 's' && s[pos + 2] == 'h' && s[pos + 3] == 'a' && s[pos + 4] == 'd')
            {
                tagEnd = SkipNumericValue(s, pos + 5);
                return true;
            }

            // \be — blur edges
            if (remaining >= 3 && s[pos + 1] == 'b' && s[pos + 2] == 'e')
            {
                tagEnd = SkipNumericValue(s, pos + 3);
                return true;
            }

            // \blur — gaussian blur
            if (remaining >= 5 && s[pos + 1] == 'b' && s[pos + 2] == 'l' && s[pos + 3] == 'u' && s[pos + 4] == 'r')
            {
                tagEnd = SkipNumericValue(s, pos + 5);
                return true;
            }

            // Color tags: \c, \1c, \2c, \3c, \4c
            if (remaining >= 2 && s[pos + 1] == 'c')
            {
                // \c — shorthand primary color (value is &H...& or just hex)
                tagEnd = SkipColorValue(s, pos + 2);
                return true;
            }
            if (remaining >= 3 && s[pos + 1] >= '1' && s[pos + 1] <= '4' && s[pos + 2] == 'c')
            {
                // \1c, \2c, \3c, \4c
                tagEnd = SkipColorValue(s, pos + 3);
                return true;
            }

            // Alpha tags: \alpha, \1a, \2a, \3a, \4a
            if (remaining >= 6 && s[pos + 1] == 'a' && s[pos + 2] == 'l' && s[pos + 3] == 'p' && s[pos + 4] == 'h' && s[pos + 5] == 'a')
            {
                tagEnd = SkipColorValue(s, pos + 6);
                return true;
            }
            if (remaining >= 3 && s[pos + 1] >= '1' && s[pos + 1] <= '4' && s[pos + 2] == 'a')
            {
                // \1a, \2a, \3a, \4a
                tagEnd = SkipColorValue(s, pos + 3);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Finds the start of the next tag (next backslash) or end of string,
        /// used to determine the extent of the current tag's value.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FindNextTagStart(string s, int from)
        {
            for (int i = from; i < s.Length; i++)
            {
                if (s[i] == '\\') return i;
            }
            return s.Length;
        }

        /// <summary>
        /// Skips past a numeric value (optional sign, digits, optional decimal point and digits).
        /// Returns the position after the numeric value.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int SkipNumericValue(string s, int from)
        {
            int i = from;
            // Optional leading sign
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
            // Digits
            while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
            // Optional decimal
            if (i < s.Length && s[i] == '.')
            {
                i++;
                while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
            }
            return i;
        }

        /// <summary>
        /// Skips past an ASS color/alpha value in &amp;H...&amp; format or plain hex.
        /// Handles both &amp;H0000FF&amp; and bare hex values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int SkipColorValue(string s, int from)
        {
            int i = from;
            if (i < s.Length && s[i] == '&') i++;
            if (i < s.Length && (s[i] == 'H' || s[i] == 'h')) i++;
            // Hex digits
            while (i < s.Length && IsHexDigit(s[i])) i++;
            // Trailing &
            if (i < s.Length && s[i] == '&') i++;
            return i;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsHexDigit(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');
        }

        /// <summary>
        /// Checks if string <paramref name="s"/> matches <paramref name="pattern"/>
        /// starting at position <paramref name="pos"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool MatchAt(string s, int pos, string pattern)
        {
            if (pos + pattern.Length > s.Length) return false;
            for (int i = 0; i < pattern.Length; i++)
            {
                if (s[pos + i] != pattern[i]) return false;
            }
            return true;
        }

        private static string DecodeAssText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            text = text.Replace("\\n", "\n").Replace("\\N", "\n").Replace("\\h", " ");
            return text;
        }

        /// <summary>
        /// Embeds a TTF font directly into the ASS file content using standard ASS uuencoding.
        /// This ensures libass will always use the font, even on UWP where DirectWrite system
        /// font restrictions apply.
        /// </summary>
        public static string EmbedFontToAss(string assContent, string fontFileName, byte[] fontBytes)
        {
            if (string.IsNullOrWhiteSpace(assContent) || fontBytes == null || fontBytes.Length == 0)
                return assContent;

            // Build (or reuse) the UU-encoded font block. The 120 KB TTF always encodes to the
            // same ~160 KB string — computing it once per process saves 50–300 ms per subtitle file.
            string fontUuBlock;
            if (_cachedFontUuEncoded != null)
            {
                fontUuBlock = _cachedFontUuEncoded;
            }
            else
            {
                lock (_fontUuLock)
                {
                    if (_cachedFontUuEncoded == null)
                    {
                        var enc = new StringBuilder((fontBytes.Length * 4 / 3) + (fontBytes.Length / 60) + 100);
                        enc.AppendLine("[Fonts]");
                        enc.AppendLine("fontname: Futura Cyrillic Medium");
                        enc.AppendLine($"fontname: {fontFileName}");

                        int lineLen = 0;
                        for (int i = 0; i < fontBytes.Length; i += 3)
                        {
                            int b1 = fontBytes[i];
                            int b2 = (i + 1 < fontBytes.Length) ? fontBytes[i + 1] : 0;
                            int b3 = (i + 2 < fontBytes.Length) ? fontBytes[i + 2] : 0;

                            int o1 = b1 >> 2;
                            int o2 = ((b1 & 3) << 4) | (b2 >> 4);
                            int o3 = ((b2 & 15) << 2) | (b3 >> 6);
                            int o4 = b3 & 63;

                            enc.Append((char)(o1 + 33));
                            enc.Append((char)(o2 + 33));
                            if (i + 1 < fontBytes.Length) enc.Append((char)(o3 + 33));
                            if (i + 2 < fontBytes.Length) enc.Append((char)(o4 + 33));

                            lineLen += 4;
                            if (lineLen >= 80)
                            {
                                enc.AppendLine();
                                lineLen = 0;
                            }
                        }
                        if (lineLen > 0) enc.AppendLine();

                        _cachedFontUuEncoded = enc.ToString();
                    }
                    fontUuBlock = _cachedFontUuEncoded;
                }
            }

            var sb = new StringBuilder(assContent.Length + fontUuBlock.Length + 4);
            sb.Append(assContent);
            if (!assContent.EndsWith("\n")) sb.AppendLine();
            sb.AppendLine();
            sb.Append(fontUuBlock);

            return sb.ToString();
        }

        public static AssTrack? ParseAssToTrack(string assContent)
        {
            if (string.IsNullOrWhiteSpace(assContent)) return null;

            var lines = assContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var track = new AssTrack();
            bool inEvents = false;
            ulong readOrder = 0;

            foreach (var line in lines)
            {
                if (line.StartsWith("[Events]", StringComparison.OrdinalIgnoreCase))
                {
                    inEvents = true;
                    continue;
                }

                if (inEvents && line.StartsWith("["))
                {
                    break;
                }

                if (inEvents && line.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase))
                {
                    int colonIdx = line.IndexOf(':');
                    if (colonIdx < 0) continue;

                    var payloadPart = line.Substring(colonIdx + 1).TrimStart();
                    var parts = payloadPart.Split(new[] { ',' }, 10);
                    if (parts.Length < 10) continue;

                    if (TryParseAssTime(parts[1].Trim(), out long startMs) && 
                        TryParseAssTime(parts[2].Trim(), out long endMs))
                    {
                        // Payload in MKV has format: ReadOrder,Layer,Style,Name,MarginL,MarginR,MarginV,Effect,Text
                        // We construct it to match what MkvSubtitleExtractor.ExtractDialogueText expects.
                        string mkvPayload = $"{readOrder},{parts[0]},{parts[3]},{parts[4]},{parts[5]},{parts[6]},{parts[7]},{parts[8]},{parts[9]}";

                        track.Events.Add(new AssEvent
                        {
                            TimecodeMs = startMs,
                            DurationMs = endMs - startMs,
                            Payload = mkvPayload
                        });
                        readOrder++;
                    }
                }
            }

            return track.Events.Count > 0 ? track : null;
        }

        private static bool TryParseAssTime(string timeStr, out long timeMs)
        {
            timeMs = 0;
            var parts = timeStr.Split(new[] { ':', '.' }, StringSplitOptions.None);
            if (parts.Length >= 4)
            {
                if (int.TryParse(parts[0], out int h) &&
                    int.TryParse(parts[1], out int m) &&
                    int.TryParse(parts[2], out int s) &&
                    int.TryParse(parts[3], out int cs))
                {
                    timeMs = h * 3600000L + m * 60000L + s * 1000L + cs * 10L;
                    return true;
                }
            }
            return false;
        }

        public static string StripAssToPlainText(string assContent)
        {
            if (string.IsNullOrWhiteSpace(assContent)) return string.Empty;

            var sb = new StringBuilder(assContent.Length / 2);
            bool inEvents = false;
            int srtIndex = 1;

            int formatStart = 1, formatEnd = 2, formatText = 9;

            ReadOnlySpan<char> span = assContent.AsSpan();
            int lineStart = 0;

            while (lineStart < span.Length)
            {
                int lineEnd = span.Slice(lineStart).IndexOfAny('\r', '\n');
                int nextLineStart;

                ReadOnlySpan<char> line;
                if (lineEnd < 0)
                {
                    line = span.Slice(lineStart).Trim();
                    nextLineStart = span.Length;
                }
                else
                {
                    line = span.Slice(lineStart, lineEnd).Trim();
                    nextLineStart = lineStart + lineEnd + 1;
                    if (nextLineStart < span.Length && (span[lineStart + lineEnd] == '\r' || span[lineStart + lineEnd] == '\n'))
                    {
                        if (span[lineStart + lineEnd] == '\r' && nextLineStart < span.Length && span[nextLineStart] == '\n')
                            nextLineStart++;
                    }
                }

                lineStart = nextLineStart;

                if (line.IsEmpty) continue;

                if (line.Equals("[Events]".AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    inEvents = true;
                    continue;
                }

                if (inEvents && line.StartsWith("[".AsSpan()))
                {
                    break;
                }

                if (inEvents && line.StartsWith("Format:".AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    ReadOnlySpan<char> formatPart = line.Slice(7).Trim();
                    int fieldIdx = 0;
                    int pStart = 0;
                    for (int i = 0; i <= formatPart.Length; i++)
                    {
                        if (i == formatPart.Length || formatPart[i] == ',')
                        {
                            ReadOnlySpan<char> field = formatPart.Slice(pStart, i - pStart).Trim();
                            if (field.Equals("start".AsSpan(), StringComparison.OrdinalIgnoreCase)) formatStart = fieldIdx;
                            else if (field.Equals("end".AsSpan(), StringComparison.OrdinalIgnoreCase)) formatEnd = fieldIdx;
                            else if (field.Equals("text".AsSpan(), StringComparison.OrdinalIgnoreCase)) formatText = fieldIdx;

                            fieldIdx++;
                            pStart = i + 1;
                        }
                    }
                    continue;
                }

                if (inEvents && (line.StartsWith("Dialogue:".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
                                 line.StartsWith("Comment:".AsSpan(), StringComparison.OrdinalIgnoreCase)))
                {
                    int colonIdx = line.IndexOf(':');
                    if (colonIdx < 0) continue;

                    ReadOnlySpan<char> payloadPart = line.Slice(colonIdx + 1).TrimStart();
                    
                    int currentPartIdx = 0;
                    int partStart = 0;
                    ReadOnlySpan<char> startTimeSpan = default;
                    ReadOnlySpan<char> endTimeSpan = default;
                    ReadOnlySpan<char> textSpan = default;

                    for (int i = 0; i < payloadPart.Length; i++)
                    {
                        if (currentPartIdx < formatText)
                        {
                            if (payloadPart[i] == ',')
                            {
                                ReadOnlySpan<char> part = payloadPart.Slice(partStart, i - partStart).Trim();
                                if (currentPartIdx == formatStart) startTimeSpan = part;
                                else if (currentPartIdx == formatEnd) endTimeSpan = part;

                                currentPartIdx++;
                                partStart = i + 1;
                            }
                        }
                        else
                        {
                            textSpan = payloadPart.Slice(partStart).Trim();
                            break;
                        }
                    }

                    if (textSpan.IsEmpty && currentPartIdx < formatText) continue;

                    string startTime = startTimeSpan.IsEmpty ? "0:00:00.00" : startTimeSpan.ToString();
                    string endTime = endTimeSpan.IsEmpty ? "0:00:00.00" : endTimeSpan.ToString();
                    string text = textSpan.ToString();

                    string startSrt = FormatSrtTime(startTime);
                    string endSrt = FormatSrtTime(endTime);
                    string cleanText = MkvSubtitleExtractor.ProcessSubtitleText(text);

                    if (string.IsNullOrWhiteSpace(cleanText)) continue;

                    sb.Append(srtIndex).Append("\r\n")
                      .Append(startSrt).Append(" --> ").Append(endSrt).Append("\r\n")
                      .Append(cleanText).Append("\r\n\r\n");

                    srtIndex++;
                }
            }

            return sb.ToString();
        }

        private static string FormatSrtTime(string assTime)
        {
            // Inline parse "H:MM:SS.CC" (ASS) → "HH:MM:SS,MMM" (SRT) without Split/alloc.
            // ASS uses centiseconds (2 digits); SRT uses milliseconds (3 digits).
            int h = 0, m = 0, s = 0, sub = 0, subDigits = 0;
            int i = 0, len = assTime.Length;
            while (i < len && assTime[i] != ':') { h = h * 10 + (assTime[i] - '0'); i++; }
            i++; // skip ':'
            while (i < len && assTime[i] != ':') { m = m * 10 + (assTime[i] - '0'); i++; }
            i++; // skip ':'
            while (i < len && assTime[i] != '.' && assTime[i] != ',') { s = s * 10 + (assTime[i] - '0'); i++; }
            i++; // skip '.' or ','
            while (i < len) { sub = sub * 10 + (assTime[i] - '0'); subDigits++; i++; }
            int ms = subDigits == 3 ? sub : sub * 10; // centiseconds → milliseconds
            return $"{h:D2}:{m:D2}:{s:D2},{ms:D3}";
        }


    }
}