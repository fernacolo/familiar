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

                    _trace.TraceInformation( "Attempting to lock {0}...", mutexName );
                    mutex.WaitOne();
                    try
                    {
                        _trace.TraceInformation( "Acquired lock on {0}.", mutexName );

                        if ( state.AgeOfLastChange > _timeBetweenPolls )
                        {
                            state.Read();

                            var sourceFiles = new List<FileInfo>();
                            foreach ( var sourceFile in _source.GetFiles( "*.dat" ) )
                            {
                                if ( !_accept( sourceFile.Name ) )
                                    continue;
                                _trace.TraceInformation( "Found file to replicate: {0}", sourceFile.FullName );
                                sourceFiles.Add( sourceFile );
                            }

                            IDataFile target = null;

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
                            _trace.TraceInformation( "Ignored replication state because there was a recent change." );
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

                    _trace.TraceInformation( "Released lock on {0}. Sleeping for {1} seconds before next poll.", mutexName, _timeBetweenPolls.TotalSeconds );

                    Thread.Sleep( _timeBetweenPolls );
                }
            }
            catch ( Exception ex )
            {
                _trace.TraceError( "{0}", ex );
            }
        }

        private void Replicate( FileInfo sourceFile, FileReplicationState state, ref IDataFile target )
        {
            _trace.TraceInformation( "Verifying for modifications inbound file: {0}.", sourceFile.FullName );
            var source = new DataFile( sourceFile );

            var lastReadLink = state.LastReadLink;
            var lastRead = lastReadLink == null ? source.Bof : source.ResolveLink( lastReadLink );
            var changeDetected = false;

            for ( ;; )
            {
                var newRecord = source.GetNext( lastRead );
                if ( newRecord == source.Eof )
                    break;

                if ( changeDetected == false )
                {
                    _trace.TraceInformation( "External changes were detected." );
                    changeDetected = true;
                }

                string stateTag = null;
                if ( target == null )
                {
                    var targetFile = new FileInfo( Path.Combine( _destination.FullName, "inbound.dat" ) );
                    _trace.TraceInformation( "Preparing file to update: {0}.", targetFile.FullName );
                    target = new DataFile( targetFile );
                }

                target.Write( newRecord.WhenExecuted, newRecord.Command, ref stateTag );
                lastRead = newRecord;
            }

            if ( !changeDetected )
            {
                _trace.TraceInformation( "No external change was detected." );
                return;
            }

            state.LastReadLink = source.CreateLink( lastRead );
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
                    _trace.TraceInformation( "Replication state file {0} was modified at {1:O}.", _fileInfo.FullName, lastWriteUtc );

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

            _trace.TraceInformation( "Reading replication state file: {0}", _fileInfo.FullName );

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
            _trace.TraceInformation( "Writing replication state file: {0}", _fileInfo.FullName );

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