using Quartz.Util;

namespace Quartz;

/// <summary>
/// Strongly typed configuration for the type loader, which turns a type <em>name</em> into a type — a
/// stored <c>JOB_CLASS_NAME</c>, a <c>quartz.*</c> <c>.type</c> key, a job named in XML or JSON
/// scheduling data.
/// </summary>
/// <remarks>
/// <para>
/// Binds from the <c>TypeLoader</c> section of the Quartz configuration, so a rename can ship in
/// <c>appsettings.json</c> with the deployment that performs it:
/// <c>Quartz:TypeLoader:Aliases:&lt;old name&gt;</c> is the new name.
/// </para>
/// <para>
/// Type loading is container-wide rather than per-scheduler — one <c>ITypeLoader</c> serves every
/// scheduler in the container, which is what <c>UseTypeLoader&lt;T&gt;()</c> replaces — so these are the
/// container's options whichever scheduler's section they were written in.
/// </para>
/// </remarks>
public sealed class TypeLoaderOptions
{
    /// <summary>
    /// What a type name that no longer names anything means today: the name as it was stored or
    /// configured, mapped to the name of the type that replaced it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Keys are compared the way the built-in loader compares its own table of Quartz's 3.x → 4.0
    /// renames: ordinal, and matching either the whole name or the part of it before the comma that
    /// starts the assembly. So <c>Acme.Jobs.NightlyReport</c> matches however the assembly is spelled
    /// after it, and <c>Acme.Jobs.NightlyReport, Acme.Jobs</c> matches only that assembly.
    /// </para>
    /// <para>
    /// A value that names its own assembly replaces the whole name; one that does not keeps whatever
    /// followed the old name, which is what makes a mapping between two types in the same assembly a
    /// single namespace-qualified name.
    /// </para>
    /// <para>
    /// Nothing is written back. A job read out of the store under an aliased name still has the stored
    /// spelling in <c>JOB_CLASS_NAME</c>, so an alias is what keeps a rolling deployment working rather
    /// than a migration in disguise; the <c>UPDATE</c> in the troubleshooting page is still how an alias
    /// is eventually retired.
    /// </para>
    /// </remarks>
    public Dictionary<string, string?> Aliases { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Declares that <paramref name="name" /> — a type name already stored or already written in a
    /// configuration file — now means <paramref name="type" />.
    /// </summary>
    /// <remarks>
    /// The typed half of <see cref="Aliases" />, for a rename whose target the application can name in
    /// code. The type is recorded by the same assembly-qualified, version-free spelling Quartz stores a
    /// job type under, so a mapping written here and one written in configuration are the same entry.
    /// </remarks>
    /// <param name="name">The name as it was stored or configured.</param>
    /// <param name="type">The type that name resolves to now.</param>
    /// <returns>This instance, so several renames can be declared in one chain.</returns>
    public TypeLoaderOptions Map(string name, Type type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(type);

        Aliases[name] = type.AssemblyQualifiedNameWithoutVersion();
        return this;
    }
}
