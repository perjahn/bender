using System;
using System.IO;
using System.Text;

namespace Bender
{
    class ConsoleStream : Stream
    {
        private readonly TextReader _input;

        private readonly TextWriter _output;

        public ConsoleStream(TextReader input, TextWriter output)
        {
            _input = input;
            _output = output;
        }

        public override void Flush()
        {
            throw new NotImplementedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var ch = new char[count];
            var result = _input.Read(ch, 0, count);
            Array.Copy(Encoding.ASCII.GetBytes(ch, 0, result), 0, buffer, offset, result);
            return result;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _output.Write(Encoding.ASCII.GetString(buffer, offset, count));
        }

        public override bool CanRead => _output != null;

        public override bool CanSeek => false;

        public override bool CanWrite => _output != null;

        public override long Length { get { throw new InvalidOperationException(); } }

        public override long Position { get { throw new InvalidOperationException(); } set { throw new InvalidOperationException(); } }
    }
}
