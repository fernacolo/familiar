﻿using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using wcmd.Diagnostics;

namespace wcmd.DataFiles
{
    internal sealed class ReplicationJob : IDisposable
    {
        private readonly TraceSource _trace;
        private readonly DirectoryInfo _source;
        private readonly DirectoryInfo _destination;
        private readonly Thread _thread;
        private readonly TimeSpan _timeBetweenPolls;
        private readonly Func<string, bool> _accept;
        private int _aborted;

        public ReplicationJob( string jobName, DirectoryInfo source, DirectoryInfo destination, TimeSpan timeBetweenPolls, Func<string, bool> accept )
        {
            _trace = DiagnosticsCenter.GetTraceSource( jobName );
            _source = source;
            _destination = destination;
            _thread = new Thread( Run );
            _timeBetweenPolls = timeBetweenPolls;
            _accept = accept;
        }

        public bool Aborted => Interlocked.CompareExchange( ref _aborted, 0, -1 ) == 1;

        public void Dispose()
        {
            Interlocked.Exchange( ref _aborted, 1 );
        }

        public void Start()
        {
            _thread.Start();
        }

        private void Run()
        {
            try
            {
                for ( ;; )
                {
                    foreach ( var sourceFile in _source.GetFiles( "*.dat" ) )
                    {
                        if ( Aborted )
                            return;
                        if ( !_accept( sourceFile.Name ) )
                            continue;
                        CopyOrUpdate( sourceFile );
                    }

                    if ( Aborted )
                        return;
                    Thread.Sleep( _timeBetweenPolls );
                }
            }
            catch ( Exception ex )
            {
                _trace.TraceError( "{0}", ex );
            }
        }

        private void CopyOrUpdate( FileInfo sourceFile )
        {
            var destFile = new FileInfo( Path.Combine( _destination.FullName, sourceFile.Name ) );
            if ( !destFile.Exists )
            {
                _trace.TraceInformation( "New file detected: {0}", sourceFile.FullName );
                sourceFile.CopyTo( destFile.FullName, true );
                return;
            }

            var sourceLength = sourceFile.Length;
            var destLength = destFile.Length;
            if ( destLength >= sourceLength )
                return;

            _trace.TraceInformation( "File change detected: {0} (+{1} bytes)", sourceFile.FullName, sourceLength - destLength );
            using ( var sourceStream = new FileStream( sourceFile.FullName, FileMode.Open, FileAccess.Read, FileShare.Delete | FileShare.Read ) )
            {
                using ( var destStream = new FileStream( destFile.FullName, FileMode.Open, FileAccess.Write, FileShare.Delete ) )
                {
                    sourceStream.Position = destLength;
                    destStream.Position = destLength;
                    sourceStream.CopyTo( destStream );
                }
            }
        }
    }
}