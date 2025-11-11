using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.CodeAnalysis;
using RT.Util.ExtensionMethods;

namespace RunLoggedCs;

interface IOutcome
{
    int ExitCode { get; }
    string Summary { get; }
    void WriteFooter();
}

class ScriptSuccess : IOutcome
{
    public int ExitCode { get; set; }
    public string Summary { get => $"success"; }
    public void WriteFooter() { }
}

class ScriptFailure : IOutcome
{
    public int ExitCode { get; set; }
    public string Summary { get => $"failure"; }
    public void WriteFooter() { }
}

class InternalError : IOutcome
{
    public int ExitCode { get; } = Program.ExitInternalError;
    public string Summary { get => $"abnormal exit; internal error"; }
    public Exception Exception { get; set; }
    public void WriteFooter()
    {
        Console.WriteLine($"****** Internal error: {Exception.GetType().Name}, {Exception.Message}");
        Console.WriteLine($"****** " + Exception.StackTrace.Replace("\n", "\n****** "));
    }
}

class StartupException : Exception, IOutcome
{
    public int ExitCode { get; } = Program.ExitStartupError;

    public StartupException(string message) : base(message) { }
    protected StartupException(string message, Exception innerException) : base(message, innerException) { }

    public string Summary { get => $"abnormal exit; startup error"; }

    public void WriteFooter()
    {
        Console.WriteLine($"****** {Message}");
    }
}

class CompileException : Exception, IOutcome
{
    public int ExitCode { get; } = Program.ExitScriptCompile;
    public IReadOnlyList<Diagnostic> Errors { get; init; }

    public CompileException(List<Diagnostic> errors) : base("Script compilation error")
    {
        Errors = errors;
    }
    public CompileException(string error) : base(error)
    {
        Errors = null;
    }

    public string Summary { get => $"abnormal exit; compile error in script"; }

    public void WriteFooter()
    {
        if (Errors == null)
        {
            Console.WriteLine($"****** {Message}");
        }
        else
        {
            Console.WriteLine($"****** Script compilation error:");
            foreach (var error in Errors)
            {
                Console.WriteLine($"******");
                var sp = error.Location.GetMappedLineSpan().StartLinePosition;
                var ep = error.Location.GetMappedLineSpan().EndLinePosition;
                Console.WriteLine($"****** [line {sp.Line + 1}{(sp.Line == ep.Line ? "" : $"-{ep.Line + 1}")}, col {sp.Character + 1}{(sp == ep ? "" : $"-{ep.Character + 1}")}]: {error.GetMessage()}");
            }
            Console.WriteLine($"******");
        }
    }
}

class ScriptException : Exception, IOutcome
{
    public int ExitCode { get; } = Program.ExitScriptException;

    public ScriptException(TargetInvocationException e) : base("Unhandled exception in script", e.InnerException) { }

    public string Summary { get => $"abnormal exit; unhandled exception in script"; }

    public void WriteFooter()
    {
        Console.WriteLine($"****** Unhandled exception in script:");
        foreach (var excp in InnerException.SelectChain(ee => ee.InnerException))
        {
            Console.WriteLine($"******");
            Console.WriteLine($"****** {excp.GetType().Name}: {excp.Message}");
            Console.WriteLine($"****** " + excp.StackTrace.Replace("\n", "\n****** "));
        }
        Console.WriteLine($"******");
    }
}
