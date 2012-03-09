using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using RT.Util;
using RT.Util.CommandLine;
using RT.Util.Consoles;
using RT.Util.Controls;
using RT.Util.ExtensionMethods;

namespace RunLogged
{
    class Program
    {
        private static Settings _settings;
        private static CmdLineArgs _args;
        private static string _originalCurrentDirectory;
        private static NotifyIcon _trayIcon;
        private static string _tooltipPrefix = "", _tooltipLastLine = "";
        private static ProcessRunner _runner;
        private static Stream _log;
        private static long _logStartOffset;
        private static Thread _readingThread;
        private static ToolStripMenuItem _miPause, _miResume;

        private static FileStream _shadowLock;

        public static Icon ProgramIcon { get { return _trayIcon == null ? null : _trayIcon.Icon; } }

        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

#if CONSOLE
            Console.OutputEncoding = System.Text.Encoding.UTF8;
#endif

            EqatecAnalytics.Settings = new EqatecAnalyticsSettings("518CC4A6EEFC454C8AE502FFF005B4CB");
            EqatecAnalytics.ReturnOnUnhandled = -80003;
            EqatecAnalytics.UnhandledException = e =>
            {
                var message = "\n" + "An internal error has occurred in RunLogged: ".Color(ConsoleColor.Red);
                foreach (var ex in e.SelectChain(ex => ex.InnerException))
                {
                    message += "\n" + ex.GetType().Name.Color(ConsoleColor.Yellow) + ": " + ex.Message.Color(ConsoleColor.White);
                    message += "\n" + ex.StackTrace;
                }
                tellUser(message);
            };
#if CONSOLE
            EqatecAnalytics.RunKind = "Runs.Console";
#else
            EqatecAnalytics.RunKind = "Runs.Windowless";
#endif
            return EqatecAnalytics.RunMain(() =>
            {
                try
                {
                    var result = mainCore(args);
                    return result;
                }
                catch (CommandLineParseException e)
                {
                    EqatecAnalytics.Monitor.TrackFeature("Problem.CommandLineParse");
                    var message = "\n" + e.GenerateHelp(null, wrapToWidth());
                    if (!e.WasCausedByHelpRequest)
                        message += "\n" + e.GenerateErrorText(null, wrapToWidth());
                    tellUser(message);
                    return -80001;
                }
                catch (TellUserException e)
                {
                    EqatecAnalytics.Monitor.TrackFeature("Problem.TellUser");
                    var message = "\n" + "Error: ".Color(ConsoleColor.Red) + e.Message;
#if CONSOLE
                    tellUser(message);
#else
                    if (!e.Silent)
                        tellUser(message);
#endif
                    return e.ReturnCode;
                }
                finally
                {
                    cleanup();
                }
            });
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
            Mutex mutex = null;

            if (_args.MutexName != null && !_args.ShadowCopy)
            {
                EqatecAnalytics.Monitor.TrackFeature("Feature.Mutex");
                var sid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
                var mutexsecurity = new MutexSecurity();
                mutexsecurity.AddAccessRule(new MutexAccessRule(sid, MutexRights.FullControl, AccessControlType.Allow));
                mutexsecurity.AddAccessRule(new MutexAccessRule(sid, MutexRights.ChangePermissions, AccessControlType.Deny));
                mutexsecurity.AddAccessRule(new MutexAccessRule(sid, MutexRights.Delete, AccessControlType.Deny));
                bool created;
                mutex = new Mutex(false, _args.MutexName, out created, mutexsecurity);
                if (!created)
                    throw new TellUserException("The mutex \"{0}\" is already acquired by another application.".Fmt(_args.MutexName), returnCode: -80003, silent: true);
            }

            string destPath = null;
            if (_args.ShadowCopy)
            {
                EqatecAnalytics.Monitor.TrackFeature("Feature.ShadowCopy");
                var source = Assembly.GetExecutingAssembly();
                int i = 0;
                while (true)
                {
                    i += Rnd.Next(65536);
                    destPath = Path.Combine(Path.GetTempPath(), "RunLogged-" + i);
                    if (Directory.Exists(destPath))
                        continue;
                    try
                    {
                        Directory.CreateDirectory(destPath);
                        _shadowLock = File.Open(Path.Combine(destPath, "lock"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
                        // Now verify that another copy of RunLogged hasn't copied itself over just before we created the directory,
                        // which is theoretically possible
                        var entries = Directory.GetFileSystemEntries(destPath);
                        if (entries.Length != 1 || !entries[0].EndsWith("lock"))
                            continue;
                        break;
                    }
                    catch { }
                }
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
                Process.Start(new ProcessStartInfo
                {
                    FileName = destAssembly,
                    Arguments = "--wipe-after-shutdown \"" + destPath + "\" " + newArgs,
                    UseShellExecute = true,
                    // shell execute = true: a new console window will be created
                    // shell execute = false: will attach to existing console window, but because the original process exits, this
                    //     fools cmd.exe into becoming interactive again while RunLogged is still logging to it. This looks very broken.
                    //     Hence the least evil seems to be to just create a new console window; it's unlikely that someone who wants
                    //     to use RunLogged interactively would specify --shadowcopy anyway.
                });

                return 0;
            }

            SettingsUtil.LoadSettings(out _settings);
            _settings.SaveQuiet();

            _originalCurrentDirectory = Directory.GetCurrentDirectory();

            Console.CancelKeyPress += processCtrlC;

            try
            {
                try { Directory.CreateDirectory(Path.GetDirectoryName(_args.LogFilename)); }
                catch { }
                _log = File.Open(_args.LogFilename, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
                _logStartOffset = Program._log.Position;

                var t = new Thread(threadLogFlusher);
                t.IsBackground = true;
                t.Start();
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
                _tooltipPrefix = Path.GetFileName(_args.CommandToRun[0].Split(' ').FirstOrDefault("RunLogged").SubstringSafe(0, 50));
                EqatecAnalytics.Monitor.TrackFeature("Feature.TrayIcon");
                _trayIcon = new NotifyIcon
                {
                    Icon = new System.Drawing.Icon(_args.TrayIcon),
                    ContextMenuStrip = new ContextMenuStrip(),
                    Visible = true,
                    Text = _tooltipPrefix,
                };
                _miPause = (ToolStripMenuItem) _trayIcon.ContextMenuStrip.Items.Add("&Pause for...", null, pause);
                _trayIcon.ContextMenuStrip.Items.Add("E&xit", null, (_, __) => { EqatecAnalytics.Monitor.TrackFeature("Feature.TrayMenu.Exit"); _runner.Stop(); });
                _trayIcon.ContextMenuStrip.Renderer = new NativeToolStripRenderer();
                _trayIcon.ContextMenuStrip.Opening += updateResumeMenu;
                _runner.ProcessResumed += () => { updateResumeMenu(); };
            }

            if (_args.Email != null)
                EqatecAnalytics.Monitor.TrackFeature("Feature.EmailFailure.Activated");

            Application.Run();

            GC.KeepAlive(mutex);
            return _runner.LastExitCode;
        }

        private static void pause(object _ = null, EventArgs __ = null)
        {
            EqatecAnalytics.Monitor.TrackFeature("Feature.TrayMenu.Pause");
            using (var dlg = new PauseForDlg(_settings.PauseForDlgSettings))
            {
                var result = dlg.ShowDialog();
                _settings.SaveQuiet();
                if (result == DialogResult.Cancel)
                    return;
                EqatecAnalytics.Monitor.TrackFeature("Feature.Pause.Start");
                EqatecAnalytics.Monitor.TrackFeatureValue("Feature.Pause.Time", (long) dlg.TimeSpan.TotalSeconds);
                _runner.PauseFor(dlg.TimeSpan);
                updateResumeMenu();
            }
        }

        private static void updateResumeMenu(object _ = null, EventArgs __ = null)
        {
            if (_runner.PausedUntil == null)
            {
                if (_miResume != null)
                {
                    _trayIcon.ContextMenuStrip.Items.Remove(_miResume);
                    _miResume = null;
                }
            }
            else
            {
                if (_miResume == null)
                {
                    _miResume = new ToolStripMenuItem(
                        "text",
                        null,
                        (___, ____) => { EqatecAnalytics.Monitor.TrackFeature("Feature.TrayMenu.Resume"); _runner.ResumePausedProcess(); });
                    _trayIcon.ContextMenuStrip.Items.Insert(_trayIcon.ContextMenuStrip.Items.IndexOf(_miPause) + 1, _miResume);
                }
                _miResume.Text = "&Resume" + (_runner.PausedUntil == DateTime.MaxValue ? "" : " ({0} left)".Fmt(niceTimeSpan(_runner.PausedUntil.Value - DateTime.UtcNow)));
            }
        }

        private static string niceTimeSpan(TimeSpan span)
        {
            if (span.TotalDays > 1.5)
                return "{0:0} days".Fmt(span.TotalDays);
            else if (span.TotalHours > 1.5)
                return "{0:0} hours".Fmt(span.TotalHours);
            else if (span.TotalMinutes >= 1.5)
                return "{0:0} minutes".Fmt(span.TotalMinutes);
            else
                return "{0:0} seconds".Fmt(span.TotalSeconds);
        }

        private static void cleanup()
        {
            if (_settings != null)
                _settings.SaveQuiet();

            if (_log != null)
                _log.Dispose();
            if (_trayIcon != null)
                _trayIcon.Dispose();

            if (_originalCurrentDirectory != null)
                try { Directory.SetCurrentDirectory(_originalCurrentDirectory); }
                catch { } // the script could have theoretically CD'd out of it and deleted it, so don't crash if that happens.

            if (_args != null && _args.WipeAfterShutdown != null)
            {
                var batchFile = new StringBuilder();
                batchFile.AppendLine(@"@echo off");
                batchFile.AppendLine(@":wait");
                batchFile.AppendLine(@"set HAS=No");
                batchFile.AppendLine(@"for /f %%A in ('tasklist /fi ""PID eq {0}"" /nh /fo csv ^|find ""{0}""') do set HAS=Yes".Fmt(Process.GetCurrentProcess().Id));
                batchFile.AppendLine(@"ping localhost -n 2 >nul 2>nul");
                batchFile.AppendLine(@"if '%HAS%'=='Yes' goto wait");
                batchFile.AppendLine(@"cd \");
                // Note: the command that deletes the batch file needs to be the last command in the batch file.
                batchFile.AppendLine("rd /s /q \"" + _args.WipeAfterShutdown + "\"");
                var batchFilePath = Path.Combine(_args.WipeAfterShutdown, "RunLogged_self_destruct.bat");
                File.WriteAllText(batchFilePath, batchFile.ToString());
                Process.Start(new ProcessStartInfo
                {
                    FileName = batchFilePath,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
            }
        }

        private static void processCtrlC(object sender, ConsoleCancelEventArgs e)
        {
            EqatecAnalytics.Monitor.TrackFeature("Feature.CtrlC");
            if (_runner != null)
                _runner.Stop();
            if (_readingThread != null)
                _readingThread.Join();
            cleanup(); // must do this because Ctrl+C ends the program without running "finally" clauses...
        }

        private static void readingThread()
        {
            lock (_log)
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
            }

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

            lock (_log)
                _log.Flush();

            if (_runner.LastExitCode != 0 && _args.Email != null && !_runner.LastAborted)
                emailFailureLog();

            Application.Exit();
        }

        private static void emailFailureLog()
        {
            EqatecAnalytics.Monitor.TrackFeature("Feature.EmailFailure.Used");
            string text = "<failed to read the log>";
            try
            {
                using (var log = File.Open(_args.LogFilename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var logreader = new StreamReader(log))
                {
                    log.Seek(Program._logStartOffset, SeekOrigin.Begin);
                    text = logreader.ReadToEnd();
                }
            }
            catch { }
            Emailer.SendEmail(
                to: new[] { new MailAddress(_args.Email) },
                subject: "Failure: {0}".Fmt(_runner.LastRawCommandLine.SubstringSafe(0, 50)),
                bodyPlain: text,
                account: _settings.EmailerAccount,
                fromName: "RunLogged"
			);
        }

        private static void output(string text)
        {
            // Update the last line of the log as visible in the tray icon tooltip
            if (_trayIcon != null)
            {
                _tooltipLastLine += text;
                int backspaces = 0;
                var chars = new List<char>();
                for (int i = _tooltipLastLine.Length - 1; i >= 0; i--)
                {
                    if (_tooltipLastLine[i] == '\b')
                        backspaces++;
                    else
                    {
                        if (_tooltipLastLine[i] == '\r' || _tooltipLastLine[i] == '\n')
                            break;
                        if (backspaces == 0)
                            chars.Add(_tooltipLastLine[i]);
                        else if (_tooltipLastLine[i] < 0xDC00 || _tooltipLastLine[i] > 0xDFFF)
                            backspaces--;
                    }
                }
                chars.Reverse();
                _tooltipLastLine = new string(chars.ToArray());
                var tip = _tooltipPrefix + (_tooltipLastLine.Trim() == "" ? "" : (" - " + _tooltipLastLine.Trim()));
                if (tip.Length > 63)
                    tip = _tooltipPrefix.Replace(".exe", "") + (_tooltipLastLine.Trim() == "" ? "" : (" - " + _tooltipLastLine.Trim()));
                if (tip.Length > 63)
                    tip = tip.SubstringSafe(0, 60) + "...";
                _trayIcon.Text = tip;
            }

            // Display in the real console
#if CONSOLE
            Console.Write(text);
#endif

            // Write to the log file
            lock (_log)
            {
                int index;
                while (text.Length > 0 && (index = text.IndexOf('\b')) != -1)
                {
                    if (index > 0)
                    {
                        _log.Write(text.Substring(0, index).ToUtf8());
                        _logFlushNeeded.Set();
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
                {
                    _log.Write(text.ToUtf8());
                    _logFlushNeeded.Set();
                }
            }
        }

        private static ManualResetEvent _logFlushNeeded = new ManualResetEvent(false);
        private static DateTime _logLastFlush;

        private static void threadLogFlusher()
        {
            while (true)
            {
                _logFlushNeeded.WaitOne();
                var delay = _logLastFlush + TimeSpan.FromSeconds(2.5) - DateTime.UtcNow;
                if (delay > TimeSpan.Zero)
                    Thread.Sleep(delay);
                lock (_log)
                {
                    _logFlushNeeded.Reset();
                    try { _log.Flush(); }
                    catch { } // the log file might have disappeared or something...
                    _logLastFlush = DateTime.UtcNow;
                }
            }
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
