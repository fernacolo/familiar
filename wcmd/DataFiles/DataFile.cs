using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using wcmd.Diagnostics;
using wcmd.Sessions;

namespace wcmd.DataFiles
{
    internal sealed class DataFile : IDataFile
    {
        private readonly TraceSource _trace;
        private readonly string _dataFileName;

        public DataFile( Configuration config )
        {
            _trace = DiagnosticsCenter.GetTraceSource( this );
            _dataFileName = Path.Combine( config.LocalDbDirectory.ToString(), $"{config.SessionId}.dat" );
        }

        public string StateTag
        {
            get
            {
                var info = new FileInfo( _dataFileName );
                return $"{info.LastWriteTimeUtc.Ticks}/{info.Length}";
            }
        }

        public string FileName => new FileInfo( _dataFileName ).Name;

        public void DumpRecords()
        {
            var lastGoodPosition = 0L;
            try
            {
                using ( var reader = GetReader() )
                {
                    var stream = reader.BaseStream;
                    var length = stream.Length;
                    while ( stream.Position < length )
                    {
                        _trace.TraceInformation( "Reading at {0}", stream.Position );
                        var size = ReadOpenMark( reader );
                        _trace.TraceInformation( "Found record with size: {0}", size );
                        reader.ReadBytes( Align( size ) );
                        ReadCloseMark( reader );
                        lastGoodPosition = stream.Position;
                    }
                }
            }
            catch ( Exception ex )
            {
                _trace.TraceError( "{0}", ex );
                using ( var stream = new FileStream( _dataFileName, FileMode.Open, FileAccess.Write, FileShare.Delete ) )
                    stream.SetLength( lastGoodPosition );
                _trace.TraceInformation( "File was truncated at: {0}.", lastGoodPosition );
            }
        }

        private readonly IStoredCommand _bof = new DataFileBookmark( null, -1L, null );
        private readonly IStoredCommand _eof = new DataFileBookmark( null, long.MaxValue, null );

        public IStoredCommand Bof => _bof;

        public IStoredCommand Eof => _eof;

        public IStoredCommand Write( DateTime whenExecuted, string command, ref string stateTag )
        {
            var record = new DataFileRecord
            {
                Type = DataFileRecord.CommandV1,
                WhenExecuted = whenExecuted,
                Command = command
            };

            var position = WriteRecord( record, ref stateTag );
            if ( position == -1L )
                return null;

            return new DataFileBookmark( stateTag, position, command );
        }

        private long WriteRecord( DataFileRecord record, ref string stateTag )
        {
            var dataSize = record.DataSize;
            if ( dataSize < 1 || dataSize > ushort.MaxValue )
                throw new InvalidOperationException( $"Invalid record data size: {dataSize}" );

            long position;
            var size = (ushort) dataSize;

            using ( var writer = GetWriterForAppend() )
            {
                if ( stateTag != null && StateTag != stateTag )
                    return -1L;

                var stream = writer.BaseStream;
                position = stream.Position;

                WriteOpenMark( writer, size );
                var dataOffset = stream.Position;

                WriteRecordData( writer, record );
                var writtenSize = stream.Position - dataOffset;
                if ( writtenSize != size )
                    throw new InvalidOperationException( $"Expected {size} bytes, but {writtenSize} were written." );

                WriteZeros( writer, Align( writtenSize ) - writtenSize );
                WriteCloseMark( writer, size );
            }

            // MINOR: Slight change of concurrent modification between file closure and this read.
            stateTag = StateTag;

            return position;
        }

        private void WriteRecordData( BinaryWriter writer, DataFileRecord record )
        {
            AssertAligned( writer );
            record.WriteTo( writer );
        }

        public IStoredCommand GetPrevious( IStoredCommand item )
        {
            if ( item == null )
                throw new ArgumentNullException( nameof( item ) );
            if ( item == _bof )
                throw new ArgumentException( "Cannot read before BOF." );

            var bm = (DataFileBookmark) item;
            if ( bm.Position == 0L )
                return _bof;

            using ( var reader = GetReader() )
            {
                var stream = reader.BaseStream;
                var position = bm == _eof ? stream.Length : bm.Position;
                for ( ;; )
                {
                    var record = ReadPreviousRecord( reader, ref position );
                    if ( record.Type == DataFileRecord.CommandV1 )
                        return new DataFileBookmark( StateTag, position, record.Command );
                    if ( position == 0L )
                        return _bof;
                }
            }
        }

        public IStoredCommand GetNext( IStoredCommand item )
        {
            if ( item == null )
                throw new ArgumentNullException( nameof( item ) );
            if ( item == _eof )
                throw new ArgumentException( "Cannot read after EOF." );

            var bm = (DataFileBookmark) item;

            using ( var reader = GetReader() )
            {
                long position;
                if ( bm == _bof )
                    position = 0L;
                else
                {
                    position = bm.Position;
                    ReadNextRecord( reader, ref position );
                }

                for ( ;; )
                {
                    var lastPosition = position;
                    var record = ReadNextRecord( reader, ref position );
                    if ( record == null )
                        return _eof;
                    if ( record.Type == DataFileRecord.CommandV1 )
                        return new DataFileBookmark( StateTag, lastPosition, record.Command );
                }
            }
        }

        /// <summary>
        /// Reads the record positioned before the specified position. Updates the specified position
        /// to contain the position of the record that was just read.
        /// </summary>
        private DataFileRecord ReadPreviousRecord( BinaryReader reader, ref long pos )
        {
            // Read the close mark to determine the record size.
            var stream = reader.BaseStream;
            pos -= SizeOfCloseMark;
            stream.Position = pos;
            var size = ReadCloseMark( reader );

            // Move to the open mark.
            size = Align( size );
            pos -= size;
            pos -= SizeOfOpenMark;
            stream.Position = pos;

            // Read the record.
            return ReadRecord( reader );
        }

        /// <summary>
        /// Reads the record positioned at the specified position. Updates the specified position to contain the position of the next record.
        /// Returns null if the specified position is after the end of stream.
        /// </summary>
        private DataFileRecord ReadNextRecord( BinaryReader reader, ref long pos )
        {
            AssertAligned( pos );
            var stream = reader.BaseStream;
            if ( pos >= stream.Length )
                return null;
            stream.Position = pos;
            var record = ReadRecord( reader );
            pos = Align( stream.Position );
            return record;
        }

        private DataFileRecord ReadRecord( BinaryReader reader )
        {
            var size = ReadOpenMark( reader );
            size = Align( size );

            var buffer = new byte[size];
            var index = 0;
            while ( size > 0 )
            {
                var read = reader.Read( buffer, index, size );
                if ( read < 1 || read > size )
                    throw new IOException( $"Reader method received {size}, returned {read}." );
                size -= (ushort) read;
                index += read;
            }

            ReadCloseMark( reader );

            var stream = new MemoryStream( buffer );
            var recordReader = new BinaryReader( stream, Encoding.UTF8 );
            var result = new DataFileRecord();
            result.ReadFrom( recordReader );

            return result;
        }

        private const uint OpenMark = 0xFEEADFED;
        private const int SizeOfOpenMark = sizeof( uint ) + sizeof( uint );

        private void WriteOpenMark( BinaryWriter writer, ushort size )
        {
            AssertAligned( writer );
            writer.Write( OpenMark );
            writer.Write( (uint) size );
        }

        private ushort ReadOpenMark( BinaryReader reader )
        {
            AssertAligned( reader );

            var magic = reader.ReadUInt32();
            if ( magic != OpenMark )
                ThrowDataCorruptionException( reader, -sizeof( uint ), $"Expected {OpenMark:X}, but found {magic:X}." );

            var size32 = reader.ReadUInt32();
            if ( size32 > ushort.MaxValue )
                ThrowDataCorruptionException( reader, -sizeof( uint ), $"Expected ushort value, but found {size32:X}." );

            return (ushort) size32;
        }

        private const uint CloseMark = 0xDEFDAEEF;
        private const int SizeOfCloseMark = sizeof( uint ) + sizeof( uint );

        private void WriteCloseMark( BinaryWriter writer, ushort size )
        {
            AssertAligned( writer );
            writer.Write( (uint) size );
            writer.Write( CloseMark );
        }

        private ushort ReadCloseMark( BinaryReader reader )
        {
            AssertAligned( reader );

            var size32 = reader.ReadUInt32();
            var magic = reader.ReadUInt32();

            if ( magic != CloseMark )
                ThrowDataCorruptionException( reader, -sizeof( uint ), $"Expected {OpenMark:X}, but found {magic:X}." );

            if ( size32 > ushort.MaxValue )
                ThrowDataCorruptionException( reader, 2 * -sizeof( uint ), $"Expected ushort value, but found {size32:X}." );

            return (ushort) size32;
        }

        private void ThrowDataCorruptionException( BinaryReader reader, long offset, string message )
        {
            message = $"File {_dataFileName}, position {GetPosition( reader ) + offset}: {message}";
            throw new DataCorruptionException( message );
        }

        private static long GetPosition( BinaryReader reader )
        {
            return reader.BaseStream.Position;
        }

        private void AssertAligned( BinaryWriter writer )
        {
            var pos = writer.BaseStream.Position;
            AssertAligned( pos );
        }

        private void AssertAligned( BinaryReader reader )
        {
            var pos = reader.BaseStream.Position;
            AssertAligned( pos );
        }

        private void AssertAligned( long pos )
        {
            if ( pos != Align( pos ) )
                throw new InvalidOperationException( $"File {_dataFileName}, stream is not aligned. Expected {Align( pos )}, found {pos}." );
        }

        private static ushort Align( ushort value )
        {
            var excess = value % 4;
            if ( excess == 0 )
                return value;
            return (ushort) (value + 4 - excess);
        }

        private static long Align( long value )
        {
            var excess = value % 4L;
            if ( excess == 0L )
                return value;
            return value + 4L - excess;
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

        private BinaryWriter GetWriterForAppend()
        {
            var stream = new FileStream( _dataFileName, FileMode.Append, FileAccess.Write, FileShare.Delete );
            var writer = new BinaryWriter( stream, Encoding.UTF8 );
            return writer;
        }

        private BinaryReader GetReader()
        {
            FileStream stream;

            var sw = Stopwatch.StartNew();
            for ( ;; )
            {
                try
                {
                    stream = new FileStream( _dataFileName, FileMode.OpenOrCreate, FileAccess.Read, FileShare.Delete );
                    break;
                }
                catch ( IOException ex )
                {
                    if ( (uint) ex.HResult == 0x80070020 && sw.ElapsedMilliseconds < 5000 )
                    {
                        _trace.TraceInformation( "File in use: {0}", _dataFileName );
                        Thread.Sleep( 250 );
                        continue;
                    }

                    _trace.TraceError( "{0}\r\nHResult: {1}\r\nDetails: {2}\r\n", ex.Message, ex.HResult, ex );
                    throw;
                }
            }

            var reader = new BinaryReader( stream, Encoding.UTF8 );
            return reader;
        }
    }

    internal class DataFileBookmark : IStoredCommand
    {
        public DataFileBookmark( string stateTag, long position, string command )
        {
            StateTag = stateTag;
            Position = position;
            Command = command;
        }

        public string StateTag { get; }

        public DateTime WhenExecuted => throw new NotImplementedException();

        public string Command { get; }

        public long Position { get; }
    }
}