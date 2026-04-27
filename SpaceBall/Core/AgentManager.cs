using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace SpaceDNA.Core
{
    /// <summary>
    /// Manages population of agents on planet surface
    /// </summary>
    public class AgentManager
    {
        private List<Agent> _agents = new List<Agent>();
        private List<Agent> _deadAgents = new List<Agent>(); // "Food" for others
        
        // Reference to planet data
        private float[,]? _heightmap;
        private int _heightmapSize = 512;
        private float _planetRadius = 5f;
        private float _displacementScale = 0.3f;
        private float _temperature = 0.5f;
        
        // Settings
        public int MaxPopulation { get; set; } = 100;
        public int InitialPopulation { get; set; } = 10;
        public bool EnableReproduction { get; set; } = true;
        public bool ShowDeadBodies { get; set; } = true;
        
        // Statistics
        public int TotalBorn { get; private set; } = 0;
        public int TotalDied { get; private set; } = 0;
        public float AverageGeneration => _agents.Count > 0 ? 
            (float)_agents.Sum(a => a.Generation) / _agents.Count : 0;
        
        // Events
        public event Action<Agent>? OnAgentBorn;
        public event Action<Agent>? OnAgentDied;
        
        public IReadOnlyList<Agent> Agents => _agents;
        public IReadOnlyList<Agent> DeadAgents => _deadAgents;
        public int AliveCount => _agents.Count;
        public int DeadCount => _deadAgents.Count;
        
        public AgentManager()
        {
        }
        
        /// <summary>
        /// Set planet data for environment queries
        /// </summary>
        public void SetPlanetData(float[,] heightmap, float radius, float displacementScale, float temperature)
        {
            _heightmap = heightmap;
            _heightmapSize = heightmap.GetLength(0);
            _planetRadius = radius;
            _displacementScale = displacementScale;
            _temperature = temperature;
        }
        
        /// <summary>
        /// Initialize population with random agents
        /// </summary>
        public void InitializePopulation(int count)
        {
            _agents.Clear();
            _deadAgents.Clear();
            
            var rnd = new Random();
            for (int i = 0; i < count; i++)
            {
                // Random position on sphere
                Vector3 pos = RandomSpherePoint(rnd);
                var agent = new Agent(pos);
                _agents.Add(agent);
                TotalBorn++;
                OnAgentBorn?.Invoke(agent);
            }
            
            Console.WriteLine($"[AgentManager] Initialized with {count} agents");
        }
        
        /// <summary>
        /// Spawn a single agent at position
        /// </summary>
        public void SpawnAgent(Vector3 position, AgentGenome? genome = null)
        {
            if (_agents.Count >= MaxPopulation) return;
            
            var agent = new Agent(position, genome);
            _agents.Add(agent);
            TotalBorn++;
            OnAgentBorn?.Invoke(agent);
        }
        
        /// <summary>
        /// Update all agents
        /// </summary>
        public void Update(float deltaTime)
        {
            var newAgents = new List<Agent>();
            var deadThisFrame = new List<Agent>();
            
            foreach (var agent in _agents)
            {
                if (!agent.IsAlive)
                {
                    deadThisFrame.Add(agent);
                    continue;
                }
                
                // Get environment at agent position
                float height = GetHeightAtPosition(agent.Position);
                float localTemp = GetLocalTemperature(height);
                
                // Update agent
                agent.Update(deltaTime, height, localTemp);
                
                // Check for death
                if (!agent.IsAlive)
                {
                    deadThisFrame.Add(agent);
                    continue;
                }
                
                // Check for reproduction
                if (EnableReproduction && agent.CanReproduce() && _agents.Count + newAgents.Count < MaxPopulation)
                {
                    // Find nearby potential partner
                    Agent? partner = FindNearbyAgent(agent, 0.2f);
                    
                    var child = agent.Reproduce(partner);
                    if (child != null)
                    {
                        newAgents.Add(child);
                        TotalBorn++;
                        OnAgentBorn?.Invoke(child);
                    }
                }
            }
            
            // Remove dead agents
            foreach (var dead in deadThisFrame)
            {
                _agents.Remove(dead);
                _deadAgents.Add(dead);
                TotalDied++;
                OnAgentDied?.Invoke(dead);
            }
            
            // Add new agents
            _agents.AddRange(newAgents);
            
            // Clean old dead bodies (keep only last 20)
            while (_deadAgents.Count > 20)
            {
                _deadAgents.RemoveAt(0);
            }
        }
        
        /// <summary>
        /// Get height at position on sphere surface
        /// </summary>
        private float GetHeightAtPosition(Vector3 position)
        {
            if (_heightmap == null) return 0f;
            
            // Convert 3D position to UV coordinates
            Vector3 normalized = position.Normalized();
            
            // Calculate longitude (0 to 1)
            float lon = MathF.Atan2(normalized.Z, normalized.X);
            float u = (lon + MathF.PI) / (2f * MathF.PI);
            
            // Calculate latitude (0 to 1)
            float lat = MathF.Asin(normalized.Y);
            float v = (lat + MathF.PI / 2f) / MathF.PI;
            
            // Sample heightmap
            int x = (int)(u * (_heightmapSize - 1));
            int y = (int)(v * (_heightmapSize - 1));
            x = Math.Clamp(x, 0, _heightmapSize - 1);
            y = Math.Clamp(y, 0, _heightmapSize - 1);
            
            return _heightmap[x, y];
        }
        
        /// <summary>
        /// Get local temperature adjusted for height
        /// </summary>
        private float GetLocalTemperature(float height)
        {
            // Higher = colder
            float heightFactor = height * 0.3f;
            return Math.Clamp(_temperature - heightFactor, 0f, 1f);
        }
        
        /// <summary>
        /// Find agent near position
        /// </summary>
        private Agent? FindNearbyAgent(Agent self, float maxDistance)
        {
            foreach (var other in _agents)
            {
                if (other == self || !other.IsAlive) continue;
                
                float dist = (other.Position - self.Position).Length;
                if (dist < maxDistance)
                {
                    return other;
                }
            }
            return null;
        }
        
        /// <summary>
        /// Generate random point on unit sphere
        /// </summary>
        private static Vector3 RandomSpherePoint(Random rnd)
        {
            float theta = (float)(rnd.NextDouble() * 2 * Math.PI);
            float phi = (float)Math.Acos(2 * rnd.NextDouble() - 1);
            
            float x = MathF.Sin(phi) * MathF.Cos(theta);
            float y = MathF.Sin(phi) * MathF.Sin(theta);
            float z = MathF.Cos(phi);
            
            return new Vector3(x, y, z);
        }
        
        /// <summary>
        /// Get world position for rendering
        /// </summary>
        public Vector3 GetWorldPosition(Agent agent)
        {
            float height = GetHeightAtPosition(agent.Position);
            float displacement = Math.Max(height, 0f) * _displacementScale;
            float radius = _planetRadius + displacement + 0.05f; // Slightly above surface
            
            return agent.Position.Normalized() * radius;
        }
        
        /// <summary>
        /// Get population statistics
        /// </summary>
        public string GetStats()
        {
            if (_agents.Count == 0) return "No agents";
            
            float avgEnergy = (float)_agents.Average(a => a.Energy);
            int maxGen = _agents.Max(a => a.Generation);
            int avgLegs = (int)_agents.Average(a => a.Genome.LegCount);
            float withMind = _agents.Count(a => a.Genome.HasMind) * 100f / _agents.Count;
            
            return $"Population: {AliveCount}/{MaxPopulation}\n" +
                   $"Avg Energy: {avgEnergy:F0}\n" +
                   $"Avg Generation: {AverageGeneration:F1}\n" +
                   $"Max Generation: {maxGen}\n" +
                   $"Avg Legs: {avgLegs}\n" +
                   $"Has Mind: {withMind:F0}%\n" +
                   $"Born: {TotalBorn} | Died: {TotalDied}";
        }
        
        /// <summary>
        /// Kill all agents (sterilize/genocide)
        /// </summary>
        public void KillAll()
        {
            foreach (var agent in _agents)
            {
                agent.Die();
                _deadAgents.Add(agent);
                TotalDied++;
            }
            _agents.Clear();
            Console.WriteLine("[AgentManager] All agents killed");
        }
        
        /// <summary>
        /// Remove all dead bodies
        /// </summary>
        public void ClearDead()
        {
            _deadAgents.Clear();
        }
    }
}