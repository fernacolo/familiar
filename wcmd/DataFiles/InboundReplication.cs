using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using wcmd.Diagnostics;

namespace wcmd.DataFiles
{
    internal sealed class InboundReplication
    {
        private readonly TraceSource _trace;
        private readonly DirectoryInfo _source;
        private readonly DirectoryInfo _destination;
        private readonly Thread _thread;
        private readonly TimeSpan _timeBetweenPolls;
        private readonly Func<string, bool> _accept;

        public InboundReplication( DirectoryInfo source, DirectoryInfo destination, TimeSpan timeBetweenPolls, Func<string, bool> accept )
        {
            _trace = DiagnosticsCenter.GetTraceSource( nameof( InboundReplication ) );
            _source = source;
            _destination = destination;
            _thread = new Thread( Run );
            _thread.IsBackground = true;
            _timeBetweenPolls = timeBetweenPolls;
            _accept = accept;
        }

        public void Start()
        {
            _thread.Start();
        }

        private void Run()
        {
            try
            {
                var random = new Random();
                var escapedName = _destination.FullName.Replace( ":\\", "\\" ).Replace( '\\', '/' );
                var mutexName = $"familiar/replication/{escapedName}";
                //        Mutex mutex;
                //          Mutex.TryOpenExisting( mutexName, out mutex );
                //            if (mutex == null)
                var mutex = new Mutex( false, mutexName );
                for ( ;; )
                {
                    var state = new ReplicationState( _trace, _destination );

                    _trace.TraceVerbose( "Attempting to lock {0}...", mutexName );
                    mutex.WaitOne();
                    try
                    {
                        _trace.TraceVerbose( "Acquired lock on {0}.", mutexName );

                        if ( state.AgeOfLastChange > _timeBetweenPolls )
                        {
                            state.Read();

                            var sourceFiles = new List<FileInfo>();
                            foreach ( var sourceFile in _source.GetFiles( "*.dat" ) )
                            {
                                if ( !_accept( sourceFile.Name ) )
                                    continue;
                                _trace.TraceVerbose( "Found file to replicate: {0}", sourceFile.FullName );
                                sourceFiles.Add( sourceFile );
                            }

                            IDataStore target = null;

                            while ( sourceFiles.Count > 0 )
                            {
                                var index = random.Next( sourceFiles.Count );
                                var sourceFile = sourceFiles[index];
                                sourceFiles.RemoveAt( index );

                                var fileState = state.GetOrCreateFileState( sourceFile.Name );
                                Replicate( sourceFile, fileState, ref target );
                                if ( fileState.Changed )
                                    state.Write();
                            }
                        }
                        else
                        {
                            _trace.TraceVerbose( "Replication not needed at this time." );
                        }
                    }
                    catch ( Exception ex )
                    {
                        _trace.TraceError( "{0}", ex );
                    }
                    finally
                    {
                        mutex.ReleaseMutex();
                    }

                    _trace.TraceVerbose( "Released lock on {0}. Sleeping for {1} seconds before next poll.", mutexName, _timeBetweenPolls.TotalSeconds );

                    Thread.Sleep( _timeBetweenPolls );
                }
            }
            catch ( Exception ex )
            {
                _trace.TraceError( "{0}", ex );
            }
        }

        private void Replicate( FileInfo sourceFile, FileReplicationState state, ref IDataStore target )
        {
            _trace.TraceVerbose( "Verifying for modifications inbound file: {0}.", sourceFile.FullName );
            var source = new FileStore( sourceFile );

            var lastReadLink = state.LastReadLink;
            var lastReplicated = GetLastReplicatedItem( lastReadLink, source, ref target, out var isLinkValid );
            var changeDetected = false;

            for ( ;; )
            {
                var newItem = source.GetNext( lastReplicated );
                if ( newItem == source.Eof )
                    break;

                if ( changeDetected == false )
                {
                    _trace.TraceInformation( "Changes detected at {0}.", sourceFile.FullName );
                    changeDetected = true;
                }

                string stateTag = null;
                ResolveTargetStoreIfNeeded( ref target );

                target.Write( ref stateTag, newItem.Payload );
                lastReplicated = newItem;
            }

            if ( !changeDetected )
            {
                _trace.TraceVerbose( "No change detected." );

                if ( isLinkValid )
                    return;

                _trace.TraceInformation( "Attempting to fix last read link..." );
            }

            state.LastReadLink = source.CreateLink( lastReplicated );

            if ( !isLinkValid )
                source.ResolveLink( state.LastReadLink );
        }

        private IStoredItem GetLastReplicatedItem( byte[] lastReadLink, IDataStore source, ref IDataStore target, out bool isLinkValid )
        {
            isLinkValid = true;

            if ( lastReadLink == null )
                return source.Bof;

            try
            {
                return source.ResolveLink( lastReadLink );
            }
            catch ( Exception ex )
            {
                _trace.TraceError( "Unable to resolve last read link: {0}", ex );
            }

            _trace.TraceInformation( "Trying to recover..." );
            ResolveTargetStoreIfNeeded( ref target );

            var recoveryLookupSize = 2 * 1024 * 1024;
            var recoveryLookupTimeout = TimeSpan.FromSeconds( 1 );

            var sourceItems = GetLastItems( source, recoveryLookupSize, recoveryLookupTimeout );
            var targetItems = GetLastItems( target, recoveryLookupSize, recoveryLookupTimeout );

            var result = GetLastItemFromLeftPresentOnRight( sourceItems, targetItems );

            if ( result != null )
                _trace.TraceInformation( "Recovery was successful!" );
            else
            {
                _trace.TraceWarning( "Recovery failed. Data loss is possible." );
                result = source.GetPrevious( source.Eof );
            }

            isLinkValid = false;
            return result;
        }

        private static List<IStoredItem> GetLastItems( IDataStore store, int lookupSize, TimeSpan lookupTimeout )
        {
            var current = store.Eof;
            var items = new List<IStoredItem>( 100 );

            var sw = Stopwatch.StartNew();
            do
            {
                current = store.GetPrevious( current );
                if ( current == store.Bof )
                    break;
                items.Add( current );

                lookupSize -= current.SizeInStore;
            } while ( lookupSize > 0 && sw.Elapsed < lookupTimeout );

            var result = new List<IStoredItem>( items.Count );
            for ( var i = items.Count - 1; i >= 0; --i )
                result.Add( items[i] );

            return result;
        }

        private IStoredItem GetLastItemFromLeftPresentOnRight( List<IStoredItem> leftItems, List<IStoredItem> rightItems )
        {
            if ( rightItems.Count == 0 )
                return null;

            var leftIndex = leftItems.Count;
            while ( --leftIndex >= 0 )
            {
                var leftItem = leftItems[leftIndex];
                foreach ( var rightItem in rightItems )
                    if ( Match( leftItem.Payload, rightItem.Payload ) )
                        return leftItem;
            }

            return null;
        }

        private bool Match( ItemPayload a, ItemPayload b )
        {
            var aCmd = a as CommandPayload;
            var bCmd = b as CommandPayload;
            if ( aCmd == null || bCmd == null )
                return false;

            if ( aCmd.WhenExecuted == bCmd.WhenExecuted && aCmd.Command == bCmd.Command )
                return true;

            return false;
        }

        private void ResolveTargetStoreIfNeeded( ref IDataStore target )
        {
            if ( target != null )
                return;

            var targetFile = new FileInfo( Path.Combine( _destination.FullName, "inbound.dat" ) );
            _trace.TraceInformation( "Preparing file to update: {0}.", targetFile.FullName );
            target = new FileStore( targetFile );
        }
    }

    internal class ReplicationState
    {
        private readonly TraceSource _trace;
        private readonly FileInfo _fileInfo;
        private readonly Dictionary<string, FileReplicationState> _fileStates;

        public ReplicationState( TraceSource trace, DirectoryInfo destination )
        {
            if ( trace == null )
                throw new ArgumentNullException( nameof( trace ) );
            if ( destination == null )
                throw new ArgumentNullException( nameof( destination ) );

            _trace = trace;
            _fileInfo = new FileInfo( Path.Combine( destination.FullName, "replstate-v3.dat" ) );
            _fileStates = new Dictionary<string, FileReplicationState>();
        }

        public TimeSpan AgeOfLastChange
        {
            get
            {
                if ( !_fileInfo.Exists )
                {
                    _trace.TraceInformation( "Replication state file not found: {0}.", _fileInfo.FullName );
                    return TimeSpan.MaxValue;
                }

                try
                {
                    _fileInfo.Refresh();
                    var lastWriteUtc = _fileInfo.LastWriteTimeUtc;
                    _trace.TraceVerbose( "Replication state file {0} was modified at {1:O}.", _fileInfo.FullName, lastWriteUtc );

                    return DateTime.UtcNow - _fileInfo.LastWriteTimeUtc;
                }
                catch ( Exception ex )
                {
                    _trace.TraceError( "{0}", ex );
                    return TimeSpan.Zero;
                }
            }
        }

        public void Read()
        {
            if ( !_fileInfo.Exists )
            {
                _trace.TraceInformation( "Replication state file not found: {0}. Assuming empty state.", _fileInfo.FullName );
                return;
            }

            _trace.TraceVerbose( "Reading replication state file: {0}", _fileInfo.FullName );

            using ( var stream = new FileStream( _fileInfo.FullName, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete ) )
            using ( var reader = new BinaryReader( stream, Encoding.UTF8, true ) )
            {
                var count = reader.ReadInt32();
                while ( count > 0 )
                {
                    var fileState = new FileReplicationState( reader );
                    _fileStates[fileState.Name] = fileState;
                    --count;
                }
            }
        }

        public void Write()
        {
            _trace.TraceVerbose( "Writing replication state file: {0}", _fileInfo.FullName );

            using ( var stream = new FileStream( _fileInfo.FullName, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Delete ) )
            {
                stream.SetLength( 0L );
                using ( var writer = new BinaryWriter( stream, Encoding.UTF8, true ) )
                {
                    writer.Write( _fileStates.Count );
                    foreach ( var fileState in _fileStates.Values )
                        fileState.Write( writer );
                }
            }
        }

        public FileReplicationState GetOrCreateFileState( string sourceFileName )
        {
            var key = sourceFileName.ToLowerInvariant();
            _fileStates.TryGetValue( key, out var result );
            if ( result == null )
                _fileStates[key] = result = new FileReplicationState( sourceFileName );
            return result;
        }
    }

    internal class FileReplicationState
    {
        public FileReplicationState( string name )
        {
            Name = name ?? throw new ArgumentNullException( nameof( name ) );
        }

        public FileReplicationState( BinaryReader reader )
        {
            Name = reader.ReadString();
            if ( !reader.ReadBoolean() )
                _lastReadLink = null;
            else
            {
                var linkSize = reader.ReadInt32();
                _lastReadLink = reader.ReadBytes( linkSize );
            }
        }

        public void Write( BinaryWriter writer )
        {
            Debug.Assert( Name != null );
            writer.Write( Name );
            writer.Write( _lastReadLink != null );
            if ( _lastReadLink != null )
            {
                writer.Write( _lastReadLink.Length );
                writer.Write( _lastReadLink );
            }
        }

        public string Name { get; }

        public bool Changed { get; private set; }

        private byte[] _lastReadLink;

        public byte[] LastReadLink
        {
            get => _lastReadLink;
            set
            {
                _lastReadLink = value;
                Changed = true;
            }
        }
    }
}