using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RT.Util;
using RT.Util.ExtensionMethods;

namespace RunLoggedCs;

static class Program
{
    static TextWriter _writer;
    static string _scriptName = "(unknown)"; // without path or extension
    static string _scriptDir = null;
    static Settings _settings;
    static List<string> _warnings = [];
    static List<string> _settingsFiles = [];
    static IOutcome _outcome;
    static DateTime _startedAt = DateTime.UtcNow;

    public const int ExitScriptException = -100;
    public const int ExitScriptCompile = -101;
    public const int ExitStartupError = -102;
    public const int ExitInternalError = -103;

    static int Main(string[] args)
    {
        try
        {
            DoMain(args);
        }
        catch (TellUserException ex)
        {
            _outcome = ex;
        }
#if !DEBUG
        catch (Exception ex)
        {
            _outcome = new TellUserException($"Internal error: {ex.GetType().Name}, {ex.Message}, {ex.StackTrace}", ExitInternalError);
        }
#endif
        Console.WriteLine(); // script may not have written anything, or may have written text without a newline
        _outcome.WriteFooter();
        Console.WriteLine($"****** exit code: {_outcome.ExitCode} ({_outcome.Summary})");
        Console.WriteLine($"****** exit at: {DateTime.UtcNow.ToLocalTime():yyyy-MM-dd HH:mm:ss} (ran for {(DateTime.UtcNow - _startedAt).TotalSeconds:#,0.0} seconds)");
        _writer?.Dispose(); // also restores Console.Out to original
        return _outcome.ExitCode;
    }

    static void DoMain(string[] args)
    {
        // Load core settings that apply to all scripts
        _settings = Settings.GetDefault();
        TryLoadSettings(Path.Combine(AppContext.BaseDirectory, "Settings.RunLoggedCs.xml"));
        TryLoadSettings(Path.Combine(AppContext.BaseDirectory, $"Settings.RunLoggedCs.{Environment.MachineName}.xml"));

        // Report if no arguments - might go to Telegram if configured globally
        if (args.Length == 0)
            throw new TellUserException($"Usage: RunLoggedCs.exe <script.cs> [arg0 ...]", ExitStartupError);

        // Parse args and load script-specific settings, if any
        var scriptFile = Path.GetFullPath(args[0]);
        args = args.Skip(1).ToArray();
        _scriptDir = Path.GetDirectoryName(scriptFile);
        _scriptName = Path.GetFileNameWithoutExtension(scriptFile);
        TryLoadSettings(Path.Combine(_scriptDir, $"{_scriptName}.RunLoggedCs.xml"));
        TryLoadSettings(Path.Combine(_scriptDir, $"{_scriptName}.RunLoggedCs.{Environment.MachineName}.xml"));

        // Send StdOut to a file log, now that we know where to log
        _writer = new LogAndConsoleWriter(_scriptName + ".log"); // also sets Console.Out to self

        // Log header
        Console.WriteLine($"************************************************************************");
        Console.WriteLine($"****** RunLoggedCs v[DEV] invoked at {_startedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"****** Script: |{scriptFile}|");
        Console.WriteLine($"****** Script args: {(args.Length == 0 ? "(none)" : args.JoinString(" ", "|", "|"))}");
        Console.WriteLine($"****** CurDir: |{Directory.GetCurrentDirectory()}|");
        foreach (var sf in _settingsFiles)
            Console.WriteLine($"****** Settings file: |{sf}|");

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

    static void TryLoadSettings(string fullname)
    {
        try
        {
            if (!File.Exists(fullname))
                return;
            var settings = Settings.LoadFromFile(fullname);
            _settings.AddOverrides(settings);
            _settingsFiles.Add(fullname);
        }
        catch (Exception e)
        {
            _warnings.Add($"Could not load settings from {fullname}: {e.GetType().Name}, {e.Message}");
        }
    }

    static Func<string[], int> CompileScript(string scriptFile)
    {
        // Load the script code
        if (!File.Exists(scriptFile))
            throw new TellUserException($"Script file not found: {scriptFile}", ExitStartupError);
        var code = File.ReadAllText(scriptFile);
        code = new[]
        {
            "using System;",
            "using System.Collections.Generic;",
            "using System.Threading;",
            "using System.Text.RegularExpressions;",
            "#line 2",
            code,
        }.JoinString("\r\n");

        // Compile the script
        var tree = CSharpSyntaxTree.ParseText(code, new CSharpParseOptions().WithKind(SourceCodeKind.Regular));
        var opts = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
        var comp = CSharpCompilation.Create("script", options: opts).AddSyntaxTrees(tree);

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
                throw new CompileErrorsException(errors);
        }
        throwIfErrors(comp.GetDiagnostics());
        var ms = new MemoryStream();
        var result = comp.Emit(ms);
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
                throw new TellUserException($"Found multiple candidates for the Main method (entry point).", ExitScriptCompile);
        }
        if (main == null)
            throw new TellUserException($"No candidates found for the Main method (entry point).", ExitScriptCompile);
        return main;
    }
}
