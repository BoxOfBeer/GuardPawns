using System.Text.Json.Serialization;

namespace SpaceDNA.Core
{
    /// <summary>
    /// Planet configuration loaded from JSON file.
    /// </summary>
    public class PlanetConfig
    {
        [JsonPropertyName("seed")]
        public int Seed { get; set; } = 12345;
        
        [JsonPropertyName("geologicActivity")]
        public float GeologicActivity { get; set; } = 0.4f;
        
        [JsonPropertyName("noiseOctaves")]
        public int NoiseOctaves { get; set; } = 4;
        
        [JsonPropertyName("noiseFrequency")]
        public float NoiseFrequency { get; set; } = 0.1f;
        
        [JsonPropertyName("temperature")]
        public float Temperature { get; set; } = 0.5f;
        
        [JsonPropertyName("atmosphere")]
        public float Atmosphere { get; set; } = 0.5f;
        
        [JsonPropertyName("density")]
        public float Density { get; set; } = 1.0f;
        
        [JsonPropertyName("radius")]
        public float Radius { get; set; } = 5.0f;
        
        [JsonPropertyName("displacementScale")]
        public float DisplacementScale { get; set; } = 0.3f;
        
        [JsonPropertyName("segments")]
        public int Segments { get; set; } = 256;
        
        [JsonPropertyName("starCount")]
        public int StarCount { get; set; } = 500;
        
        [JsonPropertyName("volume")]
        public float Volume { get; set; } = 0.3f;
        
        [JsonPropertyName("mutationSpeed")]
        public float MutationSpeed { get; set; } = 30f; // seconds for transition
        
        [JsonPropertyName("autoMutationInterval")]
        public float AutoMutationInterval { get; set; } = 0f; // 0 = disabled
        
        [JsonPropertyName("mutationFields")]
        public int MutationFields { get; set; } = 3; // how many fields to mutate

        /// <summary>
        /// Convert config to Genome
        /// </summary>
        public Genome ToGenome()
        {
            return new Genome
            {
                Seed = Seed,
                GeologicActivity = GeologicActivity,
                NoiseOctaves = NoiseOctaves,
                NoiseFrequency = NoiseFrequency,
                Temperature = Temperature,
                Atmosphere = Atmosphere,
                Density = Density
            };
        }

        /// <summary>
        /// Update config from Genome
        /// </summary>
        public void FromGenome(Genome g)
        {
            Seed = g.Seed;
            GeologicActivity = g.GeologicActivity;
            NoiseOctaves = g.NoiseOctaves;
            NoiseFrequency = g.NoiseFrequency;
            Temperature = g.Temperature;
            Atmosphere = g.Atmosphere;
            Density = g.Density;
        }

        /// <summary>
        /// Load config from JSON file with validation
        /// </summary>
        public static PlanetConfig? Load(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return null;
                    
                string json = File.ReadAllText(path);
                var config = System.Text.Json.JsonSerializer.Deserialize<PlanetConfig>(json);
                
                // Validate and clamp values
                if (config != null)
                {
                    config.Seed = Math.Max(0, config.Seed);
                    config.GeologicActivity = Math.Clamp(config.GeologicActivity, 0f, 2f);
                    config.NoiseOctaves = Math.Clamp(config.NoiseOctaves, 1, 8);
                    config.NoiseFrequency = Math.Clamp(config.NoiseFrequency, 0.001f, 10f);
                    config.Temperature = Math.Clamp(config.Temperature, 0f, 1f);
                    config.Atmosphere = Math.Clamp(config.Atmosphere, 0f, 1f);
                    config.Density = Math.Clamp(config.Density, 0.1f, 5f);
                    config.Radius = Math.Clamp(config.Radius, 0.1f, 100f);
                    if (config.DisplacementScale <= 0f) config.DisplacementScale = 0.3f;
                    config.DisplacementScale = Math.Clamp(config.DisplacementScale, 0.1f, 10f);
                    config.Segments = Math.Clamp(config.Segments, 32, 1024);
                    config.StarCount = Math.Clamp(config.StarCount, 10, 10000);
                    config.Volume = Math.Clamp(config.Volume, 0f, 1f);
                    config.MutationSpeed = Math.Clamp(config.MutationSpeed, 1f, 300f);
                    config.AutoMutationInterval = Math.Clamp(config.AutoMutationInterval, 0f, 600f);
                    config.MutationFields = Math.Clamp(config.MutationFields, 1, 7);
                }
                
                return config;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading config: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Save config to JSON file
        /// </summary>
        public void Save(string path)
        {
            var options = new System.Text.Json.JsonSerializerOptions 
            { 
                WriteIndented = true 
            };
            string json = System.Text.Json.JsonSerializer.Serialize(this, options);
            File.WriteAllText(path, json);
        }
    }
}
