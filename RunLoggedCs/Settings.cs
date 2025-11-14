using System;
using System.Collections.Generic;
using RT.Serialization;

namespace RunLoggedCs;

class Settings
{
    public string MachineName;
    public List<string> Usings;
    public LogSettings Log;
    public TelegramSettings Telegram;

    public static Settings GetDefault()
    {
        return new Settings
        {
            MachineName = Environment.MachineName,
            Usings = [],
            Log = new()
            {
                Enabled = true,
                Path = "{name}.log",
                DaysToKeep = 30,
                MaxTotalSizeKB = 20_000,
            },
            Telegram = null,
        };
    }

    public static Settings LoadFromFile(string path)
    {
        return ClassifyXml.DeserializeFile<Settings>(path);
    }

    public void AddOverrides(Settings settings)
    {
        if (settings.MachineName != null) MachineName = settings.MachineName;
        if (settings.Usings != null) Usings.AddRange(settings.Usings);
        if (Log == null)
            Log = settings.Log;
        if (Log != null && settings.Log != null)
        {
            if (settings.Log.Enabled != null) Log.Enabled = settings.Log.Enabled;
            if (settings.Log.Path != null) Log.Path = settings.Log.Path;
            if (settings.Log.DaysToKeep != null) Log.DaysToKeep = settings.Log.DaysToKeep;
            if (settings.Log.MaxTotalSizeKB != null) Log.MaxTotalSizeKB = settings.Log.MaxTotalSizeKB;
        }
        if (Telegram == null)
            Telegram = settings.Telegram;
        if (Telegram != null && settings.Telegram != null)
        {
            if (settings.Telegram.InfoBotToken != null) Telegram.InfoBotToken = settings.Telegram.InfoBotToken;
            if (settings.Telegram.WarnBotToken != null) Telegram.WarnBotToken = settings.Telegram.WarnBotToken;
            if (settings.Telegram.Recipient != null) Telegram.Recipient = settings.Telegram.Recipient;
        }
    }
}

class LogSettings
{
    public bool? Enabled;
    public string Path;
    public int? DaysToKeep; // deletes files containing _only_ entries that are definitely older than this. Most recent file is never affected (so this does nothing without a {<date>} template). Zero to disable.
    public int? MaxTotalSizeKB; // when exceeded, oldest files are deleted until under threshold, except last file. If last file is over the limit, it is halved by removing oldest lines. Zero to disable.
}

class TelegramSettings
{
    public string InfoBotToken;
    public string WarnBotToken;
    public string Recipient;
    public bool NotifyOnSuccess;
}
