using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using RT.Util.ExtensionMethods;

namespace RunLoggedCs.ScriptUtil;

public class Telegram
{
    private static HttpClient _http = new();
    private static Settings _settings;
    private static string _sender;

    internal static void Init(Settings settings, string scriptName)
    {
        _settings = settings;
        _sender = $"<b>{scriptName.HtmlEscape()}</b> on <b>{settings.MachineName}</b>";
    }

    public static async Task SendRawAsync(bool warn, string html)
    {
        try
        {
            var botToken = warn ? _settings.Telegram?.WarnBotToken : _settings.Telegram?.InfoBotToken;
            if (botToken == null || _settings.Telegram.Recipient == null)
            {
                Console.WriteLine("TELEGRAM: skipping send because bot token or recipient is not set");
                return;
            }
            Console.WriteLine($"Telegram {(warn ? "WARN" : "info")}: {html}");
            html = _sender + ":\n" + html;
            var url = new UrlHelper($"https://api.telegram.org/bot{botToken}/sendMessage");
            url.AddQuery("chat_id", _settings.Telegram.Recipient).AddQuery("parse_mode", "HTML").AddQuery("text", html);
            var resp = await _http.GetAsync(url);
            if ((int)resp.StatusCode != 200)
                Console.WriteLine($"TELEGRAM: status {(int)resp.StatusCode}, {await resp.Content.ReadAsStringAsync()}");
        }
        catch (Exception e)
        {
            Console.WriteLine($"TELEGRAM: {e.GetType().Name}, {e.Message}");
        }
    }

    public static Task SendAsync(bool warn, string text = null, string html = null)
    {
        if (text != null && html == null)
            html = text.HtmlEscape();
        return SendRawAsync(warn, html);
    }

    public static void Send(bool warn, string text = null, string html = null)
    {
        SendAsync(warn, text, html).ConfigureAwait(false).GetAwaiter().GetResult();
    }
}

public class UrlHelper
{
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
}
