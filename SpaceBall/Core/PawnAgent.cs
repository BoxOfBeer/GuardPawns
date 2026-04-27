using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Mathematics;

namespace SpaceDNA.Core
{
    /// <summary>
    /// Agent - global world controller that manages pawn needs and world state.
    /// Controls: hunger, thirst, temperature stress, food spawning, population balance.
    /// </summary>
    public class PawnAgent
    {
        public readonly struct PawnSurfaceDebugInfo
        {
            public int AliveCount { get; }
            public int BelowSurfaceCount { get; }
            public float AverageWorldRadius { get; }
            public float AverageSurfaceRadius { get; }
            public float AverageVisualDelta { get; }
            public float AverageHeight { get; }
            public float DisplacementScale { get; }
            public float BlendFactor { get; }

            public PawnSurfaceDebugInfo(int aliveCount, int belowSurfaceCount, float averageWorldRadius,
                float averageSurfaceRadius, float averageVisualDelta, float averageHeight,
                float displacementScale, float blendFactor)
            {
                AliveCount = aliveCount;
                BelowSurfaceCount = belowSurfaceCount;
                AverageWorldRadius = averageWorldRadius;
                AverageSurfaceRadius = averageSurfaceRadius;
                AverageVisualDelta = averageVisualDelta;
                AverageHeight = averageHeight;
                DisplacementScale = displacementScale;
                BlendFactor = blendFactor;
            }
        }

        // World parameters (from planet config)
        public float PlanetRadius { get; private set; }
        public float PlanetDensity { get; private set; }
        public float PlanetTemperature { get; private set; }
        public float PlanetAtmosphere { get; private set; }
        public float PlanetGeologicActivity { get; private set; }
        
        // Derived world constants
        public float GravityFactor { get; private set; }
        public float EffectiveSpeed { get; private set; }
        public float EnergyCostMultiplier { get; private set; }
        
        // Heightmap/surface sampler data
        private readonly SurfaceSampler _surfaceSampler = new SurfaceSampler();
        
        // Pawn population
        private List<Pawn> _pawns = new List<Pawn>();
        public IReadOnlyList<Pawn> Pawns => _pawns;
        public int AliveCount => _pawns.FindAll(p => p.IsAlive).Count;
        public int DeadCount => _pawns.FindAll(p => !p.IsAlive).Count;
        public int TotalCount => _pawns.Count;
        
        // Food items (simple food sources on planet surface)
        private List<FoodItem> _foodItems = new List<FoodItem>();
        public IReadOnlyList<FoodItem> FoodItems => _foodItems;
        
        // Statistics
        public int TotalBirths { get; private set; } = 0;
        public int TotalDeaths { get; private set; } = 0;
        public float WorldTime { get; private set; } = 0f;
        public float AverageFitness { get; private set; } = 0f;
        
        // Random for world events
        private Random _rnd = new Random();
        private const float PawnVisualSurfaceOffsetFactor = 0.008f;
        private const float PawnModelBaseOffset = 0.05f;

        // Species-level DNA (optional). If set, new pawns spawn near this genome with small mutations.
        private DnaSequence? _speciesDna;

        public void SetSpeciesDna(DnaSequence? dna)
        {
            _speciesDna = dna;
        }
        
        public PawnAgent()
        {
            // Default to Earth-like conditions
            SetPlanetParameters(
                radius: WorldConstants.EarthRadius,
                density: 1f,
                temperature: 0.5f,
                atmosphere: 1f,
                geologicActivity: 1f
            );
        }
        
        /// <summary>
        /// Set planet parameters and recalculate derived constants.
        /// </summary>
        public void SetPlanetParameters(float radius, float density, float temperature, 
            float atmosphere, float geologicActivity)
        {
            PlanetRadius = radius;
            PlanetDensity = density;
            PlanetTemperature = temperature;
            PlanetAtmosphere = atmosphere;
            PlanetGeologicActivity = geologicActivity;
            
            // Calculate derived constants
            GravityFactor = WorldConstants.CalculateGravityFactor(radius, density);
            EnergyCostMultiplier = WorldConstants.GravityEnergyMultiplier(GravityFactor);
            EffectiveSpeed = WorldConstants.GravitySpeedMultiplier(GravityFactor);
            _surfaceSampler.SetPlanet(radius, _surfaceSampler.DisplacementScale);
        }
        
        /// <summary>
        /// Set heightmap data for terrain queries.
        /// </summary>
        public void SetHeightmap(float[,] heightmap, float displacementScale)
        {
            SetSurfaceState(heightmap, null, blendFactor: 0f, displacementScale);
        }

        public void SetSurfaceState(float[,] currentHeightmap, float[,]? nextHeightmap, float blendFactor, float displacementScale)
        {
            _surfaceSampler.SetPlanet(PlanetRadius, displacementScale);
            _surfaceSampler.SetHeightmaps(currentHeightmap, nextHeightmap, blendFactor);
        }
        
        /// <summary>
        /// Initialize pawn population.
        /// </summary>
        public void InitializePopulation(int count)
        {
            _pawns.Clear();
            _foodItems.Clear();
            TotalBirths = 0;
            TotalDeaths = 0;
            WorldTime = 0f;
            
            for (int i = 0; i < count; i++)
            {
                var pawn = CreateRandomPawn();
                _pawns.Add(pawn);
                TotalBirths++;
            }
            
            Console.WriteLine($"[PawnAgent] Initialized {count} pawns");
        }
        
        /// <summary>
        /// Create a pawn at random position with random genome.
        /// Uses DNA layer: random sequence -> mutate (evolution) -> express to phenotype.
        /// </summary>
        private Pawn CreateRandomPawn()
        {
            // DNA layer: random sequence, small mutation, then express to PawnGenome
            var dna = _speciesDna != null
                ? _speciesDna.Mutate(0.15f, _rnd)
                : DnaSequence.Random(segmentCount: 12 + _rnd.Next(9), rnd: _rnd).Mutate(0.2f, _rnd);
            PawnGenome genome = dna.IsViable ? dna.Express(_rnd.Next()) : new PawnGenome().Mutate(0.2f);

            // Random position on sphere (avoid water for non-swimmers)
            Vector3 pos = Vector3.UnitZ;
            const float waterTraverseThreshold = 0.55f;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                float theta = (float)(_rnd.NextDouble() * Math.PI * 2);
                float phi = (float)Math.Acos(2 * _rnd.NextDouble() - 1);
                pos = new Vector3(
                    MathF.Sin(phi) * MathF.Cos(theta),
                    MathF.Sin(phi) * MathF.Sin(theta),
                    MathF.Cos(phi)
                );
                if (!_surfaceSampler.HasData) break;
                float h = GetHeightAtPosition(pos);
                if (h >= 0f || genome.WaterAffinity >= waterTraverseThreshold) break;
            }

            return new Pawn(pos, genome, random: _rnd);
        }
        
        /// <summary>
        /// Main update loop - called each frame.
        /// </summary>
        public void Update(float deltaTime)
        {
            WorldTime += deltaTime;
            
            // Update all pawns
            UpdatePawns(deltaTime);
            
            // Spawn food
            UpdateFoodSpawning(deltaTime);
            
            // Handle reproduction
            UpdateReproduction();
            
            // Clean up dead pawns periodically
            if (_pawns.Count > WorldConstants.MaxPopulation)
            {
                CleanupDeadPawns();
            }
            
            // Calculate statistics
            CalculateStatistics();
        }
        
        /// <summary>
        /// Update all pawns with environment data.
        /// </summary>
        private void UpdatePawns(float deltaTime)
        {
            foreach (var pawn in _pawns)
            {
                if (!pawn.IsAlive) continue;
                
                SurfaceSampler.SurfaceSample prevSample = SampleSurface(pawn.Position);
                float localTemp = CalculateLocalTemperature(prevSample.Height);

                // Update pawn physics (height = текущая высота рельефа под пешкой)
                pawn.Update(deltaTime, prevSample.Height, localTemp, GravityFactor,
                    PlanetAtmosphere, PlanetGeologicActivity);

                SurfaceSampler.SurfaceSample nextSample = SampleSurface(pawn.Position);
                bool revert = false;
                if (!CanPawnEnterTerrainAt(pawn, nextSample.Height))
                    revert = true;
                const float maxSurfaceSlope = 0.85f;
                if (!pawn.Genome.CanFly && nextSample.Slope > maxSurfaceSlope)
                    revert = true;

                SurfaceSampler.SurfaceSample activeSample = revert ? prevSample : nextSample;
                pawn.SetPosition(activeSample.Normal);
                
                // Check for food collision
                CheckFoodCollision(pawn);
                
                // Passive foraging: water and grass (low/zero height) give energy
                ApplyTerrainForaging(pawn, activeSample.Height, deltaTime);
                
                // Track deaths
                if (!pawn.IsAlive)
                {
                    TotalDeaths++;
                }
            }
        }
        
        /// <summary>
        /// Calculate local temperature based on height.
        /// </summary>
        private float CalculateLocalTemperature(float height)
        {
            // Base temperature from planet
            float temp = PlanetTemperature;
            
            // Higher elevations are colder (lapse rate)
            if (height > 0)
            {
                temp -= height * 0.1f;
            }
            
            // Geologic heat
            temp += WorldConstants.VolcanicHeatBonus(PlanetGeologicActivity, height);
            
            return Math.Clamp(temp, 0f, 1f);
        }
        
        /// <summary>
        /// Высота рельефа (из шума) в мировой системе в заданном направлении (нормализованном).
        /// Используется для позиции пешек, проверки воды/суши и полёта.
        /// </summary>
        public float GetTerrainHeightAt(Vector3 unitDirection)
        {
            return SampleSurface(unitDirection).Height;
        }

        public float GetSurfaceRadius(Vector3 direction) => SampleSurface(direction).Radius;
        public Vector3 GetSurfacePoint(Vector3 direction) => SampleSurface(direction).Position;

        public SurfaceSampler.SurfaceSample SampleSurface(Vector3 direction)
        {
            return _surfaceSampler.SampleSurface(direction);
        }
        
        /// <summary>
        /// Может ли пешка находиться на данной высоте рельефа: суша (height >= 0), вода при умении плавать, или полёт.
        /// </summary>
        public bool CanPawnEnterTerrainAt(Pawn pawn, float terrainHeight)
        {
            if (terrainHeight >= 0f) return true;
            if (pawn.Genome.WaterAffinity >= 0.55f) return true;
            if (pawn.Genome.CanFly) return true;
            return false;
        }
        
        /// <summary>
        /// Get height at position from heightmap.
        /// </summary>
        private float GetHeightAtPosition(Vector3 position)
        {
            return _surfaceSampler.SampleHeightWorld(position);
        }

        private float GetPawnSurfaceOffset(Pawn pawn)
        {
            float baseOffset = MathF.Max(WorldConstants.MinPawnSurfaceOffset, PlanetRadius * WorldConstants.PawnSurfaceOffsetFactor);
            float visualOffset = PawnVisualSurfaceOffsetFactor * PlanetRadius;
            float modelOffset = PawnModelBaseOffset * pawn.Genome.Size;
            return baseOffset + visualOffset + modelOffset;
        }
        
        /// <summary>
        /// Spawn food over time.
        /// </summary>
        private void UpdateFoodSpawning(float deltaTime)
        {
            // Age food so it can expire
            foreach (var f in _foodItems)
            {
                f.Update(deltaTime);
            }
            
            // Spawn food at constant rate
            if (_foodItems.Count < WorldConstants.MaxFoodItems)
            {
                if (_rnd.NextDouble() < WorldConstants.FoodSpawnRate * deltaTime)
                {
                    SpawnFood();
                }
            }
            
            // Remove expired food
            _foodItems.RemoveAll(f => f.IsExpired);
        }
        
        /// <summary>
        /// Spawn a food item at random position.
        /// </summary>
        private void SpawnFood()
        {
            float theta = (float)(_rnd.NextDouble() * Math.PI * 2);
            float phi = (float)Math.Acos(2 * _rnd.NextDouble() - 1);
            
            Vector3 pos = new Vector3(
                MathF.Sin(phi) * MathF.Cos(theta),
                MathF.Sin(phi) * MathF.Sin(theta),
                MathF.Cos(phi)
            );
            
            _foodItems.Add(new FoodItem(pos, lifetime: 30f));
        }
        
        /// <summary>
        /// Passive foraging: standing on water (for swimmers) or grass/low land restores energy.
        /// </summary>
        private void ApplyTerrainForaging(Pawn pawn, float heightWorld, float deltaTime)
        {
            if (!pawn.IsAlive || deltaTime <= 0f) return;
            float normH = _surfaceSampler.DisplacementScale > 0.0001f ? heightWorld / _surfaceSampler.DisplacementScale : 0f;
            float forage = 0f;
            if (normH < 0f && pawn.Genome.WaterAffinity >= 0.55f)
                forage = WorldConstants.WaterForageEnergyRate * deltaTime;
            else if (normH >= 0f && normH <= 0.35f)
                forage = WorldConstants.GrassForageEnergyRate * deltaTime;
            if (forage > 0f)
                pawn.Feed(Math.Min(1f, forage / WorldConstants.FoodEnergyGain));
        }

        /// <summary>
        /// Check if pawn collides with food.
        /// </summary>
        private void CheckFoodCollision(Pawn pawn)
        {
            for (int i = _foodItems.Count - 1; i >= 0; i--)
            {
                var food = _foodItems[i];
                
                // Simple distance check
                float dist = (pawn.Position - food.Position).LengthSquared;
                float collisionDist = 0.01f; // Collision threshold
                
                if (dist < collisionDist)
                {
                    pawn.Feed(food.Value);
                    _foodItems.RemoveAt(i);
                }
            }
        }
        
        /// <summary>
        /// Handle reproduction for eligible pawns.
        /// </summary>
        private void UpdateReproduction()
        {
            if (_pawns.Count >= WorldConstants.MaxPopulation) return;
            
            var eligible = _pawns.FindAll(p => p.CanReproduce());
            
            foreach (var pawn in eligible)
            {
                // Small chance to reproduce each frame
                if (_rnd.NextDouble() < 0.001)
                {
                    // Find nearby partner for sexual reproduction
                    Pawn? partner = FindNearbyPawn(pawn, 0.1f);
                    
                    var child = pawn.Reproduce(partner);
                    if (child != null)
                    {
                        _pawns.Add(child);
                        TotalBirths++;
                    }
                }
            }
        }
        
        /// <summary>
        /// Find nearby pawn for reproduction.
        /// </summary>
        private Pawn? FindNearbyPawn(Pawn source, float maxDistance)
        {
            foreach (var pawn in _pawns)
            {
                if (pawn == source || !pawn.IsAlive) continue;
                
                float dist = (pawn.Position - source.Position).LengthSquared;
                if (dist < maxDistance * maxDistance && pawn.CanReproduce())
                {
                    return pawn;
                }
            }
            return null;
        }
        
        /// <summary>
        /// Remove dead pawns from list.
        /// </summary>
        private void CleanupDeadPawns()
        {
            _pawns.RemoveAll(p => !p.IsAlive);
        }
        
        /// <summary>
        /// Calculate population statistics.
        /// </summary>
        private void CalculateStatistics()
        {
            if (_pawns.Count == 0)
            {
                AverageFitness = 0f;
                return;
            }
            
            float totalFitness = 0f;
            int alive = 0;
            
            foreach (var pawn in _pawns)
            {
                if (pawn.IsAlive)
                {
                    totalFitness += pawn.Genome.CalculateFitness(
                        PlanetTemperature, PlanetAtmosphere, GravityFactor);
                    alive++;
                }
            }
            
            AverageFitness = alive > 0 ? totalFitness / alive : 0f;
        }
        
        /// <summary>
        /// Get world position for rendering: on the displaced surface, slightly outward so pawns are never inside the mesh.
        /// </summary>
        public Vector3 GetWorldPosition(Pawn pawn)
        {
            var sample = SampleSurface(pawn.Position);
            float offset = GetPawnSurfaceOffset(pawn);
            return sample.Position + sample.Normal * offset;
        }

        /// <summary>
        /// Debug/runtime check: verifies that alive pawns are anchored near surface + offset.
        /// </summary>
        public int ValidateSurfaceAnchoring(float tolerance = 0.02f)
        {
            int invalid = 0;
            foreach (var pawn in _pawns)
            {
                if (!pawn.IsAlive) continue;
                var sample = SampleSurface(pawn.Position);
                float worldRadius = (GetWorldPosition(pawn)).Length;
                float offset = GetPawnSurfaceOffset(pawn);
                float target = sample.Radius + offset;
                if (MathF.Abs(worldRadius - target) > tolerance)
                    invalid++;
            }
            return invalid;
        }

        public PawnSurfaceDebugInfo GetSurfaceDebugInfo()
        {
            int alive = 0;
            int below = 0;
            float sumWorldRadius = 0f;
            float sumSurfaceRadius = 0f;
            float sumDelta = 0f;
            float sumHeight = 0f;

            foreach (var pawn in _pawns)
            {
                if (!pawn.IsAlive) continue;
                alive++;
                var sample = SampleSurface(pawn.Position);
                float worldRadius = GetWorldPosition(pawn).Length;
                float offset = GetPawnSurfaceOffset(pawn);
                float surfaceWithOffset = sample.Radius + offset;
                float delta = worldRadius - surfaceWithOffset;

                if (delta < 0f) below++;
                sumWorldRadius += worldRadius;
                sumSurfaceRadius += sample.Radius;
                sumDelta += delta;
                sumHeight += sample.Height;
            }

            if (alive == 0)
            {
                return new PawnSurfaceDebugInfo(
                    aliveCount: 0, belowSurfaceCount: 0, averageWorldRadius: 0f, averageSurfaceRadius: 0f,
                    averageVisualDelta: 0f, averageHeight: 0f,
                    displacementScale: _surfaceSampler.DisplacementScale, blendFactor: _surfaceSampler.BlendFactor);
            }

            return new PawnSurfaceDebugInfo(
                aliveCount: alive,
                belowSurfaceCount: below,
                averageWorldRadius: sumWorldRadius / alive,
                averageSurfaceRadius: sumSurfaceRadius / alive,
                averageVisualDelta: sumDelta / alive,
                averageHeight: sumHeight / alive,
                displacementScale: _surfaceSampler.DisplacementScale,
                blendFactor: _surfaceSampler.BlendFactor);
        }
        
        /// <summary>
        /// Get agent statistics string.
        /// </summary>
        public string GetStatsString()
        {
            string dnaSample = "";
            var first = _pawns.FirstOrDefault(p => p.IsAlive);
            if (first != null && DnaInterpreter.LooksLikeRawDna(first.Genome.DNA))
            {
                string raw = first.Genome.DNA;
                dnaSample = raw.Length > 40 ? raw.Substring(0, 37) + "..." : raw;
            }
            string baseLine = $"Pawns: {AliveCount}/{WorldConstants.MaxPopulation} | " +
                   $"Fitness: {AverageFitness:F2} | " +
                   $"Births: {TotalBirths} | Deaths: {TotalDeaths} | " +
                   $"Food: {_foodItems.Count} | Time: {WorldTime:F1}s";
            return string.IsNullOrEmpty(dnaSample) ? baseLine : $"{baseLine}\nDNA: {dnaSample}";
        }
    }
    
    /// <summary>
    /// Simple food item on planet surface.
    /// </summary>
    public class FoodItem
    {
        public Vector3 Position { get; private set; }
        public float Value { get; private set; }
        public float Lifetime { get; private set; }
        public float Age { get; private set; }
        public bool IsExpired => Age >= Lifetime;
        
        public FoodItem(Vector3 position, float value = 1f, float lifetime = 60f)
        {
            Position = Vector3.Normalize(position);
            Value = value;
            Lifetime = lifetime;
            Age = 0f;
        }
        
        public void Update(float deltaTime)
        {
            Age += deltaTime;
        }
    }
}
