using CtrLibrary;
using CtrLibrary.Bch;
using CtrLibrary.Rendering;
using CtrLibrary.UI;
using GLFrameworkEngine;
using MapStudio.UI;
using SPICA.Formats.Common;
using SPICA.Formats.CtrH3D;
using SPICA.Formats.CtrH3D.LUT;
using SPICA.Formats.CtrH3D.Model;
using SPICA.Formats.CtrH3D.Texture;
using SPICA.Formats.GFL2.Model;
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
    /// Viewer-only support for single GFL2 GFModel (0x15122117).
    /// </summary>
    public class GFModelFile : FileEditor, IFileFormat
    {
        public string[] Description => new string[] { "GFL2 GFModel" };
        public string[] Extension => new string[] { "*.gfbmdl", "*.gfmdl" };
        public bool CanSave { get; set; } = false;
        public File_Info FileInfo { get; set; }

        public H3DRender Render;
        public H3D H3DData;

        public override bool DisplayViewport => Render != null && Render.Renderer.Models.Count > 0;

        public GFModelFile() { FileInfo = new File_Info(); }

        public bool Identify(File_Info fileInfo, Stream stream)
        {
            byte[] data = stream.ToArray();
            if (fileInfo?.ParentArchive != null && GFL2BundleHelper.IsPcContainer(data))
                return false;
            return TryExtractGfModel(data, out _, out _);
        }

        public override bool CreateNew()
        {
            return false;
        }

        public void Load(Stream stream)
        {
            byte[] data = stream.ToArray();
            bool isPcContainer = GFL2BundleHelper.IsPcContainer(data);
            if (!TryExtractGfModel(data, out byte[] modelBytes, out List<byte[]> textureBlobs))
                throw new Exception("No GFModel found in file.");

            using var ms = new MemoryStream(modelBytes);
            using var reader = new BinaryReader(ms);
            GFModel model = new GFModel(reader, "Model");

            var h3d = new H3D();
            h3d.Models.Add(model.ToH3DModel());
            AppendLuts(h3d, model);
            AppendTexturesFromBlobs(h3d, textureBlobs);
            h3d.CopyMaterials();

            Load(h3d);
        }

        public void Save(Stream stream)
        {
            throw new NotSupportedException("GFModel viewer-only support (save not implemented).");
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

        private static bool TryExtractGfModel(
            byte[] data,
            out byte[] modelBytes,
            out List<byte[]> textureBlobs)
        {
            modelBytes = null;
            textureBlobs = new List<byte[]>();
            if (data == null || data.Length < 4)
                return false;
            return TryExtractGfModelInner(data, 0, textureBlobs, out modelBytes);
        }

        private static bool TryExtractGfModelInner(
            byte[] data,
            int depth,
            List<byte[]> textureBlobs,
            out byte[] modelBytes)
        {
            modelBytes = null;
            if (depth > 8 || data == null || data.Length < 4)
                return false;
            bool foundModel = false;

            if (LooksLikeLz11(data))
            {
                data = DecompressLz11(data);
                if (data == null || data.Length < 4)
                    return false;
            }

            if (IsGfTexture(data))
                textureBlobs.Add(data);

            if (IsGfModel(data))
            {
                modelBytes = data;
                foundModel = true;
            }

            if (!LooksLikeContainer(data))
                return foundModel;

            ushort count = (ushort)(data[2] | (data[3] << 8));
            int tableSize = 4 + (count + 1) * 4;
            if (tableSize > data.Length)
                return foundModel;

            List<int> offsets = new List<int>(count + 1);
            int prev = 0;
            for (int i = 0; i < count + 1; i++)
            {
                int off = BitConverter.ToInt32(data, 4 + i * 4);
                if (off < prev || off > data.Length)
                    return foundModel;
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
                if (!foundModel)
                {
                    if (TryExtractGfModelInner(slice, depth + 1, textureBlobs, out byte[] foundBytes))
                    {
                        if (modelBytes == null && foundBytes != null)
                            modelBytes = foundBytes;
                        foundModel = true;
                    }
                }
                else
                {
                    // Keep scanning siblings to collect textures even after the model is found.
                    TryExtractGfModelInner(slice, depth + 1, textureBlobs, out _);
                }
            }

            return foundModel;
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

        private static bool IsGfModel(byte[] data)
        {
            if (data == null || data.Length < 4)
                return false;
            return BitConverter.ToUInt32(data, 0) == 0x15122117;
        }

        private static bool IsGfTexture(byte[] data)
        {
            if (data == null || data.Length < 4)
                return false;
            return BitConverter.ToUInt32(data, 0) == 0x15041213;
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

        private static void AppendTexturesFromBlobs(H3D h3d, List<byte[]> blobs)
        {
            if (h3d == null)
                return;

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
    }
}
