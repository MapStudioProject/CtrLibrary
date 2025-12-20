using CtrLibrary;
using SPICA.Formats.Common;
using SPICA.Formats.CtrH3D;
using SPICA.Formats.CtrH3D.Animation;
using SPICA.Formats.CtrH3D.LUT;
using SPICA.Formats.CtrH3D.Model;
using SPICA.Formats.GFL2;
using SPICA.Formats.GFL2.Model;
using SPICA.Formats.GFL2.Motion;
using SPICA.Formats.GFL2.Texture;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Toolbox.Core.IO;

namespace CtrLibrary.GFL2
{
    internal static class GFL2BundleHelper
    {
        internal class Gfl2ContentInfo
        {
            public HashSet<string> TextureNames = new HashSet<string>();
            public HashSet<string> MaterialNames = new HashSet<string>();
            public HashSet<string> MeshNames = new HashSet<string>();
            public HashSet<string> BoneNames = new HashSet<string>();
            public List<byte[]> TextureBlobs = new List<byte[]>();
            public List<byte[]> MotionBlobs = new List<byte[]>();
            public bool HasModel;
            public bool HasModelPack;
            public bool HasTextures;
            public bool HasMotions;
        }

        internal class Gfl2ModelInfo
        {
            public HashSet<string> TextureNames = new HashSet<string>();
            public HashSet<string> MaterialNames = new HashSet<string>();
            public HashSet<string> MeshNames = new HashSet<string>();
            public HashSet<string> BoneNames = new HashSet<string>();
        }

        internal class Gfl2AssociatedContent
        {
            public List<byte[]> TextureBlobs = new List<byte[]>();
            public List<byte[]> MotionBlobs = new List<byte[]>();
        }

        internal static bool IsPcContainer(byte[] data)
        {
            if (data == null || data.Length < 4)
                return false;

            if (data[0] == (byte)'P' && data[1] == (byte)'C')
                return true;

            if (data[0] == 0x11)
            {
                byte[] decompressed = TryDecompressLz11(data);
                return decompressed != null
                    && decompressed.Length >= 4
                    && decompressed[0] == (byte)'P'
                    && decompressed[1] == (byte)'C';
            }

            return false;
        }

        private static byte[] TryDecompressLz11(byte[] data)
        {
            try
            {
                var lz11 = new LZSS_N();
                using var decompressed = lz11.Decompress(new MemoryStream(data));
                return decompressed.ReadAllBytes();
            }
            catch
            {
                return null;
            }
        }

        internal static Gfl2ContentInfo ScanFile(string filePath)
        {
            byte[] data = File.ReadAllBytes(filePath);
            return ScanBytes(data);
        }

        internal static Gfl2ContentInfo ScanBytes(byte[] data)
        {
            var info = new Gfl2ContentInfo();
            ScanBytesInner(data, 0, info);
            return info;
        }

        internal static Gfl2ModelInfo BuildModelInfo(GFModel model)
        {
            var info = new Gfl2ModelInfo();
            if (model == null)
                return info;

            foreach (var mat in model.Materials)
            {
                if (!string.IsNullOrEmpty(mat.MaterialName))
                    info.MaterialNames.Add(mat.MaterialName);

                for (int i = 0; i < mat.TextureCoords.Length; i++)
                {
                    string name = mat.TextureCoords[i].Name;
                    if (!string.IsNullOrEmpty(name))
                        info.TextureNames.Add(name);
                }
            }

            foreach (var mesh in model.Meshes)
            {
                if (!string.IsNullOrEmpty(mesh.Name))
                    info.MeshNames.Add(mesh.Name);
            }

            foreach (var bone in model.Skeleton)
            {
                if (!string.IsNullOrEmpty(bone.Name))
                    info.BoneNames.Add(bone.Name);
            }

            return info;
        }

        internal static Gfl2ModelInfo BuildModelInfo(GFModelPack pack)
        {
            var info = new Gfl2ModelInfo();
            if (pack == null)
                return info;

            foreach (var tex in pack.Textures)
            {
                if (!string.IsNullOrEmpty(tex.Name))
                    info.TextureNames.Add(tex.Name);
            }

            foreach (var model in pack.Models)
            {
                var modelInfo = BuildModelInfo(model);
                info.TextureNames.UnionWith(modelInfo.TextureNames);
                info.MaterialNames.UnionWith(modelInfo.MaterialNames);
                info.MeshNames.UnionWith(modelInfo.MeshNames);
                info.BoneNames.UnionWith(modelInfo.BoneNames);
            }

            return info;
        }

        internal static Gfl2AssociatedContent FindAssociatedContent(string folderPath, string currentFilePath, Gfl2ModelInfo modelInfo)
        {
            var content = new Gfl2AssociatedContent();
            if (string.IsNullOrEmpty(folderPath) || modelInfo == null)
                return content;

            foreach (var file in Directory.GetFiles(folderPath))
            {
                if (string.Equals(file, currentFilePath, StringComparison.OrdinalIgnoreCase))
                    continue;

                Gfl2ContentInfo info;
                try
                {
                    info = ScanFile(file);
                }
                catch
                {
                    continue;
                }

                if (info.HasTextures && Intersects(info.TextureNames, modelInfo.TextureNames))
                    content.TextureBlobs.AddRange(info.TextureBlobs);

                if (info.HasMotions && (Intersects(info.BoneNames, modelInfo.BoneNames)
                    || Intersects(info.MaterialNames, modelInfo.MaterialNames)
                    || Intersects(info.MeshNames, modelInfo.MeshNames)))
                {
                    content.MotionBlobs.AddRange(info.MotionBlobs);
                }
            }

            return content;
        }

        internal static string FindBestMatchingModel(string folderPath, Gfl2ContentInfo seedInfo)
        {
            if (string.IsNullOrEmpty(folderPath) || seedInfo == null)
                return null;

            int bestScore = 0;
            string bestPath = null;

            foreach (var file in Directory.GetFiles(folderPath))
            {
                Gfl2ContentInfo info;
                try
                {
                    info = ScanFile(file);
                }
                catch
                {
                    continue;
                }

                if (!info.HasModel && !info.HasModelPack)
                    continue;

                int score = 0;
                score += CountIntersection(info.TextureNames, seedInfo.TextureNames);
                score += CountIntersection(info.BoneNames, seedInfo.BoneNames);
                score += CountIntersection(info.MaterialNames, seedInfo.MaterialNames);
                score += CountIntersection(info.MeshNames, seedInfo.MeshNames);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestPath = file;
                }
            }

            return bestScore > 0 ? bestPath : null;
        }

        internal static H3D LoadModelFromFile(string filePath, out Gfl2ModelInfo modelInfo)
        {
            modelInfo = new Gfl2ModelInfo();
            if (string.IsNullOrEmpty(filePath))
                return null;

            byte[] data = File.ReadAllBytes(filePath);
            if (TryExtractGfModelPack(data, out byte[] packBytes, out List<byte[]> extraTextures, out List<byte[]> motionBlobs))
            {
                using var ms = new MemoryStream(packBytes);
                using var reader = new BinaryReader(ms);
                var pack = new GFModelPack(reader);
                if (pack.Textures.Count == 0 && extraTextures.Count > 0)
                    AppendExternalTextures(pack, extraTextures);
                modelInfo = BuildModelInfo(pack);

                var h3d = pack.ToH3D();
                EnsureTextures(h3d, packBytes, extraTextures);
                h3d.CopyMaterials();
                AppendMotions(h3d, motionBlobs, h3d.Models.Count > 0 ? h3d.Models[0].Skeleton : null);
                return h3d;
            }

            if (TryExtractGfModel(data, out byte[] modelBytes, out List<byte[]> textureBlobs, out List<byte[]> motionBlobs2))
            {
                using var ms = new MemoryStream(modelBytes);
                using var reader = new BinaryReader(ms);
                GFModel model = new GFModel(reader, "Model");
                modelInfo = BuildModelInfo(model);

                var h3d = new H3D();
                h3d.Models.Add(model.ToH3DModel());
                AppendLuts(h3d, model);
                AppendTextures(h3d, textureBlobs);
                h3d.CopyMaterials();
                AppendMotions(h3d, motionBlobs2, h3d.Models[0].Skeleton);
                return h3d;
            }

            return null;
        }

        internal static void AppendTextures(H3D h3d, List<byte[]> blobs)
        {
            if (h3d == null || blobs == null)
                return;

            foreach (var tex in blobs)
            {
                try
                {
                    using var ms = new MemoryStream(tex);
                    using var reader = new BinaryReader(ms);
                    var h3dTex = new GFTexture(reader).ToH3DTexture();
                    if (!h3d.Textures.Contains(h3dTex.Name))
                        h3d.Textures.Add(h3dTex);
                }
                catch
                {
                    // Best-effort.
                }
            }
        }

        internal static void AppendMotions(H3D h3d, List<byte[]> motionBlobs, H3DDict<H3DBone> skeleton)
        {
            if (h3d == null || motionBlobs == null || motionBlobs.Count == 0)
                return;

            int fallbackIndex = 0;
            foreach (var blob in motionBlobs)
            {
                if (blob == null || blob.Length < 4)
                    continue;

                try
                {
                    if (IsLikelyGfMotionPack(blob))
                    {
                        using var ms = new MemoryStream(blob);
                        using var reader = new BinaryReader(ms);
                        var pack = new GFMotionPack(reader);
                        foreach (var mot in pack)
                            AddMotion(h3d, mot, skeleton);
                    }
                    else if (IsGfMotion(blob))
                    {
                        using var ms = new MemoryStream(blob);
                        using var reader = new BinaryReader(ms);
                        var mot = new GFMotion(reader, fallbackIndex++);
                        AddMotion(h3d, mot, skeleton);
                    }
                }
                catch
                {
                    // Best-effort.
                }
            }
        }

        private static void AddMotion(H3D h3d, GFMotion motion, H3DDict<H3DBone> skeleton)
        {
            if (h3d == null || motion == null)
                return;

            string baseName = $"Motion_{motion.Index}";

            H3DAnimation sklAnim = null;
            if (motion.SkeletalAnimation != null && skeleton != null && skeleton.Count > 0)
                sklAnim = motion.ToH3DSkeletalAnimation(skeleton);

            H3DMaterialAnim matAnim = motion.ToH3DMaterialAnimation();
            H3DAnimation visAnim = motion.ToH3DVisibilityAnimation();

            if (sklAnim != null)
            {
                sklAnim.Name = GetUniqueName(h3d.SkeletalAnimations, baseName);
                h3d.SkeletalAnimations.Add(sklAnim);
            }

            if (matAnim != null)
            {
                matAnim.Name = GetUniqueName(h3d.MaterialAnimations, baseName);
                h3d.MaterialAnimations.Add(matAnim);
            }

            if (visAnim != null)
            {
                visAnim.Name = GetUniqueName(h3d.VisibilityAnimations, baseName);
                h3d.VisibilityAnimations.Add(visAnim);
            }
        }

        private static string GetUniqueName<T>(H3DDict<T> dict, string baseName) where T : INamed
        {
            if (dict == null)
                return baseName;
            string name = baseName;
            int suffix = 1;
            while (dict.Contains(name))
            {
                name = $"{baseName}_{suffix}";
                suffix++;
            }
            return name;
        }

        private static bool Intersects(HashSet<string> a, HashSet<string> b)
        {
            if (a == null || b == null || a.Count == 0 || b.Count == 0)
                return false;
            return a.Overlaps(b);
        }

        private static int CountIntersection(HashSet<string> a, HashSet<string> b)
        {
            if (a == null || b == null || a.Count == 0 || b.Count == 0)
                return 0;
            return a.Count(x => b.Contains(x));
        }

        private static void ScanBytesInner(byte[] data, int depth, Gfl2ContentInfo info)
        {
            if (depth > 8 || data == null || data.Length < 4)
                return;

            if (LooksLikeLz11(data))
            {
                data = DecompressLz11(data);
                if (data == null || data.Length < 4)
                    return;
            }

            if (IsGfTexture(data))
            {
                info.HasTextures = true;
                info.TextureBlobs.Add(data);
                TryAddTextureName(data, info.TextureNames);
            }

            if (IsGfMotion(data))
            {
                info.HasMotions = true;
                info.MotionBlobs.Add(data);
                TryAddMotionNames(data, info);
            }
            else if (IsLikelyGfMotionPack(data))
            {
                info.HasMotions = true;
                info.MotionBlobs.Add(data);
                TryAddMotionPackNames(data, info);
            }

            if (IsGfModel(data))
            {
                info.HasModel = true;
                TryAddModelNames(data, info);
            }
            else if (IsValidGfModelPack(data))
            {
                info.HasModelPack = true;
                TryAddModelPackNames(data, info);
            }

            if (!LooksLikeContainer(data))
                return;

            ushort count = (ushort)(data[2] | (data[3] << 8));
            int tableSize = 4 + (count + 1) * 4;
            if (tableSize > data.Length)
                return;

            List<int> offsets = new List<int>(count + 1);
            int prev = 0;
            for (int i = 0; i < count + 1; i++)
            {
                int off = BitConverter.ToInt32(data, 4 + i * 4);
                if (off < prev || off > data.Length)
                    return;
                offsets.Add(off);
                prev = off;
            }

            for (int i = 0; i < count; i++)
            {
                int start = offsets[i];
                int end = offsets[i + 1];
                if (start < 0 || end < start || end > data.Length)
                    continue;
                byte[] slice = new byte[end - start];
                Buffer.BlockCopy(data, start, slice, 0, slice.Length);
                ScanBytesInner(slice, depth + 1, info);
            }
        }

        private static void TryAddTextureName(byte[] data, HashSet<string> names)
        {
            try
            {
                using var ms = new MemoryStream(data);
                using var reader = new BinaryReader(ms);
                var tex = new GFTexture(reader);
                if (!string.IsNullOrEmpty(tex.Name))
                    names.Add(tex.Name);
            }
            catch
            {
            }
        }

        private static void TryAddMotionNames(byte[] data, Gfl2ContentInfo info)
        {
            try
            {
                using var ms = new MemoryStream(data);
                using var reader = new BinaryReader(ms);
                var mot = new GFMotion(reader, 0);
                AddMotionNames(mot, info);
            }
            catch
            {
            }
        }

        private static void TryAddMotionPackNames(byte[] data, Gfl2ContentInfo info)
        {
            try
            {
                using var ms = new MemoryStream(data);
                using var reader = new BinaryReader(ms);
                var pack = new GFMotionPack(reader);
                foreach (var mot in pack)
                    AddMotionNames(mot, info);
            }
            catch
            {
            }
        }

        private static void AddMotionNames(GFMotion mot, Gfl2ContentInfo info)
        {
            if (mot?.SkeletalAnimation != null)
            {
                foreach (var bone in mot.SkeletalAnimation.Bones)
                {
                    if (!string.IsNullOrEmpty(bone.Name))
                        info.BoneNames.Add(bone.Name);
                }
            }

            if (mot?.MaterialAnimation != null)
            {
                foreach (var mat in mot.MaterialAnimation.Materials)
                {
                    if (!string.IsNullOrEmpty(mat.Name))
                        info.MaterialNames.Add(mat.Name);
                }
            }

            if (mot?.VisibilityAnimation != null)
            {
                foreach (var vis in mot.VisibilityAnimation.Visibilities)
                {
                    if (!string.IsNullOrEmpty(vis.Name))
                        info.MeshNames.Add(vis.Name);
                }
            }
        }

        private static void TryAddModelNames(byte[] data, Gfl2ContentInfo info)
        {
            try
            {
                using var ms = new MemoryStream(data);
                using var reader = new BinaryReader(ms);
                var model = new GFModel(reader, "Model");
                var modelInfo = BuildModelInfo(model);
                info.TextureNames.UnionWith(modelInfo.TextureNames);
                info.MaterialNames.UnionWith(modelInfo.MaterialNames);
                info.MeshNames.UnionWith(modelInfo.MeshNames);
                info.BoneNames.UnionWith(modelInfo.BoneNames);
            }
            catch
            {
            }
        }

        private static void TryAddModelPackNames(byte[] data, Gfl2ContentInfo info)
        {
            try
            {
                using var ms = new MemoryStream(data);
                using var reader = new BinaryReader(ms);
                var pack = new GFModelPack(reader);
                var modelInfo = BuildModelInfo(pack);
                info.TextureNames.UnionWith(modelInfo.TextureNames);
                info.MaterialNames.UnionWith(modelInfo.MaterialNames);
                info.MeshNames.UnionWith(modelInfo.MeshNames);
                info.BoneNames.UnionWith(modelInfo.BoneNames);
            }
            catch
            {
            }
        }

        private static bool LooksLikeContainer(byte[] data)
        {
            if (data == null || data.Length < 8)
                return false;
            byte a = data[0];
            byte b = data[1];
            if (a < 0x41 || a > 0x5A || b < 0x41 || b > 0x5A)
                return false;
            ushort count = (ushort)(data[2] | (data[3] << 8));
            if (count == 0 || count > 0x4000)
                return false;
            int tableSize = 4 + (count + 1) * 4;
            return tableSize <= data.Length;
        }

        private static bool IsValidGfModelPack(byte[] data)
        {
            if (data == null || data.Length < 4)
                return false;
            if (BitConverter.ToUInt32(data, 0) != 0x00010000)
                return false;
            if (data.Length < 24)
                return false;

            int[] counts = new int[5];
            int total = 0;
            for (int i = 0; i < 5; i++)
            {
                counts[i] = BitConverter.ToInt32(data, 4 + i * 4);
                if (counts[i] < 0)
                    return false;
                total += counts[i];
            }

            int ptrBase = 4 + 5 * 4;
            int ptrTableBytes = total * 4;
            if (ptrBase + ptrTableBytes > data.Length)
                return false;

            bool hasModel = false;
            int off = ptrBase;
            for (int sect = 0; sect < 5; sect++)
            {
                for (int i = 0; i < counts[sect]; i++)
                {
                    int ptr = BitConverter.ToInt32(data, off + i * 4);
                    if (ptr <= 0 || ptr >= data.Length)
                        continue;
                    if (ptr + 1 >= data.Length)
                        continue;
                    int nameLen = data[ptr];
                    int nameEnd = ptr + 1 + nameLen;
                    if (nameEnd + 4 > data.Length)
                        continue;
                    int addr = BitConverter.ToInt32(data, nameEnd);
                    if (addr <= 0 || addr + 4 > data.Length)
                        continue;
                    if (sect == 0 && BitConverter.ToUInt32(data, addr) == 0x15122117)
                        hasModel = true;
                }
                off += counts[sect] * 4;
            }

            return hasModel;
        }

        private static bool IsGfModel(byte[] data)
        {
            return data != null && data.Length >= 4 && BitConverter.ToUInt32(data, 0) == 0x15122117;
        }

        private static bool IsGfTexture(byte[] data)
        {
            return data != null && data.Length >= 4 && BitConverter.ToUInt32(data, 0) == 0x15041213;
        }

        private static bool IsGfMotion(byte[] data)
        {
            return data != null && data.Length >= 4 && BitConverter.ToUInt32(data, 0) == 0x00060000;
        }

        private static bool IsLikelyGfMotionPack(byte[] data)
        {
            if (data == null || data.Length < 8)
                return false;

            uint count = BitConverter.ToUInt32(data, 0);
            if (count == 0 || count > 0x1000)
                return false;

            long tableEnd = 4 + count * 4;
            if (tableEnd > data.Length)
                return false;

            bool hasEntry = false;
            for (int i = 0; i < count; i++)
            {
                uint addr = BitConverter.ToUInt32(data, 4 + i * 4);
                if (addr == 0)
                    continue;
                long pos = 4 + addr;
                if (pos + 4 > data.Length || pos < 4)
                    return false;
                if (BitConverter.ToUInt32(data, (int)pos) != 0x00060000)
                    return false;
                hasEntry = true;
            }

            return hasEntry;
        }

        private static bool LooksLikeLz11(byte[] data)
        {
            if (data == null || data.Length < 4 || data[0] != 0x11)
                return false;
            int decodedLen = data[1] | (data[2] << 8) | (data[3] << 16);
            return decodedLen > data.Length;
        }

        private static byte[] DecompressLz11(byte[] data)
        {
            if (data == null || data.Length < 4)
                return data;
            if (data[0] != 0x11)
                return data;
            int decodedLen = data[1] | (data[2] << 8) | (data[3] << 16);
            byte[] output = new byte[decodedLen];
            int outOff = 0;
            int inOff = 4;
            int mask = 0;
            int header = 0;

            while (outOff < decodedLen && inOff < data.Length)
            {
                mask >>= 1;
                if (mask == 0)
                {
                    header = data[inOff++];
                    mask = 0x80;
                }

                if ((header & mask) == 0)
                {
                    if (inOff >= data.Length)
                        break;
                    output[outOff++] = data[inOff++];
                    continue;
                }

                if (inOff >= data.Length)
                    break;
                int byte1 = data[inOff++];
                int top = byte1 >> 4;
                int position;
                int length;

                if (top == 0)
                {
                    if (inOff + 1 >= data.Length)
                        break;
                    int byte2 = data[inOff++];
                    int byte3 = data[inOff++];
                    position = ((byte2 & 0xF) << 8) | byte3;
                    length = (((byte1 & 0xF) << 4) | (byte2 >> 4)) + 0x11;
                }
                else if (top == 1)
                {
                    if (inOff + 2 >= data.Length)
                        break;
                    int byte2 = data[inOff++];
                    int byte3 = data[inOff++];
                    int byte4 = data[inOff++];
                    position = ((byte3 & 0xF) << 8) | byte4;
                    length = (((byte1 & 0xF) << 12) | (byte2 << 4) | (byte3 >> 4)) + 0x111;
                }
                else
                {
                    if (inOff >= data.Length)
                        break;
                    int byte2 = data[inOff++];
                    position = ((byte1 & 0xF) << 8) | byte2;
                    length = (byte1 >> 4) + 1;
                }

                position += 1;
                for (int i = 0; i < length && outOff < decodedLen; i++)
                {
                    output[outOff] = output[outOff - position];
                    outOff++;
                }
            }

            return output;
        }

        private static void AppendExternalTextures(GFModelPack pack, List<byte[]> textureBlobs)
        {
            foreach (var tex in textureBlobs)
            {
                try
                {
                    using var ms = new MemoryStream(tex);
                    using var reader = new BinaryReader(ms);
                    pack.Textures.Add(new GFTexture(reader));
                }
                catch
                {
                }
            }
        }

        private static void EnsureTextures(H3D h3d, byte[] packBytes, List<byte[]> extraTextures)
        {
            if (h3d == null || h3d.Textures == null)
                return;
            if (h3d.Textures.Count > 0)
                return;

            AppendTexturesFromPackBytes(h3d, packBytes);
            if (h3d.Textures.Count == 0 && extraTextures.Count > 0)
                AppendTextures(h3d, extraTextures);
        }

        private static void AppendTexturesFromPackBytes(H3D h3d, byte[] packBytes)
        {
            if (h3d == null || packBytes == null || packBytes.Length < 24)
                return;
            if (BitConverter.ToUInt32(packBytes, 0) != 0x00010000)
                return;

            int[] counts = new int[5];
            int total = 0;
            for (int i = 0; i < 5; i++)
            {
                counts[i] = BitConverter.ToInt32(packBytes, 4 + i * 4);
                if (counts[i] < 0)
                    return;
                total += counts[i];
            }

            int ptrBase = 4 + 5 * 4;
            if (ptrBase + total * 4 > packBytes.Length)
                return;

            int off = ptrBase;
            for (int sect = 0; sect < 5; sect++)
            {
                for (int i = 0; i < counts[sect]; i++)
                {
                    int ptr = BitConverter.ToInt32(packBytes, off + i * 4);
                    if (ptr <= 0 || ptr >= packBytes.Length)
                        continue;
                    if (ptr + 1 >= packBytes.Length)
                        continue;
                    int nameLen = packBytes[ptr];
                    int nameEnd = ptr + 1 + nameLen;
                    if (nameEnd + 4 > packBytes.Length)
                        continue;
                    int addr = BitConverter.ToInt32(packBytes, nameEnd);
                    if (addr <= 0 || addr + 4 > packBytes.Length)
                        continue;
                    if (BitConverter.ToUInt32(packBytes, addr) != 0x15041213)
                        continue;

                    try
                    {
                        using var ms = new MemoryStream(packBytes);
                        using var reader = new BinaryReader(ms);
                        reader.BaseStream.Seek(addr, SeekOrigin.Begin);
                        var tex = new GFTexture(reader).ToH3DTexture();
                        if (!h3d.Textures.Contains(tex.Name))
                            h3d.Textures.Add(tex);
                    }
                    catch
                    {
                    }
                }
                off += counts[sect] * 4;
            }
        }

        private static void AppendLuts(H3D h3d, GFModel model)
        {
            if (h3d == null || model == null || model.LUTs.Count == 0)
                return;

            H3DLUT lut = new H3DLUT();
            lut.Name = "LookupTableSetContentCtrName";
            foreach (var sampler in model.LUTs)
            {
                lut.Samplers.Add(new H3DLUTSampler()
                {
                    Name = sampler.Name,
                    Table = sampler.Table
                });
            }
            h3d.LUTs.Add(lut);
        }

        private static bool TryExtractGfModelPack(
            byte[] data,
            out byte[] packBytes,
            out List<byte[]> textureBlobs,
            out List<byte[]> motionBlobs)
        {
            packBytes = null;
            textureBlobs = new List<byte[]>();
            motionBlobs = new List<byte[]>();
            if (data == null || data.Length < 4)
                return false;
            return TryExtractGfModelPackInner(data, 0, textureBlobs, motionBlobs, out packBytes);
        }

        private static bool TryExtractGfModelPackInner(
            byte[] data,
            int depth,
            List<byte[]> textureBlobs,
            List<byte[]> motionBlobs,
            out byte[] packBytes)
        {
            packBytes = null;
            if (depth > 8 || data == null || data.Length < 4)
                return false;

            if (LooksLikeLz11(data))
            {
                data = DecompressLz11(data);
                if (data == null || data.Length < 4)
                    return false;
            }

            if (IsGfTexture(data))
                textureBlobs.Add(data);

            if (IsGfMotion(data) || IsLikelyGfMotionPack(data))
                motionBlobs.Add(data);

            if (IsValidGfModelPack(data))
            {
                packBytes = data;
                return true;
            }

            if (!LooksLikeContainer(data))
                return false;

            ushort count = (ushort)(data[2] | (data[3] << 8));
            int tableSize = 4 + (count + 1) * 4;
            if (tableSize > data.Length)
                return false;

            List<int> offsets = new List<int>(count + 1);
            int prev = 0;
            for (int i = 0; i < count + 1; i++)
            {
                int off = BitConverter.ToInt32(data, 4 + i * 4);
                if (off < prev || off > data.Length)
                    return false;
                offsets.Add(off);
                prev = off;
            }

            for (int i = 0; i < count; i++)
            {
                int start = offsets[i];
                int end = offsets[i + 1];
                if (start < 0 || end < start || end > data.Length)
                    continue;
                byte[] slice = new byte[end - start];
                Buffer.BlockCopy(data, start, slice, 0, slice.Length);
                if (TryExtractGfModelPackInner(slice, depth + 1, textureBlobs, motionBlobs, out packBytes))
                    return true;
            }

            return false;
        }

        private static bool TryExtractGfModel(
            byte[] data,
            out byte[] modelBytes,
            out List<byte[]> textureBlobs,
            out List<byte[]> motionBlobs)
        {
            modelBytes = null;
            textureBlobs = new List<byte[]>();
            motionBlobs = new List<byte[]>();
            if (data == null || data.Length < 4)
                return false;
            return TryExtractGfModelInner(data, 0, textureBlobs, motionBlobs, out modelBytes);
        }

        private static bool TryExtractGfModelInner(
            byte[] data,
            int depth,
            List<byte[]> textureBlobs,
            List<byte[]> motionBlobs,
            out byte[] modelBytes)
        {
            modelBytes = null;
            if (depth > 8 || data == null || data.Length < 4)
                return false;

            if (LooksLikeLz11(data))
            {
                data = DecompressLz11(data);
                if (data == null || data.Length < 4)
                    return false;
            }

            if (IsGfTexture(data))
                textureBlobs.Add(data);

            if (IsGfMotion(data) || IsLikelyGfMotionPack(data))
                motionBlobs.Add(data);

            if (IsGfModel(data))
            {
                modelBytes = data;
                return true;
            }

            if (!LooksLikeContainer(data))
                return false;

            ushort count = (ushort)(data[2] | (data[3] << 8));
            int tableSize = 4 + (count + 1) * 4;
            if (tableSize > data.Length)
                return false;

            List<int> offsets = new List<int>(count + 1);
            int prev = 0;
            for (int i = 0; i < count + 1; i++)
            {
                int off = BitConverter.ToInt32(data, 4 + i * 4);
                if (off < prev || off > data.Length)
                    return false;
                offsets.Add(off);
                prev = off;
            }

            for (int i = 0; i < count; i++)
            {
                int start = offsets[i];
                int end = offsets[i + 1];
                if (start < 0 || end < start || end > data.Length)
                    continue;
                byte[] slice = new byte[end - start];
                Buffer.BlockCopy(data, start, slice, 0, slice.Length);
                if (TryExtractGfModelInner(slice, depth + 1, textureBlobs, motionBlobs, out modelBytes))
                    return true;
            }

            return false;
        }
    }
}
