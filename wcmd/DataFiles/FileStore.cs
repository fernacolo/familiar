﻿using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using wcmd.Diagnostics;
using wcmd.Sessions;

namespace wcmd.DataFiles
{
    internal sealed class FileStore : IDataStore
    {
        private readonly TraceSource _trace;
        private readonly FileInfo _fileInfo;

        public FileStore( Configuration config )
        {
            if ( config == null )
                throw new ArgumentNullException( nameof( config ) );
            _trace = DiagnosticsCenter.GetTraceSource( $"{nameof( FileStore )} - {config.SessionId}" );
            var dataFileName = Path.Combine( config.LocalDbDirectory.ToString(), $"{config.SessionId}.dat" );
            _fileInfo = new FileInfo( dataFileName );
        }

        public FileStore( FileInfo fileInfo )
        {
            if ( fileInfo == null )
                throw new ArgumentNullException( nameof( fileInfo ) );
            _trace = DiagnosticsCenter.GetTraceSource( $"{nameof( FileStore )} - {fileInfo.FullName}" );
            _fileInfo = fileInfo;
        }

        public string StateTag
        {
            get
            {
                _fileInfo.Refresh();
                return $"{_fileInfo.LastWriteTimeUtc.Ticks}/{_fileInfo.Length}";
            }
        }

        public string FileName => _fileInfo.Name;

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
                using ( var stream = new FileStream( _fileInfo.FullName, FileMode.Open, FileAccess.Write, FileShare.Delete ) )
                    stream.SetLength( lastGoodPosition );
                _trace.TraceInformation( "File was truncated at: {0}.", lastGoodPosition );
            }
        }

        private readonly IStoredItem _bof = new FileStoreItem( null, -1L, DateTime.MinValue, null );
        private readonly IStoredItem _eof = new FileStoreItem( null, long.MaxValue, DateTime.MaxValue, null );

        public IStoredItem Bof => _bof;

        public IStoredItem Eof => _eof;

        public IStoredItem Write( DateTime whenExecuted, string command, ref string stateTag )
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

            return new FileStoreItem( stateTag, position, whenExecuted, command );
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

        public IStoredItem GetPrevious( IStoredItem item )
        {
            if ( item == null )
                throw new ArgumentNullException( nameof( item ) );
            if ( item == _bof )
                throw new ArgumentException( "Cannot read before BOF." );

            var bm = (FileStoreItem) item;
            if ( bm.Position == 0L )
                return _bof;

            using ( var reader = GetReader() )
            {
                var stream = reader.BaseStream;
                var position = bm == _eof ? stream.Length : bm.Position;
                for ( ;; )
                {
                    if ( position == 0L )
                        return _bof;
                    var record = ReadPreviousRecord( reader, ref position );
                    if ( record.Type == DataFileRecord.CommandV1 )
                        return new FileStoreItem( StateTag, position, record.WhenExecuted, record.Command );
                }
            }
        }

        public IStoredItem GetNext( IStoredItem item )
        {
            if ( item == null )
                throw new ArgumentNullException( nameof( item ) );
            if ( item == _eof )
                throw new ArgumentException( "Cannot read after EOF." );

            var bm = (FileStoreItem) item;

            using ( var reader = GetReader() )
            {
                long position;
                if ( bm == _bof )
                    position = 0L;
                else
                {
                    // Skip the specified record.
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
                        return new FileStoreItem( StateTag, lastPosition, record.WhenExecuted, record.Command );
                }
            }
        }

        public byte[] CreateLink( IStoredItem item )
        {
            if ( item == _bof )
                return new byte[] {1};
            if ( item == _eof )
                return new byte[] {2};

            return ((FileStoreItem) item).CreateLink();
        }

        public IStoredItem ResolveLink( byte[] link )
        {
            if ( link.Length != 1 )
                return new FileStoreItem( link );

            switch ( link[0] )
            {
                case 1: return _bof;
                case 2: return _eof;
                default:
                    throw new ArgumentException( "Invalid value", nameof( link ) );
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
            for ( ;; )
            {
                if ( pos >= stream.Length )
                {
                    _trace.TraceInformation( "End of stream found at offset {0}.", pos );
                    return null;
                }

                stream.Position = pos;

                try
                {
                    var record = ReadRecord( reader );
                    _trace.TraceInformation( "A record was read at offset {0}.", pos );
                    pos = Align( stream.Position );
                    return record;
                }
                catch ( DataCorruptionException ex )
                {
                    _trace.TraceError( "{0}", ex );
                    pos = SearchNextOpenMark( stream, pos );
                }
            }
        }

        private long SearchNextOpenMark( Stream stream, long pos )
        {
            if ( pos >= stream.Length )
                return stream.Length;

            // Read chunks of 4 kib searching for the open mark.

            pos = Align( pos );
            var buffer = new byte[4096];

            for ( ;; )
            {
                stream.Position = pos;
                var len = stream.Read( buffer, 0, buffer.Length );

                // When Stream.Read returns zero, we have reached EOF, as described in the API doc.
                if ( len == 0 )
                    return pos;

                // Scan the buffer using the same logic we use for normal read: BinaryReader, ReadUInt32, etc.
                var lastPos = pos;
                try
                {
                    using ( var tempStream = new MemoryStream( buffer, 0, len, false ) )
                    using ( var reader = new BinaryReader( tempStream, Encoding.UTF8, true ) )
                    {
                        for ( ;; )
                        {
                            // Determine the absolute position.
                            lastPos = pos + tempStream.Position;
                            AssertAligned( lastPos );

                            var magic = reader.ReadUInt32();
                            if ( magic == OpenMark )
                                return lastPos;
                        }
                    }
                }
                catch ( EndOfStreamException )
                {
                    if ( lastPos == pos )
                        throw new Exception( "Unable to advance scanning position." );
                    pos = lastPos;
                }
            }
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
            message = $"File {_fileInfo.FullName}, position {GetPosition( reader ) + offset}: {message}";
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
                throw new InvalidOperationException( $"File {_fileInfo.FullName}, stream is not aligned. Expected {Align( pos )}, found {pos}." );
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
            var stream = new FileStream( _fileInfo.FullName, FileMode.Append, FileAccess.Write, FileShare.Delete );
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
                    stream = new FileStream( _fileInfo.FullName, FileMode.OpenOrCreate, FileAccess.Read, FileShare.Delete );
                    break;
                }
                catch ( IOException ex )
                {
                    if ( (uint) ex.HResult == 0x80070020 && sw.ElapsedMilliseconds < 5000 )
                    {
                        _trace.TraceInformation( "File in use: {0}", _fileInfo.FullName );
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

    internal class FileStoreItem : IStoredItem
    {
        public FileStoreItem( string stateTag, long position, DateTime whenExecuted, string command )
        {
            StateTag = stateTag;
            Position = position;
            WhenExecuted = whenExecuted;
            Command = command;
        }

        public FileStoreItem( byte[] data )
        {
            if ( data == null )
                throw new ArgumentNullException( nameof( data ) );

            var stream = new MemoryStream( data );
            var reader = new BinaryReader( stream, Encoding.UTF8, true );
            Position = reader.ReadInt64();
            StateTag = reader.ReadBoolean() ? reader.ReadString() : null;
            WhenExecuted = ReadDateTime( reader );
            Command = reader.ReadBoolean() ? reader.ReadString() : null;
        }

        public byte[] CreateLink()
        {
            var stream = new MemoryStream();
            var writer = new BinaryWriter( stream, Encoding.UTF8, true );
            writer.Write( Position );

            writer.Write( StateTag != null );
            if ( StateTag != null ) writer.Write( StateTag );

            WriteDateTime( writer, WhenExecuted );

            writer.Write( Command != null );
            if ( Command != null ) writer.Write( Command );

            return stream.ToArray();
        }

        private static DateTime ReadDateTime( BinaryReader reader )
        {
            var kind = reader.ReadByte();
            var ticks = reader.ReadInt64();
            return new DateTime( ticks, (DateTimeKind) kind );
        }

        private static void WriteDateTime( BinaryWriter writer, DateTime value )
        {
            writer.Write( (byte) value.Kind );
            writer.Write( value.Ticks );
        }

        public string StateTag { get; }

        public DateTime WhenExecuted { get; }

        public string Command { get; }

        public long Position { get; }
    }
}