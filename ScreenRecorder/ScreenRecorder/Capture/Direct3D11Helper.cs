using System;
using System.Runtime.InteropServices;
using Windows.Graphics.DirectX.Direct3D11;

namespace ScreenRecorder.Capture;

/// <summary>
/// Helper that creates a WinRT <see cref="IDirect3DDevice"/> from a D3D11 device.
/// Bridges the gap between the native D3D11 COM interface and the WinRT surface.
/// </summary>
internal static class Direct3D11Helper
{
    // ── Native interop ────────────────────────────────────────────────────────

    [DllImport("d3d11.dll", ExactSpelling = true, PreserveSig = false)]
    private static extern void D3D11CreateDevice(
        IntPtr pAdapter,
        uint DriverType,        // D3D_DRIVER_TYPE_HARDWARE = 1
        IntPtr Software,
        uint Flags,
        IntPtr pFeatureLevels,
        uint FeatureLevels,
        uint SDKVersion,        // D3D11_SDK_VERSION = 7
        out IntPtr ppDevice,
        IntPtr pFeatureLevel,
        IntPtr ppImmediateContext);

    [DllImport("d3d11.dll", ExactSpelling = true, PreserveSig = false)]
    private static extern void CreateDirect3D11DeviceFromDXGIDevice(
        IntPtr dxgiDevice,
        out IntPtr graphicsDevice);

    private static readonly Guid IID_IDXGIDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");

    // ── Public factory ────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a hardware-accelerated <see cref="IDirect3DDevice"/> suitable
    /// for use with the Windows Graphics Capture API.
    /// </summary>
    public static IDirect3DDevice CreateDevice()
    {
        // 1 = D3D_DRIVER_TYPE_HARDWARE, 7 = D3D11_SDK_VERSION
        const uint D3D_DRIVER_TYPE_HARDWARE = 1;
        const uint D3D11_SDK_VERSION = 7;
        const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;

        D3D11CreateDevice(
            IntPtr.Zero,
            D3D_DRIVER_TYPE_HARDWARE,
            IntPtr.Zero,
            D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            IntPtr.Zero,
            0,
            D3D11_SDK_VERSION,
            out IntPtr d3dDevicePtr,
            IntPtr.Zero,
            IntPtr.Zero);

        // Wrap the raw D3D11 device pointer so it is released when we exit this scope.
        IntPtr d3dDevicePtr2 = d3dDevicePtr; // captured for the finally block
        try
        {
            // Query IDXGIDevice from the D3D11 device.
            int hr = Marshal.QueryInterface(d3dDevicePtr, ref IID_IDXGIDevice, out IntPtr dxgiPtr);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);

            CreateDirect3D11DeviceFromDXGIDevice(dxgiPtr, out IntPtr graphicsDevicePtr);
            Marshal.Release(dxgiPtr);

            var inspectable = Marshal.GetObjectForIUnknown(graphicsDevicePtr) as IDirect3DDevice
                ?? throw new InvalidOperationException("Could not create IDirect3DDevice.");

            Marshal.Release(graphicsDevicePtr);
            return inspectable;
        }
        finally
        {
            // Release the D3D11 device COM pointer now that the WinRT wrapper owns a reference.
            if (d3dDevicePtr2 != IntPtr.Zero)
                Marshal.Release(d3dDevicePtr2);
        }
    }
}
