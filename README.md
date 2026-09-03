# Sequora

Lightweight, in-process, in-memory job queue and background job processing for .NET.

[English](#english) · [Azərbaycan dili](#azərbaycan-dili)

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8%20%7C%209%20%7C%2010-512BD4.svg)](#supported-frameworks)
[![Version](https://img.shields.io/badge/version-1.0.1-informational.svg)](#nuget-package)

## English

Sequora is a single NuGet package. Register it, enqueue a strongly typed job, and hosted workers run the matching handler. You do not create a `Channel<T>`, a `BackgroundService`, or a worker loop.

The simplest path is:

```csharp
services.AddSequora();
```

Then:

```csharp
await queue.EnqueueAsync(new SendEmailJob("user@example.com", "Hi", "Hello"));
```

**Sequora is in-process and in-memory.** If the process crashes or restarts, queued jobs can be lost. It does not provide exactly-once delivery, exactly-once execution, durable persistence, or distributed broker semantics.

### Key features

- One package: `Sequora`. No AspNetCore, Redis, or persistence sibling packages.
- Typed jobs and `IJobHandler<TJob>` dispatch.
- `services.AddSequora()` works with documented defaults.
- Concurrent workers started by the generic host.
- Retries after the first failed attempt, with constant, linear, or exponential backoff.
- Optional delay before a job becomes ready.
- Optional priority with FIFO default and fairness for older lower-priority work.
- Bounded or unbounded in-memory capacity, with wait / throw / drop when full.
- Optional in-process `JobId` duplicate detection.
- Drain or cancel on host shutdown.
- Per-attempt DI scope. Scoped services are not resolved from the root provider.

### Supported frameworks

Sequora multi-targets:

- .NET 8 (`net8.0`)
- .NET 9 (`net9.0`)
- .NET 10 (`net10.0`)

The project is developed with the .NET 10 SDK (`global.json` pins `10.0.400`). .NET 8 and .NET 9 are first-class supported frameworks.

### Installation

```bash
dotnet add package Sequora
```

Package id: **Sequora**. Current version: **1.0.1**. License: **MIT**.

A public NuGet.org listing and source-repository URL are not published from this tree. Pack locally with `dotnet pack` when you need a `.nupkg`.

### Quick start

```csharp
using Microsoft.Extensions.DependencyInjection;
using Sequora;

ServiceCollection services = new();
services.AddSequora()
    .AddHandler<SendEmailJob, SendEmailHandler>();

using ServiceProvider provider = services.BuildServiceProvider();
IJobQueue queue = provider.GetRequiredService<IJobQueue>();

await queue.EnqueueAsync(new SendEmailJob("user@example.com", "Hi", "Hello"));
```

In an ASP.NET Core or generic-host application, call `AddSequora()` on the host `IServiceCollection`. Workers start with the host. `EnqueueAsync` returns when the job is **accepted**, not when the handler has finished.

### Jobs and handlers

A job is any non-null payload type. A handler implements `IJobHandler<TJob>`:

```csharp
public sealed record SendEmailJob(string To, string Subject, string Body);

public sealed class SendEmailHandler : IJobHandler<SendEmailJob>
{
    public Task HandleAsync(SendEmailJob job, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

public sealed record GenerateReportJob(string ReportName);

public sealed class GenerateReportHandler : IJobHandler<GenerateReportJob>
{
    public Task HandleAsync(GenerateReportJob job, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
```

If no handler is registered for a job type, Sequora throws `SequoraHandlerNotFoundException`, logs it, and continues with later jobs. Missing handlers are not retried.

### Dependency injection registration

Register Sequora on the host `IServiceCollection`, then register handlers on the returned `ISequoraBuilder`:

```csharp
services.AddSequora()
    .AddHandler<SendEmailJob, SendEmailHandler>()
    .AddHandler<GenerateReportHandler>(ServiceLifetime.Scoped);
```

`AddHandler<TJob, THandler>()` defaults to `ServiceLifetime.Transient`. Pass `ServiceLifetime.Scoped` or `ServiceLifetime.Singleton` when you need those lifetimes. `AddHandler<THandler>()` discovers every `IJobHandler<TJob>` implemented by the handler type.

You can also apply queue configuration in the same chain, or in the `AddSequora` callback:

```csharp
services.AddSequora(options => options.WorkerCount = 4)
    .AddHandler<SendEmailJob, SendEmailHandler>();
```

Scoped handlers and scoped dependencies are resolved from a **new scope per attempt**, including each retry. They are not resolved from the root provider.

`ISequoraBuilder.Services` exposes the underlying `IServiceCollection` for advanced registration. Most applications do not need it.

### Configuration

Precedence is always:

```
Global defaults  →  Queue configuration  →  Job-level EnqueueOptions
```

More specific values replace less specific ones for the same setting. Unset job properties inherit the queue value.

#### Basic configuration

Defaults are safe. `AddSequora()` is enough. Change only what you need:

```csharp
services.AddSequora()
    .Configure(options =>
    {
        options.WorkerCount = 4;
        options.Capacity = 1024;
        options.RetryCount = 5;
    })
    .AddHandler<SendEmailJob, SendEmailHandler>();
```

`AddSequora(options => …)` applies the first queue callback. Later `Configure` callbacks run on the same `SequoraOptions` instance, so a later assignment to a property wins.

#### Advanced configuration

```csharp
services.AddSequora()
    .Configure(options =>
    {
        options.WorkerCount = 4;
        options.Capacity = 1024;
        options.RetryCount = 5;
        options.RetryDelay = TimeSpan.FromSeconds(1);
        options.MaxRetryDelay = TimeSpan.FromMinutes(1);
        options.RetryBackoff = RetryBackoffStrategy.Exponential;
        options.Priority = 0;
        options.PriorityFairnessLimit = 32;
        options.QueueFullBehavior = QueueFullBehavior.Wait;
        options.ShutdownBehavior = ShutdownBehavior.Drain;
    });
```

| Option | Default | Where it applies |
| --- | --- | --- |
| `WorkerCount` | `1` | Queue only |
| `Capacity` | unbounded (`-1`, `SequoraOptions.Unbounded`) | Queue only |
| `RetryCount` | `3` (retries after the first attempt) | Queue, overridable per job |
| `RetryDelay` | `1` second | Queue, overridable per job |
| `MaxRetryDelay` | `1` minute | Queue, overridable per job |
| `RetryBackoff` | `Exponential` | Queue, overridable per job |
| `Priority` | `0` (FIFO among unprioritized jobs) | Queue, overridable per job |
| `PriorityFairnessLimit` | `32` (`0` = strict priority) | Queue only |
| `QueueFullBehavior` | `Wait` | Queue only |
| `ShutdownBehavior` | `Drain` | Queue only |
| `Delay` | none (ready immediately) | Job only |
| `JobId` | none (anonymous) | Job only |

`IsBounded` is derived from `Capacity`; it is not a separate setting.

Per-job overrides. Unset properties inherit the queue:

```csharp
await queue.EnqueueAsync(
    new SendEmailJob("user@example.com", "Invoice", "Your invoice is ready."),
    options =>
    {
        options.RetryCount = 5;
        options.RetryDelay = TimeSpan.FromMilliseconds(200);
        options.RetryBackoff = RetryBackoffStrategy.Constant;
        options.JobId = "invoice-email-123";
        options.Delay = TimeSpan.FromMinutes(5);
        options.Priority = 10;
    });
```

Invalid queue values fail at options validation (host start or first resolve). Invalid job values throw from `EnqueueAsync` with the property name in the exception. Any `int` priority is valid.

### Retry

`RetryCount` is retries **after** the first attempt, not the total number of attempts. The default of `3` means:

```
Attempt 1
Retry 1
Retry 2
Retry 3
then final failure
```

`RetryCount = 0` is a single attempt. A handler exception is logged with the job type and attempt, not the job payload. Shutdown cancellation is not retried. A failed job does not stop the worker.

Backoff:

- `RetryBackoffStrategy.Constant` — the configured delay every retry
- `RetryBackoffStrategy.Linear` — 1×, 2×, 3×, …
- `RetryBackoffStrategy.Exponential` — 1×, 2×, 4×, … (default)

Computed delays are capped by `MaxRetryDelay`. `TimeSpan.Zero` for `MaxRetryDelay` skips retry waits. Retry delay is not applied after success or after the final failure. Each attempt, including each retry, uses a new DI scope.

### Delayed jobs

`EnqueueOptions.Delay` waits after accept before the job becomes ready. `null` or `TimeSpan.Zero` means immediate. There is no queue-level delay setting.

`EnqueueAsync` returns when the job is accepted. It does not wait for the delay. Delay is not a retry delay. Delayed jobs do not run early. Shutdown cancels delayed jobs that are not yet due, for both `Drain` and `Cancel`.

### Priority

Default priority `0` keeps FIFO order among unprioritized jobs. Higher `Priority` values dequeue first. Equal priorities stay FIFO. A retrying job stays on its worker and is not re-ranked against the ready queue.

If higher-priority work would starve an older lower-priority job, `PriorityFairnessLimit` (default `32`) inserts that oldest job after that many skips. `0` is strict priority.

### Queue capacity

`Capacity` counts ready **and** delayed jobs. In-flight work does not count. Default is unbounded (`SequoraOptions.Unbounded`, `-1`). A bounded value must be at least `1`.

When the queue is bounded and full:

- `QueueFullBehavior.Wait` — wait for space, or cancel the enqueue token (default)
- `QueueFullBehavior.Throw` — reject with `SequoraQueueFullException`
- `QueueFullBehavior.Drop` — discard the incoming job and complete enqueue successfully

Already accepted jobs are not removed. `Wait` and `Drop` do not throw `SequoraQueueFullException`.

### Workers and concurrency

`WorkerCount` is the number of concurrent workers (default `1`, minimum `1`). It cannot be set per job. Hosted workers start with the generic host. One handler exception does not stop the worker or the other workers.

### Cancellation

`EnqueueAsync` takes an optional `CancellationToken`. It cancels a **wait for capacity** when `QueueFullBehavior.Wait` is configured. It does not cancel a job that has already been accepted, and it does not cancel handler execution. A token that is already canceled throws before the job is written.

Handler `CancellationToken`:

- `ShutdownBehavior.Cancel` — signaled on host shutdown. Honor it. `OperationCanceledException` from shutdown is not retried.
- `ShutdownBehavior.Drain` (default) — in-flight handlers are not canceled by shutdown.

### Graceful shutdown

`ShutdownBehavior.Drain` (default) finishes in-flight work and drains remaining **ready** jobs, including retries. Handlers do not receive the host stopping token.

`ShutdownBehavior.Cancel` signals in-flight handlers and does not drain the ready queue. Remaining queued jobs are discarded when the process exits.

Delayed jobs that are not yet due are cancelled for **both** values. After stop, `EnqueueAsync` throws `SequoraStoppedException`.

### Job IDs and duplicates

`JobId` is optional. Anonymous jobs (no id) are not tracked and may be enqueued any number of times.

When a `JobId` is set:

| Moment | Behavior |
| --- | --- |
| Reserved | When enqueue claims the id in this process, including while waiting for capacity. Released if enqueue is canceled, dropped, or rejected |
| Active | While delayed, queued, processing, or retrying |
| Retries | Keep the same id |
| Duplicate enqueue | `SequoraDuplicateJobException` |
| Completed / failed / cancelled | The id is released immediately and may be reused |
| Concurrent enqueue | Exactly one caller is accepted; the others throw |

Comparison is ordinal and case-sensitive. Maximum length is `EnqueueOptions.MaxJobIdLength` (`256`). Empty or whitespace ids throw `ArgumentException`.

This is **not** exactly-once execution and does not survive a crash. After restart the in-memory id table is empty, so the same `JobId` can be enqueued again.

### Important limitations

Sequora is an **in-process, in-memory** queue.

- A process crash or unexpected restart can lose queued jobs.
- Sequora does **not** provide exactly-once delivery or exactly-once execution.
- Sequora does **not** persist jobs to disk, a database, or a broker.
- Duplicate protection is process-local and only covers ids that are still delayed, queued, processing, or retrying.

A typical gap:

1. The job starts.
2. An external side effect happens (email sent, payment captured).
3. The process crashes before Sequora records completion.

After restart the same work can run again. If a side effect must not repeat, make that side effect idempotent (unique constraints, idempotency keys, or an external store). Do not use Sequora when work must survive process restarts or must run exactly once.

### Architecture

Developers use `AddSequora`, `IJobQueue`, and `IJobHandler<TJob>`. Internals stay hidden.

Sequora is meant to replace the usual hand-built combination of:

- in-memory queues (including patterns built around `System.Threading.Channels`)
- `BackgroundService` worker loops
- dependency injection
- handler dispatch, retry, cancellation, and shutdown

You do not create a `Channel<T>` or a `BackgroundService`. Those types are not part of the public API.

Today the hosted worker **is** an internal `BackgroundService` registered as `IHostedService`. Each attempt uses `IServiceScopeFactory.CreateAsyncScope()`. The ready queue is an internal in-memory structure so priority and fairness can be applied; it is not exposed as `Channel<T>`. `Channel<T>` is not a public contract and must not be depended on.

```
Sequora.slnx
├── src/Sequora          # Packable library (public API + internals)
└── tests/Sequora.Tests  # xUnit, same target frameworks as the library
```

There is a **single** package. This repository does not split `Sequora.AspNetCore`, `Sequora.Core`, persistence adapters, or broker integrations.

### Testing

Requires the .NET 10 SDK (see `global.json`).

```bash
dotnet build
dotnet test
dotnet pack src/Sequora/Sequora.csproj --configuration Release
```

Build a single target framework:

```bash
dotnet build src/Sequora/Sequora.csproj -f net8.0
dotnet build src/Sequora/Sequora.csproj -f net9.0
dotnet build src/Sequora/Sequora.csproj -f net10.0
```

The test project `Sequora.Tests` covers enqueue, handlers, DI, workers, concurrency, retry, delay, priority, cancellation, shutdown, job ids, configuration, and capacity. The local package is written to `artifacts/nuget/`.

### NuGet package

| | |
| --- | --- |
| Package id | `Sequora` |
| Version | `1.0.1` |
| Targets | `net8.0`, `net9.0`, `net10.0` |
| License | MIT |
| Icon | packaged `icon.png` |
| README | this file (`PackageReadmeFile`) |
| Symbols | `Sequora.1.0.1.snupkg` |

This tree does not publish to nuget.org. Repository URL metadata is omitted until a public repository exists.

### License

MIT. See [LICENSE](LICENSE). Copyright (c) 2026 Sequora Contributors.

### Contributing

There is no public contribution URL in this tree. Local development uses `Sequora.slnx`. Keep the public API small; internals live in `Sequora.Internal` and are visible to tests via `InternalsVisibleTo`. Do not add persistence, brokers, or extra packages to the core library.

---

## Azərbaycan dili

Sequora tək NuGet paketidir. Onu qeydiyyata alır, güclü tipli işi növbəyə əlavə edirsiniz; host-un worker-ləri uyğun handler-i icra edir. `Channel<T>`, `BackgroundService` və ya worker döngüsü yazmırsınız.

Ən sadə yol:

```csharp
services.AddSequora();
```

Sonra:

```csharp
await queue.EnqueueAsync(new SendEmailJob("user@example.com", "Hi", "Hello"));
```

**Sequora prosesdaxili və yaddaşdaxili növbədir.** Proses çökərsə və ya yenidən başlasa, növbədəki işlər itə bilər. Dəqiq bir dəfə çatdırılma, dəqiq bir dəfə icra, qalıcı saxlama və paylanmış mesaj brokeri semantikası yoxdur.

### Əsas imkanlar

- Tək paket: `Sequora`. AspNetCore, Redis və ya saxlama üçün ayrı paket yoxdur.
- Tipli işlər və `IJobHandler<TJob>` yönləndirməsi.
- `services.AddSequora()` sənədləşdirilmiş standartlarla dərhal işləyir.
- Generic host ilə başlayan paralel worker-lər.
- İlk uğursuz cəhddən sonra təkrar cəhdlər; sabit, xətti və ya eksponensial backoff.
- İş hazır olmadan əvvəl ixtiyari gecikmə.
- İxtiyari prioritet; standart FIFO və köhnə aşağı prioritetli işlər üçün ədalət.
- Məhdud və ya qeyri-məhdud yaddaş tutumu; dolu olanda gözləmə, istisna atma və ya gələn işi buraxma.
- Prosesdaxili `JobId` ilə təkrar növbəyə əlavənin aşkarlanması.
- Host bağlananda növbəni boşaltmaq və ya ləğv etmək.
- Hər cəhd üçün ayrıca DI scope. Scoped xidmətlər kök provayderdən alınmır.

### Dəstəklənən çərçivələr

Sequora aşağıdakı hədəf çərçivələri dəstəkləyir:

- .NET 8 (`net8.0`)
- .NET 9 (`net9.0`)
- .NET 10 (`net10.0`)

Layihə .NET 10 SDK ilə hazırlanır (`global.json` `10.0.400` versiyasını bağlayır). .NET 8 və .NET 9 tam dəstəklənən çərçivələrdir.

### Quraşdırma

```bash
dotnet add package Sequora
```

Paket identifikatoru: **Sequora**. Cari versiya: **1.0.1**. Lisenziya: **MIT**.

Bu layihə ağacından nuget.org-a paket göndərilmir və ictimai mənbə ünvanı yoxdur. `.nupkg` lazım olanda `dotnet pack` ilə yerli paket yığın.

### Qısa başlanğıc

```csharp
using Microsoft.Extensions.DependencyInjection;
using Sequora;

ServiceCollection services = new();
services.AddSequora()
    .AddHandler<SendEmailJob, SendEmailHandler>();

using ServiceProvider provider = services.BuildServiceProvider();
IJobQueue queue = provider.GetRequiredService<IJobQueue>();

await queue.EnqueueAsync(new SendEmailJob("user@example.com", "Hi", "Hello"));
```

ASP.NET Core və ya generic host tətbiqində `AddSequora()`-i host-un `IServiceCollection` üzərində çağırın. Worker-lər host ilə başlayır. `EnqueueAsync` iş **qəbul olunanda** qayıdır; handler bitəndə yox.

### İşlər və handler-lər

İş istənilən `null` olmayan yük növü ola bilər. Handler `IJobHandler<TJob>` interfeysini həyata keçirir:

```csharp
public sealed record SendEmailJob(string To, string Subject, string Body);

public sealed class SendEmailHandler : IJobHandler<SendEmailJob>
{
    public Task HandleAsync(SendEmailJob job, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

public sealed record GenerateReportJob(string ReportName);

public sealed class GenerateReportHandler : IJobHandler<GenerateReportJob>
{
    public Task HandleAsync(GenerateReportJob job, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
```

İş növü üçün handler yoxdursa, Sequora `SequoraHandlerNotFoundException` atır, jurnalda yazır və sonrakı işlərə davam edir. Çatışmayan handler təkrar cəhd olunmur.

### Asılılığın yeridilməsi (DI) qeydiyyatı

Sequora-ı host-un `IServiceCollection` üzərində qeydiyyata alın, sonra qaytarılan `ISequoraBuilder` üzərində handler-ləri qeyd edin:

```csharp
services.AddSequora()
    .AddHandler<SendEmailJob, SendEmailHandler>()
    .AddHandler<GenerateReportHandler>(ServiceLifetime.Scoped);
```

`AddHandler<TJob, THandler>()` standart olaraq `ServiceLifetime.Transient` istifadə edir. `ServiceLifetime.Scoped` və ya `ServiceLifetime.Singleton` lazım olanda ötürülür. `AddHandler<THandler>()` handler növünün həyata keçirdiyi bütün `IJobHandler<TJob>` interfeyslərini aşkar edir.

Növbə konfiqurasiyasını eyni zəncirdə, və ya `AddSequora` callback-ində də tətbiq etmək olar:

```csharp
services.AddSequora(options => options.WorkerCount = 4)
    .AddHandler<SendEmailJob, SendEmailHandler>();
```

Scoped handler və asılılıqlar **hər cəhddə yeni scope-dan** alınır, təkrar cəhdlər də daxil. Kök provayderdən həll olunmurlar.

`ISequoraBuilder.Services` əlavə qeydiyyat üçün altdakı `IServiceCollection`-u açır. Əksər tətbiqlərə lazım olmur.

### Konfiqurasiya

Üstünlük həmişə belədir:

```
Qlobal standartlar  →  Növbə konfiqurasiyası  →  İş səviyyəli EnqueueOptions
```

Eyni parametr üçün daha konkret dəyər az konkret olanı əvəz edir. Təyin olunmayan iş xassələri növbə dəyərini miras alır.

#### Əsas konfiqurasiya

Standartlar təhlükəsizdir. `AddSequora()` kifayətdir. Yalnız dəyişmək istədiyinizi yazın:

```csharp
services.AddSequora()
    .Configure(options =>
    {
        options.WorkerCount = 4;
        options.Capacity = 1024;
        options.RetryCount = 5;
    })
    .AddHandler<SendEmailJob, SendEmailHandler>();
```

`AddSequora(options => …)` ilk növbə callback-ini tətbiq edir. Sonrakı `Configure` eyni `SequoraOptions` nümunəsi üzərində işləyir; eyni xassəyə sonrakı mənimsətmə qalib gəlir.

#### Ətraflı konfiqurasiya

```csharp
services.AddSequora()
    .Configure(options =>
    {
        options.WorkerCount = 4;
        options.Capacity = 1024;
        options.RetryCount = 5;
        options.RetryDelay = TimeSpan.FromSeconds(1);
        options.MaxRetryDelay = TimeSpan.FromMinutes(1);
        options.RetryBackoff = RetryBackoffStrategy.Exponential;
        options.Priority = 0;
        options.PriorityFairnessLimit = 32;
        options.QueueFullBehavior = QueueFullBehavior.Wait;
        options.ShutdownBehavior = ShutdownBehavior.Drain;
    });
```

| Seçim | Standart | Harada keçərlidir |
| --- | --- | --- |
| `WorkerCount` | `1` | Yalnız növbə |
| `Capacity` | qeyri-məhdud (`-1`, `SequoraOptions.Unbounded`) | Yalnız növbə |
| `RetryCount` | `3` (ilk cəhddən sonrakı təkrarlar) | Növbə, işdə əvəz oluna bilər |
| `RetryDelay` | `1` saniyə | Növbə, işdə əvəz oluna bilər |
| `MaxRetryDelay` | `1` dəqiqə | Növbə, işdə əvəz oluna bilər |
| `RetryBackoff` | `Exponential` | Növbə, işdə əvəz oluna bilər |
| `Priority` | `0` (prioritetsiz işlər arasında FIFO) | Növbə, işdə əvəz oluna bilər |
| `PriorityFairnessLimit` | `32` (`0` = sərt prioritet) | Yalnız növbə |
| `QueueFullBehavior` | `Wait` | Yalnız növbə |
| `ShutdownBehavior` | `Drain` | Yalnız növbə |
| `Delay` | yoxdur (dərhal hazır) | Yalnız iş |
| `JobId` | yoxdur (adsız) | Yalnız iş |

`IsBounded` `Capacity`-dən törəyir; ayrı parametr deyil.

İş səviyyəli əvəzetmələr. Təyin olunmayan xassələr növbəni miras alır:

```csharp
await queue.EnqueueAsync(
    new SendEmailJob("user@example.com", "Invoice", "Your invoice is ready."),
    options =>
    {
        options.RetryCount = 5;
        options.RetryDelay = TimeSpan.FromMilliseconds(200);
        options.RetryBackoff = RetryBackoffStrategy.Constant;
        options.JobId = "invoice-email-123";
        options.Delay = TimeSpan.FromMinutes(5);
        options.Priority = 10;
    });
```

Yanlış növbə dəyərləri seçimlərin yoxlanmasında uğursuz olur (host işə düşəndə və ya növbə ilk dəfə həll olunanda). Yanlış iş dəyərləri `EnqueueAsync`-dən xassə adı ilə istisna atır. İstənilən `int` prioritet keçərlidir.

### Təkrar cəhd

`RetryCount` ilk cəhddən **sonrakı** təkrarlardır, cəhdlərin cəmi deyil. Standart `3` belə oxunur:

```
Cəhd 1
Təkrar 1
Təkrar 2
Təkrar 3
sonra yekun uğursuzluq
```

`RetryCount = 0` tək cəhddir. Handler istisnası iş tipi və cəhd nömrəsi ilə jurnala yazılır, işin özü (payload) yox. Bağlanma ləğvi təkrar olunmur. Uğursuz iş worker-i dayandırmır.

Backoff:

- `RetryBackoffStrategy.Constant` — hər təkrarda eyni gecikmə
- `RetryBackoffStrategy.Linear` — 1×, 2×, 3×, …
- `RetryBackoffStrategy.Exponential` — 1×, 2×, 4×, … (standart)

Hesablanan gecikmələr `MaxRetryDelay` ilə kəsilir. `MaxRetryDelay` üçün `TimeSpan.Zero` təkrar gözləməsini buraxır. Uğurdan və son uğursuzluqdan sonra təkrar gecikməsi yoxdur. Hər cəhd, təkrarlar daxil, yeni DI scope istifadə edir.

### Gecikdirilmiş işlər

`EnqueueOptions.Delay` qəbuldan sonra işin hazır olması üçün gözləyir. `null` və ya `TimeSpan.Zero` dərhal hazır deməkdir. Növbə səviyyəsində gecikmə parametri yoxdur.

`EnqueueAsync` iş qəbul olunanda qayıdır; gecikməni gözləmir. Bu, təkrar cəhd gecikməsi deyil. Gecikdirilmiş işlər vaxtından əvvəl işləmir. Hələ vaxtı çatmamış gecikdirilmiş işlər həm `Drain`, həm `Cancel` zamanı ləğv olunur.

### Prioritet

Standart prioritet `0` prioritetsiz işlər arasında FIFO saxlayır. Daha yüksək `Priority` əvvəl çıxarılır. Eyni prioritet FIFO qalır. Təkrar cəhd edən iş öz worker-ində qalır və hazır növbəyə qarşı yenidən sıralanmır.

Yüksək prioritetli axın köhnə aşağı prioritetli işi ac qoyarsa, `PriorityFairnessLimit` (standart `32`) o qədər ötürmədən sonra həmin ən köhnə işi çıxarır. `0` sərt prioritetdir.

### Növbə tutumu

`Capacity` hazır **və** gecikdirilmiş işləri sayır. İcrada olan işlər sayılmır. Standart qeyri-məhduddur (`SequoraOptions.Unbounded`, `-1`). Məhdud dəyər ən azı `1` olmalıdır.

Növbə məhduddursa və doludursa:

- `QueueFullBehavior.Wait` — yer boşalana qədər gözləyir, və ya enqueue token-ini ləğv edir (standart)
- `QueueFullBehavior.Throw` — `SequoraQueueFullException` ilə rədd edir
- `QueueFullBehavior.Drop` — gələn işi buraxır və enqueue-i uğurla bitirir

Artıq qəbul olunmuş işlər silinmir. `Wait` və `Drop` `SequoraQueueFullException` atmır.

### Worker-lər və paralellik

`WorkerCount` eyni anda işləyən worker sayıdır (standart `1`, minimum `1`). İş səviyyəsində təyin olunmur. Host olunan worker-lər generic host ilə başlayır. Bir handler istisnası həmin worker-i və digər worker-ləri dayandırmır.

### Ləğvetmə

`EnqueueAsync` ixtiyari `CancellationToken` qəbul edir. `QueueFullBehavior.Wait` olanda **tutum üçün gözləməni** ləğv edir. Artıq qəbul olunmuş işi və handler icrasını ləğv etmir. Token artıq ləğv olunubsa, iş yazılmazdan əvvəl istisna atılır.

Handler `CancellationToken`:

- `ShutdownBehavior.Cancel` — host bağlananda siqnal gəlir. Ona əməl edin. Bağlanmadan gələn `OperationCanceledException` təkrar olunmur.
- `ShutdownBehavior.Drain` (standart) — icrada olan handler-lər bağlanma ilə ləğv olunmur.

### Səlis bağlanma

`ShutdownBehavior.Drain` (standart) icradakı işi bitirir və qalan **hazır** növbəni, təkrar cəhdlər daxil, boşaldır. Handler-lər host-un dayandırma token-ini almır.

`ShutdownBehavior.Cancel` icradakı handler-lərə siqnal göndərir və hazır növbəni boşaltmır. Proses çıxanda qalan növbə atılır.

Hələ vaxtı çatmamış gecikdirilmiş işlər **hər iki** rejimdə ləğv olunur. Dayandıqdan sonra `EnqueueAsync` `SequoraStoppedException` atır.

### İş identifikatorları və təkrarlar

`JobId` ixtiyaridir. Adsız işlər (id yoxdur) izlənilmir və istənilən sayda növbəyə düşə bilər.

`JobId` təyin olunanda:

| An | Davranış |
| --- | --- |
| Rezerv | Enqueue bu prosesdə id-ni iddia edəndə, tutum üçün gözləmə daxil. Enqueue ləğv, buraxılma və ya rədd olunarsa azad edilir |
| Aktiv | Gecikmə, növbə, icra və ya təkrar cəhd zamanı |
| Təkrar cəhdlər | Eyni id saxlanılır |
| Təkrar enqueue | `SequoraDuplicateJobException` |
| Bitdi / uğursuz / ləğv | Id dərhal azad olur, yenidən istifadə oluna bilər |
| Eyni anda enqueue | Dəqiq bir çağırış qəbul olunur; qalanı istisna atır |

Müqayisə ordinaldır və böyük-kiçik hərflərə həssasdır. Maksimum uzunluq `EnqueueOptions.MaxJobIdLength` (`256`). Boş və ya yalnız boşluq id `ArgumentException` atır.

Bu, dəqiq bir dəfə icra deyil və çöküşdən sonra yaşamır. Yenidən başlanandan sonra yaddaşdakı id cədvəli boşdur; eyni `JobId` yenidən növbəyə düşə bilər.

### Vacib məhdudiyyətlər

Sequora **prosesdaxili, yaddaşdaxili** növbədir.

- Proses çökərsə və ya gözlənilmədən yenidən başlasa, növbədəki işlər itə bilər.
- Sequora dəqiq bir dəfə çatdırılma və dəqiq bir dəfə icra **vermir**.
- Sequora işləri diskə, verilənlər bazasına və ya broker-ə **yazmır**.
- Təkrar qorunması yalnız bu prosesə aiddir və hələ gecikmədə, növbədə, icrada və ya təkrar cəhddə olan id-ləri əhatə edir.

Tipik ssenari:

1. İş başlayır.
2. Xarici yan təsir baş verir (məktub gedir, ödəniş tutulur).
3. Sequora tamamlanmanı qeyd etməmiş proses çökür.

Yenidən başlanandan sonra eyni iş yenidən gedə bilər. Yan təsir təkrarlanmamalıdırsa, o təsiri idempotent edin (unikal məhdudiyyət, idempotency açarı, xarici anbar). İş proses yenidən başlamasını yaşamalıdırsa və ya dəqiq bir dəfə getməlidirsə, Sequora istifadə etməyin.

### Memarlıq

Tərtibatçılar `AddSequora`, `IJobQueue` və `IJobHandler<TJob>` istifadə edir. Daxili təfərrüatlar gizlidir.

Sequora adətən əl ilə yığılan bu kombinasiyanı əvəz etmək üçündür:

- yaddaşdaxili növbələr (`System.Threading.Channels` ətrafındakı nümunələr daxil)
- `BackgroundService` worker döngüləri
- asılılığın yeridilməsi
- handler yönləndirməsi, təkrar cəhd, ləğvetmə və bağlanma

`Channel<T>` və ya `BackgroundService` yaratmırsınız. Bu tiplər ictimai API-nin hissəsi deyil.

Hazırda host tərəfindən işə salınan worker daxildə `IHostedService` kimi qeydiyyatdan keçən `BackgroundService`-dir. Hər cəhd `IServiceScopeFactory.CreateAsyncScope()` istifadə edir. Hazır növbə prioritet və ədalət üçün daxili yaddaş strukturudur; `Channel<T>` kimi açılmır. `Channel<T>` ictimai müqavilə deyil və ona əsaslanmaq olmaz.

```
Sequora.slnx
├── src/Sequora          # Paketlənən kitabxana (ictimai API + daxili)
└── tests/Sequora.Tests  # xUnit, kitabxana ilə eyni hədəf çərçivələr
```

**Tək** paket var. Bu layihə `Sequora.AspNetCore`, `Sequora.Core`, saxlama adapterləri və ya broker inteqrasiyalarına bölünmür.

### Testlər

.NET 10 SDK tələb olunur (`global.json`).

```bash
dotnet build
dotnet test
dotnet pack src/Sequora/Sequora.csproj --configuration Release
```

Tək hədəf çərçivə:

```bash
dotnet build src/Sequora/Sequora.csproj -f net8.0
dotnet build src/Sequora/Sequora.csproj -f net9.0
dotnet build src/Sequora/Sequora.csproj -f net10.0
```

`Sequora.Tests` növbəyə əlavə, handler, DI, worker, paralellik, təkrar cəhd, gecikmə, prioritet, ləğvetmə, bağlanma, iş id-ləri, konfiqurasiya və tutumu əhatə edir. Yerli paket `artifacts/nuget/` qovluğuna yazılır.

### NuGet paketi

| | |
| --- | --- |
| Paket id | `Sequora` |
| Versiya | `1.0.1` |
| Hədəflər | `net8.0`, `net9.0`, `net10.0` |
| Lisenziya | MIT |
| İkon | paketlənmiş `icon.png` |
| README | bu fayl (`PackageReadmeFile`) |
| Simvollar | `Sequora.1.0.1.snupkg` |

Bu ağac nuget.org-a dərc etmir. İctimai depo olmayana qədər Repository URL metadata-sı yazılmır.

### Lisenziya

MIT. Baxın: [LICENSE](LICENSE). Müəllif hüququ (c) 2026 Sequora Contributors.

### Töhfə vermə

Bu ağacda ictimai töhfə ünvanı yoxdur. Yerli iş `Sequora.slnx` üzərindən gedir. İctimai API kiçik qalsın; daxili tiplər `Sequora.Internal`-dədir və testlər `InternalsVisibleTo` ilə görür. Əsas kitabxanaya saxlama, broker və əlavə paket əlavə etməyin.
