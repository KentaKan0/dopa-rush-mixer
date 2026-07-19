using System.IO;
using System.Text.Json;

namespace DopaRushMixer;

internal sealed class OutputDeviceHistory
{
    private const int MaximumEntries = 12;
    private readonly string filePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DopaRushMixer",
        "output-device-history.json");
    private readonly List<string> deviceIds;

    internal OutputDeviceHistory()
    {
        try
        {
            deviceIds = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(filePath)) ?? [];
        }
        catch (IOException) { deviceIds = []; }
        catch (JsonException) { deviceIds = []; }
        catch (UnauthorizedAccessException) { deviceIds = []; }
    }

    internal int GetRank(string deviceId)
    {
        var index = deviceIds.IndexOf(deviceId);
        return index < 0 ? int.MaxValue : index;
    }

    internal void Record(string deviceId)
    {
        deviceIds.Remove(deviceId);
        deviceIds.Insert(0, deviceId);
        if (deviceIds.Count > MaximumEntries) deviceIds.RemoveRange(MaximumEntries, deviceIds.Count - MaximumEntries);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, JsonSerializer.Serialize(deviceIds));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
