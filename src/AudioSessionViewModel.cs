using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DopaRushMixer;

public sealed class AudioSessionViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IAudioSessionControl? sessionControl;
    private readonly ISimpleAudioVolume? volumeControl;
    private readonly IAudioMeterInformation? meterControl;
    private float volume;
    private bool isMuted;
    private double peakTargetPercent;

    internal AudioSessionViewModel(string sessionKey, uint processId, string processName, string name, string detail, IAudioSessionControl? sessionControl, ISimpleAudioVolume? volumeControl, IAudioMeterInformation? meterControl, float volume, bool isMuted)
    {
        SessionKey = sessionKey;
        ProcessName = processName;
        AppIcon = AppIconFactory.GetForProcess(processId);
        Name = name;
        Detail = detail;
        this.sessionControl = sessionControl;
        this.volumeControl = volumeControl;
        this.meterControl = meterControl;
        this.volume = volume;
        this.isMuted = isMuted;
    }

    public string SessionKey { get; }
    public string ProcessName { get; }
    public string AppIdentity => string.Equals(ProcessName, "Unknown", StringComparison.OrdinalIgnoreCase) ? Name : ProcessName;
    public ImageSource? AppIcon { get; }
    public string Name { get; private set; }
    public string Detail { get; private set; }
    public double Volume { get => Math.Round(volume * 100); set { volume = (float)(value / 100); OnPropertyChanged(); OnPropertyChanged(nameof(VolumeText)); } }
    public string VolumeText => $"{Volume:0}%";
    public bool IsMuted { get => isMuted; set { isMuted = value; OnPropertyChanged(); } }
    public double PeakPercent { get; private set; }

    public void SetVolume(double value)
    {
        Volume = value;
        try { volumeControl?.SetMasterVolume(volume, Guid.Empty); }
        catch (System.Runtime.InteropServices.COMException) { }
    }

    public void SetMute()
    {
        try { volumeControl?.SetMute(IsMuted, Guid.Empty); }
        catch (System.Runtime.InteropServices.COMException) { }
    }

    internal void Update(string name, string detail, float newVolume, bool newIsMuted)
    {
        Name = name;
        Detail = detail;
        Volume = Math.Round(newVolume * 100);
        IsMuted = newIsMuted;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Detail));
    }

    public void UpdatePeak()
    {
        if (meterControl is null) return;
        try
        {
            meterControl.GetPeakValue(out var peak);
            peakTargetPercent = AudioLevel.DecayTarget(peakTargetPercent, AudioLevel.ToDisplayPercent(peak));
        }
        catch (System.Runtime.InteropServices.COMException) { }
    }

    public void AdvancePeakFrame()
    {
        var nextPeak = AudioLevel.Interpolate(PeakPercent, peakTargetPercent);
        if (Math.Abs(nextPeak - PeakPercent) < 0.05) return;
        PeakPercent = nextPeak;
        OnPropertyChanged(nameof(PeakPercent));
    }

    public void Dispose()
    {
        if (sessionControl is not null) System.Runtime.InteropServices.Marshal.ReleaseComObject(sessionControl);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
