using System;

namespace SpaceDNA.Core
{
    /// <summary>
    /// Genome parameters for procedural planet generation.
    /// Contains all the genetic information that determines a planet's characteristics.
    /// </summary>
    public class Genome
    {
        /// <summary>Random seed for reproducible generation</summary>
        public int Seed { get; set; } = 42;

        /// <summary>Geologic activity level - affects mountain height/roughness and volcanoes</summary>
        public float GeologicActivity { get; set; } = 1.0f;

        /// <summary>Number of noise octaves for detail level</summary>
        public int NoiseOctaves { get; set; } = 4;

        /// <summary>Base frequency for noise - higher = more peaks</summary>
        public float NoiseFrequency { get; set; } = 1.0f;

        /// <summary>Planet temperature: 0.0 = frozen/ice world, 0.5 = Earth-like, 1.0 = molten/volcanic</summary>
        public float Temperature { get; set; } = 0.5f;

        /// <summary>Atmosphere density: 0.0 = none, 0.5 = thin, 1.0 = thick haze</summary>
        public float Atmosphere { get; set; } = 0.5f;

        /// <summary>Planet density: affects surface gravity appearance (not visual in this demo)</summary>
        public float Density { get; set; } = 1.0f;

        /// <summary>
        /// Creates a genome with default parameters.
        /// </summary>
        public static Genome FromDefaults()
        {
            return new Genome();
        }

        /// <summary>
        /// Creates a random genome with sensible defaults.
        /// </summary>
        public static Genome Random()
        {
            var random = new Random();
            return new Genome
            {
                Seed = random.Next(),
                GeologicActivity = 0.5f + (float)random.NextDouble() * 1.5f,
                NoiseOctaves = 3 + random.Next(4), // 3-6
                NoiseFrequency = 0.5f + (float)random.NextDouble() * 3f,
                Temperature = (float)random.NextDouble(),
                Atmosphere = (float)random.NextDouble(),
                Density = 0.5f + (float)random.NextDouble() * 1.5f
            };
        }
    }
}
