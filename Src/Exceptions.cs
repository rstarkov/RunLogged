using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RunLogged
{
    class TellUserException : Exception
    {
        public int ReturnCode { get; private set; }
        public bool Silent { get; private set; }

        /// <summary>Constructor.</summary>
        /// <param name="message">The message to be displayed to the user.</param>
        /// <param name="returnCode">Error code to return on exit. Must be in the -80999...-80000 range. Leave unset for a generic error code.</param>
        /// <param name="silent">If true, the Windowless variant will not say anything and just terminate quietly.</param>
        public TellUserException(string message, int returnCode = -80002, bool silent = false)
            : base(message)
        {
            ReturnCode = returnCode;
            Silent = silent;
        }
    }
}
