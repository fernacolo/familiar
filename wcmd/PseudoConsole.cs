using System;
using System.IO;
using System.Text;
using Microsoft.Win32.SafeHandles;
using wcmd.Native;

namespace wcmd
{
    internal class PseudoConsole
    {
        private IntPtr _handle;
        private StreamWriter _input;
        private StreamReader _output;

        public IntPtr Handle => _handle;
        public StreamWriter Input => _input;
        public StreamReader Output => _output;

        public PseudoConsole( short width, short height )
        {
            var size = new COORD
            {
                X = width,
                Y = height
            };

            if ( !Kernel32.CreatePipe( out var inputReadSide, out var inputWriteSide, IntPtr.Zero, 0 ) )
                throw new InvalidOperationException( "Unable to create input pipe." );
            _input = new StreamWriter(
                new FileStream( new SafeFileHandle( inputWriteSide, false ), FileAccess.Write ),
                Encoding.UTF8
            );

            if ( !Kernel32.CreatePipe( out var outputReadSide, out var outputWriteSide, IntPtr.Zero, 0 ) )
                throw new InvalidOperationException( "Unable to create output pipe." );
            _output = new StreamReader(
                new FileStream( new SafeFileHandle( outputReadSide, false ), FileAccess.Read ),
                Encoding.UTF8
            );

            var result = Kernel32.CreatePseudoConsole( size, inputReadSide, outputWriteSide, 0, out _handle );
            if ( result != Kernel32.HRESULT.S_OK )
                throw new InvalidOperationException( $"CreatePseudoConsole returned {result}." );
        }
    }
}