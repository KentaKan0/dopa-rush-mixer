using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DopaRushMixer;

public sealed class AudioEndpointViewModel : INotifyPropertyChanged
{
    private readonly IAudioEndpointVolume? volumeControl;
    private readonly IAudioMeterInformation? meterControl;
    private float volume;
    private bool isMuted;
    private double peakTargetPercent;

    internal AudioEndpointViewModel(IAudioEndpointVolume? volumeControl, IAudioMeterInformation? meterControl, float volume, bool isMuted)
    {
        this.volumeControl = volumeControl;
        this.meterControl = meterControl;
        this.volume = volume;
        this.isMuted = isMuted;
    }

    public double Volume { get => Math.Round(volume * 100); set { volume = (float)(value / 100); OnPropertyChanged(); OnPropertyChanged(nameof(VolumeText)); } }
    public string VolumeText => $"{Volume:0}%";
    public bool IsMuted { get => isMuted; set { isMuted = value; OnPropertyChanged(); } }
    public double PeakPercent { get; private set; }

    public void SetVolume(double value)
    {
        Volume = value;
        volumeControl?.SetMasterVolumeLevelScalar(volume, Guid.Empty);
    }

    public void SetMute() => volumeControl?.SetMute(IsMuted, Guid.Empty);

    internal void UpdateState(double newVolume, bool newIsMuted)
    {
        Volume = newVolume;
        IsMuted = newIsMuted;
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

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
