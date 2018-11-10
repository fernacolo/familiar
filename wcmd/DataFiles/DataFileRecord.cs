using System;
using System.IO;
using System.Text;

namespace wcmd.DataFiles
{
    public sealed class DataFileRecord
    {
        public const byte CommandV1 = 0x01;
        public const byte CommandV2 = 0x02;
        public const byte Raw = 0xFF;

        public byte Type;
        public byte[] Buffer;

        public string MachineName;
        public int Pid;
        public DateTime WhenExecuted;
        public string Command;
        public string Output;

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

        private void Clear()
        {
            Type = default;
            MachineName = default;
            Pid = default;
            WhenExecuted = default;
            Command = default;
            Output = default;
            _binarySize = default;
        }

        public void WriteTo( BinaryWriter writer )
        {
            switch ( Type )
            {
                case CommandV1:
                    writer.Write( Type );
                    WriteDateTime( writer, WhenExecuted );
                    writer.Write( Command );
                    break;

                case CommandV2:
                    writer.Write( Type );
                    WriteNullableStr( writer, MachineName );
                    writer.Write( Pid );
                    WriteDateTime( writer, WhenExecuted );
                    WriteNullableStr( writer, Command );
                    WriteNullableStr( writer, Output );
                    break;

                case Raw:
                    writer.Write( Buffer );
                    break;

                default:
                    throw new InvalidOperationException( $"Unknown record type: 0x{Type:X2}" );
            }
        }

        public void ReadFrom( BinaryReader reader )
        {
            Clear();
            Type = reader.ReadByte();
            switch ( Type )
            {
                case CommandV1:
                    WhenExecuted = ReadDateTime( reader );
                    Command = reader.ReadString().TrimEnd( '\r', '\n', ' ', '\t' );
                    break;

                case CommandV2:
                    MachineName = ReadNullableStr( reader );
                    Pid = reader.ReadInt32();
                    WhenExecuted = ReadDateTime( reader );
                    Command = ReadNullableStr( reader );
                    Output = ReadNullableStr( reader );
                    break;

                default:
                    throw new InvalidOperationException( $"Unknown record type: 0x{Type:X2}" );
            }
        }

        private static void WriteNullableStr( BinaryWriter writer, string value )
        {
            writer.Write( value != null );
            if ( value != null )
                writer.Write( value );
        }

        private static string ReadNullableStr( BinaryReader reader )
        {
            return reader.ReadBoolean() ? reader.ReadString() : null;
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

        public void SetRawData( byte[] buffer )
        {
            Clear();
            Type = Raw;
            Buffer = buffer;
        }
    }
}