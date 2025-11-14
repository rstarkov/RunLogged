using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using RT.Util;
using RT.Util.ExtensionMethods;
using RunLoggedCs.ScriptUtil;

namespace RunLoggedCs;

static class Program
{
    public static Settings Settings;
    public static string ScriptName = "(unknown)"; // without path or extension
    public static string ScriptDir;

    static LogAndConsoleWriter _writer;
    static List<string> _warnings = []; // issues not critical enough to abort a run, but important enough to be worth a Telegram warning
    static List<string> _infos = []; // things we want logged before we know where to log them to
    static IOutcome _outcome;
    static DateTime _startedAt = DateTime.UtcNow;
    static TimeSpan _duration;

    public const int ExitScriptException = -100; // the script compiled and ran, but threw an unhandled exception
    public const int ExitScriptCompile = -101; // we have the script, but we couldn't compile or run it due to a problem with the script
    public const int ExitStartupError = -102; // there's a problem that prevented us from even attempting to compile and execute the script
    public const int ExitInternalError = -103; // a bug in the runner

    static int Main(string[] args)
    {
        try
        {
            DoMain(args);
        }
        catch (Exception ex) when (ex is IOutcome exo)
        {
            _outcome = exo;
        }
#if !DEBUG
        catch (Exception ex)
        {
            _outcome = new InternalError { Exception = ex };
        }
#endif
        _duration = DateTime.UtcNow - _startedAt;
        Console.WriteLine(); // script may not have written anything, or may have written text without a newline
        NotifyOutcome();
        _outcome.WriteFooter();
        WriteLinePrefixed($"exit at: {DateTime.UtcNow.ToLocalTime():yyyy-MM-dd HH:mm:ss} (ran for {_duration.TotalSeconds:#,0.0} seconds)");
        WriteLinePrefixed($"exit code: {_outcome.ExitCode} ({_outcome.Summary})");
        _writer?.Dispose(); // also restores Console.Out to original
        return _outcome.ExitCode;
    }

    static void DoMain(string[] args)
    {
        // Load core settings that apply to all scripts
        Settings = Settings.GetDefault();
        TryLoadSettings(AppContext.BaseDirectory, "Settings.RunLoggedCs.xml");
        TryLoadSettings(AppContext.BaseDirectory, $"Settings.RunLoggedCs.{Environment.MachineName}.xml");

        // Report if no arguments - might go to Telegram if configured globally
        if (args.Length == 0)
            throw new StartupException($"Usage: RunLoggedCs.exe <script.cs> [arg0 ...]");

        // Parse args and load script-specific settings, if any
        var scriptFile = Path.GetFullPath(args[0]);
        args = args.Skip(1).ToArray();
        ScriptDir = Path.GetDirectoryName(scriptFile);
        ScriptName = Path.GetFileNameWithoutExtension(scriptFile);
        TryLoadSettings(ScriptDir, $"{ScriptName}.RunLoggedCs.xml");
        TryLoadSettings(ScriptDir, $"{ScriptName}.RunLoggedCs.{Environment.MachineName}.xml");

        // Initialise StdOut logging, now that we know where to log
        var logFile = RotateLogsAndGetPath();
        if (logFile != null) // null means disabled or invalid config
            _writer = new LogAndConsoleWriter(logFile); // also sets Console.Out to self

        // Log header
        if (_writer?.IsNewFile == false)
            Console.WriteLine(); // previous log may not have had a proper newline
        Console.WriteLine($"************************************************************************");
        WriteLinePrefixed($"RunLoggedCs v[DEV] invoked at {_startedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        WriteLinePrefixed($"Script: |{scriptFile}|");
        WriteLinePrefixed($"Script args: {(args.Length == 0 ? "(none)" : args.JoinString(" ", "|", "|"))}");
        WriteLinePrefixed($"CurDir: |{Directory.GetCurrentDirectory()}|");
        WriteLinePrefixed($"Logging to file: {(logFile == null ? "disabled" : $"|{logFile}|")}");
        foreach (var info in _infos)
            WriteLinePrefixed(info);
        Console.WriteLine();
        _infos = null;

        // Compile everything and get a runnable function
        var main = CompileScript(scriptFile);

        // Run it and determine the outcome
        try
        {
            int exitCode = main(args);
            if (exitCode == 0)
                _outcome = new ScriptSuccess { ExitCode = exitCode };
            else
                _outcome = new ScriptFailure { ExitCode = exitCode };
        }
        catch (TargetInvocationException e)
        {
            throw new ScriptException(e);
        }
    }

    static void TryLoadSettings(string path, string filename)
    {
        var fullname = Path.Combine(path, filename);
        try
        {
            if (!File.Exists(fullname))
                return;
            var settings = Settings.LoadFromFile(fullname);
            settings.ExpandPaths(path);
            Settings.AddOverrides(settings);
            _infos.Add($"Settings file: |{fullname}|");
        }
        catch (Exception e)
        {
            Warn($"Could not load settings from {fullname}: {e.GetType().Name}, {e.Message}");
        }
    }

    static Func<string[], int> CompileScript(string scriptFile)
    {
        // Load the script source
        var trees = new List<SyntaxTree>();
        if (!File.Exists(scriptFile))
            throw new StartupException($"Script file not found: {scriptFile}");
        var code = File.ReadAllText(scriptFile);
        code = Settings.Usings.Distinct().Select(u => $"using {u};").Concat(["#line 1", code]).JoinString("\r\n");
        trees.Add(CSharpSyntaxTree.ParseText(code, path: scriptFile, encoding: Encoding.UTF8, options: new CSharpParseOptions().WithKind(SourceCodeKind.Regular)));
        // Load include sources
        foreach (var includeFile in Settings.IncludeScripts.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(includeFile))
                throw new StartupException($"Included script file not found: {includeFile}");
            var includeCode = File.ReadAllText(includeFile);
            trees.Add(CSharpSyntaxTree.ParseText(includeCode, path: includeFile, encoding: Encoding.UTF8, options: new CSharpParseOptions().WithKind(SourceCodeKind.Regular)));
        }

        // Compile the script
        var opts = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithOptimizationLevel(OptimizationLevel.Debug);
        var comp = CSharpCompilation.Create("script", options: opts).AddSyntaxTrees(trees);

        var added = new HashSet<Assembly>();
        void addAssembly(Assembly assy)
        {
            if (!added.Add(assy))
                return;
            comp = comp.AddAssemblyReference(assy);
            foreach (var aref in assy.GetReferencedAssemblies())
                addAssembly(Assembly.Load(aref));
        }
        addAssembly(Assembly.GetEntryAssembly());

        void throwIfErrors(IEnumerable<Diagnostic> diagnostics)
        {
            var errors = diagnostics.Where(e => e.Severity == DiagnosticSeverity.Error).ToList();
            if (errors.Count > 0)
                throw new CompileException(errors);
        }
        throwIfErrors(comp.GetDiagnostics());
        var ms = new MemoryStream();
        var result = comp.Emit(ms, options: new EmitOptions(debugInformationFormat: DebugInformationFormat.Embedded));
        throwIfErrors(result.Diagnostics);
        Ut.Assert(result.Success);

        // Find the entry point
        Func<string[], int> main = null;
        var mainCandidates = Assembly.Load(ms.ToArray())
            .GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Where(m => m.Name == "Main")
            .ToList();
        foreach (var candidate in mainCandidates)
        {
            var parameters = candidate.GetParameters();
            bool had = main != null;
            if (candidate.ReturnType == typeof(void) && parameters.Length == 0)
                main = _ => { candidate.Invoke(null, []); return 0; };
            else if (candidate.ReturnType == typeof(void) && parameters.Length == 1 && parameters[0].ParameterType == typeof(string[]))
                main = args => { candidate.Invoke(null, [args]); return 0; };
            else if (candidate.ReturnType == typeof(int) && parameters.Length == 0)
                main = _ => (int)candidate.Invoke(null, []);
            else if (candidate.ReturnType == typeof(int) && parameters.Length == 1 && parameters[0].ParameterType == typeof(string[]))
                main = args => (int)candidate.Invoke(null, [args]);
            else
                continue;
            if (main != null && had)
                throw new CompileException($"Found multiple candidates for the Main method (entry point).");
        }
        if (main == null)
            throw new CompileException($"No candidates found for the Main method (entry point).");
        return main;
    }

    private static string RotateLogsAndGetPath()
    {
        // We run this before we start logging; makes sense to delete old logs first. It's tempting to run it again on exit, to maintain the
        // limits with the newly added logs, but then we might end up trimming a long output from the current run, which is highly undesirable.

        if (Settings.Log.Enabled != true)
            return null;

        var fullPath = Path.Combine(ScriptDir, Settings.Log.Path).Replace("{name}", ScriptName, StringComparison.OrdinalIgnoreCase);

        (int pos, string pattern) split(string pattern) => (fullPath.IndexOf(pattern, StringComparison.OrdinalIgnoreCase), pattern);
        var s = split("{daily}");
        if (s.pos < 0) s = split("{monthly}");
        if (s.pos < 0) s = split("{yearly}");
        int dateStart = s.pos;
        string dateType = s.pos < 0 ? null : s.pattern;
        string dateSearch = null;
        string currentPath = fullPath;
        if (dateType == "{daily}")
        {
            dateSearch = "????-??-??";
            currentPath = fullPath.Substring(0, dateStart) + $"{_startedAt:yyyy-MM-dd}" + fullPath.Substring(dateStart + dateType.Length);
        }
        else if (dateType == "{monthly}")
        {
            dateSearch = "????-??";
            currentPath = fullPath.Substring(0, dateStart) + $"{_startedAt:yyyy-MM}" + fullPath.Substring(dateStart + dateType.Length);
        }
        else if (dateType == "{yearly}")
        {
            dateSearch = "????";
            currentPath = $"{fullPath[..dateStart]}{_startedAt:yyyy}{fullPath[(dateStart + dateType.Length)..]}";
        }

        var logdir = Path.GetDirectoryName(dateType == null ? fullPath : $"{fullPath[..dateStart]}*{fullPath[(dateStart + dateType.Length)..]}"); // * is invalid; if it's part of a directory name we'll get an exception below
        if (!Directory.Exists(logdir))
        {
            try { Directory.CreateDirectory(logdir); }
            catch
            {
                Warn($"Could not start logging; path: {logdir}"); // permission error or template in directory name
                return null;
            }
        }

        // Apply size and age limits
        try
        {
            // If using date template, delete old log files
            if (dateType != null)
            {
                var search = Path.GetFileName($"{fullPath[..dateStart]}{dateSearch}{fullPath[(dateStart + dateType.Length)..]}");
                var files = new DirectoryInfo(logdir).GetFiles(search);
                var kept = new List<(FileInfo file, DateTime lastlog)>();
                foreach (var file in files.OrderBy(f => f.Name))
                {
                    if (file.FullName.EqualsIgnoreCase(currentPath))
                        continue;
                    var dateStr = file.FullName[dateStart..(dateStart + dateSearch.Length)];
                    if (dateType == "{monthly}")
                        dateStr += "-01";
                    else if (dateType == "{yearly}")
                        dateStr += "-01-01";
                    if (!DateTime.TryParseExact(dateStr, "yyyy'-'MM'-'dd", null, DateTimeStyles.AssumeLocal, out var lastlog))
                        continue; // could add to warning but probably not necessary
                    if (dateType == "{daily}")
                        lastlog = lastlog.AddDays(1);
                    else if (dateType == "{monthly}")
                        lastlog = lastlog.AddMonths(1);
                    else if (dateType == "{yearly}")
                        lastlog = lastlog.AddYears(1);
                    if (Settings.Log.DaysToKeep > 0 && (DateTime.Now - lastlog).TotalDays > Settings.Log.DaysToKeep)
                    {
                        try
                        {
                            file.Delete();
                            _infos.Add($"Deleted log file due to age limit: {file.FullName}");
                        }
                        catch { Warn($"Could not delete old log file (1): {file.FullName}"); }
                    }
                    else
                        kept.Add((file, lastlog));
                }
                // Now delete oldest files until we're below the size limit (note that files over the age limit that failed to be deleted are not included in the calculation - on purpose, to limit stuck files impacting on recent logs)
                if (Settings.Log.MaxTotalSizeKB > 0)
                {
                    var totalSize = File.Exists(currentPath) ? new FileInfo(currentPath).Length : 0;
                    totalSize += kept.Sum(k => k.file.Length);
                    foreach (var k in kept.OrderBy(k => k.lastlog))
                    {
                        if (totalSize / 1000 <= Settings.Log.MaxTotalSizeKB)
                            break;
                        try
                        {
                            var size = k.file.Length;
                            k.file.Delete();
                            _infos.Add($"Deleted log file due to size limit: {k.file.FullName}");
                            totalSize -= size;
                        }
                        catch { Warn($"Could not delete log file (2): {k.file.FullName}"); }
                    }
                }
            }

            // If current log file is over the size limit all by itself, trim it too
            if (Settings.Log.MaxTotalSizeKB > 0 && File.Exists(currentPath) && new FileInfo(currentPath).Length / 1000 > Settings.Log.MaxTotalSizeKB)
            {
                _infos.Add($"Trimmed lines from log file due to size limit: {currentPath}");
                // Pass 1: add up line lengths until we know how many lines to skip, targeting half of MaxTotalSizeKB
                long deleteSize = new FileInfo(currentPath).Length - Settings.Log.MaxTotalSizeKB.Value * 1000 / 2;
                long sizeSoFar = 0;
                int skipLines = 0;
                foreach (var line in File.ReadLines(currentPath))
                {
                    sizeSoFar += Encoding.UTF8.GetByteCount(line) + 2;
                    skipLines++;
                    if (sizeSoFar >= deleteSize)
                        break;
                }
                // Pass 2: rewrite to temp location
                var tempPath = currentPath + ".~tmp";
                File.WriteAllLines(tempPath,
                    new[] { "****** (older logs trimmed here)", "" }.Concat(
                        File.ReadLines(currentPath).Skip(skipLines)
                    )
                );
                // Rename
                File.Move(tempPath, currentPath, overwrite: true);
            }
        }
        catch (Exception e)
        {
            // There are many ways for log trimming to fail. If it does, log a generic warning but leave logging enabled.
            Warn($"Error while trimming logs: {e.GetType().Name}, {e.Message}");
        }

        return currentPath;
    }

    public static void WriteLinePrefixed(string text)
    {
        Console.WriteLine($"****** {text.Replace("\n", "\n****** ")}");
    }

    public static void Warn(string warning)
    {
        _warnings.Add(warning);
        if (_infos != null)
            _infos.Add($"WARNING: {warning}");
        else
            WriteLinePrefixed($"WARNING: {warning}");
    }

    static void NotifyOutcome()
    {
        // Report warnings to Telegram
        if (_warnings.Count > 0 && Settings.Telegram?.WarnBotToken != null)
            Telegram.Send(warn: true, html: _warnings.Select(w => w.HtmlEscape()).JoinString("\n"));
        // Report outcome to TG
        if (_outcome is ScriptSuccess ss && Settings.Telegram?.NotifyOnSuccess == true)
            Telegram.Send(warn: false, html: $"{_outcome.Summary}; exit code {_outcome.ExitCode}; {_duration.TotalSeconds:#,0.0} seconds");
        else if (_outcome is not ScriptSuccess && Settings.Telegram?.WarnBotToken != null)
            Telegram.Send(warn: true, html: $"{_outcome.Summary}; exit code {_outcome.ExitCode}; {_duration.TotalSeconds:#,0.0} seconds{(_outcome is Exception e ? $"\n{e.Message.HtmlEscape()}" : "")}");
    }
}
