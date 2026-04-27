using System;

namespace SpaceDNA.Core
{
    /// <summary>
    /// World constants and formulas for pawn simulation.
    /// Base reference: Earth = 1.0 for all parameters
    /// </summary>
    public static class WorldConstants
    {
        #region Earth Reference Values (Base = 1.0)
        
        /// <summary>Earth radius in simulation units</summary>
        public const float EarthRadius = 5.0f;
        
        /// <summary>Earth gravity in m/s² (reference only)</summary>
        public const float EarthGravity = 9.81f;
        
        /// <summary>Earth atmosphere density at sea level (kg/m³)</summary>
        public const float EarthAtmosphereDensity = 1.225f;
        
        /// <summary>Earth average temperature in Kelvin</summary>
        public const float EarthTemperatureK = 288f; // ~15°C
        
        /// <summary>Comfortable temperature range for life</summary>
        public const float ComfortableTempMin = 0.3f; // In 0-1 scale
        public const float ComfortableTempMax = 0.7f;
        
        /// <summary>Standard atmospheric pressure at sea level (Pa)</summary>
        public const float EarthPressure = 101325f;
        
        #endregion
        
        #region Pawn Energy Constants
        
        /// <summary>Base energy consumption per second (Earth conditions)</summary>
        public const float BaseMetabolism = 0.35f; // Energy units per second
        
        /// <summary>Maximum pawn energy</summary>
        public const float MaxEnergy = 100f;
        
        /// <summary>Energy threshold for reproduction</summary>
        public const float ReproductionEnergyThreshold = 70f;
        
        /// <summary>Energy cost for reproduction</summary>
        public const float ReproductionEnergyCost = 30f;
        
        /// <summary>Minimum age before reproduction (seconds)</summary>
        public const float MinReproductionAge = 10f;
        
        /// <summary>Energy gained from food</summary>
        public const float FoodEnergyGain = 20f;
        
        /// <summary>Energy per second when foraging on grass / low land (height 0..~0.2)</summary>
        public const float GrassForageEnergyRate = 0.12f;
        
        /// <summary>Energy per second when foraging in water (swimmers only, height &lt; 0)</summary>
        public const float WaterForageEnergyRate = 0.08f;
        
        /// <summary>Cooldown between reproductions (seconds)</summary>
        public const float ReproductionCooldown = 15f;
        
        #endregion
        
        #region Movement Constants
        
        /// <summary>Base movement speed on Earth (units per second)</summary>
        public const float BaseMoveSpeed = 0.5f;
        
        /// <summary>Speed multiplier per leg</summary>
        public const float LegSpeedBonus = 0.1f;
        
        /// <summary>Maximum speed cap</summary>
        public const float MaxSpeed = 2.0f;
        
        /// <summary>Direction change interval (seconds)</summary>
        public const float MinDirectionChangeTime = 2f;
        public const float MaxDirectionChangeTime = 7f;
        
        #endregion
        
        #region Gravity Formulas
        
        /// <summary>
        /// Calculate gravity factor based on planet size and density.
        /// Returns multiplier relative to Earth gravity.
        /// Formula: g ∝ (radius × density)
        /// </summary>
        public static float CalculateGravityFactor(float radius, float density)
        {
            // Normalize to Earth values
            float radiusFactor = radius / EarthRadius;
            float densityFactor = density; // Already normalized (Earth = 1)
            
            // Gravity proportional to radius × density
            return radiusFactor * densityFactor;
        }
        
        /// <summary>
        /// Calculate energy cost multiplier from gravity.
        /// Higher gravity = more energy needed to move.
        /// </summary>
        public static float GravityEnergyMultiplier(float gravityFactor)
        {
            // Linear scaling: 2x gravity = 2x energy cost
            return Math.Clamp(gravityFactor, 0.5f, 5f);
        }
        
        /// <summary>
        /// Calculate speed reduction from gravity.
        /// Higher gravity = slower movement.
        /// </summary>
        public static float GravitySpeedMultiplier(float gravityFactor)
        {
            // Inverse scaling: higher gravity = lower speed
            // 2x gravity = 0.7x speed
            return Math.Clamp(1f / MathF.Sqrt(gravityFactor), 0.3f, 2f);
        }
        
        #endregion
        
        #region Atmosphere Formulas
        
        /// <summary>
        /// Calculate atmosphere drag factor.
        /// Denser atmosphere = more resistance = slower movement but also more protection.
        /// </summary>
        public static float CalculateAtmosphereDrag(float atmosphere)
        {
            // Atmosphere 1 = Earth, 2 = Venus-like, 0 = Vacuum
            return 1f + atmosphere * 0.5f;
        }
        
        /// <summary>
        /// Calculate speed in atmosphere.
        /// Denser atmosphere = slower ground movement due to drag.
        /// </summary>
        public static float AtmosphereSpeedMultiplier(float atmosphere)
        {
            // Linear inverse: 2x atmosphere = 0.75x speed
            return Math.Clamp(1f / (1f + atmosphere * 0.5f), 0.3f, 1.5f);
        }
        
        /// <summary>
        /// Calculate radiation protection from atmosphere.
        /// Returns damage reduction factor (0-1).
        /// </summary>
        public static float AtmosphereRadiationProtection(float atmosphere)
        {
            // Thicker atmosphere = more protection
            // 0 atmosphere = 0 protection
            // 1 atmosphere = 0.7 protection
            // 2+ atmosphere = 0.95 protection
            return Math.Clamp(atmosphere * 0.7f, 0f, 0.95f);
        }
        
        /// <summary>
        /// Calculate temperature insulation from atmosphere.
        /// Returns how much atmosphere buffers temperature extremes.
        /// </summary>
        public static float AtmosphereTemperatureInsulation(float atmosphere)
        {
            // Denser atmosphere = more temperature stability
            return Math.Clamp(atmosphere * 0.3f, 0f, 0.6f);
        }
        
        #endregion
        
        #region Temperature Formulas

        /// <summary>
        /// Effective temperature from base slider and geology. Scale: 0 = Earth-like (-20..+20°C).
        /// High volcanic activity adds heat (outgassing, geothermal).
        /// </summary>
        public static float GetEffectiveTemperature(float baseTemperature, float geologicActivity)
        {
            float volcanicHeat = geologicActivity * 0.14f;
            return Math.Clamp(baseTemperature + volcanicHeat, 0f, 1f);
        }

        /// <summary>
        /// Effective atmosphere from base slider and geology. Scale: 0 = Earth standard atmosphere.
        /// High volcanic activity adds atmosphere (outgassing).
        /// </summary>
        public static float GetEffectiveAtmosphere(float baseAtmosphere, float geologicActivity)
        {
            float outgassing = geologicActivity * 0.2f;
            return Math.Clamp(baseAtmosphere + outgassing, 0f, 1f);
        }
        
        /// <summary>
        /// Convert temperature scale (0-1) to effective temperature.
        /// 0 = Extreme cold, 0.5 = Comfortable, 1 = Extreme heat
        /// </summary>
        public static float CalculateEffectiveTemperature(float tempScale, float atmosphere)
        {
            // Atmosphere provides insulation
            float insulation = AtmosphereTemperatureInsulation(atmosphere);
            
            // Extreme temperatures are moderated by atmosphere
            // Without atmosphere: temp ranges from -50 to +50 effective
            // With thick atmosphere: temp ranges from -20 to +20 effective
            
            float extremeTemp = (tempScale - 0.5f) * 100f; // -50 to +50
            float moderatedTemp = extremeTemp * (1f - insulation);
            
            return moderatedTemp;
        }
        
        /// <summary>
        /// Calculate energy drain from temperature stress.
        /// </summary>
        public static float TemperatureEnergyDrain(float tempScale, float atmosphere)
        {
            // Comfortable range = no drain
            if (tempScale >= ComfortableTempMin && tempScale <= ComfortableTempMax)
            {
                return 0f;
            }
            
            // Calculate distance from comfort zone
            float distance;
            if (tempScale < ComfortableTempMin)
            {
                distance = ComfortableTempMin - tempScale;
            }
            else
            {
                distance = tempScale - ComfortableTempMax;
            }
            
            // Atmosphere provides some protection
            float protection = AtmosphereTemperatureInsulation(atmosphere);
            float effectiveDistance = distance * (1f - protection);
            
            // Energy drain scales with distance
            return effectiveDistance * 0.5f; // Energy per second
        }
        
        /// <summary>
        /// Calculate cold damage (for extreme cold)
        /// </summary>
        public static float ColdDamage(float tempScale, float coldResistance)
        {
            if (tempScale >= 0.2f) return 0f;
            
            float coldIntensity = 0.2f - tempScale;
            return coldIntensity * (1f - coldResistance) * 0.3f;
        }
        
        /// <summary>
        /// Calculate heat damage (for extreme heat)
        /// </summary>
        public static float HeatDamage(float tempScale, float heatResistance)
        {
            if (tempScale <= 0.8f) return 0f;
            
            float heatIntensity = tempScale - 0.8f;
            return heatIntensity * (1f - heatResistance) * 0.3f;
        }
        
        #endregion
        
        #region Water Environment
        
        /// <summary>
        /// Calculate drowning damage for non-aquatic pawns.
        /// </summary>
        public static float DrowningDamage(float waterAffinity)
        {
            // No damage if adapted to water
            if (waterAffinity >= 0.5f) return 0f;
            
            // Partial damage for semi-aquatic
            return (0.5f - waterAffinity) * 0.5f;
        }
        
        /// <summary>
        /// Calculate swimming speed in water.
        /// </summary>
        public static float SwimmingSpeedMultiplier(float waterAffinity)
        {
            // Good swimmers: 0.8x speed
            // Non-swimmers: 0.3x speed
            return 0.3f + waterAffinity * 0.5f;
        }
        
        #endregion
        
        #region Geologic Activity
        
        /// <summary>
        /// Calculate hazard from geologic activity.
        /// High activity = volcanic eruptions, earthquakes.
        /// </summary>
        public static float GeologicHazard(float geologicActivity)
        {
            // 0-1: Safe
            // 1-2: Moderate risk
            // 2-3: High risk
            if (geologicActivity <= 1f) return 0f;
            
            return (geologicActivity - 1f) * 0.1f;
        }
        
        /// <summary>
        /// Calculate volcanic temperature bonus.
        /// </summary>
        public static float VolcanicHeatBonus(float geologicActivity, float height)
        {
            // Higher elevations near volcanoes are hotter
            if (geologicActivity < 1f) return 0f;
            
            float volcanicFactor = (geologicActivity - 1f) * 0.2f;
            return volcanicFactor * Math.Max(height, 0f);
        }
        
        #endregion
        
        #region Pawn Genome Defaults
        
        /// <summary>Default speed for new pawn</summary>
        public const float DefaultSpeed = 1.0f;
        
        /// <summary>Default cold resistance (0-1)</summary>
        public const float DefaultColdResistance = 0.5f;
        
        /// <summary>Default heat resistance (0-1)</summary>
        public const float DefaultHeatResistance = 0.5f;
        
        /// <summary>Default water affinity (0-1)</summary>
        public const float DefaultWaterAffinity = 0.2f;
        
        /// <summary>Default vision range</summary>
        public const float DefaultVisionRange = 0.2f;
        
        /// <summary>Default leg count</summary>
        public const int DefaultLegCount = 4;
        
        /// <summary>Default size</summary>
        public const float DefaultSize = 1.0f;
        
        /// <summary>Minimum legs</summary>
        public const int MinLegs = 0;
        
        /// <summary>Maximum legs</summary>
        public const int MaxLegs = 12;
        
        /// <summary>Minimum size</summary>
        public const float MinSize = 0.5f;
        
        /// <summary>Maximum size</summary>
        public const float MaxSize = 2.0f;
        
        #endregion
        
        #region Population Limits
        
        /// <summary>Maximum pawn population</summary>
        public const int MaxPopulation = 200;
        
        /// <summary>Initial population count</summary>
        public const int InitialPopulation = 10;
        
        /// <summary>Food spawn rate per second</summary>
        public const float FoodSpawnRate = 0.7f;
        
        /// <summary>Maximum food items on planet</summary>
        public const int MaxFoodItems = 70;
        
        #endregion
    }
}