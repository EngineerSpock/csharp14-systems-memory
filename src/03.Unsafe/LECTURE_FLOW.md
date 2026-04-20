# Module 03 Lecture Flow

Этот compact demo надо рассказывать не как "смотрите, сколько тут unsafe", а как историю про быстрый interop-path, в котором нарушение контракта проявляется позже и не там, где был сделан неправильный шаг.

## Короткий сюжет

У нас есть managed-приложение, которое формирует telemetry frame и отдаёт его native worker-у.

Почему часть системы native:

- нужен точный wire layout,
- есть асинхронный worker thread,
- есть работа с unmanaged memory,
- нужен copy path и более быстрый zero-copy path,
- есть явный контракт на границе managed/native.

Главная мысль на старте:

- managed-код отвечает за orchestration и формирование кадра,
- native-код выступает как внешний packet processor с понятным контрактом,
- ошибка возникает не потому, что "unsafe сам по себе опасен", а потому что на межъязыковой границе легко сломать адрес, длину или lifetime.

## Что Показывать В Первом Проходе

Показывать только эти файлы:

1. [Program.cs](c:/git/csharp14-systems-memory/src/03.Unsafe/Program.cs)
2. [CompactInteropCorruptionDemo.cs](c:/git/csharp14-systems-memory/src/03.Unsafe/Examples/CompactInteropCorruptionDemo.cs)
3. [TelemetrySession.cs](c:/git/csharp14-systems-memory/src/03.Unsafe/Interop/TelemetrySession.cs)
4. [NativeMethods.cs](c:/git/csharp14-systems-memory/src/03.Unsafe/Interop/NativeMethods.cs)

Этого достаточно, чтобы студент понял систему на уровне контракта, не зная деталей C++ реализации.

## Что Не Показывать Сразу

Не надо на первом проходе разбирать:

- весь native-код,
- детали `.csproj` и `vcxproj`,
- каждую P/Invoke сигнатуру по отдельности,
- внутренности worker loop,
- cleanup-путь до того, как по сюжету станет понятно, что проблема связана с lifetime.

Фраза для лекции:

"Сейчас нам важны только байты, адрес, длина и время жизни памяти. Всё остальное пока фон."

## Порядок Подачи

### 1. Начать С `Program.cs`

Что показать:

- есть два режима: `copy` и `fast`,
- сценарий очень короткий:
  - собрали frame,
  - отправили его в native,
  - получили completion позже.

Что проговорить:

- это небольшой, но production-adjacent interop-сценарий,
- copy path нужен как baseline,
- fast path нужен как "оптимизированный" путь, где контракт становится жёстче.

### 2. Перейти К `CompactInteropCorruptionDemo.cs`

Что показать:

- `copy` использует обычный managed frame,
- `fast` идёт через отдельный session API,
- в обоих случаях снаружи код выглядит невинно: мы не видим никаких "опасных" явных действий вроде ручного free сразу после submit.

Ключевая формулировка:

"Снаружи fast path выглядит как нормальная оптимизация. Это важно: источник бага не должен бросаться в глаза в demo runner-е."

### 3. Показать `FrameCodec`

Можно открыть [FrameCodec.cs](c:/git/csharp14-systems-memory/src/03.Unsafe/Telemetry/FrameCodec.cs) и быстро пройтись только по этим вещам:

- packed `TelemetryWireHeader`,
- `fixed byte Tag[8]`,
- `stackalloc` для `TelemetrySample`,
- `MemoryMarshal.AsBytes`,
- `Unsafe.WriteUnaligned`,
- byte-based stepping.

Что проговорить:

- до interop-границы кадр формируется корректно,
- managed side умеет показать валидный header и checksum ещё до submit,
- это важно для дальнейшего расследования: frame выглядел правильным до передачи в native.

### 4. Перейти К `TelemetrySession`

Здесь ключевое место сюжета.

Что показать:

- `LibraryImport`,
- callback через `delegate* unmanaged`,
- `TaskCompletionSource` как managed completion bridge,
- `SubmitCopyAsync`,
- `SubmitFastAsync`.

Что проговорить:

- `copy` и `fast` отличаются не синтаксисом, а контрактом,
- на interop-границе всегда надо ответить на четыре вопроса:
  - какие байты,
  - по какому адресу,
  - кто владелец памяти,
  - сколько этот адрес обязан жить.

## Как Показывать Baseline

Запуск:

```powershell
dotnet run --project src\03.Unsafe -- copy
```

Что сказать:

- frame собирается корректно,
- completion приходит со статусом `Ok`,
- это baseline, от которого потом удобно отклоняться.

Главная цель baseline:

- показать, что сам native processor не "рандомно сломан",
- зафиксировать, как выглядит нормальный кадр и нормальный completion.

## Как Показывать Broken Fast Path

Запуск:

```powershell
dotnet run --project src\03.Unsafe -- fast
```

Что видеть без page heap:

- managed-side описание frame выглядит валидно,
- completion приходит как `BadHeader`.

Что говорить:

- symptom появляется позже, на worker thread,
- это не доказывает, что worker и есть источник проблемы,
- на старте мы знаем только одно: до native boundary frame выглядел корректно.

## Как Подводить К Clip 4 / WinDbg

Для расследования тот же `fast` режим запускается не через `dotnet run`, а через apphost exe под WinDbg:

[Csharp14.SystemsMemory.Unsafe.exe](c:/git/csharp14-systems-memory/src/03.Unsafe/bin/Debug/net10.0/Csharp14.SystemsMemory.Unsafe.exe)

Если для этого exe включить full page heap, тот же самый lifetime bug обычно манифестируется уже не как `BadHeader`, а как `AccessViolation`.

Именно это даёт правильную драматургию:

1. В обычном запуске fast path выглядит как delayed corruption.
2. Под диагностикой тот же кейс становится AV.
3. После фикса lifetime ошибка не исчезает полностью: остаётся второй contract bug, который уже проявляется как `BadHeader`.

## Что Должен Унести Студент После Первой Части

До отдельной лекции про WinDbg студент должен уже понимать:

- где формируются байты,
- где проходит managed/native boundary,
- почему fast path требует более строгого контракта, чем copy path,
- почему symptom site и root cause часто не совпадают,
- почему расследование memory corruption надо начинать не с угадывания, а с фиксации адреса, длины и lifetime.

## Одной Фразой Вся История

"Мы сделали быстрый zero-copy interop path, который выглядел как нормальная оптимизация, но на границе managed/native незаметно нарушили контракт на адрес и lifetime — и из-за этого получили delayed corruption, которую приходится расследовать уже через WinDbg."
