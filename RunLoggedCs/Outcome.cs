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
    public void WriteFooter()
    {
    }
}

class ScriptFailure : IOutcome
{
    public int ExitCode { get; set; }
    public string Summary { get => $"failure"; }
    public void WriteFooter()
    {
    }
}

class TellUserException : Exception, IOutcome
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
    public virtual string Summary { get => $"abnormal exit; startup error"; }
    public virtual void WriteFooter()
    {
        Console.WriteLine($"****** {Message}");
    }
}

class CompileErrorsException : TellUserException, IOutcome
{
    public IReadOnlyList<Diagnostic> Errors { get; init; }
    public CompileErrorsException(List<Diagnostic> errors) : base("Script compilation error", Program.ExitScriptCompile)
    {
        Errors = errors;
    }

    public override string Summary { get => $"abnormal exit; compile error in script"; }

    public override void WriteFooter()
    {
        Console.WriteLine($"****** Script compilation error:");
        foreach (var error in Errors)
        {
            Console.WriteLine($"******");
            Console.WriteLine($"****** [{error.Location.GetMappedLineSpan().StartLinePosition}]: {error.GetMessage()}");
        }
        Console.WriteLine($"******");
    }
}

class ScriptException : TellUserException, IOutcome
{
    public ScriptException(TargetInvocationException e) : base("Unhandled exception in script", e.InnerException, Program.ExitScriptException)
    {
    }

    public override string Summary { get => $"abnormal exit; unhandled exception in script"; }

    public override void WriteFooter()
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
