using System;
using System.IO;
using RT.Util.CommandLine;
using RT.Util.ExtensionMethods;

/// TODO:
/// - console & no-console variants
/// - proper (albeit approximate) by-line handling: nice interleaving of stderr with stdout; label the approx time of every line
/// - send email on failure

namespace RunLogged
{
    class Program
    {
        public static CmdLineArgs Args;
        public static StreamWriter Log;
        public static bool LogIsNew;

        static int Main(string[] args)
        {
#if CONSOLE
            Console.OutputEncoding = System.Text.Encoding.UTF8;
#else
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
#endif

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

                if (!LogIsNew)
                    for (int i = 0; i < 3; i++)
                        outputLine();
                outputLine("************************************************************************");
                outputLine("****** RunLogged invoked at {0}".Fmt(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
                outputLine("****** Command: {0}".Fmt(Args.CommandToRun.JoinString("|", "|", " ")));
                outputLine("****** CurDir: |{0}|".Fmt(Directory.GetCurrentDirectory()));
                outputLine("****** LogTo: |{0}|".Fmt(Args.Log));

                var runner = new ProcessRunner(Args.CommandToRun, Directory.GetCurrentDirectory());
                runner.StdoutText += runner_StdoutText;
                runner.StderrText += runner_StderrText;
                runner.Start();
                runner.WaitForExit();

                outputLine("****** completed at {0}".Fmt(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
                outputLine("****** exit code: {0}".Fmt(runner.LastExitCode));

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
            Console.Write(text);
            Log.Write(text);
        }

        private static void outputLine()
        {
            output(Environment.NewLine);
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
        public string Log;

        //email

        [IsPositional]
        [DocumentationLiteral("Command to be executed, with arguments if any.")]
        public string[] CommandToRun;

        public string Validate()
        {
            if (CommandToRun.Length == 0)
                return "You must specify the command to be executed (CommandToRun).";

            if (Log == null)
                Log = Path.GetFileName(CommandToRun[0]).Replace(".", "_") + "--{}.log";
            Log = Path.GetFullPath(Log.Replace("{}", DateTime.Now.ToString("yyyy-MM-dd")));

            if (WorkingDir != null)
                try { Directory.SetCurrentDirectory(WorkingDir); }
                catch { return "Cannot set working directory - check that the directory exists and that the path is valid. \"{0}\"".Fmt(WorkingDir); }

            try
            {
                try { Directory.CreateDirectory(Path.GetDirectoryName(Log)); }
                catch { }
                Program.LogIsNew = !File.Exists(Log);
                Program.Log = new StreamWriter(File.Open(Log, FileMode.Append, FileAccess.Write, FileShare.Read));
                Program.Log.AutoFlush = true;
            }
            catch
            {
                return "Could not open the log file for writing. File \"{0}\".".Fmt(Log);
            }

            return null;
        }
    }
}
