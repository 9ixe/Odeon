#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Odeon.Core.Interop
{
    /// <summary>
    /// Parsed track metadata from mpv's 'track-list' property.
    /// </summary>
    public sealed class MpvTrackInfo
    {
        public long Id { get; set; }
        public string Type { get; set; } = string.Empty; // "audio", "video", "sub"
        public string? Title { get; set; }
        public string? Language { get; set; }
        public string? Codec { get; set; }
        public uint DemuxWidth { get; set; }
        public uint DemuxHeight { get; set; }
        public bool Selected { get; set; }
        public bool External { get; set; }
        public bool Default { get; set; }
        public bool Forced { get; set; }
    }

    /// <summary>
    /// Parsed chapter metadata from mpv's 'chapter-list' property.
    /// </summary>
    public sealed class MpvChapterInfo
    {
        public string Title { get; set; } = string.Empty;
        public double Time { get; set; } // seconds
    }

    /// <summary>
    /// Helper for parsing libmpv MPV_FORMAT_NODE structures into C# objects.
    /// </summary>
    public static class MpvNodeReader
    {
        public static string? Utf8ToString(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) return null;
            int len = 0;
            while (Marshal.ReadByte(ptr, len) != 0) len++;
            if (len == 0) return string.Empty;
            byte[] buffer = new byte[len];
            Marshal.Copy(ptr, buffer, 0, len);
            return Encoding.UTF8.GetString(buffer);
        }

        public static List<MpvTrackInfo> ParseTrackList(IntPtr nodePtr)
        {
            if (nodePtr == IntPtr.Zero) return new List<MpvTrackInfo>();
            MpvNode node = Marshal.PtrToStructure<MpvNode>(nodePtr);
            return ParseTrackList(ref node);
        }

        public static List<MpvTrackInfo> ParseTrackList(ref MpvNode rootNode)
        {
            var result = new List<MpvTrackInfo>();
            if (rootNode.Format != MpvFormat.NodeArray || rootNode.Value.NodeList == IntPtr.Zero)
                return result;

            MpvNodeList list = Marshal.PtrToStructure<MpvNodeList>(rootNode.Value.NodeList);
            int nodeSize = Marshal.SizeOf<MpvNode>();

            for (int i = 0; i < list.Num; i++)
            {
                IntPtr entryPtr = IntPtr.Add(list.Values, i * nodeSize);
                MpvNode entryNode = Marshal.PtrToStructure<MpvNode>(entryPtr);
                if (entryNode.Format != MpvFormat.NodeMap || entryNode.Value.NodeList == IntPtr.Zero)
                    continue;

                MpvNodeList map = Marshal.PtrToStructure<MpvNodeList>(entryNode.Value.NodeList);
                var track = new MpvTrackInfo();

                for (int k = 0; k < map.Num; k++)
                {
                    IntPtr keyPtr = Marshal.ReadIntPtr(map.Keys, k * IntPtr.Size);
                    string? key = Utf8ToString(keyPtr);
                    if (string.IsNullOrEmpty(key)) continue;

                    IntPtr valPtr = IntPtr.Add(map.Values, k * nodeSize);
                    MpvNode valNode = Marshal.PtrToStructure<MpvNode>(valPtr);

                    switch (key)
                    {
                        case "id":
                            if (valNode.Format == MpvFormat.Int64) track.Id = valNode.Value.Int64;
                            break;
                        case "type":
                            if (valNode.Format == MpvFormat.String) track.Type = Utf8ToString(valNode.Value.String) ?? string.Empty;
                            break;
                        case "title":
                            if (valNode.Format == MpvFormat.String) track.Title = Utf8ToString(valNode.Value.String);
                            break;
                        case "lang":
                            if (valNode.Format == MpvFormat.String) track.Language = Utf8ToString(valNode.Value.String);
                            break;
                        case "codec":
                            if (valNode.Format == MpvFormat.String) track.Codec = Utf8ToString(valNode.Value.String);
                            break;
                        case "demux-w":
                            if (valNode.Format == MpvFormat.Int64) track.DemuxWidth = (uint)valNode.Value.Int64;
                            break;
                        case "demux-h":
                            if (valNode.Format == MpvFormat.Int64) track.DemuxHeight = (uint)valNode.Value.Int64;
                            break;
                        case "selected":
                            if (valNode.Format == MpvFormat.Flag) track.Selected = valNode.Value.Flag != 0;
                            else if (valNode.Format == MpvFormat.Int64) track.Selected = valNode.Value.Int64 != 0;
                            break;
                        case "external":
                            if (valNode.Format == MpvFormat.Flag) track.External = valNode.Value.Flag != 0;
                            else if (valNode.Format == MpvFormat.Int64) track.External = valNode.Value.Int64 != 0;
                            break;
                        case "default":
                            if (valNode.Format == MpvFormat.Flag) track.Default = valNode.Value.Flag != 0;
                            else if (valNode.Format == MpvFormat.Int64) track.Default = valNode.Value.Int64 != 0;
                            break;
                        case "forced":
                            if (valNode.Format == MpvFormat.Flag) track.Forced = valNode.Value.Flag != 0;
                            else if (valNode.Format == MpvFormat.Int64) track.Forced = valNode.Value.Int64 != 0;
                            break;
                    }
                }

                result.Add(track);
            }

            return result;
        }

        public static List<MpvChapterInfo> ParseChapterList(IntPtr nodePtr)
        {
            if (nodePtr == IntPtr.Zero) return new List<MpvChapterInfo>();
            MpvNode node = Marshal.PtrToStructure<MpvNode>(nodePtr);
            return ParseChapterList(ref node);
        }

        public static List<MpvChapterInfo> ParseChapterList(ref MpvNode rootNode)
        {
            var result = new List<MpvChapterInfo>();
            if (rootNode.Format != MpvFormat.NodeArray || rootNode.Value.NodeList == IntPtr.Zero)
                return result;

            MpvNodeList list = Marshal.PtrToStructure<MpvNodeList>(rootNode.Value.NodeList);
            int nodeSize = Marshal.SizeOf<MpvNode>();

            for (int i = 0; i < list.Num; i++)
            {
                IntPtr entryPtr = IntPtr.Add(list.Values, i * nodeSize);
                MpvNode entryNode = Marshal.PtrToStructure<MpvNode>(entryPtr);
                if (entryNode.Format != MpvFormat.NodeMap || entryNode.Value.NodeList == IntPtr.Zero)
                    continue;

                MpvNodeList map = Marshal.PtrToStructure<MpvNodeList>(entryNode.Value.NodeList);
                var chapter = new MpvChapterInfo();

                for (int k = 0; k < map.Num; k++)
                {
                    IntPtr keyPtr = Marshal.ReadIntPtr(map.Keys, k * IntPtr.Size);
                    string? key = Utf8ToString(keyPtr);
                    if (string.IsNullOrEmpty(key)) continue;

                    IntPtr valPtr = IntPtr.Add(map.Values, k * nodeSize);
                    MpvNode valNode = Marshal.PtrToStructure<MpvNode>(valPtr);

                    switch (key)
                    {
                        case "title":
                            if (valNode.Format == MpvFormat.String) chapter.Title = Utf8ToString(valNode.Value.String) ?? string.Empty;
                            break;
                        case "time":
                            if (valNode.Format == MpvFormat.Double) chapter.Time = valNode.Value.Double;
                            else if (valNode.Format == MpvFormat.Int64) chapter.Time = valNode.Value.Int64;
                            break;
                    }
                }

                result.Add(chapter);
            }

            return result;
        }

        public static List<MpvTrackInfo> GetTrackList(IntPtr mpv)
        {
            if (mpv == IntPtr.Zero) return new List<MpvTrackInfo>();
            int err = MpvInterop.mpv_get_property(mpv, MpvInterop.GetUtf8Bytes("track-list"), MpvFormat.Node, out MpvNode node);
            if (err >= 0)
            {
                try
                {
                    return ParseTrackList(ref node);
                }
                finally
                {
                    MpvInterop.mpv_free_node_contents(ref node);
                }
            }
            return new List<MpvTrackInfo>();
        }

        public static List<MpvChapterInfo> GetChapterList(IntPtr mpv)
        {
            if (mpv == IntPtr.Zero) return new List<MpvChapterInfo>();
            int err = MpvInterop.mpv_get_property(mpv, MpvInterop.GetUtf8Bytes("chapter-list"), MpvFormat.Node, out MpvNode node);
            if (err >= 0)
            {
                try
                {
                    return ParseChapterList(ref node);
                }
                finally
                {
                    MpvInterop.mpv_free_node_contents(ref node);
                }
            }
            return new List<MpvChapterInfo>();
        }
    }
}
