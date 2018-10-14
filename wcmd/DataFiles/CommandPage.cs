using System;
using System.Collections.Generic;

namespace wcmd.DataFiles
{
    public sealed class CommandPage
    {
        public long Offset;
        public readonly List<DataFileRecord> Items = new List<DataFileRecord>();
        public int Count => Items?.Count ?? 0;

        public void Add( DataFileRecord record )
        {
            if ( record == null )
                throw new ArgumentNullException( nameof( record ) );
            Items.Add( record );
        }
    }
}