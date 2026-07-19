using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;

namespace DopaRushMixer;

public sealed class VivaldiBridgeService : IDisposable
{
    private const string Prefix = "http://127.0.0.1:32145/";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpListener listener = new();
    private readonly ConcurrentDictionary<int, BrowserTabReport> tabs = new();
    private readonly ConcurrentDictionary<int, DetectedBrowserTab> detectedTabs = new();
    private readonly ConcurrentQueue<BrowserCommand> commands = new();
    private readonly CancellationTokenSource cancellation = new();
    private BrowserMasterReport master = new(100, false, 0);

    public event EventHandler? TabsChanged;

    public void Start()
    {
        listener.Prefixes.Add(Prefix);
        listener.Start();
        _ = Task.Run(ListenAsync);
    }

    public IReadOnlyList<BrowserTabReport> GetTabs() => tabs.Values.OrderBy(tab => tab.Title).ToList();

    public IReadOnlyList<DetectedBrowserTab> GetDetectedTabs() => detectedTabs.Values.OrderBy(tab => tab.Title).ToList();

    public BrowserMasterReport GetMaster() => master;

    public void Enqueue(BrowserCommand command) => commands.Enqueue(command);

    private async Task ListenAsync()
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                var context = await listener.GetContextAsync().WaitAsync(cancellation.Token);
                _ = Task.Run(() => HandleAsync(context));
            }
        }
        catch (OperationCanceledException) { }
        catch (HttpListenerException) when (cancellation.IsCancellationRequested) { }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        context.Response.Headers["Access-Control-Allow-Origin"] = "*";
        context.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
        context.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type";

        if (context.Request.HttpMethod == "OPTIONS")
        {
            context.Response.StatusCode = (int)HttpStatusCode.NoContent;
            context.Response.Close();
            return;
        }

        if (context.Request.HttpMethod == "POST" && context.Request.Url?.AbsolutePath == "/bridge/tabs")
        {
            using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
            var report = JsonSerializer.Deserialize<BrowserTabsPayload>(await reader.ReadToEndAsync(), JsonOptions);
            if (report is not null)
            {
                tabs.Clear();
                foreach (var tab in report.Tabs) tabs[tab.TabId] = tab;
                master = new BrowserMasterReport(report.MasterVolume, report.MasterMuted, report.MasterPeak);
                TabsChanged?.Invoke(this, EventArgs.Empty);
            }
            await WriteJsonAsync(context, new { ok = true });
            return;
        }

        if (context.Request.HttpMethod == "POST" && context.Request.Url?.AbsolutePath == "/bridge/detected-tabs")
        {
            using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
            var report = JsonSerializer.Deserialize<DetectedBrowserTabsPayload>(await reader.ReadToEndAsync(), JsonOptions);
            if (report is not null)
            {
                detectedTabs.Clear();
                foreach (var tab in report.Tabs) detectedTabs[tab.TabId] = tab;
                TabsChanged?.Invoke(this, EventArgs.Empty);
            }
            await WriteJsonAsync(context, new { ok = true });
            return;
        }

        if (context.Request.HttpMethod == "GET" && context.Request.Url?.AbsolutePath == "/bridge/commands")
        {
            var pending = new List<BrowserCommand>();
            while (commands.TryDequeue(out var command)) pending.Add(command);
            await WriteJsonAsync(context, new BrowserCommandsPayload(pending));
            return;
        }

        context.Response.StatusCode = (int)HttpStatusCode.NotFound;
        context.Response.Close();
    }

    private static async Task WriteJsonAsync(HttpListenerContext context, object response)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response, JsonOptions));
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }

    public void Dispose()
    {
        cancellation.Cancel();
        listener.Close();
        cancellation.Dispose();
    }
}

public sealed record BrowserTabReport(int TabId, string Title, bool IsMuted, double Volume, bool IsAudible, double Peak = 0, string? FavIconUrl = null);
public sealed record BrowserTabsPayload(IReadOnlyList<BrowserTabReport> Tabs, double MasterVolume = 100, bool MasterMuted = false, double MasterPeak = 0);
public sealed record BrowserCommand(int TabId, string Type, double? Volume = null, bool? IsMuted = null);
public sealed record BrowserCommandsPayload(IReadOnlyList<BrowserCommand> Commands);
public sealed record BrowserMasterReport(double Volume, bool IsMuted, double Peak);
public sealed record DetectedBrowserTab(int TabId, string Title);
public sealed record DetectedBrowserTabsPayload(IReadOnlyList<DetectedBrowserTab> Tabs);
