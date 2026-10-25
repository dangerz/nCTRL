using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace NGarage.Core;

/// <summary>Google Maps shares short links (maps.app.goo.gl/…) that hold no place until opened. The app opens one
/// only when it is shared to nCTRL, reading just where it redirects: at most a few hops, Google addresses only, no
/// cookies or account details, and never the page itself.</summary>
public static class ShortLinks
{
    public const int MaxHops=3;
    public static bool IsGoogleShort(string link)=>Uri.TryCreate(link,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&
        (uri.Host.Equals("maps.app.goo.gl",StringComparison.OrdinalIgnoreCase)||uri.Host.Equals("g.co",StringComparison.OrdinalIgnoreCase)||
         uri.Host.Equals("goo.gl",StringComparison.OrdinalIgnoreCase)&&uri.AbsolutePath.StartsWith("/maps",StringComparison.OrdinalIgnoreCase));
    private static bool Google(Uri uri)
    {
        string host=uri.Host.ToLowerInvariant();
        return uri.Scheme=="https"&&(host=="google.com"||host.EndsWith(".google.com")||host=="goo.gl"||host.EndsWith(".goo.gl")||host=="g.co");
    }
    /// <summary>The full Google Maps address a short link leads to, or null.</summary>
    public static async Task<string?> ResolveAsync(string link,HttpMessageHandler? handler,CancellationToken ct)
    {
        if(!IsGoogleShort(link))return null;
        using var http=new HttpClient(handler??new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false},handler==null)
            {Timeout=TimeSpan.FromSeconds(10),MaxResponseContentBufferSize=64*1024};
        http.DefaultRequestHeaders.UserAgent.ParseAdd("nCTRL/1.0 (shared-link)");
        var current=new Uri(link);
        for(int hop=0;hop<MaxHops;hop++) {
            using var request=new HttpRequestMessage(HttpMethod.Get,current);
            using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
            if((int)response.StatusCode is <300 or >=400||response.Headers.Location is not Uri next)return null;
            next=next.IsAbsoluteUri?next:new Uri(current,next);
            if(!Google(next))return null;
            if(!IsGoogleShort(next.ToString()))return next.ToString();
            current=next;
        }
        return null;
    }
}
