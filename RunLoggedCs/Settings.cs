using System;
using System.Collections.Generic;
using RT.Serialization;

namespace RunLoggedCs;

class Settings
{
    public string MachineName;
    public List<string> Usings;
    public TelegramSettings Telegram;

    public static Settings GetDefault()
    {
        return new Settings
        {
            MachineName = Environment.MachineName,
            Usings = [],
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

class TelegramSettings
{
    public string InfoBotToken;
    public string WarnBotToken;
    public string Recipient;
    public bool NotifyOnSuccess;
}
