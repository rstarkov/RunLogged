using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RT.Util.ExtensionMethods;

namespace RunLoggedCs;

class Program
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        var scriptFile = Path.GetFullPath(args[0]);

        using var writer = File.AppendText(args[0] + ".log");
        Console.SetOut(writer);

        if (!File.Exists(scriptFile))
        {
            Console.WriteLine($"ERROR: script file not found: {scriptFile}");
            return -1;
        }

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

        Func<string[], int> main = null;
        var errors = comp.GetDiagnostics().Where(e => e.Severity == DiagnosticSeverity.Error).ToList();
        if (errors.Count == 0)
        {
            var ms = new MemoryStream();
            var result = comp.Emit(ms);
            errors = result.Diagnostics.Where(e => e.Severity == DiagnosticSeverity.Error).ToList();
            if (result.Success)
            {
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
                    {
                        Console.WriteLine($"ERROR: multiple main method candidates");
                        return -1;
                    }
                }
            }
        }

        if (main == null)
        {
            foreach (var error in errors)
            {
                Console.WriteLine();
                Console.WriteLine($"[{error.Location.GetMappedLineSpan().StartLinePosition}]: {error.GetMessage()}");
            }
            return -1;
        }

        // Execute the script
        try
        {
            return main(args.Skip(1).ToArray());
        }
        catch (Exception e)
        {
            Console.WriteLine();
            Console.WriteLine($"UNHANDLED EXCEPTION:");
            foreach (var excp in e.SelectChain(ee => ee.InnerException))
            {
                Console.WriteLine();
                Console.WriteLine($"{excp.GetType().Name}: {excp.Message}");
                Console.WriteLine(excp.StackTrace);
            }
            Console.WriteLine();
            return -2;
        }
    }
}
