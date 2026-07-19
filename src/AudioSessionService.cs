using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DopaRushMixer;

public sealed class AudioSessionService : IDisposable
{
    private static readonly HashSet<string> IgnoredProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "nvcontainer"
    };
    private readonly IMMDeviceEnumerator deviceEnumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
    private IAudioEndpointVolume? endpointVolume;
    private IAudioMeterInformation? endpointMeter;

    public string? SelectedDeviceId { get; private set; }

    public IReadOnlyList<AudioOutputDevice> GetOutputDevices()
    {
        deviceEnumerator.EnumAudioEndpoints(EDataFlow.eRender, DeviceState.Active, out var collection);
        try
        {
            collection.GetCount(out var count);
            var devices = new List<AudioOutputDevice>(count);
            for (var index = 0; index < count; index++)
            {
                collection.Item(index, out var device);
                try
                {
                    device.GetId(out var id);
                    devices.Add(new AudioOutputDevice(id, GetDeviceName(device) ?? id));
                }
                finally { Marshal.ReleaseComObject(device); }
            }
            return devices;
        }
        finally { Marshal.ReleaseComObject(collection); }
    }

    public void SelectOutputDevice(string? deviceId)
    {
        if (SelectedDeviceId == deviceId) return;
        SelectedDeviceId = deviceId;
        ReleaseEndpointInterfaces();
    }

    public void SetDefaultOutputDevice(string deviceId)
    {
        var policyConfig = (IPolicyConfigVista)new PolicyConfigVistaClient();
        try
        {
            policyConfig.SetDefaultEndpoint(deviceId, ERole.eConsole);
            policyConfig.SetDefaultEndpoint(deviceId, ERole.eMultimedia);
            policyConfig.SetDefaultEndpoint(deviceId, ERole.eCommunications);
        }
        finally { Marshal.ReleaseComObject(policyConfig); }
    }

    public string GetCurrentOutputDeviceId()
    {
        var device = GetOutputDevice();
        try
        {
            device.GetId(out var id);
            return id;
        }
        finally { Marshal.ReleaseComObject(device); }
    }

    public AudioEndpointViewModel GetEndpointVolume()
    {
        if (endpointVolume is null)
        {
            var device = GetOutputDevice();
            try
            {
                var endpointVolumeId = typeof(IAudioEndpointVolume).GUID;
                device.Activate(ref endpointVolumeId, CLSCTX.ALL, IntPtr.Zero, out var endpointVolumeObject);
                endpointVolume = (IAudioEndpointVolume)endpointVolumeObject;
                endpointMeter = (IAudioMeterInformation)endpointVolume;
            }
            finally { Marshal.ReleaseComObject(device); }
        }

        endpointVolume.GetMasterVolumeLevelScalar(out var level);
        endpointVolume.GetMute(out var muted);
        return new AudioEndpointViewModel(endpointVolume, endpointMeter, level, muted);
    }

    public IReadOnlyList<AudioSessionViewModel> GetSessions()
    {
        var sessions = new List<AudioSessionViewModel>();

        var device = GetOutputDevice();
        try
        {
            var managerId = typeof(IAudioSessionManager2).GUID;
            device.Activate(ref managerId, CLSCTX.ALL, IntPtr.Zero, out var managerObject);
            var manager = (IAudioSessionManager2)managerObject;
            try
            {
                manager.GetSessionEnumerator(out var enumerator);
                try
                {
                    enumerator.GetCount(out var count);
                    for (var index = 0; index < count; index++) AddSession(enumerator, index, sessions);
                }
                finally { Marshal.ReleaseComObject(enumerator); }
            }
            finally { Marshal.ReleaseComObject(manager); }
        }
        finally { Marshal.ReleaseComObject(device); }
        return sessions;
    }

    private static void AddSession(IAudioSessionEnumerator enumerator, int index, ICollection<AudioSessionViewModel> sessions)
    {
        enumerator.GetSession(index, out var control);
        var keepControl = false;
        try
        {
            control.GetState(out var state);
            if (state != AudioSessionState.Active) return;

            var control2 = (IAudioSessionControl2)control;
            control2.GetProcessId(out var processId);
            control2.GetSessionIdentifier(out var sessionId);
            control2.GetSessionInstanceIdentifier(out var instanceId);
            var processName = GetProcessName(processId);
            if (IgnoredProcessNames.Contains(processName)) return;
            var volume = (ISimpleAudioVolume)control;
            var meter = (IAudioMeterInformation)control;
            volume.GetMasterVolume(out var level);
            volume.GetMute(out var muted);
            sessions.Add(new AudioSessionViewModel($"{sessionId}|{instanceId}", processId, processName, GetSessionName(control, processId, processName), GetSessionDetail(processId, instanceId), control, volume, meter, level, muted));
            keepControl = true;
        }
        finally { if (!keepControl) Marshal.ReleaseComObject(control); }
    }

    private static string GetSessionDetail(uint processId, string instanceId)
    {
        var suffix = instanceId.Length > 12 ? instanceId[^12..] : instanceId;
        return $"PID {processId} ・ セッション {suffix}";
    }

    private IMMDevice GetOutputDevice()
    {
        if (!string.IsNullOrWhiteSpace(SelectedDeviceId))
        {
            try
            {
                deviceEnumerator.GetDevice(SelectedDeviceId, out var device);
                return device;
            }
            catch (COMException)
            {
                SelectedDeviceId = null;
            }
        }

        deviceEnumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var defaultDevice);
        return defaultDevice;
    }

    private static string? GetDeviceName(IMMDevice device)
    {
        device.OpenPropertyStore(0, out var properties);
        try
        {
            var key = new PropertyKey(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);
            properties.GetValue(ref key, out var value);
            try { return value.GetString(); }
            finally { value.Dispose(); }
        }
        finally { Marshal.ReleaseComObject(properties); }
    }

    private void ReleaseEndpointInterfaces()
    {
        endpointMeter = null;
        if (endpointVolume is null) return;
        Marshal.ReleaseComObject(endpointVolume);
        endpointVolume = null;
    }

    private static string GetProcessName(uint processId)
    {
        if (processId == 0) return "system";
        try { return Process.GetProcessById((int)processId).ProcessName; }
        catch (ArgumentException) { return $"app-{processId}"; }
    }

    private static string GetSessionName(IAudioSessionControl control, uint processId, string processName)
    {
        control.GetDisplayName(out var displayName);
        var displayNameText = Marshal.PtrToStringUni(displayName);
        try
        {
            if (!string.IsNullOrWhiteSpace(displayNameText)) return displayNameText;
            if (processId == 0) return "システム サウンド";
            return processName;
        }
        finally { if (displayName != IntPtr.Zero) Marshal.FreeCoTaskMem(displayName); }
    }

    public void Dispose()
    {
        ReleaseEndpointInterfaces();
        Marshal.ReleaseComObject(deviceEnumerator);
    }
}

internal enum EDataFlow { eRender, eCapture, eAll }
internal enum ERole { eConsole, eMultimedia, eCommunications }
internal enum AudioSessionState { Inactive, Active, Expired }
internal static class DeviceState { public const uint Active = 0x00000001; }

[Flags] internal enum CLSCTX { INPROC_SERVER = 1, INPROC_HANDLER = 2, LOCAL_SERVER = 4, REMOTE_SERVER = 16, ALL = INPROC_SERVER | INPROC_HANDLER | LOCAL_SERVER | REMOTE_SERVER }

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] internal class MMDeviceEnumeratorComObject { }
[ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")] internal class PolicyConfigClient { }
[ComImport, Guid("294935CE-F637-4E7C-A41B-AB255460B862")] internal class PolicyConfigVistaClient { }

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    void EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IMMDeviceCollection devices);
    void GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
    void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    void RegisterEndpointNotificationCallback(IntPtr client);
    void UnregisterEndpointNotificationCallback(IntPtr client);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    void Activate(ref Guid interfaceId, CLSCTX classContext, IntPtr activationParameters, [MarshalAs(UnmanagedType.IUnknown)] out object interfaceObject);
    void OpenPropertyStore(uint access, out IPropertyStore properties);
    void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    void GetState(out uint state);
}

[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    void GetCount(out int deviceCount);
    void Item(int deviceNumber, out IMMDevice device);
}

[ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    void GetCount(out uint propertyCount);
    void GetAt(uint propertyIndex, out PropertyKey key);
    void GetValue(ref PropertyKey key, out PropVariant value);
    void SetValue(ref PropertyKey key, ref PropVariant value);
    void Commit();
}

[ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2
{
    void GetAudioSessionControl(ref Guid groupingParameter, uint streamFlags, out IAudioSessionControl sessionControl);
    void GetSimpleAudioVolume(ref Guid groupingParameter, uint streamFlags, out ISimpleAudioVolume audioVolume);
    void GetSessionEnumerator(out IAudioSessionEnumerator sessionEnumerator);
    void RegisterSessionNotification(IntPtr sessionNotification);
    void UnregisterSessionNotification(IntPtr sessionNotification);
    void RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionId, IntPtr duckNotification);
    void UnregisterDuckNotification(IntPtr duckNotification);
}

[ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumerator
{
    void GetCount(out int sessionCount);
    void GetSession(int sessionCount, out IAudioSessionControl session);
}

[ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl
{
    void GetState(out AudioSessionState state);
    void GetDisplayName(out IntPtr displayName);
    void SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string displayName, Guid eventContext);
    void GetIconPath(out IntPtr iconPath);
    void SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string iconPath, Guid eventContext);
    void GetGroupingParam(out Guid groupingId);
    void SetGroupingParam(Guid groupingId, Guid eventContext);
    void RegisterAudioSessionNotification(IntPtr client);
    void UnregisterAudioSessionNotification(IntPtr client);
}

[ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2 : IAudioSessionControl
{
    new void GetState(out AudioSessionState state);
    new void GetDisplayName(out IntPtr displayName);
    new void SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string displayName, Guid eventContext);
    new void GetIconPath(out IntPtr iconPath);
    new void SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string iconPath, Guid eventContext);
    new void GetGroupingParam(out Guid groupingId);
    new void SetGroupingParam(Guid groupingId, Guid eventContext);
    new void RegisterAudioSessionNotification(IntPtr client);
    new void UnregisterAudioSessionNotification(IntPtr client);
    void GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string sessionIdentifier);
    void GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string sessionInstanceIdentifier);
    void GetProcessId(out uint processId);
    void IsSystemSoundsSession();
    void SetDuckingPreference(bool optOut);
}

[ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISimpleAudioVolume
{
    void SetMasterVolume(float level, Guid eventContext);
    void GetMasterVolume(out float level);
    void SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, Guid eventContext);
    void GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
}

[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    void RegisterControlChangeNotify(IntPtr notify);
    void UnregisterControlChangeNotify(IntPtr notify);
    void GetChannelCount(out uint channelCount);
    void SetMasterVolumeLevel(float levelDb, Guid eventContext);
    void SetMasterVolumeLevelScalar(float level, Guid eventContext);
    void GetMasterVolumeLevel(out float levelDb);
    void GetMasterVolumeLevelScalar(out float level);
    void SetChannelVolumeLevel(uint channel, float levelDb, Guid eventContext);
    void SetChannelVolumeLevelScalar(uint channel, float level, Guid eventContext);
    void GetChannelVolumeLevel(uint channel, out float levelDb);
    void GetChannelVolumeLevelScalar(uint channel, out float level);
    void SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, Guid eventContext);
    void GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
    void GetVolumeStepInfo(out uint step, out uint stepCount);
    void VolumeStepUp(Guid eventContext);
    void VolumeStepDown(Guid eventContext);
    void QueryHardwareSupport(out uint hardwareSupportMask);
    void GetVolumeRange(out float volumeMinDb, out float volumeMaxDb, out float volumeIncrementDb);
}

[ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioMeterInformation
{
    void GetPeakValue(out float peak);
    void GetMeteringChannelCount(out int channelCount);
    void GetChannelsPeakValues(int channelCount, out float peakValues);
    void QueryHardwareSupport(out int hardwareSupportMask);
}

[ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    void GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, out IntPtr format);
    void GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool defaultFormat, out IntPtr format);
    void SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr endpointFormat, IntPtr mixFormat);
    void GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool defaultPeriod, out long defaultPeriodValue, out long minimumPeriod);
    void SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ref long period);
    void GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, out IntPtr mode);
    void SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);
    void GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool defaultValue, ref PropertyKey key, out PropVariant value);
    void SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool defaultValue, ref PropertyKey key, ref PropVariant value);
    void SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
    void SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool visible);
}

[ComImport, Guid("568B9108-44BF-40B4-9006-86AFE5B5A620"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfigVista
{
    void GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, out IntPtr format);
    void GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool defaultFormat, out IntPtr format);
    void SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr endpointFormat, IntPtr mixFormat);
    void GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool defaultPeriod, out long defaultPeriodValue, out long minimumPeriod);
    void SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ref long period);
    void GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, out IntPtr mode);
    void SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);
    void GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ref PropertyKey key, out PropVariant value);
    void SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ref PropertyKey key, ref PropVariant value);
    void SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
    void SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool visible);
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey
{
    public PropertyKey(Guid formatId, uint propertyId) { FormatId = formatId; PropertyId = propertyId; }
    public Guid FormatId;
    public uint PropertyId;
}

[StructLayout(LayoutKind.Explicit)]
internal struct PropVariant
{
    [FieldOffset(0)] private ushort valueType;
    [FieldOffset(8)] private IntPtr pointerValue;

    public string? GetString() => valueType == 31 ? Marshal.PtrToStringUni(pointerValue) : null;
    public void Dispose() => PropVariantClear(ref this);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);
}
