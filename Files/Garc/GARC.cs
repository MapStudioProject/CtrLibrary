using CtrLibrary;
using Syroot.BinaryData;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Toolbox.Core;
using Toolbox.Core.IO;
using UIFramework;

namespace FirstPlugin
{
    public class GARC : MapStudio.UI.FileEditor, IArchiveFile, IFileFormat, IDisposable
    {
        public bool CanSave { get; set; } = true;

        public string[] Description { get; set; } = new string[] { "GARC" };
        public string[] Extension { get; set; } = new string[] { "*.garc" };

        public File_Info FileInfo { get; set; }

        public bool CanAddFiles { get; set; } = true;
        public bool CanRenameFiles { get; set; } = true;
        public bool CanReplaceFiles { get; set; } = true;
        public bool CanDeleteFiles { get; set; } = true;

        public IEnumerable<ArchiveFileInfo> Files => files;

        // private
        private Header header;
        private FatoHeader fatoHeader;
        private List<uint> fatoOffsets;

        private FatbHeader fatbHeader;
        private List<FatbEntry> fatbEntries;

        private FimbHeader fimbHeader;

        private List<GARC4FileInfo> files = new List<GARC4FileInfo>();

        public bool AddFile(ArchiveFileInfo archiveFileInfo)
        {
            files.Add(new GARC4FileInfo(this)
            {
                FileData = archiveFileInfo.FileData,
                FileName = archiveFileInfo.FileName,
            });
            return true;
        }

        public void ClearFiles() => files.Clear();

        public bool DeleteFile(ArchiveFileInfo archiveFileInfo)
        {
            return files.Remove((GARC4FileInfo)archiveFileInfo);
        }

        public bool Identify(File_Info fileInfo, Stream stream)
        {
            using (FileReader reader = new FileReader(stream, true))
                return reader.CheckSignature(4, "CRAG");
        }

        private static Dictionary<string, string> _knownFiles = new Dictionary<string, string>
        {
            ["BCH"] = ".bch",
            ["PC"] = ".pc",
            ["PF"] = ".pf",
            ["PF"] = ".pf",
            ["PB"] = ".pb",
            ["PT"] = ".pt",
            ["PK"] = ".pk",
            ["CGFX"] = ".cgfx",
        };

        /// <summary>
        /// Prepares the dock layouts to be used for the file format.
        /// </summary>
        public override List<DockWindow> PrepareDocks()
        {
            List<DockWindow> windows = new List<DockWindow>();
            windows.Add(Workspace.Outliner);
            windows.Add(Workspace.PropertyWindow);
            return windows;
        }

        public void Dispose()
        {
            FileInfo.Stream?.Dispose();
        }

        //Parse/save code from
        //https://github.com/IcySon55/Kuriimu/blob/3f05ffc993e0908929e92373c455acb633b8f28d/src/archive/archive_nintendo/Garc4Manager.cs

        public void Load(Stream stream)
        {
            files.Clear();

            FileInfo.Stream = stream;
            FileInfo.KeepOpen = true;
            using (FileReader reader = new FileReader(stream, true))
            {
                //header
                ByteOrder byteOrder = DetectByteOrder(reader);
                reader.ByteOrder = byteOrder;
                reader.BaseStream.Seek(0, SeekOrigin.Begin);
                header = reader.ReadStruct<Header>();
                //FATO
                reader.BaseStream.Position = header.headerSize;
                fatoHeader = reader.ReadStruct<FatoHeader>();
                reader.BaseStream.Position = reader.BaseStream.Position + 3 & ~3;
                fatoOffsets = reader.ReadUInt32s(fatoHeader.entryCount).ToList();

                //FATB
                fatbHeader = reader.ReadStruct<FatbHeader>();
                // Some GARC variants report a larger FATB entryCount than FATO; in practice
                // FATO's entryCount is the usable file count.
                uint usableCount = fatbHeader.entryCount;
                if (fatoHeader.entryCount > 0 && fatbHeader.entryCount > fatoHeader.entryCount)
                    usableCount = fatoHeader.entryCount;
                int entryCount = ValidateEntryCount(reader, usableCount);
                fatbEntries = reader.ReadMultipleStructs<FatbEntry>(entryCount);

                //FIMB
                fimbHeader = reader.ReadStruct<FimbHeader>();

                for (int i = 0; i < entryCount; i++)
                {
                    var size = (fatbEntries[i].size == 0) ? fatbEntries[i].endOffset - fatbEntries[i].offset : fatbEntries[i].size;
                    long entryPos = (long)fatbEntries[i].offset + header.dataOffset;
                    if (size == 0)
                        continue;
                    if (entryPos < 0 || entryPos >= reader.BaseStream.Length)
                        continue;
                    if (entryPos + size > reader.BaseStream.Length)
                        continue;

                    reader.BaseStream.Position = entryPos;
                    byte[] headerBytes = reader.ReadBytes((int)Math.Min(8, size));
                    if (headerBytes.Length == 0)
                        continue;

                    var mag = headerBytes[0];
                    var extension = "";
                    bool isLz11 = false;
                    if (mag == 0x11)
                    {
                        reader.BaseStream.Position = entryPos;
                        isLz11 = IsLikelyLz11(reader, fatbEntries[i]);
                        extension = isLz11 ? ".lz11" : "";
                    }
                    if (extension == ".lz11")
                    {
                        reader.Seek(4);
                        var magS = reader.ReadString(2);
                        extension = _knownFiles.ContainsKey(magS) ? _knownFiles[magS] : "";
                    }
                    if (extension == "")
                    {
                        reader.BaseStream.Position = entryPos;
                        var magS = reader.ReadString(2);
                        extension = _knownFiles.ContainsKey(magS) ? _knownFiles[magS] : ".bin";

                        if (extension == ".bin")
                        {
                            reader.BaseStream.Position = entryPos;
                            magS = reader.ReadString(4);
                            extension = _knownFiles.ContainsKey(magS) ? _knownFiles[magS] : ".bin";
                        }
                    }

                    string twoCcMagic = null;
                    if (headerBytes.Length >= 2)
                    {
                        byte a = headerBytes[0];
                        byte b = headerBytes[1];
                        if (a >= 0x41 && a <= 0x5A && b >= 0x41 && b <= 0x5A)
                            twoCcMagic = $"{(char)a}{(char)b}";
                    }

                    string name = $"{i:00000000}" + extension;
                    if (extension == ".bin" && !string.IsNullOrEmpty(twoCcMagic))
                        name += "." + twoCcMagic;
                    files.Add(new GARC4FileInfo(this)
                    {
                        IsLZ11 = isLz11,
                        FileName = name,
                        FileData = new SubStream(reader.BaseStream, entryPos, size)
                    });
                }
            }
        }
        public void Save(Stream stream)
        {
            using (FileWriter writer = new FileWriter(stream))
            {
                //Save file data
                for (int i = 0; i < files.Count; i++)
                {
                    files[i].SaveFileFormat();
                }

                //filesize
                //largestFilesize
                writer.BaseStream.Position = 0x1c;

                //FATO
                writer.WriteStruct(fatoHeader);
                writer.Write((ushort)0xFFFF);
                writer.Write(fatoOffsets);

                //FATB
                writer.WriteStruct(fatbHeader);
                uint offset = 0;
                for (int i = 0; i < files.Count; i++)
                {
                    fatbEntries[i].offset = offset;
                    offset += (uint)files[i].FileSize;
                    offset = (uint)(offset + 3 & ~3);
                    fatbEntries[i].endOffset = offset;
                    fatbEntries[i].size = (uint)files[i].FileSize;

                    writer.WriteStruct(fatbEntries[i]);
                }

                var fimbOffset = writer.BaseStream.Position;
                writer.BaseStream.Position += 0xc;
                var dataOffset = writer.BaseStream.Position;

                //Writing FileData
                uint largestFileSize = 0;
                for (int i = 0; i < files.Count; i++)
                {
                    if (files[i].FileSize > largestFileSize) largestFileSize = (uint)files[i].FileSize;
                    files[i].CompressedStream.CopyTo(writer.BaseStream);
                    writer.AlignBytes(4, 0xff);
                }

                //FIMB
                fimbHeader.dataSize = (uint)writer.BaseStream.Length - (uint)dataOffset;
                writer.BaseStream.Position = fimbOffset;
                writer.WriteStruct(fimbHeader);

                //Header
                header.fileSize = (uint)writer.BaseStream.Length;
                header.largestFileSize = largestFileSize;
                writer.BaseStream.Position = 0;
                writer.WriteStruct(header);
            }
        }

        public class GARC4FileInfo : ArchiveFileInfo
        {
            public GARC ArchiveFile;

            public uint FileSize => (uint)CompressedStream.Length;

            public bool IsLZ11;

            public GARC4FileInfo(GARC garc)
            {
                ArchiveFile = garc;
                ParentArchiveFile = garc;
            }

            public Stream CompressedStream
            {
                get { return base.FileData; }
            }

            public override Stream FileData
            {
                get { return DecompressBlock(); }
                   set
                   {
                    base.FileData = value;
                    decompressedCache = null;
                }
            }

            private byte[] decompressedCache;

            private Stream DecompressBlock()
            {
                if (decompressedCache != null)
                    return new MemoryStream(decompressedCache, false);

                byte[] data = base.FileData.ToArray();

                if (IsLZ11)
                {
                    LZSS_N lz11 = new LZSS_N();
                    using var decompressed = lz11.Decompress(new MemoryStream(data));
                    decompressedCache = decompressed.ToArray();
                    return new MemoryStream(decompressedCache, false);
                }

                decompressedCache = data;
                return new MemoryStream(decompressedCache, false);
            }

            public override Stream CompressData(Stream decompressed)
            {
                if (IsLZ11)
                {
                    LZSS_N lz11 = new LZSS_N();
                    return lz11.Compress(decompressed);
                }

                return base.CompressData(decompressed);
            }
        }

        #region Sections

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class Header
        {
            public Magic magic;
            public uint headerSize;
            public ushort byteOrder;
            public ushort version;
            public uint secCount;
            public uint dataOffset;
            public uint fileSize;
            public uint largestFileSize;
        }

        //File Allocation Table Offsets
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class FatoHeader
        {
            public Magic magic;
            public uint headerSize;
            public ushort entryCount;
            //ushort padding with 0xff
        }

        //FATB
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class FatbHeader
        {
            public Magic magic;
            public uint headerSize;
            public uint entryCount;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class FatbEntry
        {
            public uint unk1;
            public uint offset;
            public uint endOffset;
            public uint size;
        }

        //FIMB
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class FimbHeader
        {
            public Magic magic;
            public uint headerSize;
            public uint dataSize;
        }

        #endregion

        private static bool IsLikelyLz11(FileReader reader, FatbEntry entry)
        {
            long start = reader.BaseStream.Position;
            try
            {
                uint size = entry.size == 0 ? entry.endOffset - entry.offset : entry.size;
                if (size < 8)
                    return false;
                if (reader.ReadByte() != 0x11)
                    return false;

                int decodedLen = reader.ReadByte() | (reader.ReadByte() << 8) | (reader.ReadByte() << 16);
                if (decodedLen <= 0)
                    return false;
                if (decodedLen > 0x2000000) // sanity cap
                    return false;

                long remaining = (long)size - 4;
                if (remaining <= 0)
                    return false;
                if (decodedLen < remaining / 2)
                    return false;

                return true;
            }
            finally
            {
                reader.BaseStream.Position = start;
            }
        }

        private static ByteOrder DetectByteOrder(FileReader reader)
        {
            long start = reader.BaseStream.Position;
            try
            {
                reader.BaseStream.Seek(0, SeekOrigin.Begin);
                byte[] headerBytes = reader.ReadBytes(0x1C);
                if (headerBytes.Length < 0x0A)
                    throw new InvalidDataException("GARC header too small.");

                ushort leOrder = (ushort)(headerBytes[8] | (headerBytes[9] << 8));
                ushort beOrder = (ushort)((headerBytes[8] << 8) | headerBytes[9]);

                if (leOrder == 0xFEFF || leOrder == 0xFFFE)
                    return leOrder == 0xFEFF ? ByteOrder.LittleEndian : ByteOrder.BigEndian;
                if (beOrder == 0xFEFF || beOrder == 0xFFFE)
                    return beOrder == 0xFEFF ? ByteOrder.BigEndian : ByteOrder.LittleEndian;

                uint leHeaderSize = BitConverter.ToUInt32(headerBytes, 4);
                uint beHeaderSize = (uint)((headerBytes[4] << 24) | (headerBytes[5] << 16) | (headerBytes[6] << 8) | headerBytes[7]);

                if (leHeaderSize == 0x1C)
                    return ByteOrder.LittleEndian;
                if (beHeaderSize == 0x1C)
                    return ByteOrder.BigEndian;

                throw new InvalidDataException("Unable to detect GARC byte order.");
            }
            finally
            {
                reader.BaseStream.Seek(start, SeekOrigin.Begin);
            }
        }

        private static int ValidateEntryCount(FileReader reader, uint entryCount)
        {
            if (entryCount > int.MaxValue)
                throw new InvalidDataException($"GARC entry count too large: {entryCount}.");

            int count = (int)entryCount;
            int entrySize = Marshal.SizeOf<FatbEntry>();
            long remaining = reader.BaseStream.Length - reader.BaseStream.Position;
            long maxCount = remaining / entrySize;

            if (count < 0 || count > maxCount)
                throw new InvalidDataException($"GARC entry count out of range: {count} (max {maxCount}).");

            return count;
        }
    }
}
