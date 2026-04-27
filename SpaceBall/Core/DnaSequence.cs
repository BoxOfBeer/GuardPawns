using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SpaceDNA.Core
{
    /// <summary>
    /// Raw DNA sequence (species-level). Can mutate at string level, then express into PawnGenome.
    /// Evolution = small safe mutations. Revolution = big edits (player) with high risk.
    /// </summary>
    public class DnaSequence
    {
        /// <summary>Raw sequence e.g. "11-22-22-3-4-55-6-77". (segment) = sleeping.</summary>
        public string Raw { get; }

        public DnaSequence(string raw)
        {
            Raw = string.IsNullOrWhiteSpace(raw) ? DefaultSequence() : Normalize(raw);
        }

        /// <summary>Express into phenotype (PawnGenome).</summary>
        public PawnGenome Express(int seed = 0)
        {
            return DnaInterpreter.Express(Raw, seed);
        }

        /// <summary>Is this sequence viable (at least minimal body)?</summary>
        public bool IsViable => DnaInterpreter.IsViable(Raw);

        /// <summary>Default viable sequence: biped with head and senses.</summary>
        public static string DefaultSequence()
        {
            return "11-22-3-4-55-6-77";
        }

        /// <summary>Generate random viable sequence of given length (segment count). Uses 12-20 segments by default for richer DNA.</summary>
        public static DnaSequence Random(int segmentCount = 14, Random? rnd = null)
        {
            rnd ??= new Random();
            var parts = new List<string>();
            for (int i = 0; i < segmentCount; i++)
            {
                char c = DnaInterpreter.TokenChars[rnd.Next(DnaInterpreter.TokenChars.Length)];
                int repeat = rnd.Next(1, 4); // 1-3 same char
                string seg = new string(c, repeat);
                if (rnd.NextDouble() < 0.15)
                    seg = $"({seg})"; // 15% sleeping
                parts.Add(seg);
            }
            return new DnaSequence(string.Join("-", parts));
        }

        /// <summary>Evolution: small mutation (insert/delete/duplicate one segment, or toggle sleep).</summary>
        public DnaSequence Mutate(float rate = 0.2f, Random? rnd = null)
        {
            rnd ??= new Random();
            var parts = SplitToSegments(Raw).ToList();
            if (parts.Count == 0) return new DnaSequence(DefaultSequence());

            if (rnd.NextDouble() >= rate) return this;

            int action = rnd.Next(7);
            switch (action)
            {
                case 0: // Duplicate one segment
                    int dupIdx = rnd.Next(parts.Count);
                    parts.Insert(dupIdx, parts[dupIdx]);
                    break;
                case 1: // Remove one segment (if enough left)
                    if (parts.Count > 4)
                    {
                        int remIdx = rnd.Next(parts.Count);
                        parts.RemoveAt(remIdx);
                    }
                    break;
                case 2: // Toggle sleeping on one segment
                    int sleepIdx = rnd.Next(parts.Count);
                    string s = parts[sleepIdx];
                    if (s.StartsWith("("))
                        parts[sleepIdx] = s.TrimStart('(').TrimEnd(')');
                    else
                        parts[sleepIdx] = $"({s})";
                    break;
                case 3: // Change one character in a segment
                    int chIdx = rnd.Next(parts.Count);
                    string seg = parts[chIdx].TrimStart('(').TrimEnd(')');
                    if (seg.Length > 0)
                    {
                        int pos = rnd.Next(seg.Length);
                        char newChar = DnaInterpreter.TokenChars[rnd.Next(DnaInterpreter.TokenChars.Length)];
                        seg = seg.Substring(0, pos) + newChar + (pos + 1 < seg.Length ? seg.Substring(pos + 1) : "");
                        parts[chIdx] = parts[chIdx].StartsWith("(") ? $"({seg})" : seg;
                    }
                    break;
                case 4: // Duplicate block of 2-3 segments
                    if (parts.Count >= 3)
                    {
                        int blockLen = Math.Min(2 + rnd.Next(2), parts.Count);
                        int startIdx = rnd.Next(parts.Count - blockLen + 1);
                        for (int i = 0; i < blockLen; i++)
                            parts.Insert(startIdx + blockLen, parts[startIdx + i]);
                    }
                    break;
                case 5: // Remove block of 2 (if enough left)
                    if (parts.Count > 6)
                    {
                        int remStart = rnd.Next(parts.Count - 2);
                        parts.RemoveRange(remStart, 2);
                    }
                    break;
                default: // Insert new random segment
                    char c = DnaInterpreter.TokenChars[rnd.Next(DnaInterpreter.TokenChars.Length)];
                    int insertIdx = rnd.Next(parts.Count + 1);
                    parts.Insert(insertIdx, new string(c, 1 + rnd.Next(2)));
                    break;
            }

            string mutated = JoinSegments(parts);
            return DnaInterpreter.IsViable(mutated) ? new DnaSequence(mutated) : this;
        }

        /// <summary>Crossbreed two sequences (crossover + optional mutation).</summary>
        public static DnaSequence Crossbreed(DnaSequence a, DnaSequence b, Random? rnd = null)
        {
            rnd ??= new Random();
            var listA = SplitToSegments(a.Raw).ToList();
            var listB = SplitToSegments(b.Raw).ToList();
            if (listA.Count == 0) return b;
            if (listB.Count == 0) return a;

            // Single-point crossover
            int splitA = rnd.Next(1, Math.Max(2, listA.Count));
            int splitB = rnd.Next(1, Math.Max(2, listB.Count));
            var child = listA.Take(splitA).Concat(listB.Skip(splitB)).ToList();
            if (child.Count < 3)
                child = listA.Count >= listB.Count ? listA : listB;

            string raw = JoinSegments(child);
            var seq = new DnaSequence(raw);
            if (rnd.NextDouble() < 0.2)
                seq = seq.Mutate(0.3f, rnd);
            return seq.IsViable ? seq : (rnd.NextDouble() < 0.5 ? a : b);
        }

        private static string Normalize(string raw)
        {
            var sb = new StringBuilder();
            foreach (char c in raw)
            {
                if (DnaInterpreter.TokenChars.Contains(c) || c == '-' || c == ',' || c == ' ' || c == '(' || c == ')')
                    sb.Append(c);
            }
            return sb.ToString();
        }

        private static IEnumerable<string> SplitToSegments(string raw)
        {
            string n = raw.Trim();
            foreach (char c in new[] { '-', ',', ' ' })
                n = n.Replace(c, '-');
            var parts = n.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string p in parts)
            {
                string t = p.Trim();
                if (t.Length > 0) yield return t;
            }
        }

        private static string JoinSegments(List<string> parts)
        {
            return string.Join("-", parts);
        }

        public override string ToString() => Raw;
    }
}
