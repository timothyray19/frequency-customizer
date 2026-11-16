using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace FrequencyCustomizer.Audio;

/// <summary>
/// Changes the Windows default playback device via the (undocumented but long-stable)
/// IPolicyConfig COM interface — the same one the Sound control panel uses.
/// </summary>
public static class DefaultDeviceSwitcher
{
    public static string? GetDefaultRenderId()
    {
        using var en = new MMDeviceEnumerator();
        try { return en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID; }
        catch { return null; }
    }

    /// <summary>Sets the device as default for normal apps (Console + Multimedia roles). Returns false on failure.</summary>
    public static bool SetDefault(string deviceId)
    {
        try
        {
            var policy = (IPolicyConfig)new PolicyConfigClient();
            try
            {
                int hr1 = policy.SetDefaultEndpoint(deviceId, 0 /* eConsole */);
                int hr2 = policy.SetDefaultEndpoint(deviceId, 1 /* eMultimedia */);
                return hr1 >= 0 && hr2 >= 0;
            }
            finally { Marshal.ReleaseComObject(policy); }
        }
        catch { return false; }
    }

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class PolicyConfigClient { }

    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, int isDefault, IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, int isDefault, IntPtr defaultPeriod, IntPtr minPeriod);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visible);
    }
}
