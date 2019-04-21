using System;

namespace fam.CommandLine
{
    internal sealed class InvalidArgumentsException : Exception
    {
        public InvalidArgumentsException( string message ) : base( message )
        {
        }
    }
}