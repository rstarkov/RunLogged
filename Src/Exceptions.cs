using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RunLogged
{
    class TellUserException : Exception
    {
        public TellUserException(string message) : base(message) { }
    }
}
