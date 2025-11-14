using System;
using System.IO;
using System.Text;

namespace RunLoggedCs;

class LogAndConsoleWriter : TextWriter
{
    public bool IsNewFile { get; init; }
    private TextWriter _conWriter;
    private TextWriter _logWriter;
    private Stream _logStream;

    public LogAndConsoleWriter(string logFilename)
    {
        Console.OutputEncoding = Encoding.UTF8;
        _conWriter = Console.Out;
        IsNewFile = !File.Exists(logFilename);
        _logStream = File.Open(logFilename, FileMode.Append, FileAccess.Write, FileShare.Read);
        _logWriter = new StreamWriter(_logStream) { AutoFlush = true };
        Console.SetOut(this);
    }

    public override Encoding Encoding => Encoding.UTF8;

    protected override void Dispose(bool disposing)
    {
        Console.SetOut(_conWriter);
        base.Dispose(disposing);
        _logWriter?.Dispose();
        _logWriter = null;
        _logStream?.Dispose();
        _logStream = null;
    }

    public override void Write(char value) { _logWriter.Write(value); _conWriter.Write(value); }
    public override void Write(char[] buffer, int index, int count) { _logWriter.Write(buffer, index, count); _conWriter.Write(buffer, index, count); }
    public override void Write(string value) { _logWriter.Write(value); _conWriter.Write(value); }
    public override void WriteLine(string value) { _logWriter.WriteLine(value); _conWriter.WriteLine(value); }
}
