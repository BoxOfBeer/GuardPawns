# Цель
Внести минимальные правки для единого источника истины поверхности и стабильной привязки пешек к рельефу планеты.

# Контекст
Входные данные: результаты аудита (рассинхрон surface/world formulas, отсутствие единого surface sampler, неустойчивый Random в Pawn).
Ограничения: без крупного рефакторинга и без изменения визуальной идеи.
Ожидаемый артефакт: минимальный набор изменений в Core/Game + проверка сборки.

# План
1. Добавить единый SurfaceSample API в PawnAgent.
2. Перевести позиционирование рендера пешек на surface point + offset.
3. После шага движения всегда перепроецировать Pawn к направлению поверхности.
4. Сделать базовую runtime-валидацию привязки пешек к поверхности.
5. Убрать new Random() из частых путей Pawn.

# Изменения (по файлам)
- SpaceBall/Core/PawnAgent.cs — добавлен SurfaceSample, методы SampleSurface/GetSurfacePoint/GetSurfaceRadius, slope estimation, репроекция позиции после шага, проверка blocked movement по воде/уклону, единая world-позиция через surface+offset, debug-валидация anchoring.
- SpaceBall/Core/Pawn.cs — введён общий Random для экземпляра; убран new Random() из SetRandomMovement/Reproduce.
- SpaceBall/Core/WorldConstants.cs — добавлены константы PawnSurfaceOffsetFactor/MinPawnSurfaceOffset.
- SpaceBall/Game.cs — добавлен периодический runtime-аудит привязки пешек к поверхности через ValidateSurfaceAnchoring().
- agents.txt (root, SpaceBall/, SpaceBall/Core/, docs/, docs/codex/) — добавлены краткие описания каталогов.

# Решения/обоснования
- Единый sampler устраняет дубли формул радиуса/позиции поверхности.
- Репроекция после каждого шага стабилизирует положение пешек на сфере и исключает накопление ошибки.
- Offset задан через константы мира и не накапливается по кадрам.
- Проверка slope сделана упрощённо (градиент по двум тангенсам), чтобы не усложнять систему.

# Риски
- Порог уклона (maxSurfaceSlope) может потребовать калибровки на экстремальных seed.
- Runtime-лог в Game может быть шумным при намеренно агрессивных параметрах рельефа.
- Детерминизм полной симуляции всё ещё ограничен в местах, где new Random() используется в других классах (не в критическом контуре surface anchoring).

# Следующие шаги
1. Прогнать ручной runtime smoke на нескольких seed и радиусах/шуме.
2. При необходимости вынести SurfaceSample в отдельный сервис для переиспользования вне PawnAgent.
3. На 2-м этапе привести оставшиеся new Random() в PawnGenome/Genome к общему источнику seed.

# Саморевью (альтернативы)
- Полный рефактор Pawn в «координаты как (direction,height)»: отложен как более рискованный для первого этапа.
- Полный перенос логики в отдельный PlanetSurfaceService: отложен, чтобы сохранить минимальные изменения.

# Откат
- git restore SpaceBall/Core/PawnAgent.cs SpaceBall/Core/Pawn.cs SpaceBall/Core/WorldConstants.cs SpaceBall/Game.cs agents.txt SpaceBall/agents.txt SpaceBall/Core/agents.txt docs/agents.txt docs/codex/agents.txt
