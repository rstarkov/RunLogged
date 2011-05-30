using System;
using System.IO;
using RT.Util.CommandLine;
using RT.Util.ExtensionMethods;

namespace RunLogged
{
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

        [Option("--shadowcopy")]
        [DocumentationLiteral("Copies RunLogged to your user temporary directory and runs it from there. RunLogged will delete the temporary copy when exiting.")]
        public bool ShadowCopy;

        [Option("--mutex")]
        [DocumentationLiteral("Acquires a mutex with the specified name and holds it until exit. If the mutex is already acquired exits with error code -80003 (silently in the Windowless variant). Prefix with \"Global\\\" to make the mutex visible to other user accounts.")]
        public string MutexName;

        [Option("--wipe-after-shutdown")]
        [Undocumented] // informs RunLogged to wipe the specified directory after it terminates - used to clear the shadow copy directory.
        public string WipeAfterShutdown;

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
}
