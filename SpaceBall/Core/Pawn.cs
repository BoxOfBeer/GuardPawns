using System;
using OpenTK.Mathematics;

namespace SpaceDNA.Core
{
    /// <summary>
    /// Pawn - a creature living on planet surface.
    /// Controlled by Agent (global world controller).
    /// </summary>
    public class Pawn
    {
        // Identity
        public int Id { get; private set; }
        public PawnGenome Genome { get; private set; }
        
        // Position on sphere (normalized direction from center)
        public Vector3 Position { get; private set; }

        internal void SetPosition(Vector3 newPosition)
        {
            Position = Vector3.Normalize(newPosition);
        }
        
        // Movement
        private Vector3 _moveDirection;
        private float _moveTimer;
        public float CurrentSpeed { get; private set; }
        
        // Energy
        public float Energy { get; private set; }
        public float MaxEnergy => WorldConstants.MaxEnergy * Genome.Size;
        public float EnergyPercent => Energy / MaxEnergy;
        
        // Needs (controlled by Agent)
        public float Hunger { get; private set; } = 0f;      // 0 = full, 1 = starving
        public float Thirst { get; private set; } = 0f;      // 0 = hydrated, 1 = dehydrated
        public float Discomfort { get; private set; } = 0f;  // 0 = comfortable, 1 = in pain
        
        // State
        public bool IsAlive { get; private set; } = true;
        /// <summary>Высота рельефа (из шума) в мировой системе. &lt; 0 = вода.</summary>
        public float TerrainHeight => _lastHeight;
        public bool IsInWater => _lastHeight < 0f;
        public float Age { get; private set; } = 0f;
        
        // Environment (updated by Agent from heightmap noise)
        private float _lastHeight = 0f;
        private float _lastTemperature = 0.5f;
        private float _gravityFactor = 1f;
        private float _atmosphere = 1f;
        
        // Reproduction
        public float ReproductionCooldown { get; private set; } = 0f;
        public int Generation { get; private set; } = 1;
        public int ChildrenCount { get; private set; } = 0;
        
        // Statistics
        public float DistanceTraveled { get; private set; } = 0f;
        public float TimeSurvived { get; private set; } = 0f;
        
        private static int _nextId = 1;
        private readonly Random _rng;
        
        public Pawn(Vector3 position, PawnGenome? genome = null, int generation = 1, Random? random = null)
        {
            Id = _nextId++;
            Position = Vector3.Normalize(position);
            Genome = genome ?? new PawnGenome();
            _rng = random ?? Random.Shared;
            Generation = generation;
            Energy = MaxEnergy * 0.8f; // Start with 80% energy
            
            SetRandomMovement();
        }
        
        /// <summary>
        /// Update pawn physics and needs. Called by Agent each frame.
        /// </summary>
        public void Update(float deltaTime, float height, float temperature, 
            float gravityFactor, float atmosphere, float geologicActivity)
        {
            if (!IsAlive) return;
            
            // Update environment
            _lastHeight = height;
            _lastTemperature = temperature;
            _gravityFactor = gravityFactor;
            _atmosphere = atmosphere;
            
            Age += deltaTime;
            TimeSurvived += deltaTime;
            ReproductionCooldown = Math.Max(0, ReproductionCooldown - deltaTime);
            
            // Calculate effective speed with all modifiers
            float effectiveSpeed = CalculateEffectiveSpeed();
            CurrentSpeed = effectiveSpeed;
            
            // Move
            UpdateMovement(deltaTime, effectiveSpeed);
            
            // Update needs based on environment
            UpdateNeeds(deltaTime);
            
            // Consume energy
            UpdateEnergy(deltaTime);
            
            // Check death
            if (Energy <= 0 || Hunger >= 1f || Thirst >= 1f)
            {
                Die();
            }
        }
        
        /// <summary>
        /// Calculate effective speed with gravity, atmosphere, and water modifiers.
        /// </summary>
        private float CalculateEffectiveSpeed()
        {
            float baseSpeed = Genome.Speed;
            
            // Gravity effect
            float gravityMult = WorldConstants.GravitySpeedMultiplier(_gravityFactor);
            
            // Atmosphere effect
            float atmoMult = WorldConstants.AtmosphereSpeedMultiplier(_atmosphere);
            
            // Water effect
            float waterMult = 1f;
            if (IsInWater)
            {
                waterMult = WorldConstants.SwimmingSpeedMultiplier(Genome.WaterAffinity);
            }
            
            // Leg bonus
            float legBonus = 1f + Genome.LegCount * WorldConstants.LegSpeedBonus;
            
            float finalSpeed = baseSpeed * gravityMult * atmoMult * waterMult * legBonus;
            return Math.Clamp(finalSpeed, 0.1f, WorldConstants.MaxSpeed);
        }
        
        /// <summary>
        /// Update movement on sphere surface.
        /// </summary>
        private void UpdateMovement(float deltaTime, float speed)
        {
            _moveTimer -= deltaTime;
            if (_moveTimer <= 0)
            {
                SetRandomMovement();
            }
            
            // Calculate tangent movement
            Vector3 normal = Position.Normalized();
            Vector3 tangent = Vector3.Cross(normal, _moveDirection).Normalized();
            
            if (tangent.LengthSquared < 0.01f)
            {
                tangent = Vector3.Cross(normal, Vector3.UnitY).Normalized();
                if (tangent.LengthSquared < 0.01f)
                {
                    tangent = Vector3.Cross(normal, Vector3.UnitX).Normalized();
                }
            }
            
            // New position on sphere
            float moveDistance = speed * deltaTime * 0.1f;
            Vector3 newPos = Position + tangent * moveDistance;
            Position = Vector3.Normalize(newPos);
            
            DistanceTraveled += moveDistance;
        }
        
        /// <summary>
        /// Set random movement direction.
        /// </summary>
        private void SetRandomMovement()
        {
            _moveDirection = new Vector3(
                (float)(_rng.NextDouble() * 2 - 1),
                (float)(_rng.NextDouble() * 2 - 1),
                (float)(_rng.NextDouble() * 2 - 1)
            ).Normalized();
            _moveTimer = WorldConstants.MinDirectionChangeTime + 
                (float)_rng.NextDouble() * (WorldConstants.MaxDirectionChangeTime - WorldConstants.MinDirectionChangeTime);
        }
        
        /// <summary>
        /// Update needs based on environment and genetics.
        /// </summary>
        private void UpdateNeeds(float deltaTime)
        {
            // Hunger increases over time (faster for larger creatures)
            Hunger += deltaTime * 0.01f * Genome.Size;
            
            // Thirst increases faster in heat
            float thirstRate = 0.005f;
            if (_lastTemperature > WorldConstants.ComfortableTempMax)
            {
                thirstRate += (_lastTemperature - WorldConstants.ComfortableTempMax) * 0.02f;
            }
            Thirst += deltaTime * thirstRate;
            
            // Discomfort from temperature
            if (_lastTemperature < WorldConstants.ComfortableTempMin || 
                _lastTemperature > WorldConstants.ComfortableTempMax)
            {
                float tempDist;
                if (_lastTemperature < WorldConstants.ComfortableTempMin)
                {
                    tempDist = WorldConstants.ComfortableTempMin - _lastTemperature;
                }
                else
                {
                    tempDist = _lastTemperature - WorldConstants.ComfortableTempMax;
                }
                Discomfort = Math.Min(1f, tempDist * 2f);
            }
            else
            {
                Discomfort = Math.Max(0f, Discomfort - deltaTime * 0.5f);
            }
            
            // Cap needs at 1
            Hunger = Math.Min(1f, Hunger);
            Thirst = Math.Min(1f, Thirst);
        }
        
        /// <summary>
        /// Update energy consumption with all world modifiers.
        /// </summary>
        private void UpdateEnergy(float deltaTime)
        {
            // Base metabolism
            float baseDrain = WorldConstants.BaseMetabolism * deltaTime;
            
            // Gravity multiplier
            float gravityMult = WorldConstants.GravityEnergyMultiplier(_gravityFactor);
            
            // Temperature stress
            float tempDrain = WorldConstants.TemperatureEnergyDrain(_lastTemperature, _atmosphere) * deltaTime;
            
            // Water drowning
            float waterDrain = 0f;
            if (IsInWater)
            {
                waterDrain = WorldConstants.DrowningDamage(Genome.WaterAffinity) * deltaTime;
            }
            
            // Hunger penalty
            float hungerPenalty = Hunger * 0.5f * deltaTime;
            
            // Size factor (larger = more energy needed)
            float sizeMult = Genome.Size;
            
            // Total energy drain
            float totalDrain = (baseDrain * gravityMult + tempDrain + waterDrain + hungerPenalty) * sizeMult;
            
            Energy -= totalDrain;
        }
        
        /// <summary>
        /// Check if pawn can reproduce.
        /// </summary>
        public bool CanReproduce()
        {
            return IsAlive && 
                   Energy > WorldConstants.ReproductionEnergyThreshold && 
                   ReproductionCooldown <= 0 &&
                   Age > WorldConstants.MinReproductionAge &&
                   Hunger < 0.5f &&
                   Thirst < 0.5f;
        }
        
        /// <summary>
        /// Create offspring.
        /// </summary>
        public Pawn? Reproduce(Pawn? partner = null)
        {
            if (!CanReproduce()) return null;
            
            // Pay energy cost
            Energy -= WorldConstants.ReproductionEnergyCost;
            ReproductionCooldown = WorldConstants.ReproductionCooldown;
            ChildrenCount++;
            
            var rnd = _rng;
            
            // Create child genome: DNA-level crossover if both have raw sequence, else phenotype-level
            PawnGenome childGenome;
            if (DnaInterpreter.LooksLikeRawDna(Genome.DNA) &&
                (partner == null || DnaInterpreter.LooksLikeRawDna(partner.Genome.DNA)))
            {
                var seqA = new DnaSequence(Genome.DNA);
                if (partner != null)
                {
                    var seqB = new DnaSequence(partner.Genome.DNA);
                    var childSeq = DnaSequence.Crossbreed(seqA, seqB, rnd);
                    childGenome = childSeq.Express(rnd.Next());
                }
                else
                {
                    var childSeq = seqA.Mutate(0.15f, rnd);
                    childGenome = childSeq.IsViable ? childSeq.Express(rnd.Next()) : Genome.Mutate(0.15f);
                }
            }
            else
            {
                if (partner != null)
                    childGenome = PawnGenome.Crossbreed(Genome, partner.Genome);
                else
                    childGenome = Genome.Mutate(0.15f);
            }
            
            // Child position slightly offset from parent
            Vector3 offset = new Vector3(
                (float)(rnd.NextDouble() - 0.5) * 0.1f,
                (float)(rnd.NextDouble() - 0.5) * 0.1f,
                (float)(rnd.NextDouble() - 0.5) * 0.1f
            );
            Vector3 childPos = Vector3.Normalize(Position + offset);
            
            return new Pawn(childPos, childGenome, Generation + 1, _rng);
        }
        
        /// <summary>
        /// Feed the pawn (reduce hunger, restore energy).
        /// </summary>
        public void Feed(float foodValue = 1f)
        {
            if (!IsAlive) return;
            
            Hunger = Math.Max(0f, Hunger - foodValue * 0.3f);
            Energy = Math.Min(MaxEnergy, Energy + foodValue * WorldConstants.FoodEnergyGain);
        }
        
        /// <summary>
        /// Give water to the pawn.
        /// </summary>
        public void Hydrate(float waterValue = 1f)
        {
            if (!IsAlive) return;
            Thirst = Math.Max(0f, Thirst - waterValue * 0.5f);
        }
        
        /// <summary>
        /// Pawn dies.
        /// </summary>
        public void Die()
        {
            IsAlive = false;
        }
        
        /// <summary>
        /// Get pawn info for UI.
        /// </summary>
        public override string ToString()
        {
            string status = IsAlive ? "Alive" : "Dead";
            string env = IsInWater ? "Swimming" : "On land";
            return $"Pawn {Id}: Gen {Generation} | E:{EnergyPercent:P0} H:{Hunger:P0} T:{Thirst:P0} | {env} | {status}";
        }
    }
}