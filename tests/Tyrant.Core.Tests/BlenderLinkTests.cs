using System.Net;
using System.Net.Sockets;
using Tyrant.Core.Blender;

namespace Tyrant.Core.Tests;

public class BlenderLinkTests
{
    private static (TcpListener Listener, int Port) Listen()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        return (l, ((IPEndPoint)l.LocalEndpoint).Port);
    }

    [Fact]
    public async Task Sends_one_open_line_and_reads_ok()
    {
        var (listener, port) = Listen();
        var serve = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            var line = await new StreamReader(stream).ReadLineAsync();
            await stream.WriteAsync("{\"ok\": true}\n"u8.ToArray());
            return line;
        });

        Assert.True(new BlenderLink(port).TryOpen(@"C:\ws\blender\game\x\tyrant-blender.json"));
        Assert.Equal("{\"open\":\"C:\\\\ws\\\\blender\\\\game\\\\x\\\\tyrant-blender.json\"}", await serve);
        listener.Stop();
    }

    [Fact]
    public void Nothing_listening_is_false() => Assert.False(new BlenderLink(1).TryOpen("x"));

    [Fact]
    public void Garbage_listener_counts_as_no_blender()
    {
        var (listener, port) = Listen();
        _ = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            await client.GetStream().WriteAsync("HTTP/1.1 400\r\n"u8.ToArray());
            await Task.Delay(3000); // holds the connection
        });

        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.False(new BlenderLink(port, TimeSpan.FromMilliseconds(500)).TryOpen("x"));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2));
        listener.Stop();
    }

    [Fact]
    public void A_listener_that_never_answers_times_out()
    {
        var (listener, port) = Listen();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.False(new BlenderLink(port, TimeSpan.FromMilliseconds(400)).TryOpen("x")); // accepted by the OS backlog, never read
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2));
        listener.Stop();
    }
}
