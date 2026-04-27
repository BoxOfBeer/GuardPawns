# Цель
Исправить остаточный баг «пешка под визуальной поверхностью» через синхронизацию CPU/GPU семплинга, blend transition и model visual offset.

# Контекст
Входные данные, ограничения, ожидаемый артефакт:
- Вход: замечание по предыдущему PR, что баг сохраняется.
- Ограничения: минимальные правки, без полного рефакторинга проекта.
- Артефакт: единый SurfaceSampler, корректный blend current/next, debug-метрики по «below surface».

# План
1. Реализовать отдельный SurfaceSampler с формулой UV/height как в shader.
2. Перевести PawnAgent на SurfaceSampler.
3. Передавать в PawnAgent current+next+blendFactor в transition.
4. Добавить разделение visual/model offsets и debug summary.

# Изменения (по файлам)
- SpaceBall/Core/SurfaceSampler.cs — новый единый sampler поверхности (CPU-эквивалент shader, поддержка blend current/next).
- SpaceBall/Core/PawnAgent.cs — интеграция SurfaceSampler; SetSurfaceState(current,next,blend,disp); debug struct PawnSurfaceDebugInfo; offsets с учётом размера модели.
- SpaceBall/Core/WorldConstants.cs — добавлены PawnVisualSurfaceOffset и PawnModelBaseOffset.
- SpaceBall/Game.cs — во всех точках обновления передаётся current/next/blend в PawnAgent; расширен debug лог метриками поверхности.

# Решения/обоснования
- Исправлен критичный UV mismatch по долготе: CPU перешёл на uvX из atan2(z,x) в диапазоне [0..1), совпадающем с mesh aUV.x.
- CPU-сэмплинг теперь учитывает blendFactor между current/next, как vertex shader.
- Для 3D модели добавлен отдельный model-base offset, зависящий от размера пешки.

# Риски
- После исправления UV карта воды/суши для симуляции может «сместиться» относительно предыдущего поведения (но теперь совпадает с визуалом).
- Потребуется калибровка новых offset-констант при экстремальных размерах модели.

# Следующие шаги
1. Прогнать визуальный smoke при активном transition и 3D pawn.
2. При необходимости вынести debug метрики в ImGui-панель (сейчас лог в GameLog).
3. Добавить автотест на совпадение CPU/GPU sampling в контрольных точках сферы.

# Саморевью (альтернативы)
- Отключение blend transition: отклонено (маскирует, но не решает рассинхрон).
- Увеличение одного глобального offset: отклонено (не учитывает pivot/scale модели).

# Откат
- git restore SpaceBall/Core/SurfaceSampler.cs SpaceBall/Core/PawnAgent.cs SpaceBall/Core/WorldConstants.cs SpaceBall/Game.cs
