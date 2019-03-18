using System;
using System.IO;

namespace fam.DataFiles
{
    /// <inheritdoc />
    /// <summary>
    /// A stream that tracks only size; it contains no data.
    /// </summary>
    internal sealed class SizeOnlyStream : Stream
    {
        private long _length;
        private long _position;

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long newPosition;
            switch (origin)
            {
                case SeekOrigin.Begin:
                    newPosition = offset;
                    break;
                case SeekOrigin.Current:
                    newPosition = _position + offset;
                    break;
                case SeekOrigin.End:
                    newPosition = _length + offset;
                    break;
                default: throw new ArgumentException($"Unexpected value: {origin}", nameof(origin));
            }

            if (newPosition < 0L)
                throw new ArgumentException("Attempt to move to before beginning of stream.");

            if (newPosition > _length)
                throw new ArgumentException("Attempt to move to after end of stream.");

            _position = newPosition;
            return newPosition;
        }

        public override void SetLength(long value)
        {
            if (value < 0L)
                throw new ArgumentException("Length must not be negative.");
            if (value < _position)
                throw new ArgumentException("Length must not be smaller than current position.");
            _length = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new IOException("This is a write-only stream.");
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || offset > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));
            var lastOffset = offset + count;
            if (count < 0 || lastOffset < 0 || lastOffset > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(count));
            var newPosition = _position + count;
            if (newPosition < 0L)
                throw new IOException("Attempt to increase length to beyond maximum.");
            _position = newPosition;
            if (newPosition > _length)
                _length = newPosition;
        }

        public override bool CanRead => false;
        public override bool CanSeek => true;
        public override bool CanWrite => true;
        public override long Length => _position;

        public override long Position
        {
            get => _position;
            set => Seek(value, SeekOrigin.Begin);
        }
    }
}