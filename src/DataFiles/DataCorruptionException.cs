using System;

namespace fam.DataFiles
{
    internal sealed class DataCorruptionException : Exception
    {
        public DataCorruptionException( string message ) : base( message )
        {
        }
    }
}