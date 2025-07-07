using CtrLibrary.Rendering;
using GLFrameworkEngine;
using ImGuiNET;
using MapStudio.UI;
using Newtonsoft.Json;
using SPICA.Formats.CtrH3D.Light;
using SPICA.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Toolbox.Core.ViewModels;
using static Toolbox.Core.IO.STFileLoader;

namespace CtrLibrary.UI
{
    /// <summary>
    /// UI for configuring the scene lighting along with drawing a light instance.
    /// </summary>
    internal class SceneLightingUI
    {
        /// <summary>
        /// A sprite for displaying light sources in 3D view.
        /// </summary>
        public class LightPreview : IDrawable
        {
            public Light Light { get; set; }

            public bool IsVisible
            {
                get { return Light.Enabled; }
                set { }
            }

            SpriteDrawer Model;

            public LightPreview(Light light)
            {
                Light = light;
                Model = new SpriteDrawer(2);
                Model.XRay = false;

                if (!IconManager.HasIcon("POINT_LIGHT"))
                    IconManager.AddIcon("POINT_LIGHT", GLTexture2D.FromBitmap(Resources.Pointlight).ID);

                Model.TextureID = IconManager.GetTextureIcon("POINT_LIGHT");
            }

            public void DrawModel(GLContext control, Pass pass)
            {
                if (pass != Pass.TRANSPARENT)
                    return;

                Model.Transform.Position = new OpenTK.Vector3(
                    Light.Position.X, Light.Position.Y, Light.Position.Z);
                Model.Transform.UpdateMatrix(true);
                Model.DrawModel(control);
            }

            public void Dispose()
            {
            }
        }

        public static NodeBase Setup(H3DRender render)
        {

            var lightNode = new NodeBase("Scene Light")
            {
                Tag = SceneLightConfig.Current.Light,
                Icon = IconManager.LIGHT_ICON.ToString(),
            };
            lightNode.TagUI.UIDrawer += delegate
            {
                var light = SceneLightConfig.Current.Light;

                bool update = false;

                if (ImGui.Button("Save Settings"))
                    SceneLightConfig.Current.Save();

                if (ImGui.BeginCombo("Presets", SceneLightConfig.Current.Name))
                {
                    foreach (var preset in SceneLightConfig.Presets)
                    {
                        bool selected = SceneLightConfig.Current == preset;
                        if (ImGui.Selectable(preset.Name, selected))
                        {
                            SceneLightConfig.Current.Copy(preset);
                            SceneConfig.Current.LightPreset = preset.Name;
                            SceneConfig.Current.Save();

                            GLContext.ActiveContext.UpdateViewport = true;
                            render.UpdateAllUniforms();
                        }
                        if (selected)
                            ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }

                if (ImGui.CollapsingHeader("Hemi Lighting", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    update |= ImGui.ColorEdit3("Sky Color", ref Renderer.GlobalHsLSCol);
                    update |= ImGui.ColorEdit3("Ground Color", ref Renderer.GlobalHsLGCol);
                }
                if (ImGui.CollapsingHeader("Lighting", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    update |= ImGui.Checkbox("Enabled", ref light.Enabled);
                    update |= ImGui.Checkbox("TwoSidedDiffuse", ref light.TwoSidedDiffuse);

                    BcresUIHelper.DrawEnum("Type", ref light.Type, () => { update = true; });
                    update |= ImGui.DragFloat3("Position", ref light.Position);
                    update |= ImGui.DragFloat3("Direction", ref light.Direction);

                    update |= ImGui.ColorEdit4("Diffuse", ref light.Diffuse, ImGuiColorEditFlags.NoInputs); ImGui.SameLine();
                    update |= ImGui.ColorEdit4("Specular0", ref light.Specular0, ImGuiColorEditFlags.NoInputs); ImGui.SameLine();
                    update |= ImGui.ColorEdit4("Specular1", ref light.Specular1, ImGuiColorEditFlags.NoInputs);
                    update |= ImGui.ColorEdit4("Ambient", ref light.Ambient, ImGuiColorEditFlags.NoInputs);
                }
                if (update)
                {
                    GLContext.ActiveContext.UpdateViewport = true;
                    render.UpdateAllUniforms();
                }
            };
            return lightNode;
        }

        static bool EditColor(string label, ref OpenTK.Graphics.Color4 color)
        {
            var diffuse = new Vector4(color.R, color.G, color.B, color.A);
            if (ImGui.ColorEdit4(label, ref diffuse, ImGuiColorEditFlags.NoInputs))
            {
                color = new OpenTK.Graphics.Color4(diffuse.X, diffuse.Y, diffuse.Z, diffuse.W);
                return true;
            }
            return false;
        }

        static bool EditVec3(string label, ref OpenTK.Vector3 v)
        {
            var vec = new Vector3(v.X, v.Y, v.Z);
            if (ImGui.DragFloat3(label, ref vec, 0.1f))
            {
                v = new OpenTK.Vector3(vec.X, vec.Y, vec.Z);
                return true;
            }
            return false;
        }
    }
}
