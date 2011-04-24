using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using RT.Util;
using RT.Util.ExtensionMethods;

namespace RunLogged
{
    class ProcessRunner
    {
        private Process _process;
        private ProcessStartInfo _startInfo;
        private string _tempStdout, _tempStderr;
        private Stream _streamStdout, _streamStderr;
        private Decoder _utf8Stdout, _utf8Stderr;
        private Thread _thread;
        private ManualResetEventSlim _exited = new ManualResetEventSlim();

        public event Action ProcessExited;
        public event Action<byte[]> StdoutData;
        public event Action<byte[]> StderrData;
        public event Action<string> StdoutText;
        public event Action<string> StderrText;
        public event Action ProcessResumed;

        public string LastRawCommandLine { get; private set; }
        public int LastExitCode { get; private set; }
        public bool LastAborted { get; private set; }

        public ProcessRunner(string[] toRun, string workingDir)
        {
            _tempStdout = Path.GetTempFileName();
            _tempStderr = Path.GetTempFileName();

            _startInfo = new ProcessStartInfo();
            _startInfo.FileName = @"cmd.exe";
            LastRawCommandLine = escapeCmdline(toRun).JoinString(" ");
            _startInfo.Arguments = "/C " + LastRawCommandLine + @" >{0} 2>{1}".Fmt(_tempStdout, _tempStderr);
            _startInfo.WorkingDirectory = workingDir;
            _startInfo.RedirectStandardInput = false;
            _startInfo.RedirectStandardOutput = false;
            _startInfo.RedirectStandardError = false;
            _startInfo.CreateNoWindow = true;
            _startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            _startInfo.UseShellExecute = false;

            //StartInfo.LoadUserProfile = false;
            //StartInfo.Domain = "";
            //StartInfo.UserName = "";
            //StartInfo.Password = "";
        }

        private IEnumerable<string> escapeCmdline(IEnumerable<string> args)
        {
            foreach (var arg in args)
            {
                string newarg = arg;
                if (arg.Contains(" "))
                    newarg = "\"" + arg + "\"";
                yield return newarg;
            }
        }

        private void thread()
        {
            // There's no indication that Process members are thread-safe, so use it on this thread exclusively.
            _process = new Process();
            _process.EnableRaisingEvents = true;
            _process.StartInfo = _startInfo;
            _process.Start();
            _utf8Stdout = Encoding.UTF8.GetDecoder();
            _utf8Stderr = Encoding.UTF8.GetDecoder();
            LastExitCode = -1;
            LastAborted = false;

            while (!_process.HasExited)
            {
                checkOutputs();
                Thread.Sleep(50);
            }
            checkOutputs();

            LastExitCode = _process.ExitCode;

            if (ProcessExited != null)
                ProcessExited();

            if (_streamStdout != null)
                _streamStdout.Dispose();
            if (_streamStderr != null)
                _streamStderr.Dispose();
            File.Delete(_tempStdout);
            File.Delete(_tempStderr);
            _startInfo = null;
            _tempStdout = _tempStderr = null;
            _streamStdout = _streamStderr = null;
            _utf8Stdout = _utf8Stderr = null;
            _process = null;
            _thread = null;
            _exited.Set();
        }

        private void checkOutputs()
        {
            checkOutput(_tempStdout, ref _streamStdout, _utf8Stdout, StdoutData, StdoutText);
            checkOutput(_tempStderr, ref _streamStderr, _utf8Stderr, StderrData, StderrText);
        }

        private void checkOutput(string filename, ref Stream stream, Decoder utf8, Action<byte[]> dataEvent, Action<string> textEvent)
        {
            if (stream == null)
            {
                try { stream = File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); }
                catch { }
            }

            if (stream != null)
            {
                var newBytes = stream.ReadAllBytes();
                if (newBytes.Length > 0)
                {
                    if (dataEvent != null)
                        dataEvent(newBytes);
                    if (textEvent != null)
                    {
                        var count = utf8.GetCharCount(newBytes, 0, newBytes.Length);
                        char[] buffer = new char[count];
                        int charsObtained = utf8.GetChars(newBytes, 0, newBytes.Length, buffer, 0);
                        if (charsObtained > 0)
                            textEvent(new string(buffer));
                    }
                }
            }
        }

        public void Start()
        {
            if (_process != null)
                throw new InvalidOperationException("Cannot start another instance of the process while the current one is running.");

            _exited.Reset();
            _thread = new Thread(thread);
            _thread.Start();
        }

        public void Stop()
        {
            if (_process == null)
                return;
            LastAborted = true;
            _process.KillWithChildren();
        }

        public void WaitForExit()
        {
            if (_exited != null)
                _exited.Wait();
        }

        private Timer _pauseTimer;
        private DateTime? _pauseTimerDue;

        /// <summary>Gets the time at which the process will wake up again, or null if it is set to be permanently suspended until the timer is manually reset.</summary>
        public DateTime? PausedUntil { get { return _pauseTimerDue; } }

        public void PauseFor(TimeSpan pauseFor)
        {
            if (_process == null)
                return;

            if (_pauseTimer != null)
            {
                // The process is already paused; just update the due time
                _pauseTimer.Change(pauseFor, TimeSpan.FromMilliseconds(-1));
            }
            else
            {
                pauseProcess();

                // Set a timer to wake the process up again
                _pauseTimer = new Timer(wakeUpProcess, null, pauseFor, TimeSpan.FromMilliseconds(-1));
            }
            _pauseTimerDue = pauseFor == TimeSpan.FromMilliseconds(-1) ? (DateTime?) null : DateTime.UtcNow + pauseFor;
        }

        private void pauseProcess()
        {
            foreach (ProcessThread thr in _process.Threads)
            {
                IntPtr pThread = WinAPI.OpenThread(WinAPI.ThreadAccess.SUSPEND_RESUME, false, (uint) thr.Id);
                if (pThread == IntPtr.Zero)
                    continue;
                WinAPI.SuspendThread(pThread);
                WinAPI.CloseHandle(pThread);
            }
        }

        public void ResumePausedProcess() { wakeUpProcess(); }

        private void wakeUpProcess(object _ = null)
        {
            _pauseTimerDue = null;
            _pauseTimer.Dispose();
            _pauseTimer = null;

            foreach (ProcessThread thr in _process.Threads)
            {
                IntPtr pThread = WinAPI.OpenThread(WinAPI.ThreadAccess.SUSPEND_RESUME, false, (uint) thr.Id);
                if (pThread == IntPtr.Zero)
                    continue;
                WinAPI.ResumeThread(pThread);
                WinAPI.CloseHandle(pThread);
            }

            if (ProcessResumed != null)
                ProcessResumed();
        }
    }
}
