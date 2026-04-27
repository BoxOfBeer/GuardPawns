using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SpaceDNA.Core
{
    /// <summary>
    /// Interprets raw DNA sequence into creature traits (PawnGenome).
    /// Pattern rules (from design): 1=arms, 2=legs, 3=torso, 4=head, 5=eyes, 6=nose, 7=ears.
    /// Format: tokens separated by '-', e.g. "11-22-22-3-4-55-6-77". (token) = sleeping/inactive.
    /// Overlapping and order matter: same symbol in different context can mean different things.
    /// </summary>
    public static class DnaInterpreter
    {
        /// <summary>Token codes: 1=arms, 2=legs, 3=torso, 4=head, 5=eyes, 6=nose, 7=ears, 8=tail/balance, 9=coat/insulation</summary>
        public const string TokenChars = "123456789";

        /// <summary>
        /// Parse raw sequence into active segments. Returns list of active token chars (e.g. "2","2","3","4","5","5","6","7","7").
        /// Segments in parentheses are sleeping and excluded from phenotype.
        /// </summary>
        public static List<char> ParseActiveSegments(string rawSequence)
        {
            var active = new List<char>();
            if (string.IsNullOrWhiteSpace(rawSequence)) return active;

            // Normalize: split by dash, comma, or space; strip parentheses for "sleeping"
            string normalized = rawSequence.Trim();
            foreach (char c in new[] { '-', ',', ' ', '\t' })
                normalized = normalized.Replace(c, '-');
            var parts = normalized.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string part in parts)
            {
                string seg = part.Trim();
                bool sleeping = seg.StartsWith('(') && seg.EndsWith(')');
                if (sleeping)
                    seg = seg.TrimStart('(').TrimEnd(')').Trim();
                foreach (char c in seg)
                {
                    if (TokenChars.Contains(c) && !sleeping)
                        active.Add(c);
                }
            }

            return active;
        }

        /// <summary>
        /// Count occurrences of a character in active segments.
        /// </summary>
        private static int CountToken(List<char> active, char token)
        {
            return active.Count(c => c == token);
        }

        /// <summary>
        /// Check if token is present in active segments.
        /// </summary>
        private static bool HasToken(List<char> active, char token)
        {
            return active.Any(c => c == token);
        }

        /// <summary>True if active list contains a contiguous subsequence (e.g. 3,2,2).</summary>
        private static bool HasSubsequence(List<char> active, char a, char b, char c)
        {
            for (int i = 0; i + 2 < active.Count; i++)
                if (active[i] == a && active[i + 1] == b && active[i + 2] == c) return true;
            return false;
        }

        private static bool HasSubsequence(List<char> active, char a, char b)
        {
            for (int i = 0; i + 1 < active.Count; i++)
                if (active[i] == a && active[i + 1] == b) return true;
            return false;
        }

        /// <summary>
        /// Express raw DNA into a PawnGenome (phenotype). Uses seed for deterministic variation where needed.
        /// Tokens 8=tail/balance (speed bonus), 9=coat (cold/heat resistance). Context: 3-2-2 runner, 5-5-4 sharp vision.
        /// </summary>
        public static PawnGenome Express(string rawSequence, int seed = 0)
        {
            var active = ParseActiveSegments(rawSequence);
            var rnd = seed != 0 ? new Random(seed) : new Random();

            // Legs (2): count active "2" -> LegCount
            int legCount = CountToken(active, '2');
            if (legCount == 0) legCount = 1;
            legCount = Math.Clamp(legCount, WorldConstants.MinLegs, WorldConstants.MaxLegs);

            // Head (4): presence -> HasMind
            bool hasMind = HasToken(active, '4');

            // Eyes (5): count -> VisionRange (more eyes = better vision, with saturation)
            int eyeCount = CountToken(active, '5');
            float visionRange = WorldConstants.DefaultVisionRange + (float)(1.0 - Math.Exp(-eyeCount * 0.08)) * 0.4f;
            visionRange = Math.Clamp(visionRange, 0.05f, 0.5f);
            if (HasSubsequence(active, '5', '5', '4')) visionRange += 0.06f; // 5-5-4 sharp vision bonus

            // Ears (7): presence -> CanSeePredators
            bool canSeePredators = HasToken(active, '7');

            // Nose (6): count -> WaterAffinity (sensory)
            int noseCount = CountToken(active, '6');
            float waterAffinity = WorldConstants.DefaultWaterAffinity + noseCount * 0.05f;
            waterAffinity = Math.Clamp(waterAffinity, 0f, 1f);

            // Torso (3): count -> Size with saturation (more body mass, diminishing returns)
            int torsoCount = Math.Max(1, CountToken(active, '3'));
            float size = WorldConstants.DefaultSize + (float)(1.0 - Math.Exp(-(torsoCount - 1) * 0.25)) * 0.8f;
            size = Math.Clamp(size, WorldConstants.MinSize, WorldConstants.MaxSize);

            // Arms (1) + Legs (2): Speed from limbs with synergy (arms+legs together give extra)
            int armCount = CountToken(active, '1');
            float armBonus = armCount * 0.12f;
            float legBonus = (legCount - 1) * 0.08f;
            float synergy = (armCount >= 1 && legCount >= 2) ? 0.1f : 0f;
            float speed = WorldConstants.DefaultSpeed + armBonus + legBonus + synergy;
            if (HasSubsequence(active, '3', '2', '2')) speed += 0.08f; // 3-2-2 runner bonus
            int tailCount = CountToken(active, '8');
            speed += tailCount * 0.05f; // 8=tail/balance
            speed = Math.Clamp(speed, 0.1f, 3f);

            // Coat (9): cold/heat resistance
            int coatCount = CountToken(active, '9');
            float coatBonus = (float)(1.0 - Math.Exp(-coatCount * 0.2)) * 0.25f;

            // Metabolism: higher with more parts
            float metabolism = WorldConstants.BaseMetabolism + (legCount * 0.02f) + (hasMind ? 0.1f : 0f);
            metabolism = Math.Clamp(metabolism, 0.1f, 2f);

            // Resistances: base + coat + random spread
            float coldResistance = WorldConstants.DefaultColdResistance + coatBonus + (float)(rnd.NextDouble() - 0.5) * 0.2f;
            float heatResistance = WorldConstants.DefaultHeatResistance + coatBonus + (float)(rnd.NextDouble() - 0.5) * 0.2f;
            coldResistance = Math.Clamp(coldResistance, 0f, 1f);
            heatResistance = Math.Clamp(heatResistance, 0f, 1f);

            float aggression = 0.3f + (hasMind ? 0.2f : 0f) + (float)rnd.NextDouble() * 0.2f;
            aggression = Math.Clamp(aggression, 0f, 1f);
            float intelligence = hasMind ? (float)rnd.NextDouble() * 0.3f : 0f;

            bool canFly = (armCount >= 2 && tailCount >= 1) || HasSubsequence(active, '8', '8');
            return PawnGenome.FromTraits(
                speed, metabolism, coldResistance, heatResistance,
                waterAffinity, size, hasMind, aggression,
                visionRange, intelligence, legCount, canFly,
                canSeeFood: true, canSeePredators,
                rawDnaDisplay: rawSequence
            );
        }

        /// <summary>
        /// Validate that sequence contains at least one active segment (viable genome).
        /// </summary>
        public static bool IsViable(string rawSequence)
        {
            var active = ParseActiveSegments(rawSequence);
            return active.Count >= 3; // at least torso + something
        }

        /// <summary>
        /// True if string looks like raw DNA (digits, dashes, parentheses) not tag-based DNA.
        /// </summary>
        public static bool LooksLikeRawDna(string? dna)
        {
            if (string.IsNullOrEmpty(dna)) return false;
            if (dna.IndexOf('<') >= 0) return false; // tag format
            return dna.Any(char.IsDigit);
        }
    }
}
