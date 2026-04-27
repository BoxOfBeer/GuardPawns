using System;
using System.Collections.Generic;

namespace SpaceDNA.Core
{
    /// <summary>Suitability of a trait for given planet.</summary>
    public enum TraitSuitability
    {
        Forbidden,
        EdgeCase,
        Allowed
    }

    /// <summary>
    /// High-level baseline traits. Их ~50, это «словарь» того, что может быть у вида.
    /// Конкретная реализация мешей/анимаций будет поверх этих флагов.
    /// </summary>
    [Flags]
    public enum BaseTrait : ulong
    {
        None = 0,

        // Movement (0–9)
        GroundWalker         = 1UL << 0,
        Sprinter             = 1UL << 1,
        Climber              = 1UL << 2,
        Burrower             = 1UL << 3,
        SwimmerSurface       = 1UL << 4,
        SwimmerDeep          = 1UL << 5,
        Amphibious           = 1UL << 6,
        Glider               = 1UL << 7,
        TrueFlight           = 1UL << 8,
        HoverFlight          = 1UL << 9,

        // Environment tolerances (10–19)
        WarmBlooded          = 1UL << 10,
        ColdBlooded          = 1UL << 11,
        VacuumAdapted        = 1UL << 12,
        ThinAirAdapted       = 1UL << 13,
        DenseAirAdapted      = 1UL << 14,
        DeepSeaPressure      = 1UL << 15,
        DesertAdapted        = 1UL << 16,
        TundraAdapted        = 1UL << 17,
        VolcanicAdapted      = 1UL << 18,
        Radiotolerant        = 1UL << 19,

        // Diet (20–27)
        Herbivore            = 1UL << 20,
        Carnivore            = 1UL << 21,
        Omnivore             = 1UL << 22,
        Scavenger            = 1UL << 23,
        MineralEater         = 1UL << 24,
        Photosynthesizer     = 1UL << 25,
        FilterFeeder         = 1UL << 26,
        Parasitic            = 1UL << 27,

        // Body plan (28–37)
        HasInternalSkeleton  = 1UL << 28,
        HasExoskeleton       = 1UL << 29,
        HasShell             = 1UL << 30,
        RegeneratesLimbs     = 1UL << 31,
        MultipleHearts       = 1UL << 32,
        MultipleBrains       = 1UL << 33,
        ManyEyes             = 1UL << 34,
        TailPropulsion       = 1UL << 35,
        WingLikeLimbs        = 1UL << 36,
        FinLikeLimbs         = 1UL << 37,

        // Behaviour / cognition (38–47)
        PackHunter           = 1UL << 38,
        Territorial          = 1UL << 39,
        Nomadic              = 1UL << 40,
        NestBuilder          = 1UL << 41,
        ToolUser             = 1UL << 42,
        Nocturnal            = 1UL << 43,
        Diurnal              = 1UL << 44,
        AmbushPredator       = 1UL << 45,
        Migratory            = 1UL << 46,
        SocialHiveMind       = 1UL << 47,

        // Origin / composition (48–55)
        OrganicLife          = 1UL << 48,
        SiliconBased         = 1UL << 49,
        FungalLike           = 1UL << 50,
        CrystalBased         = 1UL << 51,
        MachineHybrid        = 1UL << 52,
        SymbioticComposite   = 1UL << 53,
        PhotosyntheticSkin   = 1UL << 54,
        GasBagBody           = 1UL << 55
    }

    /// <summary>Reduced snapshot of планеты для логики существ.</summary>
    public readonly struct PlanetSnapshot
    {
        public readonly float Temperature;   // 0–1
        public readonly float Atmosphere;    // 0–1
        public readonly float Gravity;       // относительный коэффициент
        public readonly float WaterPotential;// 0–1, эвристика «сколько воды/океанов»

        public PlanetSnapshot(float temperature, float atmosphere, float gravity, float waterPotential)
        {
            Temperature = temperature;
            Atmosphere = atmosphere;
            Gravity = gravity;
            WaterPotential = Math.Clamp(waterPotential, 0f, 1f);
        }

        public static PlanetSnapshot FromConfig(PlanetConfig cfg)
        {
            float gravity = WorldConstants.CalculateGravityFactor(cfg.Radius, cfg.Density);
            float water = 0.3f + cfg.GeologicActivity * 0.4f;
            water = Math.Clamp(water, 0f, 1f);
            float temp = WorldConstants.GetEffectiveTemperature(cfg.Temperature, cfg.GeologicActivity);
            float atm = WorldConstants.GetEffectiveAtmosphere(cfg.Atmosphere, cfg.GeologicActivity);
            return new PlanetSnapshot(temp, atm, gravity, water);
        }
    }

    /// <summary>
    /// Один «базовый вид» на планете: набор traits + мастер‑ДНК + оценка пригодности.
    /// </summary>
    public sealed class SpeciesBlueprint
    {
        public BaseTrait Traits { get; }
        public PlanetSnapshot Planet { get; }
        public DnaSequence MasterSequence { get; }
        private readonly Dictionary<BaseTrait, TraitSuitability> _suitability;

        private SpeciesBlueprint(BaseTrait traits, PlanetSnapshot planet, DnaSequence master,
            Dictionary<BaseTrait, TraitSuitability> suitability)
        {
            Traits = traits;
            Planet = planet;
            MasterSequence = master;
            _suitability = suitability;
        }

        /// <summary>Создать вид, подходящий под текущую планету. optionalOverride позволяет навязать свою ДНК.</summary>
        public static SpeciesBlueprint ForPlanet(PlanetConfig config, Random rnd, DnaSequence? optionalOverride = null)
        {
            var snapshot = PlanetSnapshot.FromConfig(config);
            var traits = BaseTrait.None;
            var suitability = new Dictionary<BaseTrait, TraitSuitability>();

            void AddIfAllowed(BaseTrait t)
            {
                var s = EvaluateTrait(t, snapshot);
                if (s == TraitSuitability.Forbidden) return;
                // EdgeCase — оставляем шанс, но не гарантированно
                if (s == TraitSuitability.EdgeCase && rnd.NextDouble() < 0.3) return;
                traits |= t;
                suitability[t] = s;
            }

            // Базовое движение — почти всегда ходим
            AddIfAllowed(BaseTrait.GroundWalker);

            // Вода
            if (snapshot.WaterPotential > 0.25f)
            {
                AddIfAllowed(BaseTrait.SwimmerSurface);
                if (snapshot.WaterPotential > 0.6f)
                    AddIfAllowed(BaseTrait.SwimmerDeep);
                AddIfAllowed(BaseTrait.Amphibious);
            }

            // Полёт / планирование
            AddIfAllowed(BaseTrait.Glider);
            AddIfAllowed(BaseTrait.TrueFlight);

            // Тепло/холод
            if (snapshot.Temperature > 0.55f)
                AddIfAllowed(BaseTrait.WarmBlooded);
            else
                AddIfAllowed(BaseTrait.ColdBlooded);

            // Атмосфера и вакуум
            AddIfAllowed(BaseTrait.VacuumAdapted);
            AddIfAllowed(BaseTrait.ThinAirAdapted);
            AddIfAllowed(BaseTrait.DenseAirAdapted);

            // Климатические адаптации
            AddIfAllowed(BaseTrait.DesertAdapted);
            AddIfAllowed(BaseTrait.TundraAdapted);
            AddIfAllowed(BaseTrait.VolcanicAdapted);

            // Диета зависит от климата и атмосферы
            AddIfAllowed(BaseTrait.Herbivore);
            AddIfAllowed(BaseTrait.Carnivore);
            AddIfAllowed(BaseTrait.Omnivore);
            if (snapshot.Atmosphere < 0.25f || snapshot.Temperature < 0.2f)
                AddIfAllowed(BaseTrait.MineralEater);
            if (snapshot.Atmosphere > 0.2f && snapshot.Temperature > 0.2f && snapshot.WaterPotential > 0.2f)
                AddIfAllowed(BaseTrait.Photosynthesizer);

            // Тело
            AddIfAllowed(BaseTrait.HasInternalSkeleton);
            AddIfAllowed(BaseTrait.HasExoskeleton);
            AddIfAllowed(BaseTrait.HasShell);
            AddIfAllowed(BaseTrait.RegeneratesLimbs);
            AddIfAllowed(BaseTrait.ManyEyes);

            // Поведение
            AddIfAllowed(BaseTrait.PackHunter);
            AddIfAllowed(BaseTrait.Territorial);
            AddIfAllowed(BaseTrait.Nomadic);
            AddIfAllowed(BaseTrait.NestBuilder);
            AddIfAllowed(BaseTrait.ToolUser);
            AddIfAllowed(BaseTrait.Nocturnal);
            AddIfAllowed(BaseTrait.Diurnal);

            // Происхождение
            AddIfAllowed(BaseTrait.OrganicLife);
            AddIfAllowed(BaseTrait.SiliconBased);
            AddIfAllowed(BaseTrait.FungalLike);

            // Если ничего не набралось (патологический случай), принудительно даём органическую ходячую форму
            if (traits == BaseTrait.None)
            {
                traits = BaseTrait.GroundWalker | BaseTrait.OrganicLife;
                suitability[BaseTrait.GroundWalker] = TraitSuitability.Allowed;
                suitability[BaseTrait.OrganicLife] = TraitSuitability.Allowed;
            }

            // Мастер‑ДНК: либо пользовательская, либо случайная
            DnaSequence master = optionalOverride != null && optionalOverride.IsViable
                ? optionalOverride
                : DnaSequence.Random(segmentCount: 12 + rnd.Next(9), rnd: rnd);

            return new SpeciesBlueprint(traits, snapshot, master, suitability);
        }

        /// <summary>Создать ДНК особи этого вида (master + лёгкая мутация).</summary>
        public DnaSequence CreateIndividualDna(Random rnd)
        {
            return MasterSequence.Mutate(0.25f, rnd);
        }

        /// <summary>Получить оценку пригодности конкретного trait на этой планете.</summary>
        public TraitSuitability GetSuitability(BaseTrait trait)
        {
            return _suitability.TryGetValue(trait, out var s) ? s : EvaluateTrait(trait, Planet);
        }

        /// <summary>Короткое текстовое описание для UI.</summary>
        public IEnumerable<string> GetSummaryLines()
        {
            yield return $"Temp={Planet.Temperature:F2} Atm={Planet.Atmosphere:F2} Grav={Planet.Gravity:F2} Water={Planet.WaterPotential:F2}";

            static string Mark(TraitSuitability s) =>
                s switch
                {
                    TraitSuitability.Forbidden => "[X]",
                    TraitSuitability.EdgeCase  => "[~]",
                    _                           => "[+]"
                };

            // Movement
            if (Traits.HasFlag(BaseTrait.GroundWalker))
                yield return $"{Mark(GetSuitability(BaseTrait.GroundWalker))} ground walker";
            if (Traits.HasFlag(BaseTrait.SwimmerSurface) || Traits.HasFlag(BaseTrait.SwimmerDeep) || Traits.HasFlag(BaseTrait.Amphibious))
                yield return $"{Mark(GetSuitability(BaseTrait.SwimmerSurface))} water‑capable (swim/amphibious)";
            if (Traits.HasFlag(BaseTrait.Glider) || Traits.HasFlag(BaseTrait.TrueFlight))
                yield return $"{Mark(GetSuitability(BaseTrait.TrueFlight))} flight / gliding";

            // Thermal / air
            if (Traits.HasFlag(BaseTrait.WarmBlooded))
                yield return $"{Mark(GetSuitability(BaseTrait.WarmBlooded))} warm‑blooded";
            if (Traits.HasFlag(BaseTrait.ColdBlooded))
                yield return $"{Mark(GetSuitability(BaseTrait.ColdBlooded))} cold‑blooded";
            if (Traits.HasFlag(BaseTrait.VacuumAdapted))
                yield return $"{Mark(GetSuitability(BaseTrait.VacuumAdapted))} vacuum‑adapted";

            // Diet
            if (Traits.HasFlag(BaseTrait.Herbivore) || Traits.HasFlag(BaseTrait.Photosynthesizer))
                yield return $"{Mark(GetSuitability(BaseTrait.Herbivore))} plant/energy eater";
            if (Traits.HasFlag(BaseTrait.Carnivore))
                yield return $"{Mark(GetSuitability(BaseTrait.Carnivore))} carnivore";
            if (Traits.HasFlag(BaseTrait.MineralEater))
                yield return $"{Mark(GetSuitability(BaseTrait.MineralEater))} mineral eater";

            // Origin
            if (Traits.HasFlag(BaseTrait.OrganicLife))
                yield return $"{Mark(GetSuitability(BaseTrait.OrganicLife))} organic origin";
            if (Traits.HasFlag(BaseTrait.SiliconBased))
                yield return $"{Mark(GetSuitability(BaseTrait.SiliconBased))} silicon‑based mix";
        }

        /// <summary>
        /// Агент принятия решения: этот trait допустим, пограничен или невозможен на такой планете?
        /// </summary>
        public static TraitSuitability EvaluateTrait(BaseTrait trait, PlanetSnapshot p)
        {
            switch (trait)
            {
                case BaseTrait.TrueFlight:
                case BaseTrait.HoverFlight:
                    if (p.Atmosphere < 0.08f) return TraitSuitability.Forbidden;        // почти вакуум
                    if (p.Atmosphere < 0.20f) return TraitSuitability.EdgeCase;         // «псевдо‑крылья» / летучая рыба
                    return TraitSuitability.Allowed;

                case BaseTrait.Glider:
                    if (p.Atmosphere < 0.04f) return TraitSuitability.Forbidden;
                    if (p.Atmosphere < 0.12f) return TraitSuitability.EdgeCase;
                    return TraitSuitability.Allowed;

                case BaseTrait.SwimmerSurface:
                case BaseTrait.SwimmerDeep:
                case BaseTrait.Amphibious:
                    if (p.WaterPotential < 0.1f) return TraitSuitability.EdgeCase;      // редкие озёра
                    return TraitSuitability.Allowed;

                case BaseTrait.WarmBlooded:
                    if (p.Temperature < 0.08f) return TraitSuitability.Forbidden;       // вечная мерзлота
                    if (p.Temperature < 0.22f) return TraitSuitability.EdgeCase;        // выживают, но тяжело
                    return TraitSuitability.Allowed;

                case BaseTrait.ColdBlooded:
                    if (p.Temperature > 0.9f) return TraitSuitability.EdgeCase;         // перегрев
                    return TraitSuitability.Allowed;

                case BaseTrait.VacuumAdapted:
                    if (p.Atmosphere < 0.02f) return TraitSuitability.Allowed;
                    if (p.Atmosphere < 0.08f) return TraitSuitability.EdgeCase;
                    return TraitSuitability.Forbidden;

                case BaseTrait.MineralEater:
                    if (p.Atmosphere < 0.15f || p.Temperature < 0.18f)
                        return TraitSuitability.Allowed;
                    return TraitSuitability.EdgeCase;

                case BaseTrait.Photosynthesizer:
                    if (p.Atmosphere < 0.12f) return TraitSuitability.Forbidden;
                    return TraitSuitability.Allowed;

                case BaseTrait.DesertAdapted:
                    if (p.WaterPotential < 0.2f && p.Temperature > 0.4f)
                        return TraitSuitability.Allowed;
                    return TraitSuitability.EdgeCase;

                case BaseTrait.TundraAdapted:
                    if (p.Temperature < 0.25f && p.WaterPotential > 0.1f)
                        return TraitSuitability.Allowed;
                    return TraitSuitability.EdgeCase;

                case BaseTrait.VolcanicAdapted:
                    if (p.Temperature > 0.7f || p.WaterPotential < 0.15f)
                        return TraitSuitability.Allowed;
                    return TraitSuitability.EdgeCase;

                case BaseTrait.OrganicLife:
                    // Практически везде допустимо, кроме совсем экстремальных случаев
                    if (p.Temperature < 0.02f || p.Temperature > 0.98f)
                        return TraitSuitability.EdgeCase;
                    return TraitSuitability.Allowed;

                case BaseTrait.SiliconBased:
                    // На холодных/горячих планетах более вероятно
                    if (p.Temperature < 0.15f || p.Temperature > 0.85f)
                        return TraitSuitability.Allowed;
                    return TraitSuitability.EdgeCase;
            }

            // Остальные пока считаем допустимыми — детализируем по мере развития проекта.
            return TraitSuitability.Allowed;
        }
    }
}

