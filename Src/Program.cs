using System;
using System.Globalization;
using System.IO;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using RT.Util;
using RT.Util.CommandLine;
using RT.Util.Consoles;
using RT.Util.ExtensionMethods;

namespace RunLogged
{
    class Program
    {
        private static Settings Settings;
        private static CmdLineArgs Args;
        private static string OriginalCurrentDirectory;
        private static NotifyIcon TrayIcon;
        private static ProcessRunner _runner;
        private static Stream Log;
        private static long LogStartOffset;
        private static Thread _readingThread;

        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
#if CONSOLE
            Console.OutputEncoding = System.Text.Encoding.UTF8;
#endif

            try
            {
                return mainCore(args);
            }
            catch (CommandLineParseException e)
            {
                var message = "\n" + e.GenerateHelp(null, wrapToWidth());
                if (!e.WasCausedByHelpRequest())
                    message += "\n" + e.GenerateErrorText(null, wrapToWidth());
                tellUser(message);
                return -80001;
            }
            catch (TellUserException e)
            {
                var message = "\n" + "Error: ".Color(ConsoleColor.Red) + e.Message;
                tellUser(message);
                return -80002;
            }
#if !DEBUG
            catch (Exception e)
            {
                var message = "\n" + "An internal error has occurred in RunLogged: ".Color(ConsoleColor.Red);
                foreach (var ex in e.SelectChain(ex => ex.InnerException))
                {
                    message += "\n" + ex.GetType().Name.Color(ConsoleColor.Yellow) + ": " + ex.Message.Color(ConsoleColor.White);
                    message += "\n" + ex.StackTrace;
                }
                tellUser(message);
                return -80003;
            }
#endif
            finally
            {
                cleanup();
            }
        }

        private static int wrapToWidth()
        {
#if CONSOLE
            return ConsoleUtil.WrapToWidth();
#else
            return 60;
#endif
        }

        private static void tellUser(ConsoleColoredString message)
        {
#if CONSOLE
            ConsoleUtil.WriteLine(message);
#else
            new RT.Util.Dialogs.DlgMessage() { Message = text, Type = RT.Util.Dialogs.DlgType.Warning, Font = new System.Drawing.Font("Consolas", 9) }.Show();
#endif
        }

        private static int mainCore(string[] args)
        {
            SettingsUtil.LoadSettings(out Settings);
            Settings.SyncPasswords();
            Settings.SaveQuiet();

            OriginalCurrentDirectory = Directory.GetCurrentDirectory();

            Console.CancelKeyPress += processCtrlC;

            Args = CommandLineParser<CmdLineArgs>.Parse(args);

            try
            {
                try { Directory.CreateDirectory(Path.GetDirectoryName(Args.LogFilename)); }
                catch { }
                Program.Log = File.Open(Args.LogFilename, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
                Program.Log.Seek(0, SeekOrigin.End);
                Program.LogStartOffset = Program.Log.Position;
            }
            catch (Exception e)
            {
                throw new TellUserException("Could not open the log file for writing. File \"{0}\".\n{1}".Fmt(Args.LogFilename, e.Message));
            }

            _runner = new ProcessRunner(Args.CommandToRun, Directory.GetCurrentDirectory());
            _runner.StdoutText += runner_StdoutText;
            _runner.StderrText += runner_StderrText;

            _readingThread = new Thread(readingThread);
            _readingThread.Start();

            if (Args.TrayIcon != null)
            {
                TrayIcon = new NotifyIcon
                {
                    Icon = new System.Drawing.Icon(Args.TrayIcon),
                    ContextMenu = new ContextMenu(new MenuItem[] {
                        new MenuItem("&Terminate", (s, e) => { _runner.Stop(); })
                    }),
                    Visible = true
                };
            }

            Application.Run();

            return _runner.LastExitCode;
        }

        private static void cleanup()
        {
            if (Log != null)
                Log.Dispose();
            if (TrayIcon != null)
                TrayIcon.Dispose();

            if (OriginalCurrentDirectory != null)
                try { Directory.SetCurrentDirectory(OriginalCurrentDirectory); }
                catch { } // the script could have theoretically CD'd out of it and deleted it, so don't crash if that happens.
        }

        private static void processCtrlC(object sender, ConsoleCancelEventArgs e)
        {
            if (_runner != null)
                _runner.Stop();
            if (_readingThread != null)
                _readingThread.Join();
            cleanup(); // must do this because Ctrl+C ends the program without running "finally" clauses...
        }

        private static void readingThread()
        {
            if (LogStartOffset > 0)
            {
                for (int i = 0; i < 3; i++)
                    Log.Write(Environment.NewLine.ToUtf8());
                LogStartOffset = Log.Position;
            }
            outputLine("************************************************************************");
            outputLine("****** RunLogged invoked at {0}".Fmt(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
            outputLine("****** Command: |{0}|".Fmt(_runner.LastRawCommandLine));
            outputLine("****** CurDir: |{0}|".Fmt(Directory.GetCurrentDirectory()));
            outputLine("****** LogTo: |{0}|".Fmt(Args.LogFilename));

            _runner.Start();
            _runner.WaitForExit();

            if (_runner.LastAborted)
            {
                outputLine("****** aborted at {0}".Fmt(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
            }
            else
            {
                outputLine("****** completed at {0}".Fmt(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
                outputLine("****** exit code: {0}".Fmt(_runner.LastExitCode));
            }

            if (_runner.LastExitCode != 0 && Args.Email != null && !_runner.LastAborted)
                emailFailureLog();

            Application.Exit();
        }

        private static void emailFailureLog()
        {
            var client = new SmtpClient(Program.Settings.SmtpHost);
            client.Credentials = new System.Net.NetworkCredential(Program.Settings.SmtpUser, Program.Settings.SmtpPasswordDecrypted);
            var mail = new MailMessage();
            mail.From = new MailAddress(Program.Settings.SmtpFrom);
            mail.To.Add(new MailAddress(Args.Email));
            mail.Subject = "[RunLogged] Failure: {0}".Fmt(_runner.LastRawCommandLine.SubstringSafe(0, 50));
            using (var log = File.Open(Args.LogFilename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var logreader = new StreamReader(log))
            {
                log.Seek(Program.LogStartOffset, SeekOrigin.Begin);
                mail.Body = logreader.ReadToEnd();
            }
            client.Send(mail);
        }

        private static void output(string text)
        {
#if CONSOLE
            Console.Write(text);
#endif
            int index;
            while (text.Length > 0 && (index = text.IndexOf('\b')) != -1)
            {
                if (index > 0)
                {
                    Log.Write(text.Substring(0, index).ToUtf8());
                    text = text.Substring(index);
                }

                // How many backspaces are there? (and remove them)
                int backspaces = text.Length;
                text = text.TrimStart('\b');
                backspaces -= text.Length;

                // Try to read that many _characters_ from the stream
                var curPos = Log.Position;
                string readAgainStr;
                int readBytes = backspaces;
                while (true)
                {
                    Log.Seek(curPos - readBytes, SeekOrigin.Begin);
                    var readAgain = Log.Read(readBytes);
                    if ((readAgain[0] & 0xc0) != 0x80 && ((readAgainStr = readAgain.FromUtf8()).Length == backspaces || readAgainStr.Contains("\n")))
                        break;
                    readBytes++;
                }

                // Don’t allow a backspace to erase a newline
                if ((index = readAgainStr.LastIndexOf('\n')) != -1)
                    Log.Seek(curPos - readAgainStr.Substring(index + 1).Utf8Length(), SeekOrigin.Begin);
                else
                    Log.Seek(curPos - readAgainStr.Utf8Length(), SeekOrigin.Begin);
            }
            if (text.Length > 0)
                Log.Write(text.ToUtf8());
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

    class TellUserException : Exception
    {
        public TellUserException(string message) : base(message) { }
    }

    [DocumentationLiteral(@"Runs the specified command and logs all the output to a timestamped log file.")]
    class CmdLineArgs : ICommandLineValidatable
    {
#pragma warning disable 649 // Field is never assigned to, and will always have its default value null
        [Option("--cd")]
        [DocumentationLiteral("Optionally changes the working directory while executing the command.")]
        public string WorkingDir;

        [Option("--log")]
        [DocumentationLiteral("Log file name pattern. May be relative to &*--cd <<WorkingDir>>*&. The timestamp YYYY-MM-DD is substituted in place of any occurrences of \"\"{{}}\"\" in &*<<LogFilename>>*&.\n\nWhere the &*--log*& argument is not specified, the log filename will be constructed from the command to be executed, a timestamp and a .log extension.\n\nIf the log file already exists, it will be appended to. The directory to contain the log file is created automatically if necessary.")]
        public string LogFilename;

        [Option("--email")]
        [DocumentationLiteral("If the exit code of the specified command is anything other than 0, send the log to this email address. Other email-related settings should be configured in the settings file at ^*%%ProgramData%%\\\\RunLogged*^.")]
        public string Email;

        [Option("--trayicon")]
        [DocumentationLiteral("Specifies the path and filename of an ico file to use for a tray icon.")]
        public string TrayIcon;

        [IsPositional]
        [DocumentationLiteral("Command to be executed, with arguments if any.")]
        public string[] CommandToRun;

        public string Validate()
        {
            if (CommandToRun.Length == 0)
                return "You must specify the command to be executed (CommandToRun).";

            if (WorkingDir != null)
                try { Directory.SetCurrentDirectory(WorkingDir); }
                catch { return "Cannot set working directory - check that the directory exists and that the path is valid. \"{0}\"".Fmt(WorkingDir); }

            if (LogFilename == null)
                LogFilename = Path.GetFileName(CommandToRun[0]).Replace(".", "_") + "--{}.log";
            LogFilename = Path.GetFullPath(LogFilename.Replace("{}", DateTime.Now.ToString("yyyy-MM-dd")));

            return null;
        }
#pragma warning restore 649
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
