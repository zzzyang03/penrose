using System.Net;
using MediaPlayer.Sources.Emby;

namespace MediaPlayer.Sources.Emby.Tests;

public sealed class EmbyClientFailureTests
{
    private const string CloudflarePage =
        "<!DOCTYPE html><html><head><title>Attention Required! | Cloudflare</title></head>" +
        "<body><h1>Sorry, you have been blocked</h1><span>Performance &amp; security by Cloudflare</span></body></html>";

    [Fact]
    public void Cloudflare_block_page_is_recognised()
    {
        Assert.Equal(EmbyFailureKind.CloudflareBlocked, EmbyClient.Classify(HttpStatusCode.Forbidden, "text/html", CloudflarePage));
        // Content-Type missing: fall back to sniffing the body.
        Assert.Equal(EmbyFailureKind.CloudflareBlocked, EmbyClient.Classify(HttpStatusCode.Forbidden, null, "  " + CloudflarePage));
    }

    [Fact]
    public void Unauthorized_and_plain_html_and_other()
    {
        Assert.Equal(EmbyFailureKind.Unauthorized, EmbyClient.Classify(HttpStatusCode.Unauthorized, "application/json", "{\"error\":\"bad password\"}"));
        Assert.Equal(EmbyFailureKind.HtmlPage, EmbyClient.Classify(HttpStatusCode.OK, "text/html", "<html><body>Router login</body></html>"));
        Assert.Equal(EmbyFailureKind.HtmlPage, EmbyClient.Classify(HttpStatusCode.NotFound, null, "<!doctype html><html>nginx</html>"));
        Assert.Equal(EmbyFailureKind.Other, EmbyClient.Classify(HttpStatusCode.NotFound, "application/json", "{}"));
        Assert.Equal(EmbyFailureKind.Other, EmbyClient.Classify(HttpStatusCode.InternalServerError, "text/plain", ""));
    }

    [Fact]
    public void A_cloudflare_403_on_a_json_only_body_is_not_a_block()
    {
        // The word alone (e.g. in a JSON error) must not trigger the firewall message.
        Assert.Equal(EmbyFailureKind.Other, EmbyClient.Classify(HttpStatusCode.Forbidden, "application/json", "{\"host\":\"cloudflare\"}"));
    }

    [Fact]
    public void User_agent_names_the_client_and_version()
    {
        Assert.Equal("MediaPlayer/0.1.0 (Windows)", EmbyClient.UserAgent("0.1.0"));
        Assert.Equal("MediaPlayer/0 (Windows)", EmbyClient.UserAgent(""));
    }

    [Fact]
    public void Exception_keeps_status_and_kind()
    {
        EmbyHttpException ex = new("msg", HttpStatusCode.Forbidden, EmbyFailureKind.CloudflareBlocked);
        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
        Assert.Equal(EmbyFailureKind.CloudflareBlocked, ex.Kind);
        Assert.IsAssignableFrom<HttpRequestException>(ex);
    }
}
