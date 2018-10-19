using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace wcmd.DataFiles
{
    internal class CachedDataFile : IDataFile
    {
        private readonly IDataFile _inner;
        private readonly List<DataFileRecord> _items;
        private readonly CachedStoredCommand _bof;
        private readonly CachedStoredCommand _eof;

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

            _bof = new CachedStoredCommand( -1, null );
            _eof = new CachedStoredCommand( int.MaxValue, null );
        }

        public IStoredCommand Bof => _bof;

        public IStoredCommand Eof => _eof;

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

        public IStoredCommand Write( DateTime whenExecuted, string command )
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
                return new CachedStoredCommand( _items.Count - 1, command );
            }
        }

        public IStoredCommand GetPrevious( IStoredCommand item )
        {
            if ( item == null )
                throw new ArgumentNullException( nameof( item ) );
            if ( item == _bof )
                throw new ArgumentException( "Cannot read before BOF." );

            var bm = (CachedStoredCommand) item;
            if ( bm.ItemIndex == 0 )
                return _bof;

            lock ( _items )
            {
                var currentIndex = item == _eof ? _items.Count : bm.ItemIndex;
                return ReadAtIndex( currentIndex - 1 );
            }
        }

        public IStoredCommand GetNext( IStoredCommand item )
        {
            if ( item == null )
                throw new ArgumentNullException( nameof( item ) );
            if ( item == _eof )
                throw new ArgumentException( "Cannot read after EOF." );

            var bm = (CachedStoredCommand) item;

            lock ( _items )
            {
                var lastIndex = _items.Count - 1;
                if ( bm.ItemIndex == lastIndex )
                    return _eof;

                return ReadAtIndex( bm.ItemIndex + 1 );
            }
        }

        private IStoredCommand ReadAtIndex( int index )
        {
            lock ( _items )
            {
                var item = _items[index];
                return new CachedStoredCommand( index, item.Command );
            }
        }
    }

    internal class CachedStoredCommand : IStoredCommand
    {
        public CachedStoredCommand( int itemIndex, string command )
        {
            ItemIndex = itemIndex;
            Command = command;
        }

        public DateTime WhenExecuted => throw new NotImplementedException();

        public string Command { get; }

        public int ItemIndex { get; }
    }
}