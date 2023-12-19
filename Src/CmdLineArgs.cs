using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using RT.CommandLine;
using RT.Util;
using RT.Util.Consoles;
using RT.Util.ExtensionMethods;

namespace RunLogged
{
    [DocumentationRhoML(@"Runs the specified command and logs all the output to a timestamped log file.")]
    class CmdLineArgs : ICommandLineValidatable
    {
#pragma warning disable 649 // Field is never assigned to, and will always have its default value null
        [Option("--cd")]
        [Documentation("Optionally changes the working directory while executing the command.")]
        public string WorkingDir;

        [Option("--log")]
        [DocumentationRhoML("Log file name pattern. May be relative to {option}--cd{} {field}WorkingDir{}. The timestamp YYYY-MM-DD is substituted in place of any occurrences of \"{{}\" in {field}LogFilename{}.\n\nWhere the {option}--log{} argument is not specified, the log filename will be constructed from the command to be executed, a timestamp and a .log extension.\n\nIf the log file already exists, it will be appended to. The directory to contain the log file is created automatically if necessary.")]
        public string LogFilename;

        [Option("--email")]
        [DocumentationRhoML("If the specified command doesn't succeed, send the log to this email address. See also {option}--success-codes{}. Other email-related settings should be configured in the settings file at {h}%ProgramData%\\RunLogged{}.")]
        public string Email;

        [Option("--trayicon")]
        [Documentation("Shows a tray icon while the specified command is running, with options to abort the command, pause its execution, and view the last log line in a tooltip. Specifies the path and filename of an ico file to use for the tray icon.")]
        public string TrayIcon;

        [Option("--shadowcopy")]
        [Documentation("Copies RunLogged to your user temporary directory and runs it from there. RunLogged will delete the temporary copy when exiting.")]
        public bool ShadowCopy;

        [Option("--mutex")]
        [Documentation("Acquires a mutex with the specified name and holds it until exit. If the mutex is already acquired, exits with error code -80001 (silently in the Windowless variant). Prefix with \"{h}Global\\{}\" to make the mutex visible to other user accounts.")]
        public string MutexName;

        [Option("--success-codes")]
        [DocumentationRhoML("Specifies which exit codes of the specified command are to be treated as success. Example: \"{h}0,3,5-7,9{}\", or \"{h}0,-1{}\" for negative codes. If specified, all other codes are treated as failure. By default, only {h}0{} is considered a success. See also {option}--failure-codes{}.")]
        public string SuccessCodes;

        [Option("--failure-codes")]
        [DocumentationRhoML("Specifies which exit codes of the specified command are to be treated as failure. If specified, all other codes are treated as success.")]
        public string FailureCodes;

        [Option("--indicate-success")]
        [DocumentationRhoML("By default, RunLogged exits with the same status code as the specified command. RunLogged can also exit with error codes in the range {h}-80000{} to {h}-80999{} to indicate that the command didn't finish (or didn't even start).\n\nIf this option is specified, RunLogged will not pass through the comamnd's exit code, and will instead exit with {h}0{} (success) or {h}1{} (failure) when the specified command finishes, or one of {h}-80xxx{} codes otherwise. See {option}--success-codes{} for details on what determines success.")]
        public bool IndicateSuccess;

        [Option("--max-duration-sec")]
        [DocumentationRhoML("Specifies the maximum duration for the process to run. If not finished within the specified time, the process is aborted and a failure is reported.")]
        public int? MaxDurationSeconds;

        [Option("--wipe-after-shutdown")]
        [Undocumented] // informs RunLogged to wipe the specified directory after it terminates - used to clear the shadow copy directory.
        public string WipeAfterShutdown;

        [IsPositional]
        [Documentation("Command to be executed, with arguments if any.")]
        public string[] CommandToRun;

        [Ignore]
        public List<Tuple<int, int>> SuccessCodesParsed;
        [Ignore]
        public List<Tuple<int, int>> FailureCodesParsed;

        public ConsoleColoredString Validate()
        {
            if (CommandToRun.Length == 0)
                return "You must specify the command to be executed (CommandToRun).";

            if (WorkingDir != null)
                try { Directory.SetCurrentDirectory(WorkingDir); }
                catch { return "Cannot set working directory - check that the directory exists and that the path is valid. \"{0}\"".Fmt(WorkingDir); }

            if (LogFilename == null)
                LogFilename = Path.GetFileName(CommandToRun[0]).Replace(".", "_") + "--{}.log";
            LogFilename = Path.GetFullPath(LogFilename.Replace("{}", DateTime.Now.ToString("yyyy-MM-dd")));

            if (SuccessCodes != null && FailureCodes != null)
                return CommandLineParser.Colorize(RhoML.Parse("The options {option}--success-codes{} and {option}--failure-codes{} are mutually exclusive and cannot be specified together."));

            // Parse the success/failure codes list
            var codes = SuccessCodes ?? FailureCodes;
            if (codes != null)
            {
                var result = new List<Tuple<int, int>>();
                foreach (var part in codes.Split(','))
                {
                    var match = Regex.Match(part, @"^\s*(?<fr>-?\d+)\s*(-\s*(?<to>-?\d+\s*))?$");
                    if (!match.Success)
                        return CommandLineParser.Colorize(RhoML.Parse("Could not parse the exit code list for {option}{0}{}: cannot parse segment \"{h}{1}{}\".".Fmt(SuccessCodes != null ? "--success-codes" : "--failure-codes", part)));
                    var fr = int.Parse(match.Groups["fr"].Value);
                    if (!match.Groups["to"].Success)
                        result.Add(Tuple.Create(fr, fr));
                    else
                    {
                        var to = int.Parse(match.Groups["to"].Value);
                        result.Add(Tuple.Create(Math.Min(fr, to), Math.Max(fr, to)));
                    }
                }
                if (SuccessCodes != null)
                    SuccessCodesParsed = result;
                else
                    FailureCodesParsed = result;
            }

            return null;
        }
#pragma warning restore 649
    }
}
