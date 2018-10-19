using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using wcmd.Diagnostics;

namespace wcmd.DataFiles
{
    internal sealed class ReplicationJob
    {
        private readonly TraceSource _trace;
        private readonly DirectoryInfo _source;
        private readonly DirectoryInfo _destination;
        private readonly Thread _thread;
        private readonly TimeSpan _timeBetweenPolls;
        private readonly Func<string, bool> _accept;

        public ReplicationJob( string jobName, DirectoryInfo source, DirectoryInfo destination, TimeSpan timeBetweenPolls, Func<string, bool> accept )
        {
            _trace = DiagnosticsCenter.GetTraceSource( this, jobName );
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
                for ( ;; )
                {
                    foreach ( var sourceFile in _source.GetFiles( "*.dat" ) )
                    {
                        if ( !_accept( sourceFile.Name ) )
                            continue;
                        CopyOrUpdate( sourceFile );
                    }

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