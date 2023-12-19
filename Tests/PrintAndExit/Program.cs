namespace PrintAndExit;

internal class Program
{
    static int Main(string[] args)
    {
        // PrintAndExit.exe <exit code> [<commands>]
        // commands:
        //    w:100 - wait 100ms
        //    o:foo - print "foo" to stdout
        //    ol:foo - println "foo" to stdout
        //    e:foo - print "foo" to stderr
        //    el:foo - println "foo" to stderr
        int exitCode = int.Parse(args[0]);
        for (int i = 1; i < args.Length; i++)
        {
            var pt = args[i].Split(':');
            if (pt.Length != 2)
                continue;
            if (pt[0] == "w")
                Thread.Sleep(int.Parse(pt[1]));
            else if (pt[0] == "o")
                Console.Write(pt[1]);
            else if (pt[0] == "ol")
                Console.WriteLine(pt[1]);
            else if (pt[0] == "e")
                Console.Error.Write(pt[1]);
            else if (pt[0] == "el")
                Console.Error.WriteLine(pt[1]);
        }
        return exitCode;
    }
}
