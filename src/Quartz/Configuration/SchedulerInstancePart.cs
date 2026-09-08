namespace Quartz.Configuration;

/// <summary>
/// A note in a scheduler's collection that one of its parts was supplied as an object rather than as a
/// type or a factory.
/// </summary>
/// <remarks>
/// <para>
/// Nothing resolves this. It exists so that "was this part handed to the recipe as an instance" can be
/// answered by <em>reading</em> the collection, before anything has been built — because the only other
/// way to answer it is to build a second generation and compare the parts by reference, and by then a
/// refusal has to dispose the container it built. Microsoft's container disposes what a factory delegate
/// returned, and for these four overloads that object is the one the <em>running</em> scheduler is using.
/// A refusal that killed the scheduler it was protecting would be worse than the mistake it reports.
/// </para>
/// <para>
/// One marker per part supplied that way, added beside the registration itself and only when that
/// registration is the one that took — <c>TryAdd</c> is first-wins, and a marker for an instance nothing
/// will ever resolve would refuse a restart that is perfectly replayable.
/// </para>
/// </remarks>
/// <param name="PartType">The service the instance was supplied for, which is what a refusal names.</param>
internal sealed record SchedulerInstancePart(Type PartType);
