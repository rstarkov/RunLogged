using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.CodeAnalysis;
using RT.Util.ExtensionMethods;

namespace RunLoggedCs;

interface IOutcome
{
    int ExitCode { get; }
    void WriteToConsole();
}

class ScriptSuccess : IOutcome
{
    public int ExitCode { get; set; }
    public void WriteToConsole()
    {
        Console.WriteLine($"****** exit code: {ExitCode} (success)");
    }
}

class ScriptFailure : IOutcome
{
    public int ExitCode { get; set; }
    public void WriteToConsole()
    {
        Console.WriteLine($"****** exit code: {ExitCode} (failure)");
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
    public virtual void WriteToConsole()
    {
        Console.WriteLine($"****** {Message}");
        Console.WriteLine($"****** exit code: {ExitCode} (abnormal exit; startup error)");
    }
}

class CompileErrorsException : TellUserException, IOutcome
{
    public IReadOnlyList<Diagnostic> Errors { get; init; }
    public CompileErrorsException(List<Diagnostic> errors) : base("Script compilation error", Program.ExitScriptCompile)
    {
        Errors = errors;
    }

    public override void WriteToConsole()
    {
        Console.WriteLine($"****** Script compilation error:");
        foreach (var error in Errors)
        {
            Console.WriteLine($"******");
            Console.WriteLine($"****** [{error.Location.GetMappedLineSpan().StartLinePosition}]: {error.GetMessage()}");
        }
        Console.WriteLine($"******");
        Console.WriteLine($"****** exit code: {ExitCode} (abnormal exit; compile error in script)");
    }
}

class ScriptException : TellUserException, IOutcome
{
    public ScriptException(TargetInvocationException e) : base("Unhandled exception in script", e.InnerException, Program.ExitScriptException)
    {
    }

    public override void WriteToConsole()
    {
        Console.WriteLine($"****** Unhandled exception in script:");
        foreach (var excp in InnerException.SelectChain(ee => ee.InnerException))
        {
            Console.WriteLine($"******");
            Console.WriteLine($"****** {excp.GetType().Name}: {excp.Message}");
            Console.WriteLine($"****** " + excp.StackTrace.Replace("\n", "\n****** "));
        }
        Console.WriteLine($"******");
        Console.WriteLine($"****** exit code: {ExitCode} (abnormal exit; unhandled exception in script)");
    }
}
