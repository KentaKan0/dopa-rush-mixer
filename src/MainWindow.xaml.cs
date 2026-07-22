using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;

namespace DopaRushMixer;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly AudioSessionService audioSessions = new();
    private readonly VivaldiBridgeService vivaldiBridge = new();
    private readonly OutputDeviceHistory outputDeviceHistory = new();
    private readonly DispatcherTimer refreshTimer;
    private readonly DispatcherTimer meterTimer;
    private readonly DispatcherTimer meterRenderTimer;
    private bool hasSessions;
    private bool isRefreshing;
    private bool isInteracting;
    private bool isUpdatingOutputDevices;
    private AudioOutputDevice? selectedOutputDevice;
    private string? outputDeviceError;
    private AudioSessionViewModel? vivaldiApplicationSession;
    private DateTime inputHoldUntilUtc;
    private readonly HashSet<int> pendingBrowserTabRemovals = new();
    private readonly HashSet<string> removedSessionKeys = new();
    private readonly Dictionary<string, DateTime> pendingSessionCandidates = new();

    public ObservableCollection<AudioSessionViewModel> Sessions { get; } = new();
    public ObservableCollection<BrowserTabViewModel> BrowserTabs { get; } = new();
    public ObservableCollection<DetectedBrowserTabViewModel> DetectedBrowserTabs { get; } = new();
    public ObservableCollection<AudioOutputDevice> OutputDevices { get; } = new();
    public AudioEndpointViewModel? EndpointVolume { get; private set; }
    public VivaldiMasterViewModel? VivaldiMaster { get; private set; }
    public ImageSource? VivaldiIcon => vivaldiApplicationSession?.AppIcon;
    public int DetectedBrowserTabCount => DetectedBrowserTabs.Count;
    public string WindowTitle => "DopaRush Mixer";

    public AudioOutputDevice? SelectedOutputDevice
    {
        get => selectedOutputDevice;
        set
        {
            if (selectedOutputDevice == value) return;
            selectedOutputDevice = value;
            OnPropertyChanged();
            if (!isUpdatingOutputDevices && value is not null)
            {
                try
                {
                    audioSessions.SetDefaultOutputDevice(value.Id);
                    audioSessions.SelectOutputDevice(value.Id);
                    outputDeviceHistory.Record(value.Id);
                    EndpointVolume = null;
                    OutputDeviceError = null;
                    RefreshSessions(force: true);
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    OutputDeviceError = "Windows の出力先を切り替えられませんでした。";
                    RefreshOutputDevices();
                }
            }
        }
    }

    public string? OutputDeviceError
    {
        get => outputDeviceError;
        private set { outputDeviceError = value; OnPropertyChanged(); }
    }

    public bool HasSessions
    {
        get => hasSessions;
        private set { hasSessions = value; OnPropertyChanged(); }
    }

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        refreshTimer.Tick += (_, _) => RefreshSessions();
        meterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        meterTimer.Tick += (_, _) => UpdateMeters();
        meterRenderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        meterRenderTimer.Tick += (_, _) => AnimateMeters();
        Loaded += (_, _) =>
        {
            vivaldiBridge.TabsChanged += VivaldiTabsChanged;
            vivaldiBridge.Start();
            RefreshSessions(force: true);
            refreshTimer.Start();
            meterTimer.Start();
            meterRenderTimer.Start();
        };
        Closed += (_, _) =>
        {
            refreshTimer.Stop();
            meterTimer.Stop();
            meterRenderTimer.Stop();
            vivaldiBridge.TabsChanged -= VivaldiTabsChanged;
            vivaldiBridge.Dispose();
            foreach (var session in Sessions) session.Dispose();
            vivaldiApplicationSession?.Dispose();
            audioSessions.Dispose();
        };
    }

    private void RefreshSessions(bool force = false)
    {
        if (!force && IsInputHeld()) return;
        isRefreshing = true;
        RefreshOutputDevices();
        var refreshedEndpoint = audioSessions.GetEndpointVolume();
        if (EndpointVolume is null)
        {
            EndpointVolume = refreshedEndpoint;
            OnPropertyChanged(nameof(EndpointVolume));
        }
        else
        {
            EndpointVolume.UpdateState(refreshedEndpoint.Volume, refreshedEndpoint.IsMuted);
        }
        var refreshed = audioSessions.GetSessions();
        var existing = Sessions.ToDictionary(session => session.SessionKey);
        var existingByAppIdentity = Sessions
            .GroupBy(session => session.AppIdentity, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var activeSessionKeys = refreshed.Select(session => session.SessionKey).ToHashSet();
        var activeAppIdentities = refreshed.Select(session => session.AppIdentity).ToHashSet(StringComparer.OrdinalIgnoreCase);
        removedSessionKeys.RemoveWhere(sessionKey => !activeSessionKeys.Contains(sessionKey));
        foreach (var staleCandidate in pendingSessionCandidates.Keys.Where(appIdentity => !activeAppIdentities.Contains(appIdentity)).ToArray())
            pendingSessionCandidates.Remove(staleCandidate);
        var hasVivaldiSession = false;
        foreach (var session in refreshed)
        {
            if (IsVivaldiSession(session))
            {
                if (!hasVivaldiSession)
                {
                    UpdateVivaldiApplicationSession(session);
                    hasVivaldiSession = true;
                }
                else
                {
                    session.Dispose();
                }
                continue;
            }

            if (removedSessionKeys.Contains(session.SessionKey))
            {
                session.Dispose();
                continue;
            }

            if (existing.Remove(session.SessionKey, out var retained))
            {
                retained.Update(session.Name, session.Detail, (float)(session.Volume / 100), session.IsMuted);
                session.Dispose();
            }
            else if (existingByAppIdentity.TryGetValue(session.AppIdentity, out retained))
            {
                retained.Rebind(session);
                session.Dispose();
            }
            else if (pendingSessionCandidates.TryGetValue(session.AppIdentity, out var firstSeenAt) && DateTime.UtcNow - firstSeenAt >= TimeSpan.FromSeconds(2))
            {
                pendingSessionCandidates.Remove(session.AppIdentity);
                Sessions.Add(session);
            }
            else
            {
                pendingSessionCandidates.TryAdd(session.AppIdentity, DateTime.UtcNow);
                session.Dispose();
            }
        }
        if (!hasVivaldiSession)
        {
            vivaldiApplicationSession?.Dispose();
            vivaldiApplicationSession = null;
            OnPropertyChanged(nameof(VivaldiIcon));
        }
        RefreshVivaldiMaster();
        HasSessions = Sessions.Count > 0;
        isRefreshing = false;
    }

    private void RefreshOutputDevices()
    {
        var devices = audioSessions.GetOutputDevices();
        isUpdatingOutputDevices = true;
        OutputDevices.Clear();
        foreach (var device in devices
            .OrderBy(device => outputDeviceHistory.GetRank(device.Id))
            .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            OutputDevices.Add(device);
        }
        var currentId = audioSessions.GetCurrentOutputDeviceId();
        SelectedOutputDevice = OutputDevices.FirstOrDefault(device => device.Id == currentId);
        isUpdatingOutputDevices = false;
    }

    private void UpdateMeters()
    {
        EndpointVolume?.UpdatePeak();
        foreach (var session in Sessions) session.UpdatePeak();
        vivaldiApplicationSession?.UpdatePeak();
    }

    private void AnimateMeters()
    {
        EndpointVolume?.AdvancePeakFrame();
        foreach (var session in Sessions) session.AdvancePeakFrame();
        vivaldiApplicationSession?.AdvancePeakFrame();
        VivaldiMaster?.AdvancePeakFrame();
    }

    private void VivaldiTabsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(RefreshBrowserTabs);

    private void RefreshBrowserTabs()
    {
        if (IsInputHeld()) return;
        isRefreshing = true;
        var existing = BrowserTabs.ToDictionary(tab => tab.TabId);
        var reportedTabs = vivaldiBridge.GetTabs();
        pendingBrowserTabRemovals.RemoveWhere(tabId => reportedTabs.All(tab => tab.TabId != tabId));
        RefreshVivaldiMaster();
        foreach (var tab in reportedTabs)
        {
            if (pendingBrowserTabRemovals.Contains(tab.TabId)) continue;
            if (existing.Remove(tab.TabId, out var retained)) retained.Update(tab);
            else BrowserTabs.Add(new BrowserTabViewModel(tab, vivaldiBridge));
        }
        foreach (var stale in existing.Values) BrowserTabs.Remove(stale);
        DetectedBrowserTabs.Clear();
        foreach (var tab in vivaldiBridge.GetDetectedTabs()) DetectedBrowserTabs.Add(new DetectedBrowserTabViewModel(tab));
        OnPropertyChanged(nameof(DetectedBrowserTabCount));
        isRefreshing = false;
    }

    private static bool IsVivaldiSession(AudioSessionViewModel session) =>
        string.Equals(session.ProcessName, "vivaldi", StringComparison.OrdinalIgnoreCase);

    private void UpdateVivaldiApplicationSession(AudioSessionViewModel session)
    {
        if (vivaldiApplicationSession is not null && vivaldiApplicationSession.SessionKey == session.SessionKey)
        {
            vivaldiApplicationSession.Update(session.Name, session.Detail, (float)(session.Volume / 100), session.IsMuted);
            session.Dispose();
            return;
        }

        vivaldiApplicationSession?.Dispose();
        vivaldiApplicationSession = session;
        OnPropertyChanged(nameof(VivaldiIcon));
    }

    private void RefreshVivaldiMaster()
    {
        var master = vivaldiBridge.GetMaster();
        if (VivaldiMaster is null) VivaldiMaster = new VivaldiMasterViewModel(master, vivaldiBridge, vivaldiApplicationSession);
        else VivaldiMaster.Update(master, vivaldiApplicationSession);
        OnPropertyChanged(nameof(VivaldiMaster));
    }

    private void RefreshClicked(object sender, RoutedEventArgs e) => RefreshSessions(force: true);

    private void ApplicationMenuClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu })
        {
            menu.PlacementTarget = sender as Button;
            menu.IsOpen = true;
        }
    }

    private void HeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void MinimizeClicked(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void CloseClicked(object sender, RoutedEventArgs e) => Close();

    private void InteractionStarted(object sender, System.Windows.Input.MouseButtonEventArgs e) => isInteracting = true;

    private void InteractionEnded(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        isInteracting = false;
        inputHoldUntilUtc = DateTime.UtcNow.AddMilliseconds(500);
    }

    private void InteractionCaptureLost(object sender, MouseEventArgs e)
    {
        isInteracting = false;
        inputHoldUntilUtc = DateTime.UtcNow.AddMilliseconds(500);
    }

    private void FaderMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (isRefreshing || sender is not Slider slider) return;
        inputHoldUntilUtc = DateTime.UtcNow.AddMilliseconds(1250);
        var step = e.Delta > 0 ? 1 : -1;
        slider.Value = Math.Clamp(slider.Value + step, slider.Minimum, slider.Maximum);
        e.Handled = true;
    }

    private bool IsInputHeld() =>
        isInteracting || DateTime.UtcNow < inputHoldUntilUtc || Keyboard.FocusedElement is Slider or ToggleButton;

    private void TopmostChanged(object sender, RoutedEventArgs e) =>
        Topmost = sender switch
        {
            MenuItem menuItem => menuItem.IsChecked,
            CheckBox checkBox => checkBox.IsChecked == true,
            _ => Topmost
        };

    private void VolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!isRefreshing && sender is Slider { DataContext: AudioSessionViewModel session }) session.SetVolume(e.NewValue);
    }

    private void MuteChanged(object sender, RoutedEventArgs e)
    {
        if (!isRefreshing && sender is ToggleButton { DataContext: AudioSessionViewModel session }) session.SetMute();
    }

    private void RemoveSessionClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AudioSessionViewModel session })
        {
            removedSessionKeys.Add(session.SessionKey);
            Sessions.Remove(session);
            session.Dispose();
            HasSessions = Sessions.Count > 0;
        }
    }

    private void MasterVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!isRefreshing && EndpointVolume is not null) EndpointVolume.SetVolume(e.NewValue);
    }

    private void MasterMuteChanged(object sender, RoutedEventArgs e)
    {
        if (!isRefreshing && EndpointVolume is not null) EndpointVolume.SetMute();
    }

    private void BrowserVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!isRefreshing && sender is Slider { DataContext: BrowserTabViewModel tab }) tab.SetVolume(e.NewValue);
    }

    private void BrowserMuteChanged(object sender, RoutedEventArgs e)
    {
        if (!isRefreshing && sender is ToggleButton { DataContext: BrowserTabViewModel tab }) tab.SetMute();
    }

    private void RemoveBrowserTabClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: BrowserTabViewModel tab })
        {
            vivaldiBridge.Enqueue(new BrowserCommand(tab.TabId, "remove-tab"));
            pendingBrowserTabRemovals.Add(tab.TabId);
            BrowserTabs.Remove(tab);
        }
    }

    private void VivaldiMasterVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!isRefreshing && VivaldiMaster is not null) VivaldiMaster.SetVolume(e.NewValue);
    }

    private void VivaldiMasterMuteChanged(object sender, RoutedEventArgs e)
    {
        if (!isRefreshing && VivaldiMaster is not null) VivaldiMaster.SetMute();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
