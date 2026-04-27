# Цель
Снять остаточные compile-риски по символам WorldConstants и стабилизировать сборку фикса между разными ревизиями локальных копий.

# Контекст
Входные данные, ограничения, ожидаемый артефакт:
- Вход: пользовательский лог с CS0117 по `PawnVisualSurfaceOffset`/`PawnModelBaseOffset` и CS0103 по `_heightmap`.
- Ограничения: не откатывать SurfaceSampler-архитектуру; минимальная правка.
- Артефакт: `PawnAgent` компилируется без жёсткой зависимости от новых символов `WorldConstants`.

# План
1. Убрать прямую зависимость `PawnAgent` от новых констант `WorldConstants`.
2. Оставить модель offset в `PawnAgent` как локальные константы.
3. Проверить, что `_heightmap` больше нигде не используется в `PawnAgent`.

# Изменения (по файлам)
- SpaceBall/Core/PawnAgent.cs — добавлены локальные `PawnVisualSurfaceOffsetFactor` и `PawnModelBaseOffset`; расчёт offset переведён на них.
- agents.txt, SpaceBall/agents.txt, SpaceBall/Core/agents.txt, docs/agents.txt, docs/codex/agents.txt — обновлены краткие описания каталогов.

# Решения/обоснования
- Такой шаг сохраняет поведение визуального offset, но устраняет жесткую компиляционную связь с конкретной версией `WorldConstants`.
- Ошибка `_heightmap` закрыта тем, что `PawnAgent` использует только `_surfaceSampler` для высоты.

# Риски
- Локальные константы в `PawnAgent` и значения в `WorldConstants` могут разъехаться при будущих изменениях.

# Следующие шаги
1. При стабилизации ветки вернуть чтение offset из единого конфига/констант с гарантированным обновлением всех файлов.
2. Прогнать локальную сборку у пользователя на актуальном HEAD.

# Саморевью (альтернативы)
- Оставить только `WorldConstants` и требовать полный pull: отвергнуто для быстрого восстановления сборки.
- Жёстко откатить offset-логику: отвергнуто, т.к. теряется фикс pivot у 3D пешек.

# Откат
- git restore SpaceBall/Core/PawnAgent.cs agents.txt SpaceBall/agents.txt SpaceBall/Core/agents.txt docs/agents.txt docs/codex/agents.txt
