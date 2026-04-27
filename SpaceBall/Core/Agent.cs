using System;
using OpenTK.Mathematics;

namespace SpaceDNA.Core
{
    /// <summary>
    /// Agent state: position on sphere, energy, genome, behavior
    /// </summary>
    public class Agent
    {
        // Identity
        public int Id { get; private set; }
        public AgentGenome Genome { get; private set; }
        
        // Position on sphere (normalized direction from center)
        public Vector3 Position { get; private set; }
        
        // Movement direction (tangent to sphere)
        private Vector3 _moveDirection;
        private float _moveTimer;
        
        // Energy
        public float Energy { get; private set; }
        public float MaxEnergy => 100f * Genome.Size;
        
        // State
        public bool IsAlive { get; private set; } = true;
        public bool IsInWater => _lastHeight < 0f;
        public float Age { get; private set; } = 0f;
        
        // Last known environment
        private float _lastHeight = 0f;
        private float _lastTemperature = 0.5f;
        
        // Reproduction
        public float ReproductionCooldown { get; private set; } = 0f;
        private const float ReproductionEnergyThreshold = 70f;
        private const float ReproductionCost = 30f;
        
        // Statistics
        public int Generation { get; private set; } = 1;
        public int ChildrenCount { get; private set; } = 0;
        
        private static int _nextId = 1;
        
        public Agent(Vector3 position, AgentGenome? genome = null, int generation = 1)
        {
            Id = _nextId++;
            Position = Vector3.Normalize(position);
            Genome = genome ?? new AgentGenome();
            Generation = generation;
            Energy = MaxEnergy * 0.5f; // Start with half energy
            
            // Random initial movement
            SetRandomMovement();
        }
        
        /// <summary>
        /// Update agent logic
        /// </summary>
        public void Update(float deltaTime, float height, float temperature)
        {
            if (!IsAlive) return;
            
            _lastHeight = height;
            _lastTemperature = temperature;
            Age += deltaTime;
            ReproductionCooldown = Math.Max(0, ReproductionCooldown - deltaTime);
            
            // Metabolism - consume energy
            float energyDrain = Genome.EnergyCost * deltaTime;
            
            // Additional drain from hostile environment
            if (temperature < 0.3f && Genome.ColdResistance < 0.5f)
            {
                // Cold damage
                energyDrain += (0.3f - temperature) * (1f - Genome.ColdResistance) * 0.5f * deltaTime;
            }
            else if (temperature > 0.7f && Genome.HeatResistance < 0.5f)
            {
                // Heat damage
                energyDrain += (temperature - 0.7f) * (1f - Genome.HeatResistance) * 0.5f * deltaTime;
            }
            
            // Water handling
            if (height < 0f)
            {
                if (Genome.WaterAffinity < 0.3f)
                {
                    // Drowning - increased energy drain
                    energyDrain += 0.02f * deltaTime;
                }
                // Can swim if has water affinity
            }
            
            Energy -= energyDrain;
            
            // Check death
            if (Energy <= 0)
            {
                Die();
                return;
            }
            
            // Movement
            UpdateMovement(deltaTime);
        }
        
        /// <summary>
        /// Update movement on sphere surface
        /// </summary>
        private void UpdateMovement(float deltaTime)
        {
            _moveTimer -= deltaTime;
            if (_moveTimer <= 0)
            {
                SetRandomMovement();
            }
            
            // Move along sphere surface
            float speed = Genome.Speed * deltaTime * 0.1f;
            
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
            Vector3 newPos = Position + tangent * speed;
            Position = Vector3.Normalize(newPos);
        }
        
        /// <summary>
        /// Set random movement direction
        /// </summary>
        private void SetRandomMovement()
        {
            var rnd = new Random();
            _moveDirection = new Vector3(
                (float)(rnd.NextDouble() * 2 - 1),
                (float)(rnd.NextDouble() * 2 - 1),
                (float)(rnd.NextDouble() * 2 - 1)
            ).Normalized();
            _moveTimer = 2f + (float)rnd.NextDouble() * 5f; // Change direction every 2-7 seconds
        }
        
        /// <summary>
        /// Check if agent can reproduce
        /// </summary>
        public bool CanReproduce()
        {
            return IsAlive && 
                   Energy > ReproductionEnergyThreshold && 
                   ReproductionCooldown <= 0 &&
                   Age > 5f; // Must survive at least 5 seconds
        }
        
        /// <summary>
        /// Create offspring
        /// </summary>
        public Agent? Reproduce(Agent? partner = null)
        {
            if (!CanReproduce()) return null;
            
            // Pay energy cost
            Energy -= ReproductionCost;
            ReproductionCooldown = 10f;
            ChildrenCount++;
            
            // Create child genome
            AgentGenome childGenome;
            if (partner != null && partner.Genome != null)
            {
                // Crossbreed (20% chance)
                childGenome = AgentGenome.Crossbreed(Genome, partner.Genome);
            }
            else
            {
                // Asexual reproduction with mutation
                childGenome = Genome.Mutate(0.1f);
            }
            
            // Child position slightly offset from parent
            var rnd = new Random();
            Vector3 offset = new Vector3(
                (float)(rnd.NextDouble() - 0.5) * 0.1f,
                (float)(rnd.NextDouble() - 0.5) * 0.1f,
                (float)(rnd.NextDouble() - 0.5) * 0.1f
            );
            Vector3 childPos = Vector3.Normalize(Position + offset);
            
            return new Agent(childPos, childGenome, Generation + 1);
        }
        
        /// <summary>
        /// Agent dies
        /// </summary>
        public void Die()
        {
            IsAlive = false;
        }
        
        /// <summary>
        /// Feed agent (gain energy)
        /// </summary>
        public void Feed(float amount)
        {
            if (!IsAlive) return;
            Energy = Math.Min(Energy + amount, MaxEnergy);
        }
        
        /// <summary>
        /// Get agent info for UI
        /// </summary>
        public override string ToString()
        {
            string status = IsAlive ? "Alive" : "Dead";
            string env = IsInWater ? "Swimming" : "On land";
            return $"Agent {Id}: Gen {Generation} | Energy: {Energy:F0}/{MaxEnergy:F0} | {env} | {status}\n{Genome}";
        }
    }
}