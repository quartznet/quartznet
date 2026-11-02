# Public API decisions

What was settled, frozen, kept and promised about the public API. Read the reason before reopening
any of it; most reasons are also on the member's XML docs.

`AGENTS.md` states each rule here in brief and routes to this document, which holds the full text
and wins where the two differ.

## `ValueTask`, and its one exception

- **All public APIs return `ValueTask`** rather than `Task` (e.g., `IJob.Execute`, `IScheduler` methods). This
  holds for classes too. The single exception is `Quartz.Dashboard.Hubs.IQuartzDashboardHubClient`, whose shape
  SignalR dictates: its typed-client proxy only implements `Task`-returning members, and it is emitted into a
  dynamic assembly, so the interface and its DTOs cannot be internal either — a strong-named assembly can only
  grant `InternalsVisibleTo` to a friend it names by public key. `QuartzDashboardHubClientProxyTest` is the guard.

## Naming decisions that are settled

These spots look inconsistent on purpose. Each was examined and ratified in the 4.0 API-finalization
pass; do not "finish" any of them.

- **The scheduler's noun is `JobDetail`; the store's noun is `Job`.** `IScheduler` hands users
  `IJobDetail`, so it says `GetJobDetail`/`GetJobDetails`; `IJobStore` speaks in storage terms, so it
  says `GetJob`/`GetJobs` beside `GetTrigger`/`GetTriggers`. Each interface is consistent *within*
  itself, and aligning one with the other would break the pairs on whichever side got "fixed".
- **`StdAdoDelegate` and the `*Delegate` dialect family keep their names.** The vocabulary is Quartz's
  cross-ecosystem identity: Java parity, `quartz.jobStore.driverDelegateType` spelled in countless
  configuration files, `database/README.md`, and the dialect docs all teach against
  `IDriverDelegate`/`SqlServerDelegate`/`PostgreSQLDelegate`/…. The `Std` prefix was retired everywhere
  else; `StdAdoDelegate` is the sole deliberate survivor.
- **`RAMJobStore` keeps its name**, for the reason `StdAdoDelegate` does: `quartz.jobStore.type` names
  the type, and twenty years of configuration files and tutorials spell it. Its options type is
  `InMemoryJobStoreOptions` and the builder method is `UseInMemoryStore`, because *those* are new names
  with no history to keep faith with — the mismatch is deliberate, not an unfinished rename.
- **The `*Utc` suffix on `DateTimeOffset` members stays.** `StartTimeUtc`, `EndTimeUtc`,
  `NextFireTimeUtc` and the rest carry an offset, which makes the suffix redundant on its face; it is
  Java parity, it is what every Quartz tutorial teaches, and it is twelve members against roughly 1,400
  call sites in this repository alone.
- **`Use*` is the verb for an extension that registers a plugin** — `UseStructuredJobLogging`,
  `UseJobHistoryLogging`, `UseXmlSchedulingConfiguration` — because a plugin is middleware over a
  scheduler's lifecycle and that is how middleware reads. `AddPlugin<T>` is the generic form, for a
  plugin with no extension of its own. `Add*` stays for things a scheduler *contains*: jobs, triggers,
  calendars, listeners. `UseTimeZoneConverter` keeps the verb although it registers no plugin, because
  what it installs is still a scheduler-wide capability.
- **Four history-logging plugins ship, not two.** `LoggingJobHistoryPlugin` /
  `LoggingTriggerHistoryPlugin` log through numbered format strings and
  `StructuredLoggingJobHistoryPlugin` / `StructuredLoggingTriggerHistoryPlugin` log the same events
  through named templates. The structured pair is the better default and is documented as such; the
  classic pair stays because a message template is a contract to whatever parses a deployment's logs,
  so retiring it is a break with no migration to offer. The plugin sweep of #3593 retired the plugins
  whose job the host already does — these are not those.
- **The `AddQuartz(NameValueCollection, …)` overloads stay beside their dictionary twins.** They are the
  3.x on-ramp, because a `NameValueCollection` is what an application migrating from
  `StdSchedulerFactory` already holds.
- **Reading has two altitudes, and both stay.** `IScheduler`'s `Query*` members take a query record —
  filter, page, optional total — and answer with headers; `SchedulerQueryExtensions`' `Get*` conveniences
  take none of that and answer with bare keys and names. The pause/resume matcher members are `*Groups`
  because the group set is what they write and answer with, which no list of keys can say.
- **`ISchedulerRepository` and `ISchedulerRegistry` answer different questions.** The repository is the
  live-instance directory — bind, remove, look up; the registry lists what a container has *registered*,
  built or not. Both stay.
- **`NameMatcher` — the arity-free one — sits outside `IMatcher<T>` on purpose.** That interface and the
  `Matchers.And`/`Or`/`Not` combinators are constrained to `Key<T>`, and a calendar's or a group's name
  is not a key.

## Overload sets audited and frozen in #3598

Counted, argued and left as they are. The reason each is not a set to thin is on the members
themselves, so read the XML docs before reopening one.

- **Job-store selection keeps all seven members**, because each says something the others cannot:
  the two shipped stores (`UseInMemoryStore`, `UsePersistentStore`), a persistent store of another type
  (`UsePersistentStore<T>`), a store of your own built by the container (`UseJobStore<T>`, plus
  `<T, TOptions>` for one with named options, which `custom-job-store.md` teaches), one you built
  (`UseJobStore(IJobStore)`), and one a factory builds over the scheduler's own parts
  (`UseJobStore(Func<…>)`).
- **Hosted-service registration keeps all six**: three shapes — the ordinary one, `<T>` for a subclass,
  and `(schedulerName, …)` for one scheduler's settings — on each of the two receivers. C# has no
  default type argument, so the first shape is not the second one written shorter.
- **`IThreadPool` keeps all six members**; each has exactly one caller in the scheduler, and
  `ZeroSizeThreadPool` stays public because `UseThreadPool<ZeroSizeThreadPool>()` needs it reachable.
- **`JobBuilder`/`TriggerBuilder` keep their companion classes while the schedule builders carry their
  own `Create`.** Generic inference forces it — `JobBuilder<MyJob>.Create()` would name the job type
  twice — and no schedule builder is generic.
- **`IScheduleBuilder.Build()` keeps returning `IMutableTrigger`,** and `ConfigureJobScope` keeps taking
  `TriggerFiredBundle`. Each is the only type that says the thing: the trigger builder must write onto
  what it is handed, and a firing before its job exists has no `IJobExecutionContext` yet.

## Examined in the alpha.5 audit and kept (#3603)

The rule above, for the things that are not names. The reason is on the member too.

- **The two day fields follow Vixie, not Cronos.** A field written exactly `*` or `?` restricts nothing
  and defers to the other; when both name days the expression fires on their *union*, so
  `0 0 0 13 * FRI` is every Friday **and** the 13th, where Cronos ANDs them and would fire only on
  Friday the 13th. The union is `crontab(5)`'s rule and what `cron-expressions.md` has always taught,
  and `UnixCronFormatTest` pins it. `*/n` is restricted here, unlike Vixie, whose parser reads the
  leading `*` first.
- **`CronFormat` is stated, never sniffed.** A five-field string throws in the default `Quartz` format,
  and the message names `CronFormat.Unix` and the rewritten expression. Auto-detect was rejected because
  the same digit is a different day in each dialect and a dropped middle field would change a schedule
  in silence; it can be added later, removing it could not.
- **Wrapping ranges and `H` are Quartz supersets over standard cron, and are identity.** A range whose
  end is below its start wraps instead of sorting its endpoints — `22-2` is five hours — and `H` spreads
  a firing deterministically from the trigger key or an explicit one.
  `CronExpressionWrappingRangeTest` pins the wrap, and the Unix rewrite carries both into five fields.
- **Every `ISchedulerListener` member leads with `IScheduler`.** No scheduler-listener notification has
  an execution context to reach its scheduler through, so the scheduler is the first argument instead —
  which is also what lets one instance serve several schedulers. `ITriggerListener` leads with the
  trigger, for the mirror reason.
- **`SchedulerContext` and `JobDataMap` are not twins to align.** The map is persisted, dirty-tracked
  and equatable, and its `PutAsString` writers are instance members because they take part in that
  change tracking; the context is never persisted and has nothing to track. Only the typed *read*
  accessors are shared, in `DataMapExtensions`.
- **`LogProvider.SetLogProvider` is the one static escape hatch, kept knowingly.** Plenty of things that
  log are never handed a logger by a container: a listener you constructed, a trigger deserialized out of
  a job store, the static helpers, a standalone `QuartzSchedulerBuilder`. Nor is it seeded from the
  container, whose `ILoggerFactory` a rebuilt host would leave disposed behind it.
- **The built-in trigger serializers are public and unsealed, and so are all five `*TriggerImpl` types.**
  That pairing is the subclassed-trigger seam: derive from the trigger, derive from its serializer, call
  `base.SerializeFields`/`base.DeserializeFields`. `BuiltInTriggerSerializerDerivationTest` fails if
  either half closes.
- **A job's timeout is `[JobTimeout]`, not a builder member.** How long the work may take is a property
  of the code that does it, and an attribute travels with the type through every job store, wire format
  and way of scheduling, as with `[DisallowConcurrentExecution]`. Nothing enforces it until
  `AddJobTimeout` registers the middleware.

## Promises 4.0 makes (#3647)

Extension policy, not taste. `how-tos/extending-quartz.md` is the reader-facing form.

- **A 4.0.x patch is additive.** Its baseline diff is additions only, and the database schema is frozen
  for the whole 4.0 line: a fix needing a new column waits for 4.1, one needing a changed public
  signature waits for 5.0.

- **A collaborator is handed a context object** — parameterless ctor, `init` properties — never a
  parameter list: `DriverDelegateContext`, `LockHandlerContext`, `TriggerFiredBundle` and the rest. A
  new datum is a non-`required` property, so it is source- and binary-compatible.
- **A member added to a public interface lands as a default interface member**, as on `IJobStore`,
  `IScheduler`, `ILockHandler` and `ITriggerPersistenceDelegate`; that is what makes freezing `IQuartzApiClient` and `ITriggerSerializer` without default bodies safe, and the baselines
  mark DIMs so the promise is checkable. A forwarder must *declare* every such member — an omitted one
  runs the default on the forwarder; `DelegatingForwardingTest` sweeps both delegating types for it.
- **Read-replica routing, if ever, is a DIM `IDbProvider.CreateReadConnection()`** — never a `readOnly`
  parameter on `CreateConnection`, which would break both public `IDbProvider` implementations.
- **`MON/2` is 4.1's `2/2`**, and `MON-FRI/2` is `2-6/2`: a textual day-of-week takes a step, in lockstep
  with the numeric form. 4.0 refused it because 3.x read it as a fortnight; that silent change of meaning
  is a documented hazard, not a reason to reopen this.
