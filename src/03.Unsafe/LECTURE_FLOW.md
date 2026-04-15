# Module 03 Lecture Flow

Этот compact demo надо рассказывать не как "смотрите, сколько тут unsafe", а как историю про систему с понятной задачей и двумя разными классами ошибок.

## Короткий сюжет

У нас есть managed-приложение, которое готовит и отправляет telemetry frames.

Почему часть системы native:

- нужен точный контроль wire layout,
- есть worker thread, который обрабатывает кадры позже,
- есть работа с unmanaged heap,
- нужен zero-copy путь,
- есть явный ABI-контракт между managed и native частями.

Главная мысль на старте:

- managed-код удобен для orchestration и общей логики,
- native-код нужен там, где важны layout, ownership, lifetime и работа с сырой памятью,
- unsafe и interop здесь не "для демонстрации", а потому что задача реально такая.

## Что Показывать В Первом Проходе

Показывать только 4 файла:

1. [Program.cs](c:/git/csharp14-systems-memory/src/03.Unsafe/Program.cs)
2. [CompactInteropCorruptionDemo.cs](c:/git/csharp14-systems-memory/src/03.Unsafe/Examples/CompactInteropCorruptionDemo.cs)
3. [NativeMethods.cs](c:/git/csharp14-systems-memory/src/03.Unsafe/Interop/NativeMethods.cs)
4. [CompactTelemetryNative.cpp](c:/git/csharp14-systems-memory/src/03.Unsafe/native/CompactTelemetryNative.cpp)

Этого достаточно, чтобы студент понял систему целиком.

## Что Не Показывать Сразу

Не надо на первом проходе разбирать:

- детали `.csproj`,
- весь `vcxproj`,
- каждую P/Invoke сигнатуру по отдельности,
- все helper-методы подряд,
- каждую строку worker-loop,
- детали cleanup до того, как они понадобятся по сюжету.

Фраза для лекции:

"Сейчас нам важны только форма данных, граница managed/native, и кто владеет памятью. Остальное пока фон."

## Порядок Подачи

### 1. Начать С `Program.cs`

Что показать:

- есть режимы `healthy`, `bugA`, `bugB`, `fixed`,
- сценарий очень короткий:
  - построили frame,
  - отправили в native,
  - получили completion.

Что проговорить:

- это не большой фреймворк,
- это маленький, но правдоподобный кусок production-adjacent кода,
- уже здесь видно, что система асинхронная и что symptom может появиться позже.

### 2. Перейти К `FrameCodec` В `CompactInteropCorruptionDemo.cs`

Показать только эти вещи:

- packed `TelemetryWireHeader`,
- `fixed byte Tag[8]`,
- `stackalloc` для `TelemetrySample`,
- `MemoryMarshal.AsBytes`,
- `Unsafe.WriteUnaligned`,
- `Unsafe.AddByteOffset`,
- `nuint` и byte-based stepping.

Что проговорить:

- здесь формируются правильные байты,
- это место про layout и сериализацию,
- тут важно различать "шаг по элементам" и "шаг по байтам".

Ключевая формулировка:

"До interop-границы мы должны чётко понимать, какие именно байты отправляем."

### 3. Перейти К `TelemetrySession` И `NativeMethods`

Что показать:

- `LibraryImport`,
- callback через `delegate* unmanaged`,
- `SafeHandle` для session,
- два пути: `SubmitCopyAsync` и `SubmitZeroCopyAsync`,
- `NativeBuffer` как владелец native memory.

Что проговорить:

- это место не про layout, а про контракт,
- copy path и zero-copy path отличаются не только производительностью,
- они отличаются ownership/lifetime требованиями.

Ключевая формулировка:

"На interop-границе всегда надо ответить на четыре вопроса: какие байты, по какому адресу, кто владелец, и как долго адрес живёт."

### 4. Потом Только Открыть `CompactTelemetryNative.cpp`

Показывать не весь файл, а только 4 смысловых блока:

1. packed `ct_wire_header`
2. `ct_session_submit_copy`
3. `ct_session_submit_zero_copy`
4. `ct_process_pending`

Что проговорить:

- native side хранит очередь,
- worker later обрабатывает кадр,
- callback возвращает completion обратно в managed,
- это и создаёт realistic delayed-failure behavior.

## Как Показывать Healthy Path

Сначала запуск:

```powershell
dotnet run --project src\03.Unsafe -- --mode healthy
```

Что сказать:

- frame собирается корректно,
- copy path работает корректно,
- worker later валидирует payload и завершает операцию,
- в healthy path всё хорошо и по layout, и по ownership.

Главная цель healthy path:

- зафиксировать baseline,
- чтобы дальше отличать "что сломалось" от "как оно должно было работать".

## Как Подводить К Bug A

Запуск:

```powershell
dotnet run --project src\03.Unsafe -- --mode bugA
```

Что ожидать от студентов:

- они сначала заподозрят parser,
- checksum,
- managed serializer,
- worker thread.

Как вести:

1. Показать, что frame на managed side выглядит нормально.
2. Показать, что `submitted length = 88`, а `copied length = 96`.
3. Показать `ct_wire_header` и `ct_buggy_header_layout`.
4. Показать, что проблема не в lifetime, а в неверном size/layout calculation.

Ключевая формулировка:

"Это классическая история wrong bytes at the right address."

## Как Подводить К Bug B

Запуск:

```powershell
dotnet run --project src\03.Unsafe -- --mode bugB
```

Что ожидать от студентов:

- "parser flaky",
- "worker race",
- "checksum почему-то не сошёлся",
- "callback что-то сломал".

Как вести:

1. Показать, что frame до отправки валиден.
2. Показать разницу между copy и zero-copy path.
3. Показать, что `NativeBuffer.Dispose()` завершает lifetime owner'а.
4. Показать, что native сохранил только raw pointer и пошёл обрабатывать позже.
5. Сравнить с `fixed`, где owner удерживается до completion.

Ключевая формулировка:

"Это уже не wrong bytes. Это address used after lifetime ended."

## Как Показать Fixed

Запуск:

```powershell
dotnet run --project src\03.Unsafe -- --mode fixed
```

Что проговорить:

- zero-copy сам по себе не зло,
- unsafe сам по себе не зло,
- проблема была не в том, что указатели вообще использовались,
- проблема была в нарушенном ownership/lifetime contract.

Ключевая формулировка:

"Правильный low-level код держится не на осторожности, а на явно сформулированных инвариантах."

## Что Должен Унести Студент После Первой Части

После demo, но до отдельной лекции про WinDbg, студент должен уже понимать:

- где формируются байты,
- где проходит managed/native boundary,
- чем отличаются copy и zero-copy,
- почему `bugA` и `bugB` это разные классы проблем,
- почему crash site или symptom site могут не совпадать с root cause.

## Одной Фразой Вся История

"Мы интегрировали native packet processor в managed приложение, потому что нам одновременно нужны удобство .NET и низкоуровневый контроль над layout, памятью и lifetime. На этой границе и возникли два разных бага: один сломал размер/layout и записал не те байты, а второй сломал ownership/lifetime и сохранил адрес дольше, чем жил его владелец."
