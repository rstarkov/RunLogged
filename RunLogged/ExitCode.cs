namespace RunLogged;

static class ExitCode
{
    // When using --indicate-success
    public const int Success = 0;
    public const int Failure = 1;

    // Not really errors
    public const int Aborted = -80000;
    public const int MutexInUse = -80001;

    // User errors
    public const int ErrorParsingCommandLine = -80100;
    public const int CannotOpenLogFile = -80101;

    // Programmer errors
    public const int ErrorInternal = -80999;
}
