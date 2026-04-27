using System;
using System.Text;

namespace SpaceDNA.Core
{
    /// <summary>
    /// Pawn genome - tag-based DNA structure.
    /// Tags: <b>body</b>, <m>mind</m>, <l>legs</l>, <v>vision</v>
    /// </summary>
    public class PawnGenome
    {
        // Body parameters (physical traits)
        public float Speed { get; private set; }
        public float Metabolism { get; private set; }
        public float ColdResistance { get; private set; }
        public float HeatResistance { get; private set; }
        public float WaterAffinity { get; private set; }
        public float Size { get; private set; }
        
        // Mind parameters (mental traits)
        public bool HasMind { get; private set; }
        public float Aggression { get; private set; }
        public float VisionRange { get; private set; }
        public float Intelligence { get; private set; }
        
        // Legs (movement appendages)
        public int LegCount { get; private set; }
        
        /// <summary>Может игнорировать рельеф (вода, крутизна) при движении. Задаётся из ДНК/черт.</summary>
        public bool CanFly { get; private set; }
        
        // Vision (sensory)
        public bool CanSeeFood { get; private set; }
        public bool CanSeePredators { get; private set; }
        
        /// <summary>
        /// DNA string representation (tag-based).
        /// </summary>
        public string DNA { get; private set; }
        
        public PawnGenome()
        {
            Speed = WorldConstants.DefaultSpeed;
            Metabolism = WorldConstants.BaseMetabolism;
            ColdResistance = WorldConstants.DefaultColdResistance;
            HeatResistance = WorldConstants.DefaultHeatResistance;
            WaterAffinity = WorldConstants.DefaultWaterAffinity;
            Size = WorldConstants.DefaultSize;
            
            HasMind = false;
            Aggression = 0.5f;
            VisionRange = WorldConstants.DefaultVisionRange;
            Intelligence = 0f;
            
            LegCount = WorldConstants.DefaultLegCount;
            CanFly = false;
            
            CanSeeFood = true;
            CanSeePredators = false;
            
            DNA = GenerateDNA();
        }
        
        private PawnGenome(
            float speed, float metabolism, float coldResistance, float heatResistance,
            float waterAffinity, float size, bool hasMind, float aggression, 
            float visionRange, float intelligence, int legCount, bool canFly, bool canSeeFood, bool canSeePredators,
            string? rawDnaOverride = null)
        {
            Speed = Math.Clamp(speed, 0.1f, 3f);
            Metabolism = Math.Clamp(metabolism, 0.1f, 2f);
            ColdResistance = Math.Clamp(coldResistance, 0f, 1f);
            HeatResistance = Math.Clamp(heatResistance, 0f, 1f);
            WaterAffinity = Math.Clamp(waterAffinity, 0f, 1f);
            Size = Math.Clamp(size, WorldConstants.MinSize, WorldConstants.MaxSize);
            
            HasMind = hasMind;
            Aggression = Math.Clamp(aggression, 0f, 1f);
            VisionRange = Math.Clamp(visionRange, 0.05f, 0.5f);
            Intelligence = Math.Clamp(intelligence, 0f, 1f);
            
            LegCount = Math.Clamp(legCount, WorldConstants.MinLegs, WorldConstants.MaxLegs);
            CanFly = canFly;
            
            CanSeeFood = canSeeFood;
            CanSeePredators = canSeePredators;
            
            DNA = rawDnaOverride ?? GenerateDNA();
        }

        /// <summary>
        /// Create genome from raw DNA sequence (pattern-based interpreter).
        /// Uses seed for slight per-individual variation. Returns null if sequence is not viable.
        /// </summary>
        public static PawnGenome? FromRawDna(string rawSequence, int seed = 0)
        {
            if (!DnaInterpreter.IsViable(rawSequence)) return null;
            return DnaInterpreter.Express(rawSequence, seed);
        }

        /// <summary>
        /// Create genome from explicit traits (e.g. after DNA interpretation).
        /// If rawDnaDisplay is set, it is used as DNA string for display/export.
        /// </summary>
        public static PawnGenome FromTraits(
            float speed, float metabolism, float coldResistance, float heatResistance,
            float waterAffinity, float size, bool hasMind, float aggression,
            float visionRange, float intelligence, int legCount, bool canFly, bool canSeeFood, bool canSeePredators,
            string? rawDnaDisplay = null)
        {
            return new PawnGenome(speed, metabolism, coldResistance, heatResistance,
                waterAffinity, size, hasMind, aggression, visionRange, intelligence,
                legCount, canFly, canSeeFood, canSeePredators, rawDnaDisplay);
        }
        
        /// <summary>
        /// Generate tag-based DNA string.
        /// </summary>
        private string GenerateDNA()
        {
            var sb = new StringBuilder();
            
            // Body tag
            sb.Append($"<b s{Speed:F1} m{Metabolism:F1} c{ColdResistance:F1} h{HeatResistance:F1} w{WaterAffinity:F1} z{Size:F1}/>");
            
            // Mind tag (optional)
            if (HasMind)
            {
                sb.Append($"<m a{Aggression:F1} v{VisionRange:F1} i{Intelligence:F1}/>");
            }
            
            // Legs tag
            sb.Append($"<l {LegCount}/>");
            
            // Vision tag
            sb.Append($"<v f{(CanSeeFood ? 1 : 0)} p{(CanSeePredators ? 1 : 0)}/>");
            
            return sb.ToString();
        }
        
        /// <summary>
        /// Create mutated copy of this genome.
        /// </summary>
        public PawnGenome Mutate(float mutationRate)
        {
            var rnd = new Random();
            
            float MutateValue(float value, float max, float min = 0f)
            {
                if (rnd.NextDouble() < mutationRate)
                {
                    float delta = (float)(rnd.NextDouble() - 0.5) * 0.2f;
                    return value + delta;
                }
                return value;
            }
            
            int MutateInt(int value, int max, int min = 0)
            {
                if (rnd.NextDouble() < mutationRate)
                {
                    return value + (rnd.Next(2) * 2 - 1); // +1 or -1
                }
                return value;
            }
            
            bool MutateBool(bool value)
            {
                if (rnd.NextDouble() < mutationRate * 0.3f)
                {
                    return !value;
                }
                return value;
            }
            
            return new PawnGenome(
                speed: MutateValue(Speed, 3f, 0.1f),
                metabolism: MutateValue(Metabolism, 2f, 0.1f),
                coldResistance: MutateValue(ColdResistance, 1f),
                heatResistance: MutateValue(HeatResistance, 1f),
                waterAffinity: MutateValue(WaterAffinity, 1f),
                size: MutateValue(Size, WorldConstants.MaxSize, WorldConstants.MinSize),
                hasMind: MutateBool(HasMind),
                aggression: MutateValue(Aggression, 1f),
                visionRange: MutateValue(VisionRange, 0.5f, 0.05f),
                intelligence: MutateValue(Intelligence, 1f),
                legCount: MutateInt(LegCount, WorldConstants.MaxLegs, WorldConstants.MinLegs),
                canFly: MutateBool(CanFly),
                canSeeFood: MutateBool(CanSeeFood),
                canSeePredators: MutateBool(CanSeePredators)
            );
        }
        
        /// <summary>
        /// Crossbreed two genomes (sexual reproduction).
        /// </summary>
        public static PawnGenome Crossbreed(PawnGenome a, PawnGenome b)
        {
            var rnd = new Random();
            
            float Inherit(float valA, float valB)
            {
                // Mix with some randomness
                float baseVal = rnd.NextDouble() < 0.5 ? valA : valB;
                float mix = (valA + valB) / 2f;
                return rnd.NextDouble() < 0.3 ? mix : baseVal;
            }
            
            int InheritInt(int valA, int valB)
            {
                return rnd.NextDouble() < 0.5 ? valA : valB;
            }
            
            bool InheritBool(bool valA, bool valB)
            {
                return rnd.NextDouble() < 0.5 ? valA : valB;
            }
            
            var child = new PawnGenome(
                speed: Inherit(a.Speed, b.Speed),
                metabolism: Inherit(a.Metabolism, b.Metabolism),
                coldResistance: Inherit(a.ColdResistance, b.ColdResistance),
                heatResistance: Inherit(a.HeatResistance, b.HeatResistance),
                waterAffinity: Inherit(a.WaterAffinity, b.WaterAffinity),
                size: Inherit(a.Size, b.Size),
                hasMind: InheritBool(a.HasMind, b.HasMind),
                aggression: Inherit(a.Aggression, b.Aggression),
                visionRange: Inherit(a.VisionRange, b.VisionRange),
                intelligence: Inherit(a.Intelligence, b.Intelligence),
                legCount: InheritInt(a.LegCount, b.LegCount),
                canFly: InheritBool(a.CanFly, b.CanFly),
                canSeeFood: InheritBool(a.CanSeeFood, b.CanSeeFood),
                canSeePredators: InheritBool(a.CanSeePredators, b.CanSeePredators)
            );
            
            // Small chance of mutation after crossover
            if (rnd.NextDouble() < 0.1)
            {
                return child.Mutate(0.1f);
            }
            
            return child;
        }
        
        /// <summary>
        /// Get fitness score for this genome in given environment.
        /// </summary>
        public float CalculateFitness(float temperature, float atmosphere, float gravity)
        {
            float fitness = 1f;
            
            // Temperature fitness
            if (temperature < WorldConstants.ComfortableTempMin)
            {
                fitness *= ColdResistance;
            }
            else if (temperature > WorldConstants.ComfortableTempMax)
            {
                fitness *= HeatResistance;
            }
            
            // Speed efficiency (faster = better, but uses more energy)
            fitness *= 0.5f + Speed * 0.5f;
            
            // Size survival (medium size is optimal)
            float sizePenalty = Math.Abs(Size - 1f);
            fitness *= 1f - sizePenalty * 0.3f;
            
            // Legs help on land
            fitness *= 1f + LegCount * 0.05f;
            
            // Mind is bonus
            if (HasMind)
            {
                fitness *= 1.2f;
            }
            
            return Math.Clamp(fitness, 0f, 2f);
        }
        
        public override string ToString()
        {
            return DNA;
        }
    }
}