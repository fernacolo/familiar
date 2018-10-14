using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace wcmd.DataFiles
{
    internal class CachedDataFile : IDataFile
    {
        private readonly IDataFile _inner;
        private readonly List<DataFileRecord> _items;

        public string FileName => _inner.FileName;

        public CachedDataFile( IDataFile inner )
        {
            _inner = inner ?? throw new ArgumentNullException( nameof( inner ) );

            var entireFile = _inner.ReadCommandsFromEnd( null, int.MaxValue, TimeSpan.MaxValue );
            _items = new List<DataFileRecord>( entireFile.Items.Count );

            foreach ( var record in entireFile.Items )
            {
                if ( record.Type == DataFileRecord.CommandV1 )
                    _items.Insert( 0, record );
            }
        }

        public CommandPage ReadCommandsFromEnd( CommandPage previous, int maxResults, TimeSpan maxDuration )
        {
            var result = new CommandPage();

            lock ( _items )
            {
                var pos = (int) (previous?.Offset ?? _items.Count);
                while ( pos > 0 )
                {
                    // If we have read the maximum number, stop reading.
                    if ( result.Count >= maxResults )
                        break;

                    var record = _items[--pos];
                    Debug.Assert( record.Type == DataFileRecord.CommandV1 );
                    result.Add( record );
                }

                result.Offset = pos;
                return result;
            }
        }

        public void Write( DateTime whenExecuted, string command )
        {
            _inner.Write( whenExecuted, command );

            var record = new DataFileRecord
            {
                Type = DataFileRecord.CommandV1,
                WhenExecuted = whenExecuted,
                Command = command
            };

            lock ( _items )
            {
                _items.Add( record );
            }
        }
    }
}