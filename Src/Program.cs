using System;
using System.Globalization;
using System.IO;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using RT.Util;
using RT.Util.CommandLine;
using RT.Util.ExtensionMethods;

/// TODO:
/// - proper (albeit approximate) by-line handling: nice interleaving of stderr with stdout; label the approx time of every line; backspaces in file stream
/// - execute via a batch file

namespace RunLogged
{
    class Program
    {
        public static Settings Settings;
        public static CmdLineArgs Args;
        public static StreamWriter Log;
        public static long LogStartOffset;

        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
#if CONSOLE
            Console.OutputEncoding = System.Text.Encoding.UTF8;
#endif

            SettingsUtil.LoadSettings(out Settings);
            Settings.SyncPasswords();
            Settings.Save();

            string originalWorkingDir = Directory.GetCurrentDirectory();
            try
            {
                try
                {
                    Args = new CommandLineParser<CmdLineArgs>().Parse(args);
                }
                catch (CommandLineParseException e)
                {
#if CONSOLE
                    Console.WriteLine();
                    e.WriteUsageInfoToConsole();
#else
                    var tr = new Translation();
                    string text = e.GenerateHelp(tr, 60).ToString();
                    if (!e.WasCausedByHelpRequest())
                        text += Environment.NewLine + e.GenerateErrorText(tr, 60).ToString();
                    new RT.Util.Dialogs.DlgMessage() { Message = text, Type = RT.Util.Dialogs.DlgType.Warning, Font = new System.Drawing.Font("Consolas", 9) }.Show();
#endif
                    return 1;
                }

                var runner = new ProcessRunner(Args.CommandToRun, Directory.GetCurrentDirectory());
                runner.StdoutText += runner_StdoutText;
                runner.StderrText += runner_StderrText;

                if (LogStartOffset > 0)
                {
                    for (int i = 0; i < 3; i++)
                        Log.WriteLine();
                    LogStartOffset = Program.Log.BaseStream.Position;
                }
                outputLine("************************************************************************");
                outputLine("****** RunLogged invoked at {0}".Fmt(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
                outputLine("****** Command: |{0}|".Fmt(runner.LastRawCommandLine));
                outputLine("****** CurDir: |{0}|".Fmt(Directory.GetCurrentDirectory()));
                outputLine("****** LogTo: |{0}|".Fmt(Args.LogFilename));

                runner.Start();
                runner.WaitForExit();

                outputLine("****** completed at {0}".Fmt(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
                outputLine("****** exit code: {0}".Fmt(runner.LastExitCode));

                if (runner.LastExitCode != 0 && Args.Email != null)
                    try
                    {
                        var client = new SmtpClient(Program.Settings.SmtpHost);
                        client.Credentials = new System.Net.NetworkCredential(Program.Settings.SmtpUser, Program.Settings.SmtpPasswordDecrypted);
                        var mail = new MailMessage();
                        mail.From = new MailAddress(Program.Settings.SmtpFrom);
                        mail.To.Add(new MailAddress(Args.Email));
                        mail.Subject = "[RunLogged] Failure: {0}".Fmt(runner.LastRawCommandLine.SubstringSafe(0, 50));
                        using (var log = File.Open(Args.LogFilename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        using (var logreader = new StreamReader(log))
                        {
                            log.Seek(Program.LogStartOffset, SeekOrigin.Begin);
                            mail.Body = logreader.ReadToEnd();
                        }
                        client.Send(mail);
                    }
                    catch (Exception e)
                    {
                        output("Exception:");
                        while (e != null)
                        {
                            output("{0}: {1}".Fmt(e.GetType().Name, e.Message));
                            output(e.StackTrace);
                            e = e.InnerException;
                        }
                    }

                return runner.LastExitCode;
            }
            finally
            {
                if (Log != null)
                    Log.Dispose();
                try { Directory.SetCurrentDirectory(originalWorkingDir); }
                catch { } // the script could have theoretically CD'd out of it and deleted it, so don't crash if that happens.
            }
        }

        private static void output(string text)
        {
#if CONSOLE
            Console.Write(text);
#endif
            Log.Write(text);
        }

        private static void outputLine(string text)
        {
            output(text);
            output(Environment.NewLine);
        }

        static void runner_StdoutText(object sender, EventArgs<string> e)
        {
            output(e.Data);
        }

        static void runner_StderrText(object sender, EventArgs<string> e)
        {
            output("STDERR: ");
            output(e.Data);
        }
    }

    [DocumentationLiteral(@"Runs the specified command and logs all the output to a timestamped log file.")]
    public class CmdLineArgs : ICommandLineValidatable
    {
        [Option("--cd")]
        [DocumentationLiteral("Optionally changes the working directory while executing the command.")]
        public string WorkingDir;

        [Option("--log")]
        [DocumentationLiteral("Log file name pattern. May be relative to --cd WorkingDir. The timestamp YYYY-MM-DD is substituted in place of any occurrences of \"{}\" in the --log parameter.\n\nWhere the --log argument is not specified, the log filename will be constructed from the command to be executed, a timestamp and a .log extension.\n\nIf the log file already exists, it will be appended to. The directory to contain the log file is created automatically if necessary.")]
        public string LogFilename;

        [Option("--email")]
        [DocumentationLiteral("If the exit code of the specified command is anything other than 0, send the log to this email address. Other email-related settings should be configured in the settings file at %%ProgramData%%\\\\RunLogged.")]
        public string Email;

        [IsPositional]
        [DocumentationLiteral("Command to be executed, with arguments if any.")]
        public string[] CommandToRun;

        public string Validate()
        {
            if (CommandToRun.Length == 0)
                return "You must specify the command to be executed (CommandToRun).";

            if (LogFilename == null)
                LogFilename = Path.GetFileName(CommandToRun[0]).Replace(".", "_") + "--{}.log";
            LogFilename = Path.GetFullPath(LogFilename.Replace("{}", DateTime.Now.ToString("yyyy-MM-dd")));

            if (WorkingDir != null)
                try { Directory.SetCurrentDirectory(WorkingDir); }
                catch { return "Cannot set working directory - check that the directory exists and that the path is valid. \"{0}\"".Fmt(WorkingDir); }

            try
            {
                try { Directory.CreateDirectory(Path.GetDirectoryName(LogFilename)); }
                catch { }
                Program.Log = new StreamWriter(File.Open(LogFilename, FileMode.Append, FileAccess.Write, FileShare.Read));
                Program.Log.AutoFlush = true;
                Program.LogStartOffset = Program.Log.BaseStream.Position;
            }
            catch
            {
                return "Could not open the log file for writing. File \"{0}\".".Fmt(LogFilename);
            }

            return null;
        }
    }

    [Settings("RunLogged", SettingsKind.MachineSpecific)]
    class Settings : SettingsBase
    {
        public string SmtpHost = "example.com";
        public string SmtpUser = "runlogged";
        public string SmtpFrom = "runlogged@example.com";
        private string SmtpPassword = "password";
        public string SmtpPasswordEncrypted;
        public string SmtpPasswordDecrypted { get { return SmtpPassword ?? Settings.DecryptPwd(SmtpPasswordEncrypted); } }

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
