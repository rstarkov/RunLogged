using System;
using System.Linq;
using System.IO;
using System.Net.Mail;
using System.Threading;
using System.Windows.Forms;
using RT.Util;
using RT.Util.CommandLine;
using RT.Util.Consoles;
using RT.Util.ExtensionMethods;
using System.Reflection;
using System.Diagnostics;
using System.Text;

namespace RunLogged
{
    class Program
    {
        private static Settings _settings;
        private static CmdLineArgs _args;
        private static string _originalCurrentDirectory;
        private static NotifyIcon _trayIcon;
        private static ProcessRunner _runner;
        private static Stream _log;
        private static long _logStartOffset;
        private static Thread _readingThread;
        private static MenuItem _miResume;

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
            new RT.Util.Dialogs.DlgMessage() { Message = message.ToString(), Type = RT.Util.Dialogs.DlgType.Warning, Font = new System.Drawing.Font("Consolas", 9) }.Show();
#endif
        }

        private static int mainCore(string[] args)
        {
            _args = CommandLineParser<CmdLineArgs>.Parse(args);

            string destPath = null;
            if (_args.ShadowCopy)
            {
                var source = Assembly.GetExecutingAssembly();
                int i = 0;
                do
                {
                    i += Rnd.Next(65536);
                    destPath = Path.Combine(Path.GetTempPath(), "RunLogged-" + i);
                }
                while (Directory.Exists(destPath));
                Directory.CreateDirectory(destPath);
                var destAssembly = Path.Combine(destPath, Path.GetFileName(source.Location));
                var newArgs = args.Where(arg => arg != "--shadowcopy").Select(arg => arg.Any(ch => "&()[]{}^=;!'+,`~ ".Contains(ch)) ? "\"" + arg + "\"" : arg).JoinString(" ");
                File.Copy(source.Location, destAssembly);
                foreach (var dep in source.GetReferencedAssemblies())
                {
                    var file = new[] { ".dll", ".exe" }.Select(ext => Path.Combine(Path.GetDirectoryName(source.Location), dep.Name + ext)).FirstOrDefault(fn => File.Exists(fn));
                    if (file == null)
                        continue;
                    File.Copy(file, Path.Combine(destPath, Path.GetFileName(file)));
                }
                var batchFile = new StringBuilder();
                batchFile.AppendLine("@echo off");
                batchFile.AppendLine("\"" + destAssembly + "\" " + newArgs);
                batchFile.AppendLine("cd \\");
                // Note: the command that deletes the batch file needs to be the last command in the batch file.
                batchFile.AppendLine("rd /s /q \"" + destPath + "\"");
                var batchFilePath = Path.Combine(destPath, "RunLogged_then_delete.bat");
                File.WriteAllText(batchFilePath, batchFile.ToString());
                Process.Start(batchFilePath);
                return 0;
            }

            SettingsUtil.LoadSettings(out _settings);
            _settings.SyncPasswords();
            _settings.SaveQuiet();

            _originalCurrentDirectory = Directory.GetCurrentDirectory();

            Console.CancelKeyPress += processCtrlC;

            try
            {
                try { Directory.CreateDirectory(Path.GetDirectoryName(_args.LogFilename)); }
                catch { }
                Program._log = File.Open(_args.LogFilename, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
                Program._log.Seek(0, SeekOrigin.End);
                Program._logStartOffset = Program._log.Position;
            }
            catch (Exception e)
            {
                throw new TellUserException("Could not open the log file for writing. File \"{0}\".\n{1}".Fmt(_args.LogFilename, e.Message));
            }

            _runner = new ProcessRunner(_args.CommandToRun, Directory.GetCurrentDirectory());
            _runner.StdoutText += runner_StdoutText;
            _runner.StderrText += runner_StderrText;

            _readingThread = new Thread(readingThread);
            _readingThread.Start();

            if (_args.TrayIcon != null)
            {
                _trayIcon = new NotifyIcon
                {
                    Icon = new System.Drawing.Icon(_args.TrayIcon),
                    ContextMenu = new ContextMenu(new MenuItem[] {
                        new MenuItem("&Pause for...", pause),
                        (_miResume = new MenuItem("&Resume (...)", (_, __) => { _runner.ResumePausedProcess(); })),
                        new MenuItem("&Terminate", (_, __) => { _runner.Stop(); })
                    }),
                    Visible = true
                };
                _miResume.Visible = false;
                _runner.ProcessResumed += () => { _miResume.Visible = false; };
            }

            Application.Run();

            return _runner.LastExitCode;
        }

        private static void pause(object _ = null, EventArgs __ = null)
        {
            using (var dlg = new PauseForDlg(_settings.PauseForDlgSettings))
            {
                var result = dlg.ShowDialog();
                if (result == DialogResult.Cancel)
                    return;
                _runner.PauseFor(
                    _settings.PauseForDlgSettings.IntervalType == PauseForDlg.IntervalType.Seconds ? TimeSpan.FromSeconds((double) _settings.PauseForDlgSettings.Interval) :
                    _settings.PauseForDlgSettings.IntervalType == PauseForDlg.IntervalType.Minutes ? TimeSpan.FromMinutes((double) _settings.PauseForDlgSettings.Interval) :
                    _settings.PauseForDlgSettings.IntervalType == PauseForDlg.IntervalType.Hours ? TimeSpan.FromHours((double) _settings.PauseForDlgSettings.Interval) :
                    _settings.PauseForDlgSettings.IntervalType == PauseForDlg.IntervalType.Days ? TimeSpan.FromDays((double) _settings.PauseForDlgSettings.Interval) :
                    TimeSpan.FromMilliseconds(-1)
                );
                if (_settings.PauseForDlgSettings.IntervalType == PauseForDlg.IntervalType.Forever)
                    _miResume.Text = "&Resume";
                else
                    _miResume.Text = "&Resume (automatically resumes: {0})".Fmt(_runner.PausedUntil);
                _miResume.Visible = true;
            }
        }

        private static void cleanup()
        {
            if (_log != null)
                _log.Dispose();
            if (_trayIcon != null)
                _trayIcon.Dispose();

            if (_originalCurrentDirectory != null)
                try { Directory.SetCurrentDirectory(_originalCurrentDirectory); }
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
            if (_logStartOffset > 0)
            {
                for (int i = 0; i < 3; i++)
                    _log.Write(Environment.NewLine.ToUtf8());
                _logStartOffset = _log.Position;
            }
            outputLine("************************************************************************");
            outputLine("****** RunLogged invoked at {0}".Fmt(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
            outputLine("****** Command: |{0}|".Fmt(_runner.LastRawCommandLine));
            outputLine("****** CurDir: |{0}|".Fmt(Directory.GetCurrentDirectory()));
            outputLine("****** LogTo: |{0}|".Fmt(_args.LogFilename));

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

            if (_runner.LastExitCode != 0 && _args.Email != null && !_runner.LastAborted)
                emailFailureLog();

            Application.Exit();
        }

        private static void emailFailureLog()
        {
            var client = new SmtpClient(Program._settings.SmtpHost);
            client.Credentials = new System.Net.NetworkCredential(Program._settings.SmtpUser, Program._settings.SmtpPasswordDecrypted);
            var mail = new MailMessage();
            mail.From = new MailAddress(Program._settings.SmtpFrom);
            mail.To.Add(new MailAddress(_args.Email));
            mail.Subject = "[RunLogged] Failure: {0}".Fmt(_runner.LastRawCommandLine.SubstringSafe(0, 50));
            using (var log = File.Open(_args.LogFilename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var logreader = new StreamReader(log))
            {
                log.Seek(Program._logStartOffset, SeekOrigin.Begin);
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
                    _log.Write(text.Substring(0, index).ToUtf8());
                    text = text.Substring(index);
                }

                // How many backspaces are there? (and remove them)
                int backspaces = text.Length;
                text = text.TrimStart('\b');
                backspaces -= text.Length;

                // Try to read that many _characters_ from the stream
                var curPos = _log.Position;
                string readAgainStr;
                int readBytes = backspaces;
                while (true)
                {
                    _log.Seek(curPos - readBytes, SeekOrigin.Begin);
                    var readAgain = _log.Read(readBytes);
                    if ((readAgain[0] & 0xc0) != 0x80 && ((readAgainStr = readAgain.FromUtf8()).Length == backspaces || readAgainStr.Contains("\n")))
                        break;
                    readBytes++;
                }

                // Don’t allow a backspace to erase a newline
                if ((index = readAgainStr.LastIndexOf('\n')) != -1)
                    _log.Seek(curPos - readAgainStr.Substring(index + 1).Utf8Length(), SeekOrigin.Begin);
                else
                    _log.Seek(curPos - readAgainStr.Utf8Length(), SeekOrigin.Begin);
            }
            if (text.Length > 0)
                _log.Write(text.ToUtf8());
        }

        private static void outputLine(string text)
        {
            output(text);
            output(Environment.NewLine);
        }

        static void runner_StdoutText(string data)
        {
            output(data);
        }

        static void runner_StderrText(string data)
        {
            output("STDERR: ");
            output(data);
        }
    }
}
