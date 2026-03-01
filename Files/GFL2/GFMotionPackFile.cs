using CtrLibrary;
using CtrLibrary.Bch;
using CtrLibrary.UI;
using CtrLibrary.Rendering;
using GLFrameworkEngine;
using MapStudio.UI;
using SPICA.Formats.Common;
using SPICA.Formats.CtrH3D;
using SPICA.Formats.CtrH3D.Animation;
using SPICA.Formats.CtrH3D.LUT;
using SPICA.Formats.CtrH3D.Model;
using SPICA.Formats.CtrH3D.Texture;
using SPICA.Formats.GFL2;
using SPICA.Formats.GFL2.Model;
using SPICA.Formats.GFL2.Motion;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Toolbox.Core;
using Toolbox.Core.IO;
using UIFramework;

namespace CtrLibrary.GFL2
{
    /// <summary>
    /// Viewer-only support for GFL2 GFMotion (0x00060000) and motion packs.
    /// </summary>
    public class GFMotionPackFile : FileEditor, IFileFormat
    {
        public string[] Description => new string[] { "GFL2 GFMotion" };
        public string[] Extension => new string[] { "*.gfmot", "*.gfmotion" };
        public bool CanSave { get; set; } = false;
        public File_Info FileInfo { get; set; }

        public H3DRender Render;
        public H3D H3DData;

        public override bool DisplayViewport => Render != null && Render.Renderer.Models.Count > 0;

        public GFMotionPackFile() { FileInfo = new File_Info(); }

        public bool Identify(File_Info fileInfo, Stream stream)
        {
            byte[] data = stream.ToArray();
            if (fileInfo?.ParentArchive != null && GFL2BundleHelper.IsPcContainer(data))
                return false;
            return TryExtractMotions(data, out _);
        }

        public override bool CreateNew()
        {
            return false;
        }

        public void Load(Stream stream)
        {
            byte[] data = stream.ToArray();
            bool isPcContainer = GFL2BundleHelper.IsPcContainer(data);
            if (!TryExtractMotions(data, out List<byte[]> motionBlobs))
                throw new Exception("No GFMotion data found in file.");

            var h3d = new H3D();
            var model = H3DRender.GetFirstVisibleModel();
            AppendAnimations(h3d, motionBlobs, model?.Skeleton);

            Load(h3d);
        }

        public void Save(Stream stream)
        {
            throw new NotSupportedException("GFMotion viewer-only support (save not implemented).");
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

            if (h3d.Models.Count > 0)
            {
                Render = new H3DRender(H3DData, null);
                AddRender(Render);

                Root.AddChild(SceneLightingUI.Setup(Render));

                var bch = new BCH();
                bch.Render = Render;
                bch.H3DData = H3DData;

                Root.AddChild(new ModelFolder<H3DModel>(bch, H3DData, H3DData.Models));
                Root.AddChild(new TextureFolder<H3DTexture>(Render, H3DData.Textures));
                Root.AddChild(new LUTFolder<H3DLUT>(Render, H3DData.LUTs));
                AddAnimationGroups(h3d);

                FrameCamera();
                Root.OnSelected += delegate { FrameCamera(); };
            }
            else
            {
                AddAnimationGroups(h3d);
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

            var translation = new OpenTK.Vector3(0, 0, dimension);
            GLContext.ActiveContext.Camera.SetPosition(center + translation);
            GLContext.ActiveContext.Camera.RotationX = 0;
            GLContext.ActiveContext.Camera.RotationY = 0;
            GLContext.ActiveContext.Camera.RotationZ = 0;
            GLContext.ActiveContext.Camera.UpdateMatrices();
        }

        private static bool TryExtractMotions(byte[] data, out List<byte[]> motionBlobs)
        {
            motionBlobs = new List<byte[]>();
            if (data == null || data.Length < 4)
                return false;
            return TryExtractMotionsInner(data, 0, motionBlobs);
        }

        private static bool TryExtractMotionsInner(byte[] data, int depth, List<byte[]> motionBlobs)
        {
            if (depth > 8 || data == null || data.Length < 4)
                return false;

            bool found = false;

            if (LooksLikeLz11(data))
            {
                data = DecompressLz11(data);
                if (data == null || data.Length < 4)
                    return false;
            }

            if (IsGfMotion(data) || IsLikelyGfMotionPack(data))
            {
                motionBlobs.Add(data);
                found = true;
            }

            if (!LooksLikeContainer(data))
                return found;

            ushort count = (ushort)(data[2] | (data[3] << 8));
            int tableSize = 4 + (count + 1) * 4;
            if (tableSize > data.Length)
                return found;

            List<int> offsets = new List<int>(count + 1);
            int prev = 0;
            for (int i = 0; i < count + 1; i++)
            {
                int off = BitConverter.ToInt32(data, 4 + i * 4);
                if (off < prev || off > data.Length)
                    return found;
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
                if (TryExtractMotionsInner(slice, depth + 1, motionBlobs))
                    found = true;
            }

            return found;
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

        private static void AppendAnimations(H3D h3d, List<byte[]> motionBlobs, H3DDict<H3DBone> skeleton)
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
            if (motion.SkeletalAnimation != null)
            {
                if (skeleton != null && skeleton.Count > 0)
                    sklAnim = motion.ToH3DSkeletalAnimation(skeleton);
                else
                    sklAnim = motion.ToH3DSkeletalAnimation(BuildSkeleton(motion));
            }

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

        private static List<GFBone> BuildSkeleton(GFMotion motion)
        {
            var skeleton = new List<GFBone>();
            if (motion?.SkeletalAnimation == null)
                return skeleton;

            foreach (var bone in motion.SkeletalAnimation.Bones)
            {
                if (skeleton.Exists(x => x.Name == bone.Name))
                    continue;

                skeleton.Add(new GFBone()
                {
                    Name = bone.Name,
                    Parent = string.Empty,
                    Flags = 1,
                    Scale = Vector3.One,
                    Rotation = Vector3.Zero,
                    Translation = Vector3.Zero
                });
            }

            return skeleton;
        }
    }
}
