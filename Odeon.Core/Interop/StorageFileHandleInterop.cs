#nullable enable

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Odeon.Core.Interop
{
    /// <summary>
    /// Provides a Win32 HANDLE to a StorageFile that was already granted access
    /// by UWP (e.g. via FileOpenPicker). This bypasses the AppContainer sandbox
    /// restriction while still respecting UWP broker-granted file access.
    /// Works for local drives, removable storage, AND mapped network drives.
    ///
    /// Uses Marshal.QueryInterface + vtable call because WinRT RCWs in UWP do not
    /// support the as cast for [ComImport] interfaces registered outside WinRT metadata.
    /// </summary>
    internal static class StorageFileHandleInterop
    {
        // IStorageItemHandleAccess (windows.storage.h)
        // GUID: 5CA296B2-2C25-4D22-B785-B885C8201E6A
        // Vtable layout (inherits IUnknown):
        //   [0] QueryInterface
        //   [1] AddRef
        //   [2] Release
        //   [3] Create(accessOptions, sharingOptions, options, oplockHandler, out HANDLE)
        private static readonly Guid IID_IStorageItemHandleAccess =
            new Guid("5CA296B2-2C25-4D22-B785-B885C8201E6A");

        // HANDLE_ACCESS_OPTIONS enum values (NOT Win32 GENERIC_* flags!)
        // From Windows SDK windows.storage.h
        private const uint HAO_NONE            = 0x0;
        private const uint HAO_READ_ATTRIBUTES = 0x80;
        private const uint HAO_READ            = 0x120089;
        private const uint HAO_WRITE           = 0x120116;
        private const uint HAO_DELETE          = 0x10000;

        // HANDLE_SHARING_OPTIONS enum values
        private const uint HSO_SHARE_NONE   = 0x0;
        private const uint HSO_SHARE_READ   = 0x1;
        private const uint HSO_SHARE_WRITE  = 0x2;
        private const uint HSO_SHARE_DELETE = 0x4;

        // HANDLE_OPTIONS enum values
        private const uint HO_NONE           = 0x0;
        private const uint HO_SEQUENTIAL_SCAN = 0x8000000;  // optimize for sequential read

        // Delegate matching the Create vtable slot (stdcall, thisPtr first)
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateHandleDelegate(
            IntPtr thisPtr,
            uint   accessOptions,
            uint   sharingOptions,
            uint   options,
            IntPtr oplockBreakingHandler,
            out    IntPtr interopHandle);

        // _open_osfhandle: converts a Win32 HANDLE to a CRT file descriptor (POSIX fd)
        // Use api-ms-win-crt-stdio instead of ucrtbase.dll — the latter is not allowed
        // in UWP Release builds (.NET Native AOT) and throws EntryPointNotFoundException.
        [DllImport("api-ms-win-crt-stdio-l1-1-0.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int _open_osfhandle(IntPtr osfHandle, int flags);

        // CloseHandle: closes a Win32 HANDLE if fd conversion fails
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const int _O_RDONLY = 0x0000;

        /// <summary>
        /// Opens a Win32 file descriptor for a UWP-accessible StorageFile.
        /// Returns a non-negative fd on success (mpv closes it via fdclose://).
        /// Returns -1 on any failure.
        /// </summary>
        public static int OpenFileDescriptor(Windows.Storage.IStorageFile file)
        {
            IntPtr unknown         = IntPtr.Zero;
            IntPtr handleAccessPtr = IntPtr.Zero;
            IntPtr fileHandle      = IntPtr.Zero;

            try
            {
                // Step 1: Get IUnknown from the WinRT RCW
                unknown = Marshal.GetIUnknownForObject(file);
                if (unknown == IntPtr.Zero)
                {
                    Debug.WriteLine("[StorageFileHandleInterop] GetIUnknownForObject returned null.");
                    return -1;
                }

                // Step 2: QueryInterface for IStorageItemHandleAccess
                Guid iid = IID_IStorageItemHandleAccess;
                int  hrQI = Marshal.QueryInterface(unknown, ref iid, out handleAccessPtr);
                if (hrQI != 0 || handleAccessPtr == IntPtr.Zero)
                {
                    Debug.WriteLine($"[StorageFileHandleInterop] QI failed hr=0x{hrQI:X8}");
                    return -1;
                }

                // Step 3: Call Create via vtable slot 3
                //   Slot 0 = QueryInterface, 1 = AddRef, 2 = Release, 3 = Create
                IntPtr vtable      = Marshal.ReadIntPtr(handleAccessPtr);
                IntPtr createFnPtr = Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size);
                var    createFn    = Marshal.GetDelegateForFunctionPointer<CreateHandleDelegate>(createFnPtr);

                int hrCreate = createFn(
                    handleAccessPtr,
                    HAO_READ,                                        // read access
                    HSO_SHARE_READ | HSO_SHARE_WRITE | HSO_SHARE_DELETE, // share all
                    HO_SEQUENTIAL_SCAN,                              // hint: sequential video read
                    IntPtr.Zero,                                     // no oplock handler
                    out fileHandle);

                if (hrCreate != 0 || fileHandle == IntPtr.Zero || fileHandle == new IntPtr(-1))
                {
                    Debug.WriteLine($"[StorageFileHandleInterop] Create failed hr=0x{hrCreate:X8}");
                    return -1;
                }

                // Step 4: Convert Win32 HANDLE -> POSIX fd for mpv fdclose:// protocol
                int fd = _open_osfhandle(fileHandle, _O_RDONLY);
                if (fd >= 0)
                {
                    fileHandle = IntPtr.Zero; // fd now owns the handle; skip CloseHandle
                    Debug.WriteLine($"[StorageFileHandleInterop] Success: fd={fd}");
                }
                else
                {
                    Debug.WriteLine("[StorageFileHandleInterop] _open_osfhandle failed.");
                }
                return fd;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StorageFileHandleInterop] Exception: {ex.GetType().Name}: {ex.Message}");
                return -1;
            }
            finally
            {
                // Only close the HANDLE if the fd conversion failed (fd = -1)
                if (fileHandle != IntPtr.Zero && fileHandle != new IntPtr(-1))
                    CloseHandle(fileHandle);

                if (handleAccessPtr != IntPtr.Zero)
                    Marshal.Release(handleAccessPtr);

                if (unknown != IntPtr.Zero)
                    Marshal.Release(unknown);
            }
        }
    }
}
