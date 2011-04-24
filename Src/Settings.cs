using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using RT.Util;
using RT.Util.Forms;

namespace RunLogged
{
    [Settings("RunLogged", SettingsKind.MachineSpecific)]
    class Settings : SettingsBase
    {
        public string SmtpHost = "example.com";
        public string SmtpUser = "runlogged";
        public string SmtpFrom = "runlogged@example.com";
        private string SmtpPassword = "password";
        public string SmtpPasswordEncrypted;
        public string SmtpPasswordDecrypted { get { return SmtpPassword ?? Settings.DecryptPwd(SmtpPasswordEncrypted); } }
        public PauseForDlg.Settings PauseForDlgSettings = new PauseForDlg.Settings();

        public void SyncPasswords()
        {
            SmtpPasswordEncrypted = Settings.EncryptPwd(SmtpPasswordDecrypted);
            SmtpPassword = null;
        }

        private static byte[] _pwdInitVector = HexToBytes("49443950fb0c02c8ac0253d507b1a420"); // exactly 16 bytes
        private static byte[] _pwdKey = HexToBytes("7c7e84e4f6989cbb9825165608eadb1aac74801e23d6552c60b6a08b08f2e1fb"); // exactly 256 bits
        private static byte[] _pwdSalt = HexToBytes("c2b04a54e0f5700067426b0add78ab6549ae2779370431c4beb0068ca23b5206"); // any length

        public static byte[] HexToBytes(string str)
        {
            var result = new byte[str.Length / 2];
            for (int i = 0; i < result.Length; i++)
                result[i] = byte.Parse(str.Substring(i * 2, 2), NumberStyles.HexNumber);
            return result;
        }

        public static string EncryptPwd(string plain)
        {
            if (plain == null) return null;
            var aes = new RijndaelManaged() { Mode = CipherMode.CBC };
            var encryptor = aes.CreateEncryptor(_pwdKey, _pwdInitVector);

            var memoryStream = new MemoryStream();
            var cryptoStream = new CryptoStream(memoryStream, encryptor, CryptoStreamMode.Write);

            var plainBytes = Encoding.UTF8.GetBytes(plain);
            cryptoStream.Write(plainBytes, 0, plainBytes.Length);
            cryptoStream.FlushFinalBlock();
            cryptoStream.Close();

            return Convert.ToBase64String(memoryStream.ToArray());
        }

        public static string DecryptPwd(string cipher)
        {
            if (cipher == null) return null;
            var aes = new RijndaelManaged() { Mode = CipherMode.CBC };
            var decryptor = aes.CreateDecryptor(_pwdKey, _pwdInitVector);

            var cipherBytes = Convert.FromBase64String(cipher);
            var memoryStream = new MemoryStream(cipherBytes);
            var cryptoStream = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Read);

            var plainBytes = new byte[cipherBytes.Length];
            int plainByteCount = cryptoStream.Read(plainBytes, 0, plainBytes.Length);
            cryptoStream.Close();

            return Encoding.UTF8.GetString(plainBytes, 0, plainByteCount);
        }
    }
}
