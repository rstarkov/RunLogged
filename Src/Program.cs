using System.Diagnostics;
using System.Net.Mail;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using RT.CommandLine;
using RT.Emailer;
using RT.Serialization.Settings;
using RT.Util;
using RT.Util.Consoles;
using RT.Util.Controls;
using RT.Util.ExtensionMethods;

namespace RunLogged;

class Program
{
    private static SettingsFile<Settings> _settingsFile;
    private static Settings _settings;
    private static CmdLineArgs _args;
    private static string _originalCurrentDirectory;
    private static NotifyIcon _trayIcon;
    private static string _tooltipPrefix = "", _tooltipLastLine = "";
    private static CommandRunner _runner;
    private static Stream _log;
    private static long _logStartOffset;
    private static Thread _readingThread;
    private static int _readingThreadExitCode = ExitCode.ErrorInternal;
    private static ToolStripMenuItem _miPause, _miResume;

    private static FileStream _shadowLock;

    public static Icon ProgramIcon { get { return _trayIcon == null ? null : _trayIcon.Icon; } }

    static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--post-build-check")
            return RT.PostBuild.PostBuildChecker.RunPostBuildChecks(args[1], Assembly.GetExecutingAssembly());

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

#if CONSOLE
        Console.OutputEncoding = System.Text.Encoding.UTF8;
#endif

        return Ut.RunMain(() =>
        {
            _args = CommandLineParser.ParseOrWriteUsageToConsole<CmdLineArgs>(args);
            if (_args == null)
                return ExitCode.ErrorParsingCommandLine;
            try
            {
                var result = mainCore(args);
                return result;
            }
            catch (TellUserException e)
            {
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
        },
        e =>
        {
            var message = "\n" + "An internal error has occurred in RunLogged: ".Color(ConsoleColor.Red);
            foreach (var ex in e.SelectChain(ex => ex.InnerException))
            {
                message += "\n" + ex.GetType().Name.Color(ConsoleColor.Yellow) + ": " + ex.Message.Color(ConsoleColor.White);
                message += "\n" + ex.StackTrace;
            }
            tellUser(message);
            return ExitCode.ErrorInternal;
        });
    }

    private static void tellUser(ConsoleColoredString message)
    {
#if CONSOLE
        ConsoleUtil.WriteLine(message);
#else
        new RT.Util.Forms.DlgMessage() { Message = message.ToString(), Type = RT.Util.Forms.DlgType.Warning, Font = new System.Drawing.Font("Consolas", 9) }.Show();
#endif
    }

    private static int mainCore(string[] args)
    {
        Mutex mutex = null;

        if (_args.MutexName != null && !_args.ShadowCopy)
        {
            var sid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            var mutexsecurity = new MutexSecurity();
            mutexsecurity.AddAccessRule(new MutexAccessRule(sid, MutexRights.FullControl, AccessControlType.Allow));
            mutexsecurity.AddAccessRule(new MutexAccessRule(sid, MutexRights.ChangePermissions, AccessControlType.Deny));
            mutexsecurity.AddAccessRule(new MutexAccessRule(sid, MutexRights.Delete, AccessControlType.Deny));
            mutex = MutexAcl.Create(false, _args.MutexName, out var created, mutexsecurity);
            if (!created)
                throw new TellUserException($"The mutex \"{_args.MutexName}\" is already acquired by another application.", returnCode: ExitCode.MutexInUse, silent: true);
        }

        string destPath = null;
        if (_args.ShadowCopy)
        {
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
            var destAssembly = Path.Combine(destPath, Path.GetFileName(Environment.ProcessPath));
            var newArgs = args.Where(arg => arg != "--shadowcopy").Select(arg => arg.Any(ch => "&()[]{}^=;!'+,`~ ".Contains(ch)) ? "\"" + arg + "\"" : arg).JoinString(" ");
            File.Copy(Environment.ProcessPath, destAssembly);
            foreach (var dep in source.GetReferencedAssemblies())
            {
                var file = new[] { ".dll", ".exe" }.Select(ext => Path.Combine(Path.GetDirectoryName(Environment.ProcessPath), dep.Name + ext)).FirstOrDefault(fn => File.Exists(fn));
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

        _settingsFile = new SettingsFileXml<Settings>("RunLogged", SettingsLocation.MachineLocal);
        _settings = _settingsFile.Settings;
        _settingsFile.Save();

        _originalCurrentDirectory = Directory.GetCurrentDirectory();

        Console.CancelKeyPress += (_, __) => { processCtrlC(); };
        WinAPI.SetConsoleCtrlHandler(_ => { processCtrlC(); return true; }, true);

        if (_args.LogFilename != null)
            try
            {
                try { Directory.CreateDirectory(Path.GetDirectoryName(_args.LogFilename)); }
                catch { }
                _log = File.Open(_args.LogFilename, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
                _log.Seek(0, SeekOrigin.End);
                _logStartOffset = _log.Position;

                new Thread(threadLogFlusher) { IsBackground = true }.Start();
            }
            catch (Exception e)
            {
                throw new TellUserException($"Could not open the log file for writing. File \"{_args.LogFilename}\".\n{e.Message}", returnCode: ExitCode.CannotOpenLogFile);
            }

        _runner = new CommandRunner();
        _runner.SetCommand(_args.CommandToRun);
        _runner.WorkingDirectory = Directory.GetCurrentDirectory();
        _runner.StdoutText += runner_StdoutText;
        _runner.StderrText += runner_StderrText;

        _readingThread = new Thread(readingThread);
        _readingThread.Start();

        if (_args.TrayIcon != null)
        {
            _tooltipPrefix = Path.GetFileName(_args.CommandToRun[0].Split(' ').FirstOrDefault("RunLogged").SubstringSafe(0, 50));
            _trayIcon = new NotifyIcon
            {
                Icon = new System.Drawing.Icon(_args.TrayIcon),
                ContextMenuStrip = new ContextMenuStrip(),
                Visible = true,
                Text = _tooltipPrefix,
            };
            _miPause = (ToolStripMenuItem) _trayIcon.ContextMenuStrip.Items.Add("&Pause for...", null, pause);
            _trayIcon.ContextMenuStrip.Items.Add("E&xit", null, (_, __) => { _runner.Abort(); });
            _trayIcon.ContextMenuStrip.Renderer = new NativeToolStripRenderer();
            _trayIcon.ContextMenuStrip.Opening += updateResumeMenu;
            _runner.CommandResumed += () => { updateResumeMenu(); };
        }

        Application.Run();

        GC.KeepAlive(mutex);
        return _readingThreadExitCode;
    }

    private static void pause(object _ = null, EventArgs __ = null)
    {
        using var dlg = new PauseForDlg(_settings.PauseForDlgSettings);
        var result = dlg.ShowDialog();
        _settingsFile.Save();
        if (result == DialogResult.Cancel)
            return;
        _runner.Pause(dlg.TimeSpan);
        updateResumeMenu();
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
                    (___, ____) => { _runner.ResumePaused(); });
                _trayIcon.ContextMenuStrip.Items.Insert(_trayIcon.ContextMenuStrip.Items.IndexOf(_miPause) + 1, _miResume);
            }
            _miResume.Text = "&Resume" + (_runner.PausedUntil == DateTime.MaxValue ? "" : $" ({niceTimeSpan(_runner.PausedUntil.Value - DateTime.UtcNow)} left)");
        }
    }

    private static string niceTimeSpan(TimeSpan span)
    {
        if (span.TotalDays > 1.5)
            return $"{span.TotalDays:0} days";
        else if (span.TotalHours > 1.5)
            return $"{span.TotalHours:0} hours";
        else if (span.TotalMinutes >= 1.5)
            return $"{span.TotalMinutes:0} minutes";
        else
            return $"{span.TotalSeconds:0} seconds";
    }

    private static void cleanup()
    {
        if (_settings != null)
            _settingsFile.Save();

        _log?.Dispose();
        _trayIcon?.Dispose();

        if (_originalCurrentDirectory != null)
            try { Directory.SetCurrentDirectory(_originalCurrentDirectory); }
            catch { } // the script could have theoretically CD'd out of it and deleted it, so don't crash if that happens.

        if (_args != null && _args.WipeAfterShutdown != null)
        {
            var batchFile = new StringBuilder();
            batchFile.AppendLine(@"@echo off");
            batchFile.AppendLine(@":wait");
            batchFile.AppendLine(@"set HAS=No");
            batchFile.AppendLine($@"for /f %%A in ('tasklist /fi ""PID eq {Environment.ProcessId}"" /nh /fo csv ^|find ""{Environment.ProcessId}""') do set HAS=Yes");
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

    private static void processCtrlC()
    {
        _runner?.Abort();
        _readingThread?.Join();
        cleanup(); // must do this because Ctrl+C ends the program without running "finally" clauses...
    }

    private static void readingThread()
    {
        var startTime = DateTime.UtcNow;
        lock (_log ?? new object()) // if logging is disabled then we don't care about the log
        {
            if (_log != null && _logStartOffset > 0)
            {
                for (int i = 0; i < 3; i++)
                    _log.Write(Environment.NewLine.ToUtf8());
                _logStartOffset = _log.Position;
            }
            outputLine("************************************************************************");
            outputLine($"****** RunLogged v[DEV] invoked at {startTime.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
            outputLine($"****** Command: |{_runner.Command}|");
            outputLine($"****** CurDir: |{Directory.GetCurrentDirectory()}|");
            outputLine($"****** LogTo: {(_args.LogFilename == null ? "<none>" : $"|{_args.LogFilename}|")}");
        }

        if (_args.MaxDurationSeconds != null)
        {
            var aborterThread = new Thread(() =>
            {
                Thread.Sleep(TimeSpan.FromSeconds(_args.MaxDurationSeconds.Value));
                outputLine($"****** time limit of {_args.MaxDurationSeconds.Value:#,0} seconds reached; aborting.");
                _runner.Abort();
            });
            aborterThread.IsBackground = true; // kill thread on process exit
            aborterThread.Start();
        }

        _runner.Start();
        _runner.EndedWaitHandle.WaitOne();

        var endTime = DateTime.UtcNow;
        if (_runner.State == CommandRunnerState.Aborted)
        {
            outputLine($"****** aborted at {endTime.ToLocalTime():yyyy-MM-dd HH:mm:ss} (ran for {(endTime - startTime).TotalSeconds:#,0.0} seconds)");
            _readingThreadExitCode = ExitCode.Aborted;
            if (_args.Email != null)
            {
                outputLine("****** emailing failure log");
                emailFailureLog();
            }
            pingUrl(ok: false, msg: $"aborted; ran for {(endTime - startTime).TotalSeconds:#,0.0} seconds");
        }
        else
        {
            outputLine($"****** completed at {endTime.ToLocalTime():yyyy-MM-dd HH:mm:ss} (ran for {(endTime - startTime).TotalSeconds:#,0.0} seconds)");

            bool success = _runner.ExitCode == 0;
            if (_args.SuccessCodesParsed != null)
                success = _args.SuccessCodesParsed.Any(range => _runner.ExitCode >= range.Item1 && _runner.ExitCode <= range.Item2);
            else if (_args.FailureCodesParsed != null)
                success = !_args.FailureCodesParsed.Any(range => _runner.ExitCode >= range.Item1 && _runner.ExitCode <= range.Item2);

            outputLine($"****** exit code: {_runner.ExitCode} ({(success ? "success" : "failure")})");
            pingUrl(ok: success, msg: $"{(success ? "succeeded" : "failed")}; exit code {_runner.ExitCode}; ran for {(endTime - startTime).TotalSeconds:#,0.0} seconds");

            if (!success && _args.Email != null)
            {
                outputLine("****** emailing failure log");
                emailFailureLog();
            }

            _readingThreadExitCode = _args.IndicateSuccess ? (success ? ExitCode.Success : ExitCode.Failure) : _runner.ExitCode;
        }

        if (_log != null)
            lock (_log)
                _log.Flush();

        Application.Exit();
    }

    private static void emailFailureLog()
    {
        string text = _args.LogFilename == null ? "<log not available as file logging was disabled>" : "<failed to read the log>";
        if (_args.LogFilename != null)
            try
            {
                lock (_log)
                    _log.Flush();

                using var log = File.Open(_args.LogFilename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var logreader = new StreamReader(log);
                log.Seek(_logStartOffset, SeekOrigin.Begin);
                text = logreader.ReadToEnd();
            }
            catch { }
        Emailer.SendEmail(
            to: [new MailAddress(_args.Email)],
            subject: $"Failure: {_runner.Command.SubstringSafe(0, 50)}",
            bodyPlain: text,
            account: _settings.EmailerAccount,
            fromName: "RunLogged"
        );
    }

    private static void pingUrl(bool ok, string msg)
    {
        if (_args.PingUrl == null)
            return;
        var url = _args.PingUrl
            .Replace("{{{ok}}}", ok ? "1" : "0")
            .Replace("{{{msg}}}", msg.UrlEscape());
        try
        {
            using var hc = new HttpClient();
            hc.GetAsync(url).GetAwaiter().GetResult();
            outputLine($"****** pinged url: {url}");
        }
        catch (Exception e)
        {
            outputLine($"****** failed pinging url: {url} ({e.Message})");
        }
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
        if (_log != null)
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
                        if ((readAgain[0] & 0xc0) != 0x80 && ((readAgainStr = readAgain.FromUtf8()).Length == backspaces || readAgainStr.Contains('\n')))
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

    private static ManualResetEvent _logFlushNeeded = new(false);
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
        if (data == "")
            return;
        output(data);
    }

    static void runner_StderrText(string data)
    {
        if (data == "")
            return;
        output("STDERR: ");
        output(data);
    }
}
