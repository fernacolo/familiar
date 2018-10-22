using System;
using System.Collections.Generic;

namespace wcmd.DataFiles
{
    internal class CachedDataFile : IDataFile
    {
        private readonly IDataFile _inner;
        private readonly CacheEntry _bof;
        private readonly CacheEntry _eof;
        private readonly List<CacheEntry> _items;

        public string FileName => _inner.FileName;

        public CachedDataFile( IDataFile inner )
        {
            _inner = inner ?? throw new ArgumentNullException( nameof( inner ) );
            _bof = new CacheEntry( -1, null );
            _eof = new CacheEntry( int.MaxValue, null );

            var item = inner.GetNext( _inner.Bof );
            var index = 0;

            _items = new List<CacheEntry>();
            while ( item != inner.Eof )
            {
                _items.Add( new CacheEntry( index++, item.Command ) );
                item = inner.GetNext( item );
            }
        }

        public IStoredCommand Bof => _bof;

        public IStoredCommand Eof => _eof;

        public IStoredCommand Write( DateTime whenExecuted, string command )
        {
            _inner.Write( whenExecuted, command );
            lock ( _items )
            {
                var result = new CacheEntry( _items.Count - 1, command );
                _items.Add( result );
                return result;
            }
        }

        public IStoredCommand GetPrevious( IStoredCommand item )
        {
            if ( item == null )
                throw new ArgumentNullException( nameof( item ) );
            if ( item == _bof )
                throw new ArgumentException( "Cannot read before BOF." );

            var bm = (CacheEntry) item;
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

            var bm = (CacheEntry) item;

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
                return new CacheEntry( index, item.Command );
            }
        }

        internal class CacheEntry : IStoredCommand
        {
            public CacheEntry( int itemIndex, string command )
            {
                ItemIndex = itemIndex;
                Command = command;
            }

            public DateTime WhenExecuted => throw new NotImplementedException();

            public string Command { get; }

            public int ItemIndex { get; }
        }
    }
}