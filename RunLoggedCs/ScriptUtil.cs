using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using RT.Util.ExtensionMethods;

namespace RunLoggedCs.ScriptUtil;

public class Log
{
    /// <summary>
    ///     Use to send a non-critical failure notification (when script can continue). If the failure is critical prefer
    ///     throwing, or sending a custom message via <see cref="Telegram.Send"/>.</summary>
    public static void Warn(string warning)
    {
        Program.Warn(warning);
    }

    public static void WriteLinePrefixed(string text, int hanging = 0)
    {
        Program.WriteLinePrefixed(text, hanging);
    }
}

public class Telegram
{
    private static TelegramSettings _settings => Program.Settings.Telegram;
    private static string _sender => $"<b>{Program.Settings.MachineName}</b> - <b>{Program.ScriptName.HtmlEscape()}</b>";

    public static async Task SendRawAsync(bool warn, string html)
    {
        try
        {
            var botToken = warn ? _settings?.WarnBotToken : _settings?.InfoBotToken;
            if (botToken == null || _settings.Recipient == null)
            {
                Log.Warn("[Telegram] Skipping send because bot token or recipient is not set");
                return;
            }
            Log.WriteLinePrefixed($"[Telegram {(warn ? "WARN" : "info")}] {html}", 16);
            html = _sender + ":\n" + html;
            var url = new UrlHelper($"https://api.telegram.org/bot{botToken}/sendMessage");
            url.AddQuery("chat_id", _settings.Recipient).AddQuery("parse_mode", "HTML").AddQuery("text", html);
            var resp = await url.PingAsync(quiet: true);
            if ((int)resp.StatusCode != 200)
                Log.Warn($"[Telegram] Status {(int)resp.StatusCode}, {await resp.Content.ReadAsStringAsync()}");
        }
        catch (Exception e)
        {
            Log.Warn($"[Telegram] {e.GetType().Name}, {e.Message}");
        }
    }

    /// <summary>Use to send a custom Telegram notification to the info or the warning channel.</summary>
    public static Task SendAsync(bool warn, string text = null, string html = null)
    {
        if (text != null && html == null)
            html = text.HtmlEscape();
        return SendRawAsync(warn, html);
    }

    /// <summary>Use to send a custom Telegram notification to the info or the warning channel.</summary>
    public static void Send(bool warn, string text = null, string html = null)
    {
        SendAsync(warn, text, html).ConfigureAwait(false).GetAwaiter().GetResult();
    }
}

public class UrlHelper
{
    private static HttpClient _http = new();
    private StringBuilder _url;
    private bool _hasQuery;

    public UrlHelper(string url)
    {
        _url = new StringBuilder(url);
        _hasQuery = url.Contains('?');
    }

    public UrlHelper AddQuery(string name, string value)
    {
        if (name == null || value == null)
            return this;
        _url.Append(_hasQuery ? '&' : '?');
        _url.Append(name.UrlEscape());
        _url.Append('=');
        _url.Append(value.UrlEscape());
        _hasQuery = true;
        return this;
    }

    public static implicit operator string(UrlHelper urlHelper)
    {
        return urlHelper._url.ToString();
    }

    public async Task<HttpResponseMessage> PingAsync(bool quiet = false)
    {
        var url = _url.ToString();
        if (!quiet)
            Log.WriteLinePrefixed($"[UrlPing] Pinging URL {url}");
        var resp = await _http.GetAsync(url);
        if ((int)resp.StatusCode != 200 && !quiet)
            Log.Warn($"[UrlPing] Status {(int)resp.StatusCode}, {await resp.Content.ReadAsStringAsync()}");
        return resp;
    }

    public HttpResponseMessage Ping(bool quiet = false)
    {
        return PingAsync().GetAwaiter().GetResult();
    }
}
