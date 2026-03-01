using CtrLibrary;
using CtrLibrary.Rendering;
using CtrLibrary.Bch;
using CtrLibrary.UI;
using GLFrameworkEngine;
using MapStudio.UI;
using SPICA.Formats.Common;
using SPICA.Formats.CtrH3D;
using SPICA.Formats.CtrH3D.Animation;
using SPICA.Formats.CtrH3D.LUT;
using SPICA.Formats.CtrH3D.Model;
using SPICA.Formats.CtrH3D.Texture;
using SPICA.Formats.GFL2;
using SPICA.Formats.GFL2.Motion;
using System;
using System.Collections.Generic;
using System.IO;
using Toolbox.Core;
using Toolbox.Core.IO;
using Toolbox.Core.ViewModels;
using UIFramework;
using OpenTK;

namespace CtrLibrary.GFL2
{
    /// <summary>
    /// Viewer-only support for GFL2 GFModelPack (0x00010000).
    /// </summary>
    public class GFModelPackFile : FileEditor, IFileFormat
    {
        public string[] Description => new string[] { "GFL2 GFModelPack" };
        public string[] Extension => new string[] { "*.gfmodelpack", "*.gfmodel", "*.gfbmdl" };
        public bool CanSave { get; set; } = false;
        public File_Info FileInfo { get; set; }

        public H3DRender Render;
        public H3D H3DData;

        public override bool DisplayViewport => Render != null && Render.Renderer.Models.Count > 0;

        public GFModelPackFile() { FileInfo = new File_Info(); }

        public bool Identify(File_Info fileInfo, Stream stream)
        {
            byte[] data = stream.ToArray();
            if (fileInfo?.ParentArchive != null && GFL2BundleHelper.IsPcContainer(data))
                return false;
            return TryExtractGfModelPack(data, out _, out _, out _);
        }

        public override bool CreateNew()
        {
            return false;
        }

        public void Load(Stream stream)
        {
            byte[] data = stream.ToArray();
            bool isPcContainer = GFL2BundleHelper.IsPcContainer(data);
            if (!TryExtractGfModelPack(data, out byte[] packBytes, out List<byte[]> extraTextures, out List<byte[]> motionBlobs))
                throw new Exception("No GFModelPack found in file.");

            using var ms = new MemoryStream(packBytes);
            using var reader = new BinaryReader(ms);
            GFModelPack pack = new GFModelPack(reader);
            if (pack.Textures.Count == 0 && extraTextures.Count > 0)
                AppendExternalTextures(pack, extraTextures);
            var h3d = pack.ToH3D();
            EnsureTextures(h3d, packBytes, extraTextures);
            h3d.CopyMaterials();
            AppendAnimations(h3d, motionBlobs);

            Load(h3d);
        }

        public void Save(Stream stream)
        {
            throw new NotSupportedException("GFModelPack viewer-only support (save not implemented).");
        }

        public override List<DockWindow> PrepareDocks()
        {
            List<DockWindow> windows = new List<DockWindow>();
            windows.Add(Workspace.Outliner);
            windows.Add(Workspace.PropertyWindow);
            windows.Add(Workspace.ConsoleWindow);
            windows.Add(Workspace.ViewportWindow);
            windows.Add(Workspace.TimelineWindow);
            windows.Add(Workspace.GraphWindow);
            return windows;
        }

        private void Load(H3D h3d)
        {
            H3DData = h3d;
            Root.TagUI.Tag = h3d;

            Render = new H3DRender(H3DData, null);
            AddRender(Render);

            Root.AddChild(SceneLightingUI.Setup(Render));

            var bch = new BCH();
            bch.Render = Render;
            bch.H3DData = H3DData;

            Root.AddChild(new ModelFolder<H3DModel>(bch, H3DData, H3DData.Models));
            Root.AddChild(new TextureFolder<H3DTexture>(Render, H3DData.Textures));
            Root.AddChild(new LUTFolder<H3DLUT>(Render, H3DData.LUTs));
            AddAnimationGroups(H3DData);

            FrameCamera();
            Root.OnSelected += delegate { FrameCamera(); };
        }

        private void FrameCamera()
        {
            if (Render == null || Render.Renderer.Models.Count == 0)
                return;

            var aabb = Render.Renderer.Models[0].GetModelAABB();
            var center = aabb.Center;

            float dimension = 1;
            dimension = Math.Max(dimension, Math.Abs(aabb.Size.X));
            dimension = Math.Max(dimension, Math.Abs(aabb.Size.Y));
            dimension = Math.Max(dimension, Math.Abs(aabb.Size.Z));
            dimension *= 2;

            var translation = new Vector3(0, 0, dimension);
            GLContext.ActiveContext.Camera.SetPosition(center + translation);
            GLContext.ActiveContext.Camera.RotationX = 0;
            GLContext.ActiveContext.Camera.RotationY = 0;
            GLContext.ActiveContext.Camera.RotationZ = 0;
            GLContext.ActiveContext.Camera.UpdateMatrices();
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
            bool foundPack = false;

            if (LooksLikeLz11(data))
            {
                data = DecompressLz11(data);
                if (data == null || data.Length < 4)
                    return false;
            }

            if (IsGfTexture(data))
            {
                textureBlobs.Add(data);
            }

            if (IsGfMotion(data) || IsLikelyGfMotionPack(data))
            {
                motionBlobs.Add(data);
            }

            if (IsValidGfModelPack(data))
            {
                packBytes = data;
                foundPack = true;
            }

            if (!LooksLikeContainer(data))
                return foundPack;

            string magic = $"{(char)data[0]}{(char)data[1]}";
            ushort count = (ushort)(data[2] | (data[3] << 8));
            int tableSize = 4 + (count + 1) * 4;
            if (tableSize > data.Length)
                return foundPack;

            List<int> offsets = new List<int>(count + 1);
            int prev = 0;
            for (int i = 0; i < count + 1; i++)
            {
                int off = BitConverter.ToInt32(data, 4 + i * 4);
                if (off < prev || off > data.Length)
                    return foundPack;
                offsets.Add(off);
                prev = off;
            }

            List<int> order = new List<int>();
            if (magic == "CP" && count >= 2)
                order.Add(1);
            for (int i = 0; i < count; i++)
            {
                if (!order.Contains(i))
                    order.Add(i);
            }

            foreach (int i in order)
            {
                int start = offsets[i];
                int end = offsets[i + 1];
                if (start < 0 || end < start || end > data.Length)
                    continue;
                byte[] slice = new byte[end - start];
                Buffer.BlockCopy(data, start, slice, 0, slice.Length);
                if (!foundPack)
                {
                    if (TryExtractGfModelPackInner(slice, depth + 1, textureBlobs, motionBlobs, out byte[] foundBytes))
                    {
                        if (packBytes == null && foundBytes != null)
                            packBytes = foundBytes;
                        foundPack = true;
                    }
                }
                else
                {
                    // Keep scanning siblings to collect motions/textures even after the pack is found.
                    TryExtractGfModelPackInner(slice, depth + 1, textureBlobs, motionBlobs, out _);
                }
            }

            return foundPack;
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

        private static bool IsGfTexture(byte[] data)
        {
            if (data == null || data.Length < 4)
                return false;
            return BitConverter.ToUInt32(data, 0) == 0x15041213;
        }

        private static bool IsGfMotion(byte[] data)
        {
            if (data == null || data.Length < 4)
                return false;
            return BitConverter.ToUInt32(data, 0) == 0x00060000;
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
                    pack.Textures.Add(new SPICA.Formats.GFL2.Texture.GFTexture(reader));
                }
                catch
                {
                    // Best-effort: ignore malformed texture blobs.
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
                AppendTexturesFromBlobs(h3d, extraTextures);
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
                        var tex = new SPICA.Formats.GFL2.Texture.GFTexture(reader).ToH3DTexture();
                        if (!h3d.Textures.Contains(tex.Name))
                            h3d.Textures.Add(tex);
                    }
                    catch
                    {
                        // Best-effort.
                    }
                }
                off += counts[sect] * 4;
            }
        }

        private static void AppendTexturesFromBlobs(H3D h3d, List<byte[]> blobs)
        {
            foreach (var tex in blobs)
            {
                try
                {
                    using var ms = new MemoryStream(tex);
                    using var reader = new BinaryReader(ms);
                    var h3dTex = new SPICA.Formats.GFL2.Texture.GFTexture(reader).ToH3DTexture();
                    if (!h3d.Textures.Contains(h3dTex.Name))
                        h3d.Textures.Add(h3dTex);
                }
                catch
                {
                    // Best-effort.
                }
            }
        }

        private void AddAnimationGroups(H3D h3d)
        {
            if (h3d == null)
                return;

            AddNodeGroupSimple(h3d.SkeletalAnimations, BCH.H3DGroupType.SkeletalAnim);
            AddNodeGroupSimple(h3d.MaterialAnimations, BCH.H3DGroupType.MaterialAnim);
            AddNodeGroupSimple(h3d.VisibilityAnimations, BCH.H3DGroupType.VisibiltyAnim);
        }

        private void AddNodeGroupSimple<T>(H3DDict<T> subSections, BCH.H3DGroupType type)
            where T : INamed
        {
            if (subSections == null || subSections.Count == 0)
                return;

            var folder = new BCH.H3DGroupNode<T>(type, subSections);

            foreach (var item in subSections)
            {
                if (item is H3DAnimation)
                    folder.AddChild(new AnimationNode<T>(subSections, item));
                else
                    folder.AddChild(new BCH.NodeSection<T>(subSections, item));
            }

            Root.AddChild(folder);
        }

        private class AnimationNode<T> : BCH.NodeSection<T> where T : INamed
        {
            public AnimationNode(H3DDict<T> subSections, object section) : base(subSections, section)
            {
                if (section is not H3DAnimation animation)
                    return;

                var wrapper = new AnimationWrapper(animation);
                Tag = wrapper;

                this.OnHeaderRenamed += delegate
                {
                    wrapper.Root.Header = this.Header;
                };
                wrapper.Root.OnHeaderRenamed += delegate
                {
                    this.Header = wrapper.Root.Header;
                };

                var propertyUI = new BchAnimPropertyUI();
                this.TagUI.UIDrawer += delegate
                {
                    propertyUI.Render(wrapper, null);
                };

                this.OnSelected += delegate
                {
                    if (Tag is AnimationWrapper)
                        ((AnimationWrapper)Tag).AnimationSet();
                };
            }
        }

        private static void AppendAnimations(H3D h3d, List<byte[]> motionBlobs)
        {
            if (h3d == null || motionBlobs == null || motionBlobs.Count == 0)
                return;

            var skeleton = h3d.Models.Count > 0 ? h3d.Models[0].Skeleton : null;
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
                    // Best-effort: ignore malformed motions.
                }
            }
        }

        private static void AddMotion(H3D h3d, GFMotion motion, H3DDict<H3DBone> skeleton)
        {
            if (h3d == null || motion == null)
                return;

            string baseName = $"Motion_{motion.Index}";

            H3DAnimation sklAnim = null;
            if (skeleton != null && skeleton.Count > 0)
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
    }
}
