using System.IO;
using System.Text.Json;

namespace DroidOSS.App.Ui;

/// <summary>
/// What the window remembers between runs, in <c>%APPDATA%\droidOSS\server.json</c>.
/// </summary>
/// <remarks>
/// Losing it costs nothing worse than defaults, so a missing, unreadable or
/// unwritable file is never an error.
/// </remarks>
internal sealed class ServerSettings
{
    public bool DeveloperMode { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "droidOSS", "server.json");

    public static ServerSettings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<ServerSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not worth interrupting anyone over; the switch still works this run.
        }
    }
}
