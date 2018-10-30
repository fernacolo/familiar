using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace wcmd.DataFiles
{
    internal sealed class MergedDataStore : IDataStore
    {
        private readonly IDataStore[] _innerStores;

        public MergedDataStore( IReadOnlyList<IDataStore> stores )
        {
            if ( stores == null )
                throw new ArgumentNullException( nameof( stores ) );

            _innerStores = new IDataStore[stores.Count];

            var bofItems = new IStoredItem[_innerStores.Length];
            var eofItems = new IStoredItem[_innerStores.Length];
            for ( var i = 0; i < _innerStores.Length; ++i )
            {
                _innerStores[i] = stores[i];
                bofItems[i] = _innerStores[i].Bof;
                eofItems[i] = _innerStores[i].Eof;
            }

            Bof = new MergedDataStoreItem( bofItems, _innerStores.Length - 1 );
            Eof = new MergedDataStoreItem( eofItems, 0 );
        }

        public string StateTag
        {
            get
            {
                var sb = new StringBuilder();
                foreach ( var store in _innerStores )
                    sb.Append( store.StateTag ).Append( "/" );
                return sb.ToString();
            }
        }

        public string FileName => throw new NotImplementedException();

        public IStoredItem Bof { get; }

        public IStoredItem Eof { get; }

        public IStoredItem Write( DateTime whenExecuted, string command, ref string stateTag )
        {
            // We always write to the last store.
            var storeToWrite = _innerStores[_innerStores.Length - 1];
            var storeToWriteState = (string) null;

            if ( stateTag != null )
            {
                if ( StateTag != stateTag )
                    return null;
                storeToWriteState = storeToWrite.StateTag;
                if ( StateTag != stateTag )
                    return null;
            }

            // TODO: There is a slight chance that StateTag changes between the last verification above and the execution.

            var written = storeToWrite.Write( whenExecuted, command, ref storeToWriteState );
            if ( written == null )
                return null;

            return GetPrevious( Eof );
        }

        public IStoredItem GetPrevious( IStoredItem item )
        {
            var mergedItem = (MergedDataStoreItem) item;
            var activeItems = (IStoredItem[]) mergedItem.Items.Clone();
            var activeItemIndex = mergedItem.ItemIndex;

            for ( var i = 0; i < _innerStores.Length; ++i )
            {
                --activeItemIndex;
                if ( activeItemIndex == -1 )
                    activeItemIndex = _innerStores.Length - 1;

                var innerItem = activeItems[activeItemIndex];
                var innerStore = _innerStores[activeItemIndex];

                if ( innerItem == innerStore.Bof )
                    continue;

                innerItem = activeItems[activeItemIndex] = innerStore.GetPrevious( innerItem );
                if ( innerItem != innerStore.Bof )
                    return new MergedDataStoreItem( activeItems, activeItemIndex );
            }

            for ( var i = 0; i < _innerStores.Length; ++i )
                Debug.Assert( activeItems[i] == _innerStores[i].Bof );

            return Bof;
        }

        public IStoredItem GetNext( IStoredItem item )
        {
            throw new NotImplementedException();
        }

        public byte[] CreateLink( IStoredItem item )
        {
            throw new NotImplementedException();
        }

        public IStoredItem ResolveLink( byte[] link )
        {
            throw new NotImplementedException();
        }
    }

    internal class MergedDataStoreItem : IStoredItem
    {
        private IStoredItem[] _items;
        private int _itemIndex;
        private IStoredItem _current;

        public MergedDataStoreItem( IStoredItem[] items, int itemIndex )
        {
            _items = items;
            _itemIndex = itemIndex;
            _current = items[itemIndex];
        }

        public IStoredItem[] Items => _items;

        public int ItemIndex => _itemIndex;

        public string StateTag
        {
            get
            {
                var sb = new StringBuilder();
                foreach ( var item in _items )
                    sb.Append( item.StateTag ).Append( "/" );
                return sb.ToString();
            }
        }

        public DateTime WhenExecuted => _current.WhenExecuted;

        public string Command => _current.Command;
    }
}