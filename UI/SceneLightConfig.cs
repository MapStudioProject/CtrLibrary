using Discord;
using Newtonsoft.Json;
using SPICA.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace CtrLibrary.UI
{
    public class SceneLightConfig
    {
        public static SceneLightConfig Current = new SceneLightConfig();
        public static List<SceneLightConfig> Presets = new List<SceneLightConfig>();

        private const string _folder = "LightConfigs";

        // Settings will be saved directly here instead
        public Light Light { get; set; } = new Light();

        public string Name { get; set; } = "Default";

        public SceneLightConfig() {
            Light = new Light()
            {
                Ambient = new Vector4(0.1f, 0.1f, 0.1f, 1.0f),
                Diffuse = new Vector4(1, 1, 1, 1.0f),
                Specular0 = new Vector4(0.3f, 0.3f, 0.3f, 1.0f),
                Specular1 = new Vector4(0.4f, 0.4f, 0.4f, 1.0f),
                TwoSidedDiffuse = true,
                Position = new Vector3(0, 0, 0),
                Enabled = true,
                Type = LightType.PerFragment,
            };
        }
        public SceneLightConfig(string filePath)
            => Load(filePath);

        public static void LoadDefault()
        {
            // Check if already loaded
            if (Presets.Count > 0) return;

            // Ensure the scene config is set which decides our active preset
            SceneConfig.Current.Load();

            Directory.CreateDirectory(_folder);
            foreach (var file in Directory.GetFiles(_folder, "*.json"))
                Presets.Add(new SceneLightConfig(file));

            if (Presets.Count == 0)
            {
                // Save atleast one default preset
                Current.Save();
            }

            // There should only be one set here
            foreach (var preset in Presets.Where(x => x.Name == SceneConfig.Current.LightPreset))
                Current.Copy(preset);
        }

        public void Load(string filePath)
        {
            this.Name = Path.GetFileNameWithoutExtension(filePath);
            var config = JsonConvert.DeserializeObject<SceneLightConfig>(File.ReadAllText(filePath));
            // Rather than setting the light instance, update the existing as the current is already set in renderer
            CopyLight(this.Light, config.Light);
        }

        public void Copy(SceneLightConfig config)
        {
            this.Name = config.Name;
            CopyLight(this.Light, config.Light);
        }

        public void Save()
        {
            Directory.CreateDirectory(_folder);
            Save(Path.Combine(_folder, $"{this.Name}.json"));
        }

        public void Save(string filePath) {
            this.Name = Path.GetFileNameWithoutExtension(filePath);
            string json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(filePath, json);
        }

        private void CopyLight(Light dst, Light src)
        {
            // Transfers config to light
            dst.Position = src.Position;
            dst.Direction = src.Direction;
            dst.Ambient = src.Ambient;
            dst.Diffuse = src.Diffuse;
            dst.Specular0 = src.Specular0;
            dst.Specular1 = src.Specular1;

            dst.Type = src.Type;

            dst.AngleLUTInput = src.AngleLUTInput;
            dst.AngleLUTScale = src.AngleLUTScale;

            dst.AttenuationScale = src.AttenuationScale;
            dst.AttenuationBias = src.AttenuationBias;

            dst.AngleLUTTableName = src.AngleLUTTableName;
            dst.AngleLUTSamplerName = src.AngleLUTSamplerName;

            dst.DistanceLUTTableName = src.DistanceLUTTableName;
            dst.DistanceLUTSamplerName = src.DistanceLUTSamplerName;

            dst.Enabled = src.Enabled;
            dst.DistAttEnabled = src.DistAttEnabled;
            dst.TwoSidedDiffuse = src.TwoSidedDiffuse;
            dst.Directional = src.Directional;
        }
    }

    public class SceneConfig
    {
        private const string _filePath = "SceneConfig.json";

        public static SceneConfig Current = new SceneConfig();

        public string LightPreset = "Default";

        public void Load()
        {
            if (!File.Exists(_filePath))
                return;

            var config = JsonConvert.DeserializeObject<SceneConfig>(File.ReadAllText(_filePath));
            LightPreset = config.LightPreset;
        }

        public void Save()
        {
            string json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(_filePath, json);
        }
    }
}
