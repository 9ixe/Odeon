using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace Odeon.Core.Helpers
{
    /// <summary>
    /// Lightweight value-type subtitle event. Stored inline in List&lt;AssEvent&gt;
    /// to eliminate per-event heap allocations (saves ~40 bytes GC overhead × 2000+ events).
    /// </summary>
    public struct AssEvent
    {
        public long TimecodeMs;
        public long DurationMs;
        public string Payload;
    }

    public class AssTrack
    {
        public ulong TrackNumber { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Language { get; set; } = "eng";
        public string CodecPrivate { get; set; } = string.Empty;
        public List<AssEvent> Events { get; set; } = new();
        public bool IsSrt { get; set; }
    }

    public static class MkvSubtitleExtractor
    {
        // Yield back to the thread pool every N clusters to avoid starving the UI thread
        private const int ClustersBeforeYield = 50;

        // Precomputed VINT length table: VintLengthTable[byte] → number of bytes in the VINT.
        // Single array lookup replaces 8 conditional branches per EBML element.
        private static readonly byte[] VintLengthTable = BuildVintLengthTable();

        private static byte[] BuildVintLengthTable()
        {
            var table = new byte[256];
            for (int i = 0; i < 256; i++)
            {
                if ((i & 0x80) != 0) table[i] = 1;
                else if ((i & 0x40) != 0) table[i] = 2;
                else if ((i & 0x20) != 0) table[i] = 3;
                else if ((i & 0x10) != 0) table[i] = 4;
                else if ((i & 0x08) != 0) table[i] = 5;
                else if ((i & 0x04) != 0) table[i] = 6;
                else if ((i & 0x02) != 0) table[i] = 7;
                else if ((i & 0x01) != 0) table[i] = 8;
                // else table[i] = 0 (already default)
            }
            return table;
        }

        private sealed class FastEbmlReader : IDisposable
        {
            private readonly Stream _stream;
            private readonly byte[] _buffer;
            private int _bufferPos;
            private int _bufferLen;
            private long _globalPosition;

            public FastEbmlReader(Stream stream, int bufferSize = 4 * 1024 * 1024)
            {
                _stream = stream;
                _buffer = new byte[bufferSize];
            }

            public long Position
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => _globalPosition;
            }

            public long Length
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => _stream.Length;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int ReadByte()
            {
                if (_bufferPos >= _bufferLen)
                {
                    FillBuffer();
                    if (_bufferLen == 0) return -1;
                }
                _globalPosition++;
                return _buffer[_bufferPos++];
            }

            public void ReadExactly(byte[] buffer, int offset, int count)
            {
                int totalRead = 0;
                while (totalRead < count)
                {
                    if (_bufferPos >= _bufferLen)
                    {
                        FillBuffer();
                        if (_bufferLen == 0) throw new EndOfStreamException();
                    }
                    int toCopy = Math.Min(count - totalRead, _bufferLen - _bufferPos);
                    Buffer.BlockCopy(_buffer, _bufferPos, buffer, offset + totalRead, toCopy);
                    _bufferPos += toCopy;
                    _globalPosition += toCopy;
                    totalRead += toCopy;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Skip(long count)
            {
                if (count <= 0) return;
                long remainingInBuffer = _bufferLen - _bufferPos;
                if (count <= remainingInBuffer)
                {
                    _bufferPos += (int)count;
                    _globalPosition += count;
                    return;
                }

                SkipSlow(count, remainingInBuffer);
            }

            private void SkipSlow(long count, long remainingInBuffer)
            {
                count -= remainingInBuffer;
                _globalPosition += remainingInBuffer + count;

                if (count > _buffer.Length * 2L)
                {
                    _stream.Position = _globalPosition;
                    _bufferPos = 0;
                    _bufferLen = 0;
                }
                else
                {
                    while (count > 0)
                    {
                        FillBuffer();
                        if (_bufferLen == 0) break;
                        long skipNow = Math.Min(count, _bufferLen);
                        _bufferPos += (int)skipNow;
                        count -= skipNow;
                    }
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void FillBuffer()
            {
                _bufferPos = 0;
                _bufferLen = _stream.Read(_buffer, 0, _buffer.Length);
            }

            public void Dispose()
            {
                _stream.Dispose();
            }
        }

        public static async Task<List<AssTrack>> ExtractAssTracksAsync(StorageFile file, CancellationToken ct = default)
        {
            var tracks = new List<AssTrack>();
            try
            {
                using var fs = await file.OpenStreamForReadAsync();
                long fileLength = fs.Length;
                using var reader = new FastEbmlReader(fs);

                ulong headerId = ReadEbmlId(reader);
                if (headerId != 0x1A45DFA3) return tracks;
                ReadEbmlSize(reader); // skip EBML header size

                Dictionary<ulong, AssTrack> trackMap = new();
                ulong timecodeScale = 1000000;
                long currentClusterTimecode = 0;
                bool seenCluster = false;
                int clusterCount = 0;

                // Heuristic: estimate events per track for List pre-allocation
                int estimatedEvents = Math.Min((int)(fileLength / 4000), 8192);

                while (reader.Position < reader.Length && !ct.IsCancellationRequested)
                {
                    ulong id = ReadEbmlId(reader);
                    if (id == 0) break;
                    ulong size = ReadEbmlSize(reader);

                    if (id == 0x18538067) { } // Segment — descend
                    else if (id == 0x114D9B74) // SeekHead — skip
                    {
                        if (size != ulong.MaxValue) reader.Skip((long)size);
                    }
                    else if (id == 0x1549A966) // Info
                    {
                        long endPos = reader.Position + (long)size;
                        while (reader.Position < endPos && !ct.IsCancellationRequested)
                        {
                            ulong subId = ReadEbmlId(reader);
                            ulong subSize = ReadEbmlSize(reader);
                            if (subId == 0x2AD7B1) timecodeScale = ReadUint(reader, subSize);
                            else reader.Skip((long)subSize);
                        }
                    }
                    else if (id == 0x1654AE6B) // Tracks
                    {
                        long endPos = reader.Position + (long)size;
                        while (reader.Position < endPos && !ct.IsCancellationRequested)
                        {
                            ulong subId = ReadEbmlId(reader);
                            ulong subSize = ReadEbmlSize(reader);
                            if (subId == 0xAE) // TrackEntry
                            {
                                var track = ParseTrackEntry(reader, subSize);
                                if (track != null)
                                {
                                    // Pre-allocate event list capacity
                                    track.Events = new List<AssEvent>(estimatedEvents);
                                    tracks.Add(track);
                                    trackMap[track.TrackNumber] = track;
                                }
                            }
                            else reader.Skip((long)subSize);
                        }

                        // If no subtitle tracks found, stop immediately — no point scanning clusters
                        if (trackMap.Count == 0) return tracks;
                    }
                    else if (id == 0x1F43B675) // Cluster
                    {
                        seenCluster = true;
                        clusterCount++;

                        // Cooperatively yield every N clusters to avoid starving
                        // the thread pool (video decoder, UI) on large files
                        if (clusterCount % ClustersBeforeYield == 0)
                        {
                            await Task.Yield();
                            ct.ThrowIfCancellationRequested();
                        }

                        long endPos = size != ulong.MaxValue ? reader.Position + (long)size : reader.Length;
                        while (reader.Position < endPos && !ct.IsCancellationRequested)
                        {
                            ulong subId = ReadEbmlId(reader);
                            if (subId == 0) break;
                            ulong subSize = ReadEbmlSize(reader);

                            if (subId == 0xE7) // Cluster Timecode
                            {
                                currentClusterTimecode = (long)ReadUint(reader, subSize);
                            }
                            else if (subId == 0xA0) // BlockGroup
                            {
                                long bgEnd = reader.Position + (long)subSize;
                                AssTrack? matchedTrack = null;
                                long blockTimecode = 0;
                                string? payload = null;
                                long blockDuration = 0;

                                while (reader.Position < bgEnd)
                                {
                                    ulong bgId = ReadEbmlId(reader);
                                    ulong bgSize = ReadEbmlSize(reader);
                                    if (bgId == 0xA1) // Block
                                    {
                                        ParseBlockFast(reader, bgSize, trackMap, ref matchedTrack, ref blockTimecode, ref payload);
                                    }
                                    else if (bgId == 0x9B) // BlockDuration
                                    {
                                        blockDuration = (long)ReadUint(reader, bgSize);
                                    }
                                    else reader.Skip((long)bgSize);
                                }

                                if (matchedTrack != null)
                                {
                                    long absoluteTimecodeMs = (currentClusterTimecode + blockTimecode) * (long)timecodeScale / 1000000;
                                    long durMs = blockDuration * (long)timecodeScale / 1000000;
                                    matchedTrack.Events.Add(new AssEvent { TimecodeMs = absoluteTimecodeMs, DurationMs = durMs, Payload = payload ?? string.Empty });
                                }
                            }
                            else if (subId == 0xA3) // SimpleBlock
                            {
                                AssTrack? matchedTrack = null;
                                long blockTimecode = 0;
                                string? payload = null;
                                ParseBlockFast(reader, subSize, trackMap, ref matchedTrack, ref blockTimecode, ref payload);
                                if (matchedTrack != null)
                                {
                                    long absoluteTimecodeMs = (currentClusterTimecode + blockTimecode) * (long)timecodeScale / 1000000;
                                    matchedTrack.Events.Add(new AssEvent { TimecodeMs = absoluteTimecodeMs, DurationMs = 5000, Payload = payload ?? string.Empty });
                                }
                            }
                            else reader.Skip((long)subSize);
                        }
                    }
                    else
                    {
                        // Early exit: after clusters, remaining elements (Cues/Tags/Attachments)
                        // contain no subtitle data
                        if (seenCluster && size != ulong.MaxValue)
                        {
                            break;
                        }

                        if (size != ulong.MaxValue) reader.Skip((long)size);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected — playback stopped during extraction
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"MkvSubtitleExtractor error: {ex.Message}");
#endif
            }
            return tracks;
        }

        /// <summary>
        /// Optimized block parser. Reads only the track number first; if the track
        /// is not a subtitle track, immediately skips the entire remaining block
        /// without reading timecode, flags, or payload data.
        /// This avoids reading 6+ unnecessary bytes for every video/audio block.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ParseBlockFast(FastEbmlReader reader, ulong size, Dictionary<ulong, AssTrack> trackMap,
            ref AssTrack? track, ref long timecode, ref string? payload)
        {
            if (size < 4)
            {
                reader.Skip((long)size);
                return;
            }

            int firstByte = reader.ReadByte();
            if (firstByte == -1) return;
            int vintLen = VintLengthTable[firstByte];
            if (vintLen == 0) return;

            ulong trackNum = (ulong)(firstByte & (0xFF >> vintLen));
            for (int i = 1; i < vintLen; i++)
            {
                trackNum = (trackNum << 8) | (byte)reader.ReadByte();
            }

            // FAST PATH: If this block is not a subtitle track, skip everything
            if (!trackMap.TryGetValue(trackNum, out var matchedTrack))
            {
                reader.Skip((long)size - vintLen);
                return;
            }

            int t1 = reader.ReadByte();
            int t2 = reader.ReadByte();
            timecode = (short)((t1 << 8) | t2);

            reader.ReadByte(); // flags
            int headerBytes = vintLen + 3;

            long payloadSize = (long)size - headerBytes;
            if (payloadSize > 0)
            {
                track = matchedTrack;
                // Rent from ArrayPool to avoid per-block heap allocation
                byte[] data = ArrayPool<byte>.Shared.Rent((int)payloadSize);
                try
                {
                    reader.ReadExactly(data, 0, (int)payloadSize);
                    int len = (int)payloadSize;
                    while (len > 0 && data[len - 1] == 0) len--;
                    payload = Encoding.UTF8.GetString(data, 0, len);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(data);
                }
            }
        }

        private static AssTrack? ParseTrackEntry(FastEbmlReader reader, ulong size)
        {
            long endPos = reader.Position + (long)size;
            ulong trackNum = 0;
            ulong trackType = 0;
            string codecId = "";
            string codecPrivate = "";
            string name = "";
            string language = "eng";

            while (reader.Position < endPos)
            {
                ulong id = ReadEbmlId(reader);
                ulong subSize = ReadEbmlSize(reader);

                if (id == 0xD7) trackNum = ReadUint(reader, subSize);
                else if (id == 0x83) trackType = ReadUint(reader, subSize);
                else if (id == 0x86) codecId = ReadString(reader, subSize);
                else if (id == 0x63A2) codecPrivate = ReadString(reader, subSize);
                else if (id == 0x536E) name = ReadString(reader, subSize);
                else if (id == 0x22B59C) language = ReadString(reader, subSize);
                else reader.Skip((long)subSize);
            }

#if DEBUG
            System.Diagnostics.Debug.WriteLine(
                $"[MkvExtractor] TrackEntry: num={trackNum}, type={trackType}, codec='{codecId}', name='{name}'");
#endif

            if (trackType == 17 && (codecId == "S_TEXT/ASS" || codecId == "S_TEXT/SSA"))
            {
                return new AssTrack { TrackNumber = trackNum, Name = name, Language = language, CodecPrivate = codecPrivate, IsSrt = false };
            }
            return null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong ReadEbmlId(FastEbmlReader reader)
        {
            int b = reader.ReadByte();
            if (b == -1) return 0;
            int len = VintLengthTable[b];
            ulong id = (ulong)b;
            for (int i = 1; i < len; i++)
            {
                id = (id << 8) | (byte)reader.ReadByte();
            }
            return id;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong ReadEbmlSize(FastEbmlReader reader)
        {
            int b = reader.ReadByte();
            if (b == -1) return 0;
            int len = VintLengthTable[b];
            if (len == 0) return 0;

            ulong size = (ulong)(b & (0xFF >> len));
            for (int i = 1; i < len; i++)
            {
                size = (size << 8) | (byte)reader.ReadByte();
            }

            ulong maxValue = (1UL << (7 * len)) - 1;
            if (size == maxValue) return ulong.MaxValue;
            return size;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong ReadUint(FastEbmlReader reader, ulong size)
        {
            ulong val = 0;
            for (ulong i = 0; i < size; i++)
            {
                val = (val << 8) | (byte)reader.ReadByte();
            }
            return val;
        }

        private static string ReadString(FastEbmlReader reader, ulong size)
        {
            // Use ArrayPool for string reads to eliminate per-read heap allocation
            byte[] data = ArrayPool<byte>.Shared.Rent((int)size);
            try
            {
                reader.ReadExactly(data, 0, (int)size);
                int len = (int)size;
                while (len > 0 && data[len - 1] == 0) len--;
                return Encoding.UTF8.GetString(data, 0, len);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(data);
            }
        }

        // ────────────────────────────────────────────────────────────────────
        //  SRT Output — zero intermediate string, direct stream write
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Writes the SRT file directly to a stream without materializing the entire
        /// file content as an intermediate string. For 2000+ events this saves ~200KB
        /// of transient heap allocation.
        /// </summary>
        public static void WriteSrtToStream(AssTrack track, Stream outputStream)
        {
            // 4KB StreamWriter buffer — writes flush to disk in efficient chunks
            using var writer = new StreamWriter(outputStream, new UTF8Encoding(false), bufferSize: 4096, leaveOpen: true);
            int index = 1;

            // Reusable char buffer for timecode formatting (avoids per-call allocation)
            char[] tcBuf = new char[12]; // "HH:MM:SS,MMM"

            foreach (var ev in track.Events)
            {
                string dialogueText = ExtractDialogueText(ev.Payload);
                if (string.IsNullOrWhiteSpace(dialogueText)) continue;

                // Check if text has content after processing (dry run)
                string processed = ProcessSubtitleText(dialogueText);
                if (string.IsNullOrWhiteSpace(processed)) continue;

                // Write the complete SRT entry: index, timecodes, text, blank line
                writer.Write(index);
                writer.WriteLine();
                FormatSrtTimeDirect(ev.TimecodeMs, tcBuf);
                writer.Write(tcBuf, 0, 12);
                writer.Write(" --> ");
                FormatSrtTimeDirect(ev.TimecodeMs + ev.DurationMs, tcBuf);
                writer.Write(tcBuf, 0, 12);
                writer.WriteLine();
                writer.Write(processed);
                writer.WriteLine();
                writer.WriteLine(); // Blank line between blocks
                index++;
            }
        }

        /// <summary>
        /// Backward-compatible string-returning version for callers that need a string.
        /// Uses a MemoryStream internally to avoid duplicating logic.
        /// </summary>
        public static string AssembleSrtFile(AssTrack track)
        {
            using var ms = new MemoryStream(track.Events.Count * 80);
            WriteSrtToStream(track, ms);
            return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
        }

        /// <summary>
        /// Single-pass text processor. Strips {...} ASS override tags AND converts
        /// \N, \n, \h escape sequences in one scan. Returns cleaned text.
        /// </summary>
        public static string ProcessSubtitleText(string text)
        {
            // Fast path: no tags and no escape sequences
            if (text.IndexOf('{') < 0 && text.IndexOf('\\') < 0 && text.IndexOf('<') < 0)
                return text.Trim();

            var result = new StringBuilder(text.Length);
            int i = 0;

            while (i < text.Length)
            {
                char c = text[i];

                // Skip {...} ASS override blocks
                if (c == '{')
                {
                    int closeIdx = text.IndexOf('}', i + 1);
                    if (closeIdx >= 0)
                    {
                        i = closeIdx + 1;
                        continue;
                    }
                }

                // Skip <...> HTML tags
                if (c == '<')
                {
                    int closeIdx = text.IndexOf('>', i + 1);
                    if (closeIdx >= 0)
                    {
                        i = closeIdx + 1;
                        continue;
                    }
                }

                // Handle ASS escape sequences: \N \n \h
                if (c == '\\' && i + 1 < text.Length)
                {
                    char next = text[i + 1];
                    if (next == 'N' || next == 'n')
                    {
                        result.Append("\r\n");
                        i += 2;
                        continue;
                    }
                    if (next == 'h')
                    {
                        result.Append(' ');
                        i += 2;
                        continue;
                    }
                }

                result.Append(c);
                i++;
            }

            return result.ToString().Trim();
        }

        /// <summary>
        /// Extracts the dialogue text field from an ASS payload as a string.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string ExtractDialogueText(string payload)
        {
            // ASS block payload format: ReadOrder,Layer,Style,Name,MarginL,MarginR,MarginV,Effect,Text
            // We need everything after the 8th comma
            int commaCount = 0;
            for (int i = 0; i < payload.Length; i++)
            {
                if (payload[i] == ',')
                {
                    commaCount++;
                    if (commaCount == 8) return payload.Substring(i + 1);
                }
            }

            // Fallback: some payloads have fewer fields (e.g., just ReadOrder,Duration,Text)
            commaCount = 0;
            for (int i = 0; i < payload.Length; i++)
            {
                if (payload[i] == ',')
                {
                    commaCount++;
                    if (commaCount == 2) return payload.Substring(i + 1);
                }
            }
            return payload;
        }

        /// <summary>
        /// Formats a millisecond timecode directly into a pre-allocated char buffer
        /// as "HH:MM:SS,MMM". Zero allocation — no string interpolation, no TimeSpan.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void FormatSrtTimeDirect(long ms, char[] buf)
        {
            if (ms < 0) ms = 0;
            long totalSeconds = ms / 1000;
            int millis = (int)(ms % 1000);
            int seconds = (int)(totalSeconds % 60);
            long totalMinutes = totalSeconds / 60;
            int minutes = (int)(totalMinutes % 60);
            int hours = (int)(totalMinutes / 60);

            // "HH:MM:SS,MMM" — 12 chars
            buf[0] = (char)('0' + hours / 10);
            buf[1] = (char)('0' + hours % 10);
            buf[2] = ':';
            buf[3] = (char)('0' + minutes / 10);
            buf[4] = (char)('0' + minutes % 10);
            buf[5] = ':';
            buf[6] = (char)('0' + seconds / 10);
            buf[7] = (char)('0' + seconds % 10);
            buf[8] = ',';
            buf[9] = (char)('0' + millis / 100);
            buf[10] = (char)('0' + (millis / 10) % 10);
            buf[11] = (char)('0' + millis % 10);
        }

        // ────────────────────────────────────────────────────────────────────
        //  ASS Output (kept for backward compatibility)
        // ────────────────────────────────────────────────────────────────────

        public static string AssembleAssFile(AssTrack track)
        {
            StringBuilder sb = new StringBuilder(track.CodecPrivate.Length + track.Events.Count * 120);
            sb.AppendLine(track.CodecPrivate.TrimEnd());

            if (!track.CodecPrivate.Contains("[Events]", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine();
                sb.AppendLine("[Events]");
                sb.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");
            }

            // Reusable buffer for ASS timecodes — zero allocation append via sb.Append(char[], offset, count)
            char[] tcBuf = new char[10]; // "H:MM:SS.CC"

            foreach (var ev in track.Events)
            {
                FormatAssTimeDirect(ev.TimecodeMs, tcBuf);
                FormatAssTimeDirect(ev.TimecodeMs + ev.DurationMs, tcBuf);

                int firstComma = ev.Payload.IndexOf(',');
                if (firstComma > 0)
                {
                    int secondComma = ev.Payload.IndexOf(',', firstComma + 1);
                    if (secondComma > 0)
                    {
                        string layer = ev.Payload.Substring(firstComma + 1, secondComma - firstComma - 1).Trim();
                        string rest  = ev.Payload.Substring(secondComma + 1).TrimStart();

                        sb.Append("Dialogue: ");
                        sb.Append(layer);
                        sb.Append(", ");
                        // start
                        FormatAssTimeDirect(ev.TimecodeMs, tcBuf);
                        sb.Append(tcBuf, 0, 10);
                        sb.Append(", ");
                        // end
                        FormatAssTimeDirect(ev.TimecodeMs + ev.DurationMs, tcBuf);
                        sb.Append(tcBuf, 0, 10);
                        sb.Append(", ");
                        sb.Append(rest);
                        sb.AppendLine();
                    }
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Formats a millisecond timecode directly into a pre-allocated char buffer
        /// as "H:MM:SS.CC" (ASS format). Zero allocation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void FormatAssTimeDirect(long ms, char[] buf)
        {
            if (ms < 0) ms = 0;
            long totalSeconds = ms / 1000;
            int centis = (int)(ms % 1000) / 10;
            int seconds = (int)(totalSeconds % 60);
            long totalMinutes = totalSeconds / 60;
            int minutes = (int)(totalMinutes % 60);
            int hours = (int)(totalMinutes / 60);

            // "H:MM:SS.CC" — 10 chars
            buf[0] = (char)('0' + hours % 10); // ASS uses single-digit hours
            buf[1] = ':';
            buf[2] = (char)('0' + minutes / 10);
            buf[3] = (char)('0' + minutes % 10);
            buf[4] = ':';
            buf[5] = (char)('0' + seconds / 10);
            buf[6] = (char)('0' + seconds % 10);
            buf[7] = '.';
            buf[8] = (char)('0' + centis / 10);
            buf[9] = (char)('0' + centis % 10);
        }

        public static string AssembleSrtAsAssFile(AssTrack track)
        {
            var sb = new StringBuilder(track.Events.Count * 100 + 600);

            // --- Script Info ---
            sb.AppendLine("[Script Info]");
            sb.AppendLine("ScriptType: v4.00+");
            sb.AppendLine("Collisions: Normal");
            sb.AppendLine("PlayResX: 1920");
            sb.AppendLine("PlayResY: 1080");
            sb.AppendLine("Timer: 100.0000");
            sb.AppendLine();

            // --- Styles --- (placeholder values; AssStyleRewriter rewrites every field)
            sb.AppendLine("[V4+ Styles]");
            sb.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
            sb.AppendLine("Style: Default,Arial,42,&H00FFFFFF,&H00FFFFFF,&H00000000,&H80000000,0,0,0,0,100,100,0,0,1,2,1,2,10,10,20,1");
            sb.AppendLine();

            // --- Events ---
            sb.AppendLine("[Events]");
            sb.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");

            char[] tcBuf = new char[10];
            foreach (var ev in track.Events)
            {
                FormatAssTimeDirect(ev.TimecodeMs, tcBuf);
                string start = new string(tcBuf);
                FormatAssTimeDirect(ev.TimecodeMs + ev.DurationMs, tcBuf);
                string end = new string(tcBuf);

                string text = ConvertSrtTextToAss(ev.Payload);

                sb.Append("Dialogue: 0,");
                sb.Append(start);
                sb.Append(',');
                sb.Append(end);
                sb.AppendLine(",Default,,0,0,0,," + text);
            }

            return sb.ToString();
        }

        public static string ConvertSrtTextToAss(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            var sb = new StringBuilder(text.Length + 16);
            int i = 0;

            while (i < text.Length)
            {
                char c = text[i];

                if (c == '\r' || c == '\n')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    sb.Append("\\N");
                    i++;
                    continue;
                }

                if (c == '<')
                {
                    int closeIdx = text.IndexOf('>', i + 1);
                    if (closeIdx >= 0)
                    {
                        string tag = text.Substring(i + 1, closeIdx - i - 1).Trim().ToLowerInvariant();
                        if (tag == "i" || tag.StartsWith("i ")) sb.Append("{\\i1}");
                        else if (tag == "/i") sb.Append("{\\i0}");
                        else if (tag == "b" || tag.StartsWith("b ")) sb.Append("{\\b1}");
                        else if (tag == "/b") sb.Append("{\\b0}");
                        else if (tag == "u" || tag.StartsWith("u ")) sb.Append("{\\u1}");
                        else if (tag == "/u") sb.Append("{\\u0}");
                        i = closeIdx + 1;
                        continue;
                    }
                }

                sb.Append(c);
                i++;
            }

            return sb.ToString();
        }

        public static AssTrack ParseSrtContentToAssTrack(string srtContent)
        {
            var track = new AssTrack { IsSrt = true };
            if (string.IsNullOrWhiteSpace(srtContent)) return track;

            var lines = srtContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            int i = 0;
            while (i < lines.Length)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || int.TryParse(line, out _))
                {
                    i++;
                    continue;
                }

                int arrowIdx = line.IndexOf("-->", StringComparison.Ordinal);
                if (arrowIdx > 0)
                {
                    string startStr = line.Substring(0, arrowIdx).Trim();
                    string endStr = line.Substring(arrowIdx + 3).Trim();

                    int spaceIdx = endStr.IndexOf(' ');
                    if (spaceIdx > 0) endStr = endStr.Substring(0, spaceIdx);

                    long startMs = ParseSrtTimeMs(startStr);
                    long endMs = ParseSrtTimeMs(endStr);

                    i++;
                    var textSb = new StringBuilder();
                    while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]))
                    {
                        if (textSb.Length > 0) textSb.Append('\n');
                        textSb.Append(lines[i].Trim());
                        i++;
                    }

                    if (startMs >= 0 && endMs > startMs)
                    {
                        track.Events.Add(new AssEvent
                        {
                            TimecodeMs = startMs,
                            DurationMs = endMs - startMs,
                            Payload = textSb.ToString()
                        });
                    }
                    continue;
                }
                i++;
            }
            return track;
        }

        private static long ParseSrtTimeMs(string timeStr)
        {
            var parts = timeStr.Split(new[] { ':', ',', '.' }, StringSplitOptions.None);
            if (parts.Length >= 4 &&
                int.TryParse(parts[0], out int h) &&
                int.TryParse(parts[1], out int m) &&
                int.TryParse(parts[2], out int s) &&
                int.TryParse(parts[3], out int ms))
            {
                if (parts[3].Length == 2) ms *= 10;
                return h * 3600000L + m * 60000L + s * 1000L + ms;
            }
            return -1;
        }
    }
}
