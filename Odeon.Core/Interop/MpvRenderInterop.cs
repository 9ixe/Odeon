#nullable enable

using System;
using System.Runtime.InteropServices;

namespace Odeon.Core.Interop
{
    #region Enums

    /// <summary>
    /// Parameters for mpv_render_param used in mpv_render_context_create and render calls.
    /// Mirrors enum mpv_render_param_type from mpv/render.h.
    /// </summary>
    public enum MpvRenderParamType
    {
        Invalid = 0,

        /// <summary>const char * — render API type.</summary>
        ApiType = 1,

        /// <summary>mpv_opengl_init_params — only for the "opengl" API, which is unused here.</summary>
        OpenGLInitParams = 2,

        /// <summary>mpv_opengl_fbo — only for the "opengl" API, which is unused here.</summary>
        OpenGLFbo = 3,

        FlipY = 4,
        Depth = 5,
        IccProfile = 6,
        AmbientLight = 7,
        X11Display = 8,
        WlDisplay = 9,

        /// <summary>int * — enables advanced control (required by the software renderer).</summary>
        AdvancedControl = 10,

        NextFrameInfo = 11,
        BlockForTargetTime = 12,
        SkipRendering = 13,
        DrmDisplay = 14,
        DrmDrawSurfaceSize = 15,
        DrmMode = 16,

        /// <summary>int[2] — output size for the "sw" API.</summary>
        SwSize = 17,

        /// <summary>const char * — output pixel format for the "sw" API.</summary>
        SwFormat = 18,

        /// <summary>size_t * — output stride in bytes for the "sw" API.</summary>
        SwStride = 19,

        /// <summary>void * — destination buffer for the "sw" API.</summary>
        SwPointer = 20
    }

    /// <summary>
    /// Context update flags returned by mpv_render_context_update.
    /// </summary>
    [Flags]
    public enum MpvRenderUpdateFlag : ulong
    {
        None = 0,
        Frame = 1 << 0
    }

    #endregion

    #region Structs

    /// <summary>
    /// General parameter entry for render API initialization and frame rendering.
    /// Terminated by an element with Type = MpvRenderParamType.Invalid.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MpvRenderParam
    {
        public MpvRenderParamType Type;
        public IntPtr Data;

        public MpvRenderParam(MpvRenderParamType type, IntPtr data)
        {
            Type = type;
            Data = data;
        }

        public static readonly MpvRenderParam End = new MpvRenderParam(MpvRenderParamType.Invalid, IntPtr.Zero);
    }

    #endregion

    #region Delegates

    /// <summary>
    /// Callback invoked when a new frame is ready to be drawn.
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void MpvRenderUpdateCallback(IntPtr cbCtx);

    #endregion

    /// <summary>
    /// P/Invoke bindings for the libmpv render API (render.h).
    ///
    /// libmpv defines exactly two render API types: <c>"opengl"</c> and <c>"sw"</c>. There is no
    /// Direct3D render backend — passing an unknown API type fails with MPV_ERROR_NOT_IMPLEMENTED
    /// — so this app uses the software renderer: mpv converts, scales and draws the frame on the
    /// CPU into a B8G8R8A8 buffer that is then uploaded into the swap chain back buffer.
    ///
    /// The GPU stays involved for decoding (hwdec=auto-copy) and composition, but not for the
    /// video frame's colour conversion or scaling.
    /// </summary>
    public static class MpvRenderInterop
    {
        public const string MpvDll = "mpv-2.dll";

        /// <summary>
        /// MPV_RENDER_API_TYPE_SW. The only API type this app uses.
        /// </summary>
        public const string MPV_RENDER_API_TYPE_SW = "sw";

        #region Native Methods

        [DllImport(MpvDll, EntryPoint = "mpv_render_context_create", CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_render_context_create(out IntPtr res, IntPtr mpv, [In] MpvRenderParam[] @params);

        [DllImport(MpvDll, EntryPoint = "mpv_render_context_set_update_callback", CallingConvention = CallingConvention.Cdecl)]
        public static extern void mpv_render_context_set_update_callback(IntPtr ctx, MpvRenderUpdateCallback? callback, IntPtr cbCtx);

        [DllImport(MpvDll, EntryPoint = "mpv_render_context_update", CallingConvention = CallingConvention.Cdecl)]
        public static extern ulong mpv_render_context_update(IntPtr ctx);

        [DllImport(MpvDll, EntryPoint = "mpv_render_context_render", CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_render_context_render(IntPtr ctx, [In] MpvRenderParam[] @params);

        [DllImport(MpvDll, EntryPoint = "mpv_render_context_report_swap", CallingConvention = CallingConvention.Cdecl)]
        public static extern void mpv_render_context_report_swap(IntPtr ctx);

        [DllImport(MpvDll, EntryPoint = "mpv_render_context_free", CallingConvention = CallingConvention.Cdecl)]
        public static extern void mpv_render_context_free(IntPtr ctx);

        [DllImport(MpvDll, EntryPoint = "mpv_render_context_set_parameter", CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_render_context_set_parameter(IntPtr ctx, MpvRenderParam param);

        [DllImport(MpvDll, EntryPoint = "mpv_render_context_get_info", CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_render_context_get_info(IntPtr ctx, MpvRenderParam param);

        #endregion

        #region Helpers

        /// <summary>
        /// Creates a software render context for the given mpv instance.
        /// </summary>
        public static int CreateSWContext(IntPtr mpv, out IntPtr renderContext)
        {
            renderContext = IntPtr.Zero;
            if (mpv == IntPtr.Zero)
                return (int)MpvError.InvalidParameter;

            IntPtr apiTypePtr = Marshal.StringToHGlobalAnsi(MPV_RENDER_API_TYPE_SW);
            IntPtr advancedControlPtr = Marshal.AllocHGlobal(sizeof(int));

            try
            {
                // Advanced control is mandatory for the software renderer: it is what allows
                // mpv_render_context_render to be driven from our own thread.
                Marshal.WriteInt32(advancedControlPtr, 1);

                MpvRenderParam[] @params = new[]
                {
                    new MpvRenderParam(MpvRenderParamType.ApiType, apiTypePtr),
                    new MpvRenderParam(MpvRenderParamType.AdvancedControl, advancedControlPtr),
                    MpvRenderParam.End
                };

                return mpv_render_context_create(out renderContext, mpv, @params);
            }
            finally
            {
                Marshal.FreeHGlobal(advancedControlPtr);
                Marshal.FreeHGlobal(apiTypePtr);
            }
        }

        /// <summary>
        /// Renders the current frame into the provided software pixel buffer (bgr0 / B8G8R8A8,
        /// which is the byte order the B8G8R8A8_UNORM swap chain expects).
        /// </summary>
        public static int RenderSW(IntPtr renderContext, IntPtr buffer, int width, int height, int stride)
        {
            if (renderContext == IntPtr.Zero || buffer == IntPtr.Zero || width <= 0 || height <= 0)
                return (int)MpvError.InvalidParameter;

            IntPtr sizePtr = Marshal.AllocHGlobal(sizeof(int) * 2);
            Marshal.WriteInt32(sizePtr, 0, width);
            Marshal.WriteInt32(sizePtr, 4, height);

            IntPtr formatPtr = Marshal.StringToHGlobalAnsi("bgr0");
            IntPtr stridePtr = Marshal.AllocHGlobal(IntPtr.Size);
            if (IntPtr.Size == 8)
                Marshal.WriteInt64(stridePtr, stride);
            else
                Marshal.WriteInt32(stridePtr, stride);

            try
            {
                MpvRenderParam[] @params = new[]
                {
                    new MpvRenderParam(MpvRenderParamType.SwSize, sizePtr),
                    new MpvRenderParam(MpvRenderParamType.SwFormat, formatPtr),
                    new MpvRenderParam(MpvRenderParamType.SwStride, stridePtr),
                    new MpvRenderParam(MpvRenderParamType.SwPointer, buffer),
                    MpvRenderParam.End
                };

                return mpv_render_context_render(renderContext, @params);
            }
            finally
            {
                Marshal.FreeHGlobal(sizePtr);
                Marshal.FreeHGlobal(formatPtr);
                Marshal.FreeHGlobal(stridePtr);
            }
        }

        #endregion
    }
}
