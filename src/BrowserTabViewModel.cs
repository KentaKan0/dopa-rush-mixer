using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DopaRushMixer;

public sealed class BrowserTabViewModel : INotifyPropertyChanged
{
    private readonly VivaldiBridgeService? bridge;
    private double volume;
    private bool isMuted;
    private Uri? favIconUri;

    internal BrowserTabViewModel(BrowserTabReport tab, VivaldiBridgeService? bridge)
    {
        TabId = tab.TabId;
        Title = tab.Title;
        this.bridge = bridge;
        volume = tab.Volume;
        isMuted = tab.IsMuted;
        IsAudible = tab.IsAudible;
        PeakPercent = tab.Peak;
        favIconUri = ToFavIconUri(tab.FavIconUrl);
    }

    public int TabId { get; }
    public string Title { get; private set; }
    public bool IsAudible { get; private set; }
    public double Volume { get => volume; set { volume = value; OnPropertyChanged(); OnPropertyChanged(nameof(VolumeText)); } }
    public string VolumeText => $"{Volume:0}%";
    public bool IsMuted { get => isMuted; set { isMuted = value; OnPropertyChanged(); } }
    public double PeakPercent { get; private set; }
    public Uri? FavIconUri
    {
        get => favIconUri;
        private set
        {
            if (Equals(favIconUri, value)) return;
            favIconUri = value;
            OnPropertyChanged();
        }
    }

    public void SetVolume(double value)
    {
        Volume = value;
        bridge?.Enqueue(new BrowserCommand(TabId, "set-volume", Volume: value));
    }

    public void SetMute() => bridge?.Enqueue(new BrowserCommand(TabId, "set-muted", IsMuted: IsMuted));

    internal void Update(BrowserTabReport tab)
    {
        Title = tab.Title;
        IsAudible = tab.IsAudible;
        Volume = tab.Volume;
        IsMuted = tab.IsMuted;
        PeakPercent = tab.Peak;
        FavIconUri = ToFavIconUri(tab.FavIconUrl);
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(IsAudible));
        OnPropertyChanged(nameof(PeakPercent));
    }

    internal void UpdatePeak(double peak)
    {
        PeakPercent = peak;
        OnPropertyChanged(nameof(PeakPercent));
    }

    private static Uri? ToFavIconUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) ? uri : null;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
