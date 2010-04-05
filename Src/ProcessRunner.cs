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
    class EventArgs<T> : EventArgs
    {
        public T Data { get; private set; }
        public EventArgs(T data)
        {
            Data = data;
        }
    }

    class ProcessRunner
    {
        private Process _process;
        private ProcessStartInfo _startInfo;
        private string _tempStdout, _tempStderr;
        private Stream _streamStdout, _streamStderr;
        private Decoder _utf8Stdout, _utf8Stderr;
        private Thread _thread;
        private ThreadExiter _exiter;

        public event EventHandler ProcessExited;
        public event EventHandler<EventArgs<byte[]>> StdoutData;
        public event EventHandler<EventArgs<byte[]>> StderrData;
        public event EventHandler<EventArgs<string>> StdoutText;
        public event EventHandler<EventArgs<string>> StderrText;

        public int LastExitCode { get; private set; }

        public ProcessRunner(string[] toRun, string workingDir)
        {
            _tempStdout = Path.GetTempFileName();
            _tempStderr = Path.GetTempFileName();

            _startInfo = new ProcessStartInfo();
            _startInfo.FileName = @"cmd.exe";
            _startInfo.Arguments = "/C " + escapeCmdline(toRun).JoinString(" ") + @" >{0} 2>{1}".Fmt(_tempStdout, _tempStderr);
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
                string newarg = arg.Replace("\\", "\\\\").Replace("\"", "\\\"");
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

            while (!_process.HasExited)
            {
                checkOutputs();
                Thread.Sleep(50);
            }
            checkOutputs();

            LastExitCode = _process.ExitCode;

            if (ProcessExited != null)
                ProcessExited(this, EventArgs.Empty);

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
            _exiter.SignalExited();
            _exiter = null;
        }

        private void checkOutputs()
        {
            checkOutput(_tempStdout, ref _streamStdout, _utf8Stdout, StdoutData, StdoutText);
            checkOutput(_tempStderr, ref _streamStderr, _utf8Stderr, StderrData, StderrText);
        }

        private void checkOutput(string filename, ref Stream stream, Decoder utf8, EventHandler<EventArgs<byte[]>> dataEvent, EventHandler<EventArgs<string>> textEvent)
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
                        dataEvent(this, new EventArgs<byte[]>(newBytes));
                    if (textEvent != null)
                    {
                        var count = utf8.GetCharCount(newBytes, 0, newBytes.Length);
                        char[] buffer = new char[count];
                        int charsObtained = utf8.GetChars(newBytes, 0, newBytes.Length, buffer, 0);
                        if (charsObtained > 0)
                            textEvent(this, new EventArgs<string>(new string(buffer)));
                    }
                }
            }
        }

        public void Start()
        {
            if (_process != null)
                throw new InvalidOperationException("Cannot start another instance of the process while the current one is running.");

            _exiter = new ThreadExiter();
            _thread = new Thread(thread);
            _thread.Start();
        }

        public void WaitForExit()
        {
            if (_exiter != null)
                _exiter.WaitExited();
        }
    }
}
