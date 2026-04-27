using System;
using System.Text;

namespace SpaceDNA.Core
{
    /// <summary>
    /// Simplified agent genome for MVP.
    /// Uses tag-based structure: <b>body</b>, <m>mind</m>, <l>legs</l>
    /// </summary>
    public class AgentGenome
    {
        /// <summary>Raw DNA string with tags</summary>
        public string DNA { get; private set; }
        
        // Parsed traits
        public float Speed { get; private set; } = 1.0f;
        public float Metabolism { get; private set; } = 0.01f;
        public float ColdResistance { get; private set; } = 0.5f;
        public float HeatResistance { get; private set; } = 0.5f;
        public float WaterAffinity { get; private set; } = 0.0f;
        public float VisionRange { get; private set; } = 0.1f;
        public float aggression { get; private set; } = 0.0f;
        public int LegCount { get; private set; } = 4;
        public bool HasMind { get; private set; } = true;
        public float Size { get; private set; } = 1.0f;
        
        /// <summary>Energy cost per second</summary>
        public float EnergyCost => Metabolism * Size * (HasMind ? 1.5f : 1.0f) * (1 + LegCount * 0.1f);
        
        /// <summary>Random seed for this genome</summary>
        public int Seed { get; private set; }
        
        public AgentGenome(string? dna = null)
        {
            DNA = dna ?? GenerateRandomDNA();
            Seed = new Random().Next();
            ParseDNA();
        }
        
        /// <summary>
        /// Generate a basic viable DNA string
        /// </summary>
        private string GenerateRandomDNA()
        {
            var rnd = new Random();
            var sb = new StringBuilder();
            
            // Body
            sb.Append("<b>");
            
            // Legs (1-8)
            int legs = 1 + rnd.Next(8);
            sb.Append($"<l>{legs}</l>");
            
            // Mind (optional)
            if (rnd.NextDouble() > 0.3)
            {
                sb.Append("<m></m>");
            }
            
            // Vision
            sb.Append($"<v>{rnd.Next(1, 10)}</v>");
            
            // End body
            sb.Append("</b>");
            
            return sb.ToString();
        }
        
        /// <summary>
        /// Parse DNA string and extract traits
        /// </summary>
        private void ParseDNA()
        {
            var rnd = new Random(Seed);
            
            // Count legs
            int lStart = DNA.IndexOf("<l>");
            int lEnd = DNA.IndexOf("</l>");
            if (lStart >= 0 && lEnd > lStart)
            {
                string legStr = DNA.Substring(lStart + 3, lEnd - lStart - 3);
                if (int.TryParse(legStr, out int legs))
                {
                    LegCount = Math.Clamp(legs, 0, 20);
                }
            }
            
            // Check for mind
            HasMind = DNA.Contains("<m") && DNA.Contains("</m>");
            
            // Vision range
            int vStart = DNA.IndexOf("<v>");
            int vEnd = DNA.IndexOf("</v>");
            if (vStart >= 0 && vEnd > vStart)
            {
                string vStr = DNA.Substring(vStart + 3, vEnd - vStart - 3);
                if (int.TryParse(vStr, out int vis))
                {
                    VisionRange = Math.Clamp(vis, 1, 10) * 0.1f;
                }
            }
            
            // Calculate derived stats
            Speed = 0.5f + LegCount * 0.1f + (float)rnd.NextDouble() * 0.3f;
            Metabolism = 0.005f + (float)rnd.NextDouble() * 0.02f;
            ColdResistance = 0.3f + (float)rnd.NextDouble() * 0.4f;
            HeatResistance = 0.3f + (float)rnd.NextDouble() * 0.4f;
            WaterAffinity = (float)rnd.NextDouble() * 0.5f;
            Size = 0.8f + (float)rnd.NextDouble() * 0.6f;
            aggression = (float)rnd.NextDouble() * 0.3f;
        }
        
        /// <summary>
        /// Create a mutated copy of this genome
        /// </summary>
        public AgentGenome Mutate(float strength = 0.1f)
        {
            var rnd = new Random();
            var newDna = new StringBuilder(DNA);
            
            // Small chance to add/remove/modify tags
            if (rnd.NextDouble() < strength)
            {
                // Mutate leg count
                int lStart = newDna.ToString().IndexOf("<l>");
                int lEnd = newDna.ToString().IndexOf("</l>");
                if (lStart >= 0 && lEnd > lStart)
                {
                    string legStr = newDna.ToString().Substring(lStart + 3, lEnd - lStart - 3);
                    if (int.TryParse(legStr, out int legs))
                    {
                        legs = Math.Clamp(legs + (rnd.NextDouble() > 0.5 ? 1 : -1), 0, 20);
                        newDna.Remove(lStart + 3, lEnd - lStart - 3);
                        newDna.Insert(lStart + 3, legs.ToString());
                    }
                }
            }
            
            var result = new AgentGenome(newDna.ToString());
            result.Seed = Seed + rnd.Next(1000);
            return result;
        }
        
        /// <summary>
        /// Crossbreed two genomes (20% chance of hybrid traits)
        /// </summary>
        public static AgentGenome Crossbreed(AgentGenome a, AgentGenome b)
        {
            var rnd = new Random();
            var childDna = new StringBuilder();
            
            // Take body from one parent
            childDna.Append("<b>");
            
            // Legs: 20% chance to take from other parent, or average
            int legs;
            if (rnd.NextDouble() < 0.2)
            {
                legs = rnd.NextDouble() > 0.5 ? a.LegCount : b.LegCount;
            }
            else
            {
                legs = (a.LegCount + b.LegCount) / 2;
            }
            childDna.Append($"<l>{legs}</l>");
            
            // Mind: inherit if either parent has it
            if (a.HasMind || b.HasMind)
            {
                childDna.Append("<m></m>");
            }
            
            // Vision: average or random
            int vision = (int)((a.VisionRange + b.VisionRange) * 5);
            childDna.Append($"<v>{vision}</v>");
            
            childDna.Append("</b>");
            
            return new AgentGenome(childDna.ToString());
        }
        
        public override string ToString()
        {
            return $"DNA: {DNA} | Legs:{LegCount} Mind:{HasMind} Speed:{Speed:F2} Cost:{EnergyCost:F3}";
        }
    }
}