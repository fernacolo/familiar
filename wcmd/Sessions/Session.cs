using System;
using System.IO;
using System.Text;

namespace wcmd.Sessions
{
    class Session
    {
        private Configuration _config;
        private readonly string _dataFileName;

        public Session( Configuration config )
        {
            _config = config;
            _dataFileName = Path.Combine(config.LocalDbDirectory.ToString(),  $"{config.SessionId}.dat");
        }

        public void Write( DateTime whenExecuted, string command )
        {
            var record = new SessionRecord
            {
                Type = SessionRecord.CommandV1,
                WhenExecuted = whenExecuted,
                Command = command
            };

            WriteRecord( record );
        }

        private void WriteRecord( SessionRecord record )
        {
            var dataSize = record.DataSize;
            if ( dataSize < 1 || dataSize > ushort.MaxValue )
                throw new InvalidOperationException( $"Invalid record data size: {dataSize}" );

            var size = (ushort) dataSize;

            using ( var writer = GetWriterForAppend() )
            {
                var stream = writer.BaseStream;

                WriteBeforeMark( writer, size );
                var dataOffset = stream.Position;

                WriteRecordData( writer, record );
                var writtenSize = stream.Position - dataOffset;
                if ( writtenSize != size )
                    throw new InvalidOperationException( $"Expected {size} bytes, but {writtenSize} were written." );

                WriteZeros( writer, Align( writtenSize ) - writtenSize );
                WriteAfterMark( writer, size );
            }
        }

        private BinaryWriter GetWriterForAppend()
        {
            var stream = new FileStream( _dataFileName, FileMode.Append );
            var writer = new BinaryWriter( stream, Encoding.UTF8 );
            return writer;
        }

        private static void WriteRecordData( BinaryWriter writer, SessionRecord record )
        {
            AssertAligned( writer );
            record.WriteTo( writer );
        }

        private static void WriteBeforeMark( BinaryWriter writer, ushort size )
        {
            AssertAligned( writer );
            writer.Write( (byte) 0xFC );
            writer.Write( size );
            writer.Write( (byte) 0xFD );
        }

        private static void AssertAligned( BinaryWriter writer )
        {
            var pos = writer.BaseStream.Position;
            if ( pos != Align( pos ) )
                throw new InvalidOperationException( "Stream is not aligned." );
        }

        private static long Align( long value )
        {
            var excess = value % 4L;
            if ( excess == 0L )
                return value;
            return value + 4L - excess;
        }

        private void WriteAfterMark( BinaryWriter writer, ushort size )
        {
            AssertAligned( writer );
            writer.Write( (byte) 0xFE );
            writer.Write( size );
            writer.Write( (byte) 0xFF );
        }

        private static void WriteZeros( BinaryWriter writer, long howMany )
        {
            switch ( howMany )
            {
                case 0L:
                    return;

                case 1L:
                    writer.Write( (byte) 0 );
                    return;

                case 2L:
                    writer.Write( (ushort) 0 );
                    return;

                case 3L:
                    writer.Write( (byte) 0 );
                    writer.Write( (ushort) 0 );
                    return;

                default:
                    throw new InvalidOperationException( $"Unexpected number of zeros to write: {howMany}" );
            }
        }
    }

    internal class SessionRecord
    {
        public const byte CommandV1 = 0x01;

        public byte Type;

        public DateTime WhenExecuted;
        public string Command;

        private int _binarySize;

        public int DataSize
        {
            get
            {
                if ( _binarySize != 0 )
                    return _binarySize;

                var stream = new SizeOnlyStream();
                var writer = new BinaryWriter( stream, Encoding.UTF8, true );
                WriteTo( writer );
                _binarySize = (int) stream.Length;

                return _binarySize;
            }
        }

        public void WriteTo( BinaryWriter writer )
        {
            writer.Write( Type );
            switch ( Type )
            {
                case CommandV1:
                    WriteDateTime( writer, WhenExecuted );
                    writer.Write( Command );
                    break;

                default:
                    throw new InvalidOperationException( $"Unknown record type: 0x{Type:X2}" );
            }
        }

        private static void WriteDateTime( BinaryWriter writer, DateTime value )
        {
            writer.Write( (byte) value.Kind );
            writer.Write( value.Ticks );
        }

   }
}