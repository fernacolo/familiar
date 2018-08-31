using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using wcmd.Native;

namespace wcmd
{
    internal sealed class HandleStream : Stream
    {
        private readonly IntPtr _handle;

        public HandleStream( IntPtr handle )
        {
            _handle = handle;
        }

        public override void Flush()
        {
            throw new NotImplementedException();
        }

        public override long Seek( long offset, SeekOrigin origin )
        {
            throw new NotImplementedException();
        }

        public override void SetLength( long value )
        {
            throw new NotImplementedException();
        }

        public override int Read( byte[] buffer, int offset, int count )
        {
            var readBuffer = offset == 0 ? buffer : new byte[count];
            var uCount = (uint) count;
            uint uReadCount = 0;
            var success = Kernel32.ReadFile( _handle, readBuffer, uCount, ref uReadCount, IntPtr.Zero );
            if ( !success && uReadCount == 0 )
                throw new Exception( $"Unable to use ReadFile to read {uCount} bytes: error {Marshal.GetLastWin32Error()}." );
            if ( readBuffer != buffer )
                Array.Copy( readBuffer, 0, buffer, offset, uReadCount );
            Trace.TraceInformation( "{0} bytes read from {1}.", uReadCount, _handle );
            return (int) uReadCount;
        }

        public override void Write( byte[] buffer, int offset, int count )
        {
            var toWrite = count;
            var writeBuffer = buffer;
            while ( toWrite > 0 )
            {
                if ( offset != 0 )
                {
                    if ( writeBuffer == buffer )
                        writeBuffer = new byte[toWrite];
                    Array.Copy( buffer, offset, writeBuffer, 0, toWrite );
                }

                var uWrittenCount = (uint) 0;
                var success = Kernel32.WriteFile( _handle, writeBuffer, (uint) toWrite, ref uWrittenCount, IntPtr.Zero );
                if ( !success && uWrittenCount == 0 )
                    throw new Exception( $"WriteFile {toWrite} bytes: error {Marshal.GetLastWin32Error()}." );
                Trace.TraceInformation( "{0} bytes written to {1}.", uWrittenCount, _handle );
                offset += (int) uWrittenCount;
                toWrite -= (int) uWrittenCount;
            }
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotImplementedException();

        public override long Position
        {
            get => throw new NotImplementedException();
            set => throw new NotImplementedException();
        }
    }
}