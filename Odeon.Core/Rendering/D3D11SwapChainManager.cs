#nullable enable

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Odeon.Core.Interop;
using Odeon.Core.Playback;
using Windows.UI.Xaml.Controls;

namespace Odeon.Core.Rendering
{
    /// <summary>
    /// Manages the Direct3D 11 device, DXGI composition swap chain and SwapChainPanel binding,
    /// and drives the mpv software render loop.
    ///
    /// Frames arrive from mpv as B8G8R8A8 pixels in system memory and are uploaded into the swap
    /// chain back buffer each frame. See <see cref="MpvRenderInterop"/> for why libmpv's render API
    /// has no Direct3D backend and therefore cannot hand the frame straight to the GPU.
    /// </summary>
    public sealed class D3D11SwapChainManager : IDisposable
    {
        #region COM IIDs and Constants

        private static readonly Guid IID_IDXGIDevice = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
        private static readonly Guid IID_IDXGIFactory2 = new Guid("50c83a1c-e072-4c48-87b0-3630fa36a6d0");
        private static readonly Guid IID_ID3D11Texture2D = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
        private static readonly Guid IID_ISwapChainPanelNative = new Guid("F92F19D2-3ADE-45A6-A20C-F6F1EA90554B");
        private static readonly Guid IID_IDXGISwapChain2 = new Guid("a8be2ac4-199f-4946-b331-79599fb98de7");

        private const int D3D11_SDK_VERSION = 7;
        private const int D3D_DRIVER_TYPE_HARDWARE = 1;
        private const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x0020;
        private const uint D3D11_CREATE_DEVICE_VIDEO_SUPPORT = 0x0008;

        private const uint DXGI_FORMAT_B8G8R8A8_UNORM = 87;
        private const uint DXGI_USAGE_RENDER_TARGET_OUTPUT = 0x0020;
        private const uint DXGI_SCALING_STRETCH = 0;
        private const uint DXGI_SWAP_EFFECT_FLIP_DISCARD = 4;
        private const uint DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL = 3;
        private const uint DXGI_ALPHA_MODE_IGNORE = 3;

        // VTable Slots
        private const int Slot_IUnknown_QueryInterface = 0;
        private const int Slot_IUnknown_AddRef = 1;
        private const int Slot_IUnknown_Release = 2;

        private const int Slot_IDXGIObject_GetParent = 6;
        private const int Slot_IDXGIDevice_GetAdapter = 7;

        private const int Slot_IDXGIFactory2_CreateSwapChainForComposition = 24;

        private const int Slot_ISwapChainPanelNative_SetSwapChain = 3;

        private const int Slot_ID3D11DeviceContext_UpdateSubresource = 48;

        private const int Slot_IDXGISwapChain_Present = 8;
        private const int Slot_IDXGISwapChain_GetBuffer = 9;
        private const int Slot_IDXGISwapChain_ResizeBuffers = 13;
        private const int Slot_IDXGISwapChain1_Present1 = 22;
        private const int Slot_IDXGISwapChain2_SetMatrixTransform = 34;

        #endregion

        #region Native Structs & Delegates

        [StructLayout(LayoutKind.Sequential)]
        public struct DXGI_SAMPLE_DESC
        {
            public uint Count;
            public uint Quality;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DXGI_SWAP_CHAIN_DESC1
        {
            public uint Width;
            public uint Height;
            public uint Format;
            public int Stereo;
            public DXGI_SAMPLE_DESC SampleDesc;
            public uint BufferUsage;
            public uint BufferCount;
            public uint Scaling;
            public uint SwapEffect;
            public uint AlphaMode;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DXGI_PRESENT_PARAMETERS
        {
            public uint DirtyRectsCount;
            public IntPtr pDirtyRects;
            public IntPtr pScrollRect;
            public IntPtr pScrollOffset;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DXGI_MATRIX_3X2_F
        {
            public float _11;
            public float _12;
            public float _21;
            public float _22;
            public float _31;
            public float _32;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetMatrixTransformDelegate(IntPtr thisPtr, ref DXGI_MATRIX_3X2_F pMatrix);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int QueryInterfaceDelegate(IntPtr thisPtr, ref Guid riid, out IntPtr ppvObject);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint ReleaseDelegate(IntPtr thisPtr);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetAdapterDelegate(IntPtr thisPtr, out IntPtr ppAdapter);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetParentDelegate(IntPtr thisPtr, ref Guid riid, out IntPtr ppParent);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateSwapChainForCompositionDelegate(
            IntPtr thisPtr,
            IntPtr pDevice,
            ref DXGI_SWAP_CHAIN_DESC1 pDesc,
            IntPtr pRestrictToOutput,
            out IntPtr ppSwapChain);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetSwapChainDelegate(IntPtr thisPtr, IntPtr pSwapChain);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void UpdateSubresourceDelegate(
            IntPtr thisPtr,
            IntPtr pDstResource,
            uint DstSubresource,
            IntPtr pDstBox,
            IntPtr pSrcData,
            uint SrcRowPitch,
            uint SrcDepthPitch);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int PresentDelegate(IntPtr thisPtr, uint syncInterval, uint flags);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int Present1Delegate(IntPtr thisPtr, uint syncInterval, uint presentFlags, ref DXGI_PRESENT_PARAMETERS pPresentParameters);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetBufferDelegate(IntPtr thisPtr, uint buffer, ref Guid riid, out IntPtr ppSurface);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ResizeBuffersDelegate(IntPtr thisPtr, uint bufferCount, uint width, uint height, uint newFormat, uint swapChainFlags);

        [DllImport("d3d11.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int D3D11CreateDevice(
            IntPtr pAdapter,
            int driverType,
            IntPtr software,
            uint flags,
            [In] int[]? pFeatureLevels,
            uint featureLevels,
            uint sdkVersion,
            out IntPtr ppDevice,
            out int pFeatureLevel,
            out IntPtr ppImmediateContext);

        #endregion

        #region Fields

        private readonly object _renderLock = new object();
        private readonly AutoResetEvent _renderEvent = new AutoResetEvent(false);

        private IntPtr _d3d11Device = IntPtr.Zero;
        private IntPtr _d3d11Context = IntPtr.Zero;
        private IntPtr _swapChain = IntPtr.Zero;
        private IntPtr _backBuffer = IntPtr.Zero;

        private IntPtr _mpvHandle = IntPtr.Zero;
        private IntPtr _renderContext = IntPtr.Zero;
        private IntPtr _pixelBuffer = IntPtr.Zero;
        private int _pixelBufferSize = 0;
        private MpvRenderUpdateCallback? _updateCallback;

        private Thread? _renderThread;
        private volatile bool _disposed;
        private volatile bool _forceRender;

        // mpv's software renderer fails transiently (resizes, track changes), so only report the
        // first failure of a run to avoid flooding the debug output at frame rate.
        private bool _swRenderFailureLogged;

        private int _width = 1280;
        private int _height = 720;
        private float _scaleX = 1.0f;
        private float _scaleY = 1.0f;
        private SwapChainPanel? _panel;

        // Throttle rapid successive resizes (e.g. during DPI changes, window drag, panel reloads).
        // Pending dimensions are stored and applied on the next render loop iteration to coalesce
        // multiple SizeChanged/CompositionScaleChanged events into a single swap chain resize.
        private int _pendingWidth = -1;
        private int _pendingHeight = -1;
        private float _pendingScaleX = 0f;
        private float _pendingScaleY = 0f;
        private readonly object _resizeLock = new object();

        #endregion

        #region Properties

        public bool IsInitialized => _swapChain != IntPtr.Zero;
        public bool IsAttached => _renderContext != IntPtr.Zero;

        public int Width => _width;
        public int Height => _height;
        public IntPtr D3D11Device => _d3d11Device;
        public IntPtr SwapChain => _swapChain;
        public IntPtr RenderContext => _renderContext;

        #endregion

        #region Initialization

        /// <summary>
        /// Initializes the Direct3D 11 device, DXGI composition swap chain, and binds to the SwapChainPanel.
        /// Must be called from the UI thread.
        /// </summary>
        public bool Initialize(SwapChainPanel panel, int initialWidth = 1280, int initialHeight = 720)
        {
            if (panel == null) throw new ArgumentNullException(nameof(panel));

            lock (_renderLock)
            {
                if (IsInitialized) return true;

                _panel = panel;

                // 1. Create D3D11 Device and Immediate Context
                uint deviceFlags = D3D11_CREATE_DEVICE_BGRA_SUPPORT | D3D11_CREATE_DEVICE_VIDEO_SUPPORT;
                int hr = D3D11CreateDevice(
                    IntPtr.Zero,
                    D3D_DRIVER_TYPE_HARDWARE,
                    IntPtr.Zero,
                    deviceFlags,
                    null,
                    0,
                    D3D11_SDK_VERSION,
                    out _d3d11Device,
                    out _,
                    out _d3d11Context);

                if (hr < 0 || _d3d11Device == IntPtr.Zero)
                {
                    // Fallback without VIDEO_SUPPORT flag
                    hr = D3D11CreateDevice(
                        IntPtr.Zero,
                        D3D_DRIVER_TYPE_HARDWARE,
                        IntPtr.Zero,
                        D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                        null,
                        0,
                        D3D11_SDK_VERSION,
                        out _d3d11Device,
                        out _,
                        out _d3d11Context);

                    if (hr < 0 || _d3d11Device == IntPtr.Zero)
                    {
                        Debug.WriteLine($"[D3D11SwapChainManager] D3D11CreateDevice failed: 0x{hr:X8}");
                        return false;
                    }
                }

                // 2. Query IDXGIDevice -> IDXGIAdapter -> IDXGIFactory2
                IntPtr pDxgiDevice = IntPtr.Zero;
                IntPtr pAdapter = IntPtr.Zero;
                IntPtr pFactory2 = IntPtr.Zero;

                try
                {
                    Guid iidDevice = IID_IDXGIDevice;
                    var qi = GetVTableDelegate<QueryInterfaceDelegate>(_d3d11Device, Slot_IUnknown_QueryInterface);
                    hr = qi(_d3d11Device, ref iidDevice, out pDxgiDevice);
                    if (hr < 0 || pDxgiDevice == IntPtr.Zero)
                    {
                        Debug.WriteLine($"[D3D11SwapChainManager] QueryInterface IDXGIDevice failed: 0x{hr:X8}");
                        return false;
                    }

                    var getAdapter = GetVTableDelegate<GetAdapterDelegate>(pDxgiDevice, Slot_IDXGIDevice_GetAdapter);
                    hr = getAdapter(pDxgiDevice, out pAdapter);
                    if (hr < 0 || pAdapter == IntPtr.Zero)
                    {
                        Debug.WriteLine($"[D3D11SwapChainManager] GetAdapter failed: 0x{hr:X8}");
                        return false;
                    }

                    Guid iidFactory2 = IID_IDXGIFactory2;
                    var getParent = GetVTableDelegate<GetParentDelegate>(pAdapter, Slot_IDXGIObject_GetParent);
                    hr = getParent(pAdapter, ref iidFactory2, out pFactory2);
                    if (hr < 0 || pFactory2 == IntPtr.Zero)
                    {
                        Debug.WriteLine($"[D3D11SwapChainManager] GetParent IDXGIFactory2 failed: 0x{hr:X8}");
                        return false;
                    }

                    // 3. Determine initial dimensions
                    double panelWidth = panel.ActualWidth;
                    double panelHeight = panel.ActualHeight;
                    if (panelWidth > 0 && panelHeight > 0)
                    {
                        _width = Math.Max(1, (int)Math.Round(panelWidth * panel.CompositionScaleX));
                        _height = Math.Max(1, (int)Math.Round(panelHeight * panel.CompositionScaleY));
                    }
                    else
                    {
                        _width = Math.Max(1, initialWidth);
                        _height = Math.Max(1, initialHeight);
                    }

                    // 4. Create DXGI SwapChain for Composition
                    DXGI_SWAP_CHAIN_DESC1 desc = new DXGI_SWAP_CHAIN_DESC1
                    {
                        Width = (uint)_width,
                        Height = (uint)_height,
                        Format = DXGI_FORMAT_B8G8R8A8_UNORM,
                        Stereo = 0,
                        SampleDesc = new DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
                        BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT,
                        BufferCount = 2,
                        Scaling = DXGI_SCALING_STRETCH,
                        SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD,
                        AlphaMode = DXGI_ALPHA_MODE_IGNORE,
                        Flags = 0
                    };

                    var createSC = GetVTableDelegate<CreateSwapChainForCompositionDelegate>(pFactory2, Slot_IDXGIFactory2_CreateSwapChainForComposition);
                    hr = createSC(pFactory2, _d3d11Device, ref desc, IntPtr.Zero, out _swapChain);
                    if (hr < 0 || _swapChain == IntPtr.Zero)
                    {
                        // Retry with FLIP_SEQUENTIAL
                        desc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL;
                        hr = createSC(pFactory2, _d3d11Device, ref desc, IntPtr.Zero, out _swapChain);
                        if (hr < 0 || _swapChain == IntPtr.Zero)
                        {
                            Debug.WriteLine($"[D3D11SwapChainManager] CreateSwapChainForComposition failed: 0x{hr:X8}");
                            return false;
                        }
                    }

                    _scaleX = (float)(panel.CompositionScaleX > 0 ? panel.CompositionScaleX : 1.0f);
                    _scaleY = (float)(panel.CompositionScaleY > 0 ? panel.CompositionScaleY : 1.0f);

                    // 5. Link SwapChain to SwapChainPanel via ISwapChainPanelNative
                    if (!SetPanelSwapChain(panel, _swapChain))
                    {
                        Debug.WriteLine("[D3D11SwapChainManager] SetPanelSwapChain failed.");
                        return false;
                    }

                    // Apply inverse scale matrix transform to avoid SwapChainPanel double-scaling on high-DPI displays
                    ApplyInverseScale(_scaleX, _scaleY);

                    // 6. Get backbuffer ID3D11Texture2D
                    Guid iidTex = IID_ID3D11Texture2D;
                    var getBuffer = GetVTableDelegate<GetBufferDelegate>(_swapChain, Slot_IDXGISwapChain_GetBuffer);
                    hr = getBuffer(_swapChain, 0, ref iidTex, out _backBuffer);
                    if (hr < 0 || _backBuffer == IntPtr.Zero)
                    {
                        Debug.WriteLine($"[D3D11SwapChainManager] GetBuffer failed: 0x{hr:X8}");
                        return false;
                    }

                    return true;
                }
                finally
                {
                    SafeRelease(ref pFactory2);
                    SafeRelease(ref pAdapter);
                    SafeRelease(ref pDxgiDevice);
                }
            }
        }

        private static bool SetPanelSwapChain(SwapChainPanel panel, IntPtr swapChain)
        {
            IntPtr pUnk = Marshal.GetIUnknownForObject(panel);
            if (pUnk == IntPtr.Zero)
            {
                Debug.WriteLine("[D3D11SwapChainManager] GetIUnknownForObject returned null.");
                return false;
            }

            IntPtr pNative = IntPtr.Zero;
            try
            {
                Guid iid = IID_ISwapChainPanelNative;
                int hr = Marshal.QueryInterface(pUnk, ref iid, out pNative);
                if (hr < 0 || pNative == IntPtr.Zero)
                {
                    Debug.WriteLine($"[D3D11SwapChainManager] QueryInterface ISwapChainPanelNative failed: 0x{hr:X8}");
                    return false;
                }

                var setSwapChain = GetVTableDelegate<SetSwapChainDelegate>(pNative, Slot_ISwapChainPanelNative_SetSwapChain);
                int res = setSwapChain(pNative, swapChain);
                if (res < 0)
                {
                    Debug.WriteLine($"[D3D11SwapChainManager] SetSwapChain failed: 0x{res:X8}");
                    return false;
                }

                Debug.WriteLine("[D3D11SwapChainManager] SetSwapChain succeeded!");
                return true;
            }
            finally
            {
                SafeRelease(ref pNative);
                SafeRelease(ref pUnk);
            }
        }

        #endregion

        #region Attach / Detach Player

        /// <summary>
        /// Attaches this swap chain manager to an MpvMediaPlayer instance.
        /// Initializes mpv_render_context and starts the render loop.
        /// </summary>
        public bool AttachPlayer(MpvMediaPlayer player)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            return AttachMpv(player.MpvHandle);
        }

        /// <summary>
        /// Attaches this swap chain manager to a native mpv handle.
        /// </summary>
        public bool AttachMpv(IntPtr mpvHandle)
        {
            if (mpvHandle == IntPtr.Zero)
                throw new ArgumentException("Invalid mpv handle.", nameof(mpvHandle));

            lock (_renderLock)
            {
                if (_mpvHandle == mpvHandle && _renderContext != IntPtr.Zero)
                    return true;

                DetachPlayer();

                if (!IsInitialized)
                {
                    Debug.WriteLine("[D3D11SwapChainManager] Cannot attach player before swap chain initialization.");
                    return false;
                }

                _mpvHandle = mpvHandle;
                Debug.WriteLine($"[D3D11SwapChainManager] Attaching mpv handle=0x{mpvHandle.ToInt64():X}");

                // The software render API is the only one libmpv offers, so there is no Direct3D
                // render context to try first.
                int res = MpvRenderInterop.CreateSWContext(_mpvHandle, out _renderContext);
                if (res < 0 || _renderContext == IntPtr.Zero)
                {
                    Debug.WriteLine($"[D3D11SwapChainManager] CreateSWContext failed: {res}");
                    _mpvHandle = IntPtr.Zero;
                    return false;
                }

                Debug.WriteLine($"[D3D11SwapChainManager] CreateSWContext succeeded (software renderer): rc=0x{_renderContext.ToInt64():X}");

                // Register update callback
                _updateCallback = new MpvRenderUpdateCallback(OnMpvRenderUpdate);
                MpvRenderInterop.mpv_render_context_set_update_callback(_renderContext, _updateCallback, IntPtr.Zero);

                // Start dedicated render loop thread
                _disposed = false;
                _renderThread = new Thread(RenderLoop)
                {
                    Name = "Odeon.MpvRenderThread",
                    IsBackground = true
                };
                _renderThread.Start();

                _forceRender = true;
                _renderEvent.Set();

                // If media was already loaded or opening, ensure video track is active with the new render context
                try
                {
                    MpvInterop.SetPropertyString(_mpvHandle, "vid", "auto");
                }
                catch
                {
                }

                return true;
            }
        }

        /// <summary>
        /// Detaches the current mpv instance and stops the render loop.
        /// </summary>
        public void DetachPlayer()
        {
            IntPtr oldCtx = IntPtr.Zero;

            lock (_renderLock)
            {
                if (_renderContext == IntPtr.Zero)
                {
                    _mpvHandle = IntPtr.Zero;
                    return;
                }

                oldCtx = _renderContext;
                _renderContext = IntPtr.Zero;
                _mpvHandle = IntPtr.Zero;
            }

            // Signal thread to wake and exit
            _renderEvent.Set();

            if (_renderThread != null && _renderThread.IsAlive)
            {
                _renderThread.Join(500);
                _renderThread = null;
            }

            if (oldCtx != IntPtr.Zero)
            {
                try
                {
                    MpvRenderInterop.mpv_render_context_set_update_callback(oldCtx, null, IntPtr.Zero);
                    MpvRenderInterop.mpv_render_context_free(oldCtx);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[D3D11SwapChainManager] Exception freeing render context: {ex}");
                }
            }

            _updateCallback = null;
        }

        #endregion

        #region Render Loop

        private void OnMpvRenderUpdate(IntPtr cbCtx)
        {
            // mpv callback fires from a worker thread.
            // Never call mpv_render_* directly inside this callback!
            _renderEvent.Set();
        }

        private void RenderLoop()
        {
            while (!_disposed)
            {
                _renderEvent.WaitOne(100);
                if (_disposed) break;

                if (_renderContext == IntPtr.Zero || _swapChain == IntPtr.Zero || _backBuffer == IntPtr.Zero)
                    continue;

                // Apply any pending resize before rendering. This coalesces multiple rapid
                // SizeChanged/CompositionScaleChanged events into a single resize.
                bool resizePending = false;
                lock (_resizeLock)
                {
                    if (_pendingWidth > 0 && _pendingHeight > 0)
                    {
                        int curW = _pendingWidth;
                        int curH = _pendingHeight;
                        float curScaleX = _pendingScaleX;
                        float curScaleY = _pendingScaleY;
                        _pendingWidth = -1;
                        _pendingHeight = -1;
                        _pendingScaleX = 0f;
                        _pendingScaleY = 0f;

                        if (curW != _width || curH != _height)
                        {
                            _width = curW;
                            _height = curH;
                            SafeRelease(ref _backBuffer);
                            var resizeBuffers = GetVTableDelegate<ResizeBuffersDelegate>(_swapChain, Slot_IDXGISwapChain_ResizeBuffers);
                            int hr = resizeBuffers(_swapChain, 2, (uint)curW, (uint)curH, DXGI_FORMAT_B8G8R8A8_UNORM, 0);
                            if (hr >= 0)
                            {
                                Guid iidTex = IID_ID3D11Texture2D;
                                var getBuffer = GetVTableDelegate<GetBufferDelegate>(_swapChain, Slot_IDXGISwapChain_GetBuffer);
                                getBuffer(_swapChain, 0, ref iidTex, out _backBuffer);
                            }
                            else
                            {
                                Debug.WriteLine($"[D3D11SwapChainManager] ResizeBuffers failed: 0x{hr:X8}");
                            }
                            resizePending = true;
                        }

                        if (Math.Abs(_scaleX - curScaleX) > 0.001f || Math.Abs(_scaleY - curScaleY) > 0.001f)
                        {
                            _scaleX = curScaleX;
                            _scaleY = curScaleY;
                            resizePending = true;
                        }

                        if (resizePending)
                        {
                            ApplyInverseScale(_scaleX, _scaleY);
                            Debug.WriteLine($"[D3D11SwapChainManager] Resize (coalesced): physical {_width}x{_height}, scaleX={_scaleX:F2}, scaleY={_scaleY:F2}");
                        }
                    }
                }

                ulong flags = MpvRenderInterop.mpv_render_context_update(_renderContext);
                bool hasFrame = (flags & (ulong)MpvRenderUpdateFlag.Frame) != 0;

                if (hasFrame || _forceRender || resizePending)
                {
                    _forceRender = false;
                    lock (_renderLock)
                    {
                        if (_disposed || _renderContext == IntPtr.Zero || _backBuffer == IntPtr.Zero)
                            continue;

                        int curW = _width;
                        int curH = _height;
                        if (curW <= 0 || curH <= 0) continue;

                        // mpv renders into system memory, then the pixels are copied into the
                        // swap chain's back buffer for presentation.
                        int stride = curW * 4;
                        EnsurePixelBuffer(stride * curH);

                        int renderRes = MpvRenderInterop.RenderSW(_renderContext, _pixelBuffer, curW, curH, stride);
                        if (renderRes >= 0 && _d3d11Context != IntPtr.Zero && _backBuffer != IntPtr.Zero)
                        {
                            _swRenderFailureLogged = false;
                            var updateSub = GetVTableDelegate<UpdateSubresourceDelegate>(_d3d11Context, Slot_ID3D11DeviceContext_UpdateSubresource);
                            updateSub(_d3d11Context, _backBuffer, 0, IntPtr.Zero, _pixelBuffer, (uint)stride, 0);

                            DXGI_PRESENT_PARAMETERS presentParams = new DXGI_PRESENT_PARAMETERS();
                            var present1 = GetVTableDelegate<Present1Delegate>(_swapChain, Slot_IDXGISwapChain1_Present1);
                            int hr = present1(_swapChain, 1, 0, ref presentParams);
                            if (hr < 0)
                            {
                                var present = GetVTableDelegate<PresentDelegate>(_swapChain, Slot_IDXGISwapChain_Present);
                                present(_swapChain, 1, 0);
                            }

                            MpvRenderInterop.mpv_render_context_report_swap(_renderContext);
                        }
                        else if (renderRes < 0 && !_swRenderFailureLogged)
                        {
                            _swRenderFailureLogged = true;
                            Debug.WriteLine($"[D3D11SwapChainManager] RenderSW failed: {renderRes}");
                        }
                    }
                }
            }
        }

        #endregion

        #region Resizing and Presentation

        /// <summary>
        /// Applies an inverse scale matrix to the IDXGISwapChain2 so SwapChainPanel maps
        /// buffer pixels 1:1 with physical screen pixels without double scaling.
        /// </summary>
        private void ApplyInverseScale(float scaleX, float scaleY)
        {
            if (_swapChain == IntPtr.Zero || scaleX <= 0 || scaleY <= 0) return;

            IntPtr pSwapChain2 = IntPtr.Zero;
            try
            {
                Guid iidSC2 = IID_IDXGISwapChain2;
                var qi = GetVTableDelegate<QueryInterfaceDelegate>(_swapChain, Slot_IUnknown_QueryInterface);
                int hr = qi(_swapChain, ref iidSC2, out pSwapChain2);
                if (hr >= 0 && pSwapChain2 != IntPtr.Zero)
                {
                    DXGI_MATRIX_3X2_F matrix = new DXGI_MATRIX_3X2_F
                    {
                        _11 = 1.0f / scaleX,
                        _12 = 0.0f,
                        _21 = 0.0f,
                        _22 = 1.0f / scaleY,
                        _31 = 0.0f,
                        _32 = 0.0f
                    };

                    var setMatrix = GetVTableDelegate<SetMatrixTransformDelegate>(pSwapChain2, Slot_IDXGISwapChain2_SetMatrixTransform);
                    hr = setMatrix(pSwapChain2, ref matrix);
                    if (hr < 0)
                    {
                        Debug.WriteLine($"[D3D11SwapChainManager] SetMatrixTransform failed: 0x{hr:X8}");
                    }
                    else
                    {
                        Debug.WriteLine($"[D3D11SwapChainManager] SetMatrixTransform succeeded: scaleX={scaleX}, scaleY={scaleY}");
                    }
                }
                else
                {
                    Debug.WriteLine($"[D3D11SwapChainManager] QueryInterface IDXGISwapChain2 failed: 0x{hr:X8}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[D3D11SwapChainManager] ApplyInverseScale exception: {ex}");
            }
            finally
            {
                SafeRelease(ref pSwapChain2);
            }
        }

        /// <summary>
        /// Queues a resize request to be applied on the next render loop iteration.
        /// Multiple rapid requests (e.g. during DPI change, panel reload, window resize)
        /// are coalesced into a single swap chain resize to avoid redundant D3D11 reallocations.
        /// </summary>
        public void Resize(int width, int height, float scaleX = 0f, float scaleY = 0f)
        {
            if (width <= 0 || height <= 0 || _swapChain == IntPtr.Zero)
                return;

            lock (_resizeLock)
            {
                int newW = width;
                int newH = height;
                float newScaleX = scaleX > 0 ? scaleX : _scaleX;
                float newScaleY = scaleY > 0 ? scaleY : _scaleY;

                // Only queue if dimensions actually changed (ignore sub-pixel jitter)
                bool dimsChanged = (_pendingWidth != newW || _pendingHeight != newH);
                bool scaleChanged = (Math.Abs(_pendingScaleX - newScaleX) > 0.001f ||
                                     Math.Abs(_pendingScaleY - newScaleY) > 0.001f);

                if (dimsChanged || scaleChanged)
                {
                    _pendingWidth = newW;
                    _pendingHeight = newH;
                    _pendingScaleX = newScaleX;
                    _pendingScaleY = newScaleY;
                }
                else
                {
                    // Still wake render loop to ensure pending changes are picked up
                    _forceRender = true;
                    _renderEvent.Set();
                    return;
                }
            }

            _forceRender = true;
            _renderEvent.Set();
        }

        /// <summary>
        /// Clears the video surface to black when playback is stopped.
        /// </summary>
        public void Clear()
        {
            _forceRender = true;
            _renderEvent.Set();
        }

        #endregion

        #region Helpers & Cleanup

        private static T GetVTableDelegate<T>(IntPtr comPtr, int slot) where T : Delegate
        {
            IntPtr vtable = Marshal.ReadIntPtr(comPtr);
            IntPtr methodPtr = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
            return Marshal.GetDelegateForFunctionPointer<T>(methodPtr);
        }

        private static void SafeRelease(ref IntPtr comPtr)
        {
            if (comPtr != IntPtr.Zero)
            {
                Marshal.Release(comPtr);
                comPtr = IntPtr.Zero;
            }
        }

        private void EnsurePixelBuffer(int size)
        {
            if (_pixelBuffer != IntPtr.Zero && _pixelBufferSize >= size)
                return;

            if (_pixelBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_pixelBuffer);
                _pixelBuffer = IntPtr.Zero;
            }

            _pixelBuffer = Marshal.AllocHGlobal(size);
            _pixelBufferSize = size;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            DetachPlayer();

            lock (_renderLock)
            {
                SafeRelease(ref _backBuffer);
                SafeRelease(ref _swapChain);
                SafeRelease(ref _d3d11Context);
                SafeRelease(ref _d3d11Device);

                if (_pixelBuffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_pixelBuffer);
                    _pixelBuffer = IntPtr.Zero;
                    _pixelBufferSize = 0;
                }

                _panel = null;
            }

            _renderEvent.Dispose();
        }

        #endregion
    }
}
