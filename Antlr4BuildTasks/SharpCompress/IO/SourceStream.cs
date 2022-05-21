using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SharpCompress.Readers;

namespace SharpCompress.IO
{
    public class SourceStream : Stream
    {
        private long _prevSize;
        private List<FileInfo> _files;
        private List<Stream> _streams;
        private Func<int, FileInfo> _getFilePart;
        private Func<int, Stream> _getStreamPart;
        private int _stream;

        public SourceStream(FileInfo file, Func<int, FileInfo> getPart, ReaderOptions options) : this(null, null, file, getPart, options)
        {
        }

        public SourceStream(Stream stream, Func<int, Stream> getPart, ReaderOptions options) : this(stream, getPart, null, null, options)
        {
        }

        private SourceStream(Stream stream, Func<int, Stream> getStreamPart, FileInfo file, Func<int, FileInfo> getFilePart, ReaderOptions options)
        {
            this.ReaderOptions = options;
            _files = new List<FileInfo>();
            _streams = new List<Stream>();
            IsFileMode = file != null;
            IsVolumes = false;

            if (!IsFileMode)
            {
                _streams.Add(stream);
                _getStreamPart = getStreamPart;
                _getFilePart = new Func<int, FileInfo>(a => null);
                if (stream is FileStream)
                    _files.Add(new FileInfo(((FileStream)stream).Name));
            }
            else
            {
                _files.Add(file);
                _streams.Add(_files[0].OpenRead());
                _getFilePart = getFilePart;
                _getStreamPart = new Func<int, Stream>(a => null);
            }
            _stream = 0;
            _prevSize = 0;
        }

        public void LoadAllParts()
        {
            for (int i = 1; SetStream(i); i++)
            {
            }
            SetStream(0);
        }

        public bool IsVolumes { get; set; }

        public ReaderOptions ReaderOptions { get; }
        public bool IsFileMode { get; }

        public IEnumerable<FileInfo> Files => _files;
        public IEnumerable<Stream> Streams => _streams;

        private Stream Current => _streams[_stream];
        public bool LoadStream(int index) //ensure all parts to id are loaded
        {
            while (_streams.Count <= index)
            {
                if (IsFileMode)
                {
                    FileInfo f = _getFilePart(_streams.Count);
                    if (f == null)
                    {
                        _stream = _streams.Count - 1;
                        return false;
                    }
                    //throw new Exception($"File part {idx} not available.");
                    _files.Add(f);
                    _streams.Add(_files.Last().OpenRead());
                }
                else
                {
                    Stream s = _getStreamPart(_streams.Count);
                    if (s == null)
                    {
                        _stream = _streams.Count - 1;
                        return false;
                    }
                    //throw new Exception($"Stream part {idx} not available.");
                    _streams.Add(s);
                    if (s is FileStream)
                        _files.Add(new FileInfo(((FileStream)s).Name));
                }
            }
            return true;
        }
        public bool SetStream(int idx) //allow caller to switch part in multipart
        {
            if (LoadStream(idx))
                _stream = idx;
            return _stream == idx;
        }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => (!IsVolumes ? _streams.Sum(a => a.Length) : Current.Length);

        public override long Position
        {
            get => _prevSize + Current.Position; //_prevSize is 0 for multivolume
            set => Seek(value, SeekOrigin.Begin);
        }

        public override void Flush()
        {
            Current.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (count <= 0)
                return 0;

            int total = count;
            int r = -1;

            while (count != 0 && r != 0)
            {
                r = Current.Read(buffer, offset, (int)Math.Min(count, Current.Length - Current.Position));
                count -= r;
                offset += r;

                if (!IsVolumes && count != 0 && Current.Position == Current.Length)
                {
                    _prevSize += Current.Length;
                    SetStream(_stream + 1); //will load next file
                    Current.Seek(0, SeekOrigin.Begin);
                }
            }

            return total - count;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long pos = this.Position;
            switch (origin)
            {
                case SeekOrigin.Begin: pos = offset; break;
                case SeekOrigin.Current: pos += offset; break;
                case SeekOrigin.End: pos = Length + offset; break;
            }

            _prevSize = 0;
            if (!IsVolumes)
            {
                SetStream(0);
                while (_prevSize + Current.Length < pos)
                {
                    _prevSize += Current.Length;
                    SetStream(_stream + 1);
                }
            }

            if (pos != _prevSize + Current.Position)
                Current.Seek(pos - _prevSize, SeekOrigin.Begin);
            return pos;
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public override void Close()
        {
            if (this.IsFileMode || !this.ReaderOptions.LeaveStreamOpen) //close if file mode or options specify it
            {
                foreach (Stream stream in _streams)
                {
                    try
                    {
                        if (stream != null)
                            stream.Dispose();
                    }
                    catch { }
                }
                _streams.Clear();
                _files.Clear();
            }
        }

        protected override void Dispose(bool disposing)
        {
            this.Close();
            base.Dispose(disposing);
        }
    }
}
