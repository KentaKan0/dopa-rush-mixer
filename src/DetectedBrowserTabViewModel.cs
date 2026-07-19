namespace DopaRushMixer;

public sealed class DetectedBrowserTabViewModel
{
    internal DetectedBrowserTabViewModel(DetectedBrowserTab tab)
    {
        Title = tab.Title;
    }

    public string Title { get; }
    public string Instruction => "Vivaldi の拡張機能アイコンをクリックして有効化";
}
