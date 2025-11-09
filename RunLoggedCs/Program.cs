using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RT.Util;
using RT.Util.ExtensionMethods;

namespace RunLoggedCs;

class Program
{
    static TextWriter _writer;

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            return DoMain(args);
        }
        catch (TellUserException ex)
        {
            ex.WriteToConsole();
            return ex.ExitCode;
        }
#if !DEBUG
        catch (Exception ex)
        {
            // TODO: INTERNAL ERROR
        }
#endif
        finally
        {
            _writer?.Dispose();
        }
    }

    static int DoMain(string[] args)
    {
        var scriptFile = Path.GetFullPath(args[0]);

        _writer = File.AppendText(args[0] + ".log");
        Console.SetOut(_writer);

        if (!File.Exists(scriptFile))
            throw new TellUserException($"Script file not found: {scriptFile}", -1);

        // Load the script
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
                throw new TellUserException($"Found multiple candidates for the Main method (entry point).", -1);
        }
        if (main == null)
            throw new TellUserException($"No candidates found for the Main method (entry point).", -1);

        // Execute the script
        try
        {
            return main(args.Skip(1).ToArray());
        }
        catch (TargetInvocationException e)
        {
            throw new ScriptException(e);
        }
    }
}

class TellUserException : Exception
{
    public int ExitCode { get; init; }
    public TellUserException(string message, int exitcode) : base(message)
    {
        ExitCode = exitcode;
    }
    protected TellUserException(string message, Exception innerException, int exitcode) : base(message, innerException)
    {
        ExitCode = exitcode;
    }
    public virtual void WriteToConsole()
    {
        Console.WriteLine(Message);
    }
}

class CompileErrorsException : TellUserException
{
    public IReadOnlyList<Diagnostic> Errors { get; init; }
    public CompileErrorsException(List<Diagnostic> errors) : base("Script compilation error", -1)
    {
        Errors = errors;
    }

    public override void WriteToConsole()
    {
        Console.WriteLine($"ERROR: {Message}");
        foreach (var error in Errors)
        {
            Console.WriteLine();
            Console.WriteLine($"[{error.Location.GetMappedLineSpan().StartLinePosition}]: {error.GetMessage()}");
        }
    }
}

class ScriptException : TellUserException
{
    public ScriptException(TargetInvocationException e) : base("Unhandled exception in script", e.InnerException, -2)
    {
    }

    public override void WriteToConsole()
    {
        Console.WriteLine($"ERROR: {Message}");
        foreach (var excp in InnerException.SelectChain(ee => ee.InnerException))
        {
            Console.WriteLine();
            Console.WriteLine($"{excp.GetType().Name}: {excp.Message}");
            Console.WriteLine(excp.StackTrace);
        }
        Console.WriteLine();
    }
}
