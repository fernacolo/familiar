using System;
using System.IO;
using System.Text;

namespace wcmd.DataFiles
{
    internal sealed class DataFileRecord
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

        public void ReadFrom( BinaryReader reader )
        {
            Type = reader.ReadByte();
            switch ( Type )
            {
                case CommandV1:
                    WhenExecuted = ReadDateTime( reader );
                    Command = reader.ReadString();
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

        private static DateTime ReadDateTime( BinaryReader reader )
        {
            var kind = (DateTimeKind) reader.ReadByte();
            var ticks = reader.ReadInt64();
            return new DateTime( ticks, kind );
        }
    }
}