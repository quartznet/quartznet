using System.Collections.Specialized;

using Microsoft.Extensions.Configuration;

namespace Quartz.Configuration;

/// <summary>
/// What <c>AddQuartz(name, …)</c> was told, kept so that the scheduler it registered can be built a
/// second time.
/// </summary>
/// <remarks>
/// <para>
/// A container registration is a set of descriptors fixed when the container was built, and the
/// instances they produce are singletons that cannot be initialised twice. So restarting a scheduler is
/// not restarting anything: it is running the registration again into a container of its own, which is
/// what <see cref="SchedulerGeneration" /> already does for a scheduler added at runtime. This is the
/// recipe that generation replays, and the reason it has to be recorded at registration time is that a
/// delegate which has been run and thrown away can never be run again.
/// </para>
/// <para>
/// Only what <c>AddQuartz</c> itself was handed is here. Anything written <em>beside</em> the call —
/// <c>services.Configure&lt;QuartzSchedulerOptions&gt;("acme", …)</c>, a keyed registration made
/// directly against the application's collection, <c>AddQuartzHostedService("acme", …)</c> — belongs to
/// the application's container rather than to this scheduler's registration, and is not replayed. That
/// is a limit worth knowing rather than a gap to close: replaying arbitrary registrations from the
/// application's collection would mean deciding which of them are this scheduler's, and nothing in a
/// service descriptor says.
/// </para>
/// </remarks>
/// <param name="Name">The name the scheduler was registered under, as it was spelled there.</param>
/// <param name="Properties">
/// The flat <c>quartz.*</c> bag the registration was seeded with, already checked. Empty for a
/// registration that was given none, and ignored when <paramref name="Configuration" /> is set.
/// </param>
/// <param name="Configuration">
/// The section the registration read, already resolved to whichever of the caller's section and its
/// <c>Schedulers:{name}</c> child actually held the settings, or <see langword="null" /> for a
/// registration that was configured with a property bag instead.
/// </param>
/// <param name="Configure">
/// The caller's own callback. The container-wide delegates <c>ConfigureAllQuartzSchedulers</c> recorded
/// are deliberately not folded in here: they are replayed from the registry a generation is built
/// against, which is where a scheduler added at runtime gets them too.
/// </param>
internal sealed record SchedulerBlueprint(
    string Name,
    NameValueCollection Properties,
    IConfiguration? Configuration,
    Action<IQuartzBuilder>? Configure);
