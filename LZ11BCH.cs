using System;
using System.IO;
using Toolbox.Core;

namespace CtrLibrary
{
    /// <summary>
    /// Handles BCH files wrapped in Nintendo LZ11 compression while retaining
    /// the original .bch extension.
    /// </summary>
    public class LZ11BCH : ICompressionFormat
    {
        public string[] Description { get; } = new[] { "Nintendo LZ11 BCH Compression" };

        public string[] Extension { get; } = new[] { "*.bch" };

        public bool CanCompress => true;

        public bool Identify(Stream stream, string fileName)
        {
            if (!fileName.EndsWith(".bch", StringComparison.OrdinalIgnoreCase))
                return false;

            byte[] data = stream.ToArray();
            if (data.Length < 5 || data[0] != 0x11)
                return false;

            try
            {
                using (var decompressed = new LZSS_N().Decompress(new MemoryStream(data, false)))
                {
                    return decompressed.Length >= 3 &&
                           decompressed.ReadByte() == 'B' &&
                           decompressed.ReadByte() == 'C' &&
                           decompressed.ReadByte() == 'H';
                }
            }
            catch
            {
                return false;
            }
        }

        public Stream Decompress(Stream stream)
        {
            return new LZSS_N().Decompress(stream);
        }

        public Stream Compress(Stream stream)
        {
            return new LZSS_N().Compress(stream);
        }
    }
}
