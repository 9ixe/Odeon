#nullable enable

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Odeon.Core.Interop
{
    #region Enums

    /// <summary>
    /// Data format for options and properties.
    /// </summary>
    public enum MpvFormat
    {
        None = 0,
        String = 1,
        OsdString = 2,
        Flag = 3,
        Int64 = 4,
        Double = 5,
        Node = 6,
        NodeArray = 7,
        NodeMap = 8,
        ByteArray = 9
    }

    /// <summary>
    /// Event identifiers returned by mpv_wait_event.
    /// </summary>
    public enum MpvEventId
    {
        None = 0,
        Shutdown = 1,
        LogMessage = 2,
        GetPropertyReply = 3,
        SetPropertyReply = 4,
        CommandReply = 5,
        StartFile = 6,
        EndFile = 7,
        FileLoaded = 8,
        TracksChanged = 9,
        TrackSwitched = 10,
        Idle = 11,
        Pause = 12,
        Unpause = 13,
        Tick = 14,
        ScriptInputDispatch = 15,
        ClientMessage = 16,
        VideoReconfig = 17,
        AudioReconfig = 18,
        MetadataUpdate = 19,
        Seek = 20,
        PlaybackRestart = 21,
        PropertyChange = 22,
        ChapterChange = 23,
        QueueOverflow = 24,
        Hook = 25
    }

    /// <summary>
    /// Generic error codes returned by libmpv functions.
    /// </summary>
    public enum MpvError
    {
        Success = 0,
        QueueFull = -1,
        Nomem = -2,
        Uninitialized = -3,
        InvalidParameter = -4,
        OptionNotFound = -5,
        OptionFormat = -6,
        OptionError = -7,
        PropertyNotFound = -8,
        PropertyFormat = -9,
        PropertyUnavailable = -10,
        PropertyError = -11,
        Command = -12,
        LoadingFailed = -13,
        AoInitFailed = -14,
        VoInitFailed = -15,
        NothingToPlay = -16,
        UnknownFormat = -17,
        Unsupported = -18,
        NotImplemented = -19,
        Generic = -20
    }

    /// <summary>
    /// Logging verbosity levels.
    /// </summary>
    public enum MpvLogLevel
    {
        None = 0,
        Fatal = 10,
        Error = 20,
        Warn = 30,
        Info = 40,
        V = 50,
        Debug = 60,
        Trace = 70
    }

    /// <summary>
    /// End-of-file reasons passed in mpv_event_end_file.
    /// </summary>
    public enum MpvEndFileReason
    {
        Eof = 0,
        Stop = 2,
        Quit = 3,
        Error = 4,
        Redirect = 5
    }

    #endregion

    #region Structs

    /// <summary>
    /// General event structure returned by mpv_wait_event.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MpvEvent
    {
        public MpvEventId EventId;
        public int Error;
        public ulong ReplyUserData;
        public IntPtr Data;
    }

    /// <summary>
    /// Event data for MPV_EVENT_PROPERTY_CHANGE.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MpvEventProperty
    {
        public IntPtr Name;
        public MpvFormat Format;
        public IntPtr Data;
    }

    /// <summary>
    /// Event data for MPV_EVENT_LOG_MESSAGE.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MpvEventLogMessage
    {
        public IntPtr Prefix;
        public IntPtr Level;
        public IntPtr Text;
        public MpvLogLevel LogLevel;
    }

    /// <summary>
    /// Event data for MPV_EVENT_END_FILE.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MpvEventEndFile
    {
        public MpvEndFileReason Reason;
        public int Error;
        public long PlaylistEntryId;
        public long PlaylistInsertId;
        public int PlaylistInsertNumEntries;
    }

    /// <summary>
    /// Union representation inside mpv_node.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    public struct MpvNodeUnion
    {
        [FieldOffset(0)] public IntPtr String;
        [FieldOffset(0)] public int Flag;
        [FieldOffset(0)] public long Int64;
        [FieldOffset(0)] public double Double;
        [FieldOffset(0)] public IntPtr NodeList;
        [FieldOffset(0)] public IntPtr ByteArray;
    }

    /// <summary>
    /// Generic data structure for complex properties and commands.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MpvNode
    {
        public MpvNodeUnion Value;
        public MpvFormat Format;
    }

    /// <summary>
    /// Array or map representation in mpv_node.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MpvNodeList
    {
        public int Num;
        public IntPtr Values;
        public IntPtr Keys;
    }

    /// <summary>
    /// Byte array representation in mpv_node.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MpvByteArray
    {
        public IntPtr Data;
        public UIntPtr Size;
    }

    #endregion

    #region Delegates

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void MpvWakeupCallback(IntPtr d);

    #endregion

    /// <summary>
    /// Direct P/Invoke bindings for libmpv (mpv-2.dll).
    /// </summary>
    public static class MpvInterop
    {
        public const string MpvDll = "mpv-2.dll";

        #region Native Methods

        [DllImport(MpvDll, EntryPoint = "mpv_client_api_version", CallingConvention = CallingConvention.Cdecl)]
        public static extern ulong mpv_client_api_version();

        [DllImport(MpvDll, EntryPoint = "mpv_error_string", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr mpv_error_string(int error);

        [DllImport(MpvDll, EntryPoint = "mpv_free", CallingConvention = CallingConvention.Cdecl)]
        public static extern void mpv_free(IntPtr data);

        [DllImport(MpvDll, EntryPoint = "mpv_client_name", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr mpv_client_name(IntPtr ctx);

        [DllImport(MpvDll, EntryPoint = "mpv_create", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr mpv_create();

        [DllImport(MpvDll, EntryPoint = "mpv_initialize", CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_initialize(IntPtr ctx);

        [DllImport(MpvDll, EntryPoint = "mpv_destroy", CallingConvention = CallingConvention.Cdecl)]
        public static extern void mpv_destroy(IntPtr ctx);

        [DllImport(MpvDll, EntryPoint = "mpv_terminate_destroy", CallingConvention = CallingConvention.Cdecl)]
        public static extern void mpv_terminate_destroy(IntPtr ctx);

        [DllImport(MpvDll, EntryPoint = "mpv_command", CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_command(IntPtr ctx, [In] IntPtr[] args);

        [DllImport(MpvDll, EntryPoint = "mpv_command_async", CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_command_async(IntPtr ctx, ulong replyUserData, [In] IntPtr[] args);

        [DllImport(MpvDll, EntryPoint = "mpv_command_string", CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_command_string(IntPtr ctx, [In] byte[] args);

        [DllImport(MpvDll, EntryPoint = "mpv_set_option", CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_set_option(IntPtr ctx, [In] byte[] name, MpvFormat format, IntPtr data);

        [DllImport(MpvDll, EntryPoint = "mpv_set_option_string", CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_set_option_string(IntPtr ctx, [In] byte[] name, [In] byte[] data);

        [DllImport(MpvDll, EntryPoint = "mpv_get_property", CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_get_property(IntPtr ctx, [In] byte[] name, MpvFormat format, IntPtr data);

        [DllImport(MpvDll, EntryPoint = "mpv_get_property", CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_get_property(IntPtr ctx, [In] byte[] name, MpvFormat format, out MpvNode data);

        [DllImport(MpvDll, EntryPoint = "mpv_get_property_string", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr mpv_get_property_string(IntPtr ctx, [In] byte[] name);

        [DllImport(MpvDll, EntryPoint = "mpv_set_property", CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_set_property(IntPtr ctx, [In] byte[] name, MpvFormat format, IntPtr data);

        [DllImport(MpvDll, EntryPoint = "mpv_set_property_string", CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_set_property_string(IntPtr ctx, [In] byte[] name, [In] byte[] data);

        [DllImport(MpvDll, EntryPoint = "mpv_set_property_async", CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_set_property_async(IntPtr ctx, ulong replyUserData, [In] byte[] name, MpvFormat format, IntPtr data);

        [DllImport(MpvDll, EntryPoint = "mpv_observe_property", CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_observe_property(IntPtr ctx, ulong replyUserData, [In] byte[] name, MpvFormat format);

        [DllImport(MpvDll, EntryPoint = "mpv_unobserve_property", CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_unobserve_property(IntPtr ctx, ulong registeredReplyUserData);

        [DllImport(MpvDll, EntryPoint = "mpv_wait_event", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr mpv_wait_event(IntPtr ctx, double timeout);

        [DllImport(MpvDll, EntryPoint = "mpv_wakeup", CallingConvention = CallingConvention.Cdecl)]
        public static extern void mpv_wakeup(IntPtr ctx);

        [DllImport(MpvDll, EntryPoint = "mpv_set_wakeup_callback", CallingConvention = CallingConvention.Cdecl)]
        public static extern void mpv_set_wakeup_callback(IntPtr ctx, MpvWakeupCallback cb, IntPtr d);

        [DllImport(MpvDll, EntryPoint = "mpv_request_log_messages", CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_request_log_messages(IntPtr ctx, [In] byte[] minLevel);

        [DllImport(MpvDll, EntryPoint = "mpv_free_node_contents", CallingConvention = CallingConvention.Cdecl)]
        public static extern void mpv_free_node_contents(ref MpvNode node);

        #endregion

        #region Helpers

        public static byte[] GetUtf8Bytes(string? str)
        {
            if (str == null) return new byte[] { 0 };
            byte[] bytes = Encoding.UTF8.GetBytes(str);
            byte[] result = new byte[bytes.Length + 1];
            Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
            result[bytes.Length] = 0;
            return result;
        }

        public static string? Utf8PtrToString(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) return null;
            int len = 0;
            while (Marshal.ReadByte(ptr, len) != 0) len++;
            if (len == 0) return string.Empty;
            byte[] buffer = new byte[len];
            Marshal.Copy(ptr, buffer, 0, len);
            return Encoding.UTF8.GetString(buffer);
        }

        public static string GetErrorString(int error)
        {
            IntPtr ptr = mpv_error_string(error);
            return Utf8PtrToString(ptr) ?? $"Unknown error ({error})";
        }

        public static int Command(IntPtr ctx, params string[] args)
        {
            if (ctx == IntPtr.Zero || args == null || args.Length == 0)
                return (int)MpvError.InvalidParameter;

            IntPtr[] unmanagedArgs = new IntPtr[args.Length + 1];
            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(args[i]);
                    IntPtr ptr = Marshal.AllocHGlobal(bytes.Length + 1);
                    Marshal.Copy(bytes, 0, ptr, bytes.Length);
                    Marshal.WriteByte(ptr, bytes.Length, 0);
                    unmanagedArgs[i] = ptr;
                }
                unmanagedArgs[args.Length] = IntPtr.Zero;

                return mpv_command(ctx, unmanagedArgs);
            }
            finally
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (unmanagedArgs[i] != IntPtr.Zero)
                        Marshal.FreeHGlobal(unmanagedArgs[i]);
                }
            }
        }

        public static int CommandAsync(IntPtr ctx, ulong replyUserData, params string[] args)
        {
            if (ctx == IntPtr.Zero || args == null || args.Length == 0)
                return (int)MpvError.InvalidParameter;

            IntPtr[] unmanagedArgs = new IntPtr[args.Length + 1];
            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(args[i]);
                    IntPtr ptr = Marshal.AllocHGlobal(bytes.Length + 1);
                    Marshal.Copy(bytes, 0, ptr, bytes.Length);
                    Marshal.WriteByte(ptr, bytes.Length, 0);
                    unmanagedArgs[i] = ptr;
                }
                unmanagedArgs[args.Length] = IntPtr.Zero;

                return mpv_command_async(ctx, replyUserData, unmanagedArgs);
            }
            finally
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (unmanagedArgs[i] != IntPtr.Zero)
                        Marshal.FreeHGlobal(unmanagedArgs[i]);
                }
            }
        }

        public static int SetOptionString(IntPtr ctx, string name, string value)
        {
            return mpv_set_option_string(ctx, GetUtf8Bytes(name), GetUtf8Bytes(value));
        }

        public static int SetPropertyString(IntPtr ctx, string name, string value)
        {
            return mpv_set_property_string(ctx, GetUtf8Bytes(name), GetUtf8Bytes(value));
        }

        public static int SetPropertyBool(IntPtr ctx, string name, bool value)
        {
            int flag = value ? 1 : 0;
            IntPtr ptr = Marshal.AllocHGlobal(sizeof(int));
            try
            {
                Marshal.WriteInt32(ptr, flag);
                return mpv_set_property(ctx, GetUtf8Bytes(name), MpvFormat.Flag, ptr);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        public static int SetPropertyLong(IntPtr ctx, string name, long value)
        {
            IntPtr ptr = Marshal.AllocHGlobal(sizeof(long));
            try
            {
                Marshal.WriteInt64(ptr, value);
                return mpv_set_property(ctx, GetUtf8Bytes(name), MpvFormat.Int64, ptr);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        public static int SetPropertyDouble(IntPtr ctx, string name, double value)
        {
            IntPtr ptr = Marshal.AllocHGlobal(sizeof(double));
            try
            {
                byte[] bytes = BitConverter.GetBytes(value);
                Marshal.Copy(bytes, 0, ptr, bytes.Length);
                return mpv_set_property(ctx, GetUtf8Bytes(name), MpvFormat.Double, ptr);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        public static string? GetPropertyString(IntPtr ctx, string name)
        {
            IntPtr ptr = mpv_get_property_string(ctx, GetUtf8Bytes(name));
            if (ptr == IntPtr.Zero) return null;
            try
            {
                return Utf8PtrToString(ptr);
            }
            finally
            {
                mpv_free(ptr);
            }
        }

        public static bool? GetPropertyBool(IntPtr ctx, string name)
        {
            IntPtr ptr = Marshal.AllocHGlobal(sizeof(int));
            try
            {
                int res = mpv_get_property(ctx, GetUtf8Bytes(name), MpvFormat.Flag, ptr);
                if (res < 0) return null;
                return Marshal.ReadInt32(ptr) != 0;
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        public static long? GetPropertyLong(IntPtr ctx, string name)
        {
            IntPtr ptr = Marshal.AllocHGlobal(sizeof(long));
            try
            {
                int res = mpv_get_property(ctx, GetUtf8Bytes(name), MpvFormat.Int64, ptr);
                if (res < 0) return null;
                return Marshal.ReadInt64(ptr);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        public static double? GetPropertyDouble(IntPtr ctx, string name)
        {
            IntPtr ptr = Marshal.AllocHGlobal(sizeof(double));
            try
            {
                int res = mpv_get_property(ctx, GetUtf8Bytes(name), MpvFormat.Double, ptr);
                if (res < 0) return null;
                byte[] bytes = new byte[sizeof(double)];
                Marshal.Copy(ptr, bytes, 0, bytes.Length);
                return BitConverter.ToDouble(bytes, 0);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        #endregion
    }
}
