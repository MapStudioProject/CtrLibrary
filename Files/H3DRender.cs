using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using GLFrameworkEngine;
using Toolbox.Core.ViewModels;
using SPICA.Rendering;
using SPICA.Formats.CtrH3D;
using SPICA.Formats.CtrGfx;
using OpenTK.Graphics;
using CtrLibrary.Bcres;
using OpenTK.Graphics.OpenGL;
using SPICA.Formats.CtrH3D.Texture;
using SPICA.Formats.CtrH3D.Model;
using SPICA.Formats.CtrH3D.LUT ;
using SPICA.PICA.Shader;
using Newtonsoft.Json;
using CtrLibrary.UI;
using OpenTK;
using GLFrameworkEngine.Utils;
using SPICA.Formats.ModelBinary;

namespace CtrLibrary.Rendering
{
    /// <summary>
    /// Represents a render instance for H3D data used for rendering PICA 3DS data.
    /// </summary>
    public class H3DRender : EditableObject, IColorPickable, IFrustumCulling
    {
        /// <summary>
        /// The texture cache of globally loaded textures. This cache is only used for UI purposes to get the H3D instances for viewing.
        /// </summary>
        public static Dictionary<string, H3DTexture> TextureCache = new Dictionary<string, H3DTexture>();

        //For accessing H3D instances for bones
        public static List<H3DRender> H3DRenderCache = new List<H3DRender>();

        public static List<Renderer> RenderCache = new List<Renderer>();

        //The H3D scene instance
        public H3D Scene;

        /// <summary>
        /// The renderer instance used to load and render out the scene.
        /// </summary>
        public Renderer Renderer;

        /// <summary>
        /// The skeleton list used to draw and render bone data.
        /// </summary>
        public List<SkeletonRenderer> Skeletons = new List<SkeletonRenderer>();

        public bool EnableFrustumCulling => true;

        public bool InFrustum { get; set; } = true;

        private Vector3 Min;
        private Vector3 Max;

        public H3DRender(Stream stream, NodeBase parent) : base(parent) {
            Load(Gfx.Open(stream).ToH3D());
        }

        public H3DRender(H3D h3d, NodeBase parent) : base(parent) {
            Load(h3d);
        }

        public H3DRender(H3DRender cached, NodeBase parent) : base(parent)
        {
            Renderer = new Renderer(1, 1);
            Renderer.LoadCached(cached.Renderer);
         /*   Renderer.Lights.Add(new Light()
            {
                Ambient = new Color4(0.1f, 0.1f, 0.1f, 1.0f),
                Diffuse = new Color4(0.4f, 0.4f, 0.4f, 1.0f),
                Specular0 = new Color4(0.3f, 0.3f, 0.3f, 1.0f),
                Specular1 = new Color4(0.4f, 0.4f, 0.4f, 1.0f),
                TwoSidedDiffuse = true,
                Position = new OpenTK.Vector3(0, 0, 0),
                Enabled = true,
                Type = LightType.PerFragment,
            });*/

            Renderer.Lights.Add(new Light()
            {
                Ambient = new Color4(0.1f, 0.1f, 0.1f, 1.0f),
                Diffuse = new Color4(1, 1, 1, 1.0f),
                Specular0 = new Color4(0.3f, 0.3f, 0.3f, 1.0f),
                Specular1 = new Color4(0.4f, 0.4f, 0.4f, 1.0f),
                TwoSidedDiffuse = true,
                Position = new OpenTK.Vector3(0, 0, 0),
                Enabled = true,
                Type = LightType.PerFragment,
            });

            //Caches are used to search up globally loaded data within the UI and renders
            //So a file can access the data externally from other files
            LUTCacheManager.Setup();
            LUTCacheManager.Load(Renderer);

            BoundingSphere = cached.BoundingSphere;

            bounding = new BoundingNode(cached.Min, cached.Max);
            bounding.UpdateTransform(this.Transform.TransformMatrix);
            this.Transform.TransformUpdated += delegate
            {
                bounding.UpdateTransform(this.Transform.TransformMatrix);
            };
        }

        private BoundingNode bounding = new BoundingNode();

        public override BoundingNode BoundingNode => bounding;

        public bool IsInsideFrustum(GLContext context)
        {
            return context.Camera.InFustrum(this.BoundingNode);
        }

        public static H3DModel GetFirstVisibleModel()
        {
            foreach (var render in H3DRenderCache)
            {
                if (render.IsVisible)
                {
                    foreach (var model in render.Scene.Models)
                    {
                        if (model.IsVisible)
                            return model;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Inserts a model into the scene with a given index.
        /// </summary>
        public void InsertModel(H3DModel model, int index)
        {
            if (index != -1)
            {
                Renderer.Models.RemoveAt(index);
                Renderer.Models.Insert(index, new Model(Renderer, model));
            }
            else
            {
                Renderer.Models.Add(new SPICA.Rendering.Model(Renderer, model));
            }
        }

        /// <summary>
        /// Updates all the render uniform data.
        /// </summary>
        public void UpdateAllUniforms()
        {
            Renderer.UpdateAllUniforms();
        }

        /// <summary>
        /// Updates all the renderer shaders.
        /// </summary>
        public void UpdateShaders()
        {
            Renderer.UpdateAllShaders();
            Renderer.UpdateAllUniforms();
        }

        private void Load(H3D h3d)
        {
            //Caches are used to search up globally loaded data within the UI
            //So a file can access the data externally from other files
            foreach (var tex in h3d.Textures)
            {
                if (!TextureCache.ContainsKey(tex.Name))
                    TextureCache.Add(tex.Name, tex);
            }
            foreach (var lut in h3d.LUTs)
            {
                if (!LUTCacheManager.Cache.ContainsKey(lut.Name))
                    LUTCacheManager.Cache.Add(lut.Name, lut);
            }

            Scene = h3d;

            //Local render for workspaces
            Renderer = new Renderer(1, 1);
            RenderCache.Add(Renderer);

            H3DRenderCache.Add(this);

            //Configurable scene lighting
            if (File.Exists("CtrScene.json"))
            {
                var lights = JsonConvert.DeserializeObject<Light[]>(File.ReadAllText("CtrScene.json"));
                foreach (var light in lights)
                    Renderer.Lights.Add(light);
            }
            else
            {
                Renderer.Lights.Add(new Light()
                {
                    Ambient = new Color4(0.5f, 0.45f, 0.56f, 1.0f),
                    Diffuse = new Color4(0.35f, 0.35f, 0.35f, 1.0f),
                    Specular0 = new Color4(0.7f, 0.7f, 0.7f, 1.0f),
                    Specular1 = new Color4(0.3f, 0.3f, 0.3f, 1.0f),
                    TwoSidedDiffuse = false,
                    Position = new OpenTK.Vector3(10000, 10000, 10000),
                    Direction = new Vector3(-0.286f, -0.953f, -0.095f),
                    Enabled = true,
                    Directional = true,
                    Type = LightType.PerFragment,
                });
            }
            Renderer.Merge(Scene);  

            //Load the render cache for loading globally renderable data (textures, luts)
            foreach (var tex in Renderer.Textures)
                if (!Renderer.TextureCache.ContainsKey(tex.Key))
                    Renderer.TextureCache.Add(tex.Key, tex.Value);

            foreach (var lut in Renderer.LUTs)
                if (!Renderer.LUTs.ContainsKey(lut.Key))
                    Renderer.LUTs.Add(lut.Key, lut.Value);

            //Caches are used to search up globally loaded data within the UI and renders
            //So a file can access the data externally from other files
            LUTCacheManager.Setup();
            LUTCacheManager.Load(Renderer);


            Vector3 min = new Vector3(float.MaxValue);
            Vector3 max = new Vector3(float.MinValue);

            List<Vector3> positions = new List<Vector3>();
            foreach (var model in h3d.Models)
            {
                foreach (var mesh in model.Meshes)
                {
                    foreach (var vert in mesh.GetVertices())
                    {
                        var p = new System.Numerics.Vector3(vert.Position.X, vert.Position.Y, vert.Position.Z);
                        if (model.Skeleton.Count > 0)
                        {
                            var index = vert.Indices.b0;
                            if (index != -1)
                                p = System.Numerics.Vector3.Transform(p, model.Skeleton[index].GetWorldTransform(model.Skeleton));
                        }

                        positions.Add(new Vector3(p.X, p.Y, p.Z));

                        min.X = MathF.Min(p.X, min.X);
                        min.Y = MathF.Min(p.Y, min.Y);
                        min.Z = MathF.Min(p.Z, min.Z);
                        max.X = MathF.Max(p.X, max.X);
                        max.Y = MathF.Max(p.Y, max.Y);
                        max.Z = MathF.Max(p.Z, max.Z);
                    }
                }
            }

            Min = min;
            Max = max;

            bounding = new BoundingNode(min, max);
            BoundingSphere = BoundingSphereGenerator.GenerateBoundingSphere(positions);
            bounding.UpdateTransform(this.Transform.TransformMatrix);
            this.Transform.TransformUpdated += delegate
            {
                bounding.UpdateTransform(this.Transform.TransformMatrix);
            };

            CanSelect = false;
        }

        public override void Dispose()
        {
            base.Dispose();

            foreach (var skel in this.Skeletons)
                skel.Dispose();

            //Remove from global texture cache
            foreach (var tex in Renderer.Textures)
                if (TextureCache.ContainsKey(tex.Key))
                    TextureCache.Remove(tex.Key);
 
            Renderer.DeleteAll();
            RenderCache.Remove(this.Renderer);

            H3DRenderCache.Remove(this);
        }

        public void DrawColorPicking(GLContext context)
        {
            if (!CanSelect || !InFrustum) return;

            var color = context.ColorPicker.SetPickingColor(this);

            foreach (var model in Renderer.Models)
            {
                model.Transform = this.Transform.TransformMatrix;
                model.RenderMeshesPicking(color);
            }
        }

        public override void DrawModel(GLContext context, Pass pass)
        {
            if (pass != Pass.OPAQUE || Renderer == null)
                return;

            //Setup the debug render data
            PrepareDebugShading();

            for (int i = 0; i < Skeletons.Count; i++)
            {
                if (Renderer.Models.Count <= i)
                    break;

                var skel = Skeletons[i];
                if (Renderer.Models[i].SkeletalAnim != null)
                {
                    var skelAnim = Renderer.Models[i].SkeletalAnim.FrameSkeleton;
                    //Only update when the data has been set
                    if (skelAnim.Length > 0 && skelAnim[0].Scale != OpenTK.Vector3.Zero)
                    {
                        for (int j = 0; j < skel.Bones.Count; j++)
                        {
                            if (skelAnim.Length <= j)
                                continue;

                            skel.Bones[j].BoneData.AnimationController.Position = skelAnim[j].Translation;
                            skel.Bones[j].BoneData.AnimationController.Rotation = skelAnim[j].Rotation;
                            skel.Bones[j].BoneData.AnimationController.Scale = skelAnim[j].Scale;
                        }
                        skel.Update();
                    }
                }
            }

            //Setup the camera
            Renderer.Camera.ProjectionMatrix = context.Camera.ProjectionMatrix;
            Renderer.Camera.ViewMatrix = context.Camera.ViewMatrix;
            Renderer.Camera.Translation = context.Camera.TargetPosition;

            foreach (var model in Renderer.Models)
                model.Transform = this.Transform.TransformMatrix;

            //Draw the models
            if (InFrustum)
                Renderer.Render(this.IsSelected);

            //bounding box debugging

          //  StandardMaterial mat = new StandardMaterial();
           // mat.Render(context);

         //   BoundingBoxRender.Draw(context, bounding.Box);

            /*    foreach (var model in Scene.Models)
                {
                    foreach (var mesh in model.Meshes)
                    {
                        if (!mesh.IsSelected)
                            continue;

                        foreach (var usd in mesh.MetaData)
                        {
                            if (usd.Type == H3DMetaDataType.BoundingBox)
                            {
                                var v = (H3DBoundingBox)usd.Values[0];
                                var min = v.Center - v.Size;
                                var max = v.Center + v.Size;

                                StandardMaterial mat = new StandardMaterial();
                                mat.Render(GLContext.ActiveContext);

                                BoundingBoxRender.Draw(GLContext.ActiveContext,
                                    new OpenTK.Vector3(min.X, min.Y, min.Z),
                                    new OpenTK.Vector3(max.X, max.Y, max.Z));
                            }
                        }
                    }
                }*/

            //Draw the skeleton
            foreach (var skeleton in Skeletons)
                skeleton.DrawModel(context, pass);

            //Reset depth state to defaults
            GL.DepthMask(true);
            GL.ColorMask(true, true, true, true);
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.CullFace);
            GL.CullFace(CullFaceMode.Back);
            GL.Disable(EnableCap.StencilTest);
            GL.Disable(EnableCap.Blend);
        }

        private void PrepareDebugShading()
        {
            //Todo. Would be better to have 3ds specific debugging modes than the in tool ones.
            Renderer.DebugShadingMode = (int)DebugShaderRender.DebugRendering;
            //Selected bone for debug rendering weights
            Renderer.SelectedBoneID = Toolbox.Core.Runtime.SelectedBoneIndex;
        }
    }
}
