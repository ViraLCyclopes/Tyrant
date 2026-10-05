using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Tyrant.Core.Blender;

/// <summary>Asks a Blender running Tyrant's add-on to open a project; tests use a fake.</summary>
public interface IBlenderLink
{
    /// <summary>True when a Blender answered {"ok": true}; false when none listens, it refused, or the answer was not the add-on's.</summary>
    bool TryOpen(string projectFile);
}

/// <summary>The add-on's listener on 127.0.0.1: one line {"open": "&lt;project file&gt;"}, one answer line.</summary>
public sealed class BlenderLink(int port = BlenderLink.Port, TimeSpan? timeout = null) : IBlenderLink
{
    public const int Port = 8897;

    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromMilliseconds(1500);

    public bool TryOpen(string projectFile)
    {
        try
        {
            using var client = new TcpClient();
            if (!client.ConnectAsync("127.0.0.1", port).Wait(_timeout)) return false;
            client.ReceiveTimeout = client.SendTimeout = (int)_timeout.TotalMilliseconds;
            using var stream = client.GetStream();
            var line = JsonSerializer.Serialize(new Dictionary<string, string> { ["open"] = projectFile }) + "\n";
            stream.Write(Encoding.UTF8.GetBytes(line));
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var answer = reader.ReadLine();
            if (answer is null) return false;
            using var doc = JsonDocument.Parse(answer);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;
        }
        catch (Exception ex) when (ex is SocketException or IOException or JsonException or AggregateException or ObjectDisposedException)
        {
            return false;
        }
    }
}
