namespace Quartz.Util;

/// <summary>
/// The serializers a JSON serializer knows about, keyed by every name its converters may look one up by.
/// </summary>
/// <remarks>
/// Shared by the System.Text.Json and Newtonsoft serializer registries. Those two do not agree on how a
/// serializer is found — how many names it answers to, and whether the names are matched case
/// insensitively — so both are given to the constructor and to <see cref="Add"/> rather than assumed here.
/// </remarks>
/// <typeparam name="TSerializer">The serializer abstraction being kept, which differs per assembly.</typeparam>
internal sealed class SerializerMap<TSerializer> where TSerializer : class
{
    private readonly Dictionary<string, TSerializer> serializers;

    public SerializerMap(StringComparer comparer)
    {
        serializers = new Dictionary<string, TSerializer>(comparer);
    }

    /// <summary>
    /// Registers a serializer under each of the given names, replacing any serializer already under them.
    /// </summary>
    public void Add(TSerializer serializer, params string[] names)
    {
        foreach (var name in names)
        {
            serializers[name] = serializer;
        }
    }

    /// <summary>
    /// Returns the serializer registered for a type name, or throws when there is none.
    /// </summary>
    /// <param name="typeName">The name to look up, as it appears in the JSON being read.</param>
    /// <param name="notFoundMessage">
    /// The message prefix to throw with, which differs between the two registries only because their
    /// converters have always worded it differently.
    /// </param>
    public TSerializer Get(string? typeName, string notFoundMessage)
    {
        if (string.IsNullOrWhiteSpace(typeName) || !serializers.TryGetValue(typeName, out var serializer))
        {
            throw new ArgumentException($"{notFoundMessage} {typeName}", nameof(typeName));
        }

        return serializer;
    }
}
