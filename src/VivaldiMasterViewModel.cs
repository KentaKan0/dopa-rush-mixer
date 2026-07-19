using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DopaRushMixer;

public sealed class VivaldiMasterViewModel : INotifyPropertyChanged
{
    private readonly VivaldiBridgeService? bridge;
    private AudioSessionViewModel? applicationSession;
    private double volume;
    private bool isMuted;

    internal VivaldiMasterViewModel(BrowserMasterReport master, VivaldiBridgeService? bridge, AudioSessionViewModel? applicationSession = null)
    {
        this.bridge = bridge;
        Update(master, applicationSession);
    }

    public double Volume { get => volume; set { volume = value; OnPropertyChanged(); OnPropertyChanged(nameof(VolumeText)); } }
    public string VolumeText => $"{Volume:0}%";
    public bool IsMuted { get => isMuted; set { isMuted = value; OnPropertyChanged(); } }
    public double PeakPercent { get; private set; }
    public string SourceLabel => applicationSession is null ? "Vivaldi を待機中" : "Windows の Vivaldi 音声セッションに接続中";

    public void SetVolume(double value)
    {
        Volume = value;
        if (applicationSession is not null) applicationSession.SetVolume(value);
        bridge?.Enqueue(new BrowserCommand(0, "set-master-volume", Volume: value));
    }

    public void SetMute()
    {
        if (applicationSession is not null)
        {
            applicationSession.IsMuted = IsMuted;
            applicationSession.SetMute();
        }
        bridge?.Enqueue(new BrowserCommand(0, "set-master-muted", IsMuted: IsMuted));
    }

    internal void Update(BrowserMasterReport master, AudioSessionViewModel? newApplicationSession)
    {
        applicationSession = newApplicationSession;
        Volume = applicationSession?.Volume ?? master.Volume;
        IsMuted = applicationSession?.IsMuted ?? master.IsMuted;
        PeakPercent = applicationSession?.PeakPercent ?? master.Peak;
        OnPropertyChanged(nameof(PeakPercent));
        OnPropertyChanged(nameof(SourceLabel));
    }

    internal void UpdatePeak()
    {
        if (applicationSession is null) return;
        PeakPercent = applicationSession.PeakPercent;
        OnPropertyChanged(nameof(PeakPercent));
    }

    internal void AdvancePeakFrame()
    {
        if (applicationSession is null) return;
        PeakPercent = applicationSession.PeakPercent;
        OnPropertyChanged(nameof(PeakPercent));
    }

    internal void UpdatePeak(double bridgePeak)
    {
        PeakPercent = applicationSession?.PeakPercent ?? bridgePeak;
        OnPropertyChanged(nameof(PeakPercent));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
