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
    public class TwoCC : MapStudio.UI.FileEditor, IArchiveFile, IFileFormat
    {
        public bool CanSave { get; set; } = true;

        public string[] Description { get; set; } = new string[] { ".pc" };
        public string[] Extension { get; set; } = new string[] { "*.pc" };

        // Prefer opening 2CC containers (AC/PC/CP/etc) as archives before other
        // format detectors try to recursively scan and auto-open payloads.
        public int Priority => -1;

        public File_Info FileInfo { get; set; }

        public bool CanAddFiles { get; set; } = true;
        public bool CanRenameFiles { get; set; } = true;
        public bool CanReplaceFiles { get; set; } = true;
        public bool CanDeleteFiles { get; set; } = true;

        public IEnumerable<ArchiveFileInfo> Files => files;

        private List<TFileInfo> files = new List<TFileInfo>();

        public bool AddFile(ArchiveFileInfo archiveFileInfo)
        {
            files.Add(new TFileInfo(this)
            {
                FileData = archiveFileInfo.FileData,
                FileName = archiveFileInfo.FileName,
            });
            return true;
        }

        public void ClearFiles() => files.Clear();

        public bool DeleteFile(ArchiveFileInfo archiveFileInfo)
        {
            return files.Remove((TFileInfo)archiveFileInfo);
        }

        private static readonly string[] MAGIC = new string[]
        {
            "AC", "AD", "AE", "AS",
            "BB", "BG", "BM", "BS",
            "CM", "CP",
            "EA", "ED",
            "GR",
            "MM",
            "NA",
            "PB", "PC", "PF", "PK", "PT",
            "TR",
            "ZI", "ZS",
        };

        public bool Identify(File_Info fileInfo, Stream stream)
        {
            using (FileReader reader = new FileReader(stream, true))
                for (int i = 0; i < MAGIC.Length; i++)
                    if (reader.CheckSignature(2, MAGIC[i]))
                    {
                        if (MAGIC[i] == "PC" && fileInfo.ParentArchive == null && ContainsGfl2Payload(reader, "PC", false))
                            return false;
                        if (MAGIC[i] == "CP" && ContainsGfl2Payload(reader, "CP", true))
                            return false;
                        return true;
                    }

            return false;
        }

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

        private string Identifier;

        public void Load(Stream stream)
        {
            using (FileReader reader = new FileReader(stream))
            {
                Identifier = reader.ReadMagic(0, 2);
                ushort numSections = reader.ReadUInt16();
                for (int i = 0; i < numSections; i++)
                {
                    reader.Seek(4 + (i * 4), SeekOrigin.Begin);
                    uint startOffset = reader.ReadUInt32();
                    uint endOffset = reader.ReadUInt32();

                    reader.Seek(startOffset, SeekOrigin.Begin);
                    byte[] data = reader.ReadBytes((int)(endOffset - startOffset));

                    string ext = SARC_Parser.GuessFileExtension(data);
                    files.Add(new TFileInfo(this)
                    {
                        FileName = $"File{i}{ext}",
                        IsLZ11 = data.Length > 0 ? data[0] == 0x11 : false,
                        FileData = new MemoryStream(data),
                    });
                }
            }
        }

        private static bool ContainsGfl2Payload(FileReader reader, string expectedMagic, bool deepScan)
        {
            long startPos = reader.BaseStream.Position;
            try
            {
                byte[] data = reader.BaseStream.ToArray();
                if (deepScan)
                    return ContainsGfl2PayloadDeep(data, expectedMagic, 0);
                return ContainsGfl2PayloadShallow(data, expectedMagic);
            }
            catch
            {
                return false;
            }
            finally
            {
                reader.BaseStream.Seek(startPos, SeekOrigin.Begin);
            }
        }

        private static bool ContainsGfl2PayloadShallow(byte[] data, string expectedMagic)
        {
            if (!LooksLikeContainer(data, expectedMagic))
                return false;

            ushort count = (ushort)(data[2] | (data[3] << 8));
            int tableSize = 4 + (count + 1) * 4;
            if (tableSize > data.Length)
                return false;

            for (int i = 0; i < count; i++)
            {
                int start = BitConverter.ToInt32(data, 4 + i * 4);
                int end = BitConverter.ToInt32(data, 4 + (i + 1) * 4);
                if (end <= start || end > data.Length || start < 0)
                    continue;

                if (start + 4 > data.Length)
                    continue;

                uint entryMagic = BitConverter.ToUInt32(data, start);
                if (IsGfl2Magic(entryMagic))
                    return true;
            }

            return false;
        }

        private static bool ContainsGfl2PayloadDeep(byte[] data, string expectedMagic, int depth)
        {
            if (depth > 8 || data == null || data.Length < 4)
                return false;

            if (LooksLikeLz11(data))
            {
                data = TryDecompressLz11(data);
                if (data == null || data.Length < 4)
                    return false;
            }

            uint magic = BitConverter.ToUInt32(data, 0);
            if (IsGfl2Magic(magic))
                return true;

            if (!LooksLikeContainer(data, expectedMagic) && !LooksLikeContainer(data, null))
                return false;

            ushort count = (ushort)(data[2] | (data[3] << 8));
            int tableSize = 4 + (count + 1) * 4;
            if (tableSize > data.Length)
                return false;

            int prev = 0;
            for (int i = 0; i < count + 1; i++)
            {
                int off = BitConverter.ToInt32(data, 4 + i * 4);
                if (off < prev || off > data.Length)
                    return false;
                prev = off;
            }

            for (int i = 0; i < count; i++)
            {
                int start = BitConverter.ToInt32(data, 4 + i * 4);
                int end = BitConverter.ToInt32(data, 4 + (i + 1) * 4);
                if (end <= start || end > data.Length || start < 0)
                    continue;

                int size = end - start;
                if (size < 4)
                    continue;

                byte[] slice = new byte[size];
                Buffer.BlockCopy(data, start, slice, 0, size);
                if (ContainsGfl2PayloadDeep(slice, expectedMagic, depth + 1))
                    return true;
            }

            return false;
        }

        private static bool LooksLikeContainer(byte[] data, string expectedMagic)
        {
            if (data == null || data.Length < 4)
                return false;

            string magic = $"{(char)data[0]}{(char)data[1]}";
            if (!string.IsNullOrEmpty(expectedMagic))
                return magic == expectedMagic;

            for (int i = 0; i < MAGIC.Length; i++)
            {
                if (MAGIC[i] == magic)
                    return true;
            }

            return false;
        }

        private static bool LooksLikeLz11(byte[] data)
        {
            return data.Length >= 4 && data[0] == 0x11;
        }

        private static byte[] TryDecompressLz11(byte[] data)
        {
            try
            {
                var lz11 = new LZSS_N();
                using var decompressed = lz11.Decompress(new MemoryStream(data));
                return decompressed.ToArray();
            }
            catch
            {
                return null;
            }
        }

        private static bool IsGfl2Magic(uint magic)
        {
            return magic == 0x15122117
                || magic == 0x00010000
                || magic == 0x15041213
                || magic == 0x00060000;
        }

        public void Save(Stream stream)
        {
            using (FileWriter writer = new FileWriter(stream))
            {
                foreach (var file in files)
                    file.SaveFileFormat();

                writer.WriteSignature(Identifier);
                writer.Write((ushort)files.Count);
                writer.Write(8 * files.Count); //reserved for file start/end offsets

                writer.Align(128);
                for (int i = 0; i < files.Count; i++)
                {
                    long startOffset = writer.Position;

                    using (writer.TemporarySeek(4 + (i * 4), SeekOrigin.Begin))
                    {
                        //Write start and end offsets
                        writer.Write((uint)startOffset);
                        //Last end offset
                        if (i == files.Count - 1)
                            writer.Write((uint)(startOffset + files[i].CompressedStream.Length));
                    }
                    files[i].CompressedStream.CopyTo(writer.BaseStream);
                }
            }
        }

        public class TFileInfo : ArchiveFileInfo
        {
            public TwoCC ArchiveFile;

            public bool IsLZ11;

            public Stream CompressedStream => base.FileData;

            public TFileInfo(TwoCC arc)
            {
                ArchiveFile = arc;
            }

               public override Stream FileData
               {
                   get { return DecompressBlock(); }
                   set
                   {
                       base.FileData = value;
                   }
               }

            private Stream DecompressBlock()
            {
                byte[] data = base.FileData.ToArray();
                return new MemoryStream(data);
            }
        }
    }
}
