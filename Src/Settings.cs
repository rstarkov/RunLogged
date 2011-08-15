using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using RT.Util;
using RT.Util.ExtensionMethods;

namespace RunLogged
{
    [Settings("RunLogged", SettingsKind.MachineSpecific)]
    class Settings : SettingsBase
    {
        public RunLoggedSmtpSettings SmtpSettings = new RunLoggedSmtpSettings();
        public string SmtpFrom = "RunLogged <runlogged@example.com>";
        public PauseForDlg.Settings PauseForDlgSettings = new PauseForDlg.Settings();
    }

    sealed class RunLoggedSmtpSettings : RTSmtpSettings
    {
        private static byte[] _key = "7c7e84e4f6989cbb9825165608eadb1aac74801e23d6552c60b6a08b08f2e1fb".FromHex(); // exactly 32 bytes

        protected override string DecryptPassword(string encrypted)
        {
            return SettingsUtil.DecryptPassword(encrypted, _key);
        }

        protected override string EncryptPassword(string decrypted)
        {
            return SettingsUtil.EncryptPassword(decrypted, _key);
        }
    }
}
