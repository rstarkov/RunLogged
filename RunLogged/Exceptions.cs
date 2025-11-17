namespace RunLogged;

class TellUserException : Exception
{
    public int ReturnCode { get; private set; }
    public bool Silent { get; private set; }

    /// <summary>Constructor.</summary>
    /// <param name="message">The message to be displayed to the user.</param>
    /// <param name="returnCode">Error code to return on exit. Must be in the -80999...-80000 range.</param>
    /// <param name="silent">If true, the Windowless variant will not say anything and just terminate quietly.</param>
    public TellUserException(string message, int returnCode, bool silent = false)
        : base(message)
    {
        ReturnCode = returnCode;
        Silent = silent;
    }
}
