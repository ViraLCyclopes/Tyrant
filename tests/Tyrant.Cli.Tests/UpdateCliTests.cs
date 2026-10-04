using System.Net;
using Tyrant.Cli;

namespace Tyrant.Cli.Tests;

public class UpdateCliTests
{
    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        var (oldOut, oldErr) = (Console.Out, Console.Error);
        var (o, e) = (new StringWriter(), new StringWriter());
        Console.SetOut(o);
        Console.SetError(e);
        try { return (CliApp.Run(args), o.ToString(), e.ToString()); }
        finally { Console.SetOut(oldOut); Console.SetError(oldErr); }
    }

    private sealed class Answer(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(answer(request));
    }

    private static (int Code, string Out, string Err) CheckWith(Func<HttpRequestMessage, HttpResponseMessage> answer)
    {
        var shipped = CliServices.Http;
        CliServices.Http = () => new HttpClient(new Answer(answer));
        try { return Run("update", "check"); }
        finally { CliServices.Http = shipped; }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    [Fact]
    public void A_newer_release_is_named_with_where_to_get_it()
    {
        var (code, output, _) = CheckWith(_ => Json("""
            { "tag_name": "v9.9.9", "html_url": "https://github.com/ViraLCyclopes/Tyrant/releases/tag/v9.9.9",
              "assets": [ { "name": "Tyrant_9.9.9_x64-setup.exe", "browser_download_url": "https://x/Tyrant_9.9.9_x64-setup.exe" } ] }
            """));

        Assert.Equal(ExitCodes.Partial, code);
        Assert.Contains("9.9.9", output);
        Assert.Contains("https://x/Tyrant_9.9.9_x64-setup.exe", output);
        Assert.Contains("Help → Check for updates", output);
    }

    [Fact]
    public void No_published_release_is_fine()
    {
        var (code, output, _) = CheckWith(_ => Json("""{ "message": "Not Found" }""", HttpStatusCode.NotFound));

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("No release is published yet", output);
    }

    [Fact]
    public void Offline_is_an_update_check_error()
    {
        var (code, _, err) = CheckWith(_ => throw new HttpRequestException("No such host is known."));

        Assert.Equal(ExitCodes.Error, code);
        Assert.Contains("UPDATE_CHECK_FAILED", err);
    }
}
