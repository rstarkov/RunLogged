using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using RT.Util;
using RT.Util.ExtensionMethods;

namespace RunLogged
{
    class Settings
    {
        public string EmailerAccount = null;
        public PauseForDlg.Settings PauseForDlgSettings = new PauseForDlg.Settings();
    }
}
