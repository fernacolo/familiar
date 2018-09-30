using System;

namespace wcmd.DataFiles
{
    internal sealed class DataCorruptionException : Exception
    {
        public DataCorruptionException( string message ) : base( message )
        {
        }
    }
}