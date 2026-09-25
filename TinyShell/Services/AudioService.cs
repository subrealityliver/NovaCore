using System.Runtime.InteropServices;

namespace TinyShell.Services;

/// <summary>Small Core Audio wrapper used by TinyShell's taskbar volume control.</summary>
public sealed class AudioService : IDisposable
{
    private const int CLSCTX_ALL = 23;
    private const int EDataFlowRender = 0;
    private const int ERoleMultimedia = 1;
    private readonly IAudioEndpointVolume _volume;

    public AudioService()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        enumerator.GetDefaultAudioEndpoint(EDataFlowRender, ERoleMultimedia, out var device);
        var iid = typeof(IAudioEndpointVolume).GUID;
        device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out _volume);
    }

    public float Volume
    {
        get { _volume.GetMasterVolumeLevelScalar(out var value); return Math.Clamp(value, 0, 1); }
        set { var ctx = Guid.Empty; _volume.SetMasterVolumeLevelScalar(Math.Clamp(value, 0, 1), ref ctx); }
    }

    public bool Muted
    {
        get { _volume.GetMute(out var value); return value; }
        set { var ctx = Guid.Empty; _volume.SetMute(value, ref ctx); }
    }

    public void Dispose() { if (_volume is IDisposable d) d.Dispose(); }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        int RegisterEndpointNotificationCallback(IntPtr callback);
        int UnregisterEndpointNotificationCallback(IntPtr callback);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, out IAudioEndpointVolume volume);
        int OpenPropertyStore(int access, out IntPtr properties);
        int GetId(out string id);
        int GetState(out int state);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr notify);
        int UnregisterControlChangeNotify(IntPtr notify);
        int GetChannelCount(out int channels);
        int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
        int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        int GetMasterVolumeLevel(out float levelDb);
        int GetMasterVolumeLevelScalar(out float level);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
        int GetMute(out bool mute);
        int GetVolumeRange(out float minDb, out float maxDb, out float incrementDb);
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }
}
