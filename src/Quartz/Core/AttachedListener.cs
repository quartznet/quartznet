namespace Quartz.Core;

/// <summary>
/// A listener as it is attached to a scheduler: the listener itself, the name it was attached under,
/// and the matchers that decide which notifications reach it.
/// </summary>
/// <remarks>
/// The matchers live beside the listener rather than in a second dictionary keyed by listener name,
/// because every notification consults them for every attached listener: a fire that told three
/// listeners used to cost three lookups to learn something the registration already knew. Holding the
/// pair together also makes it impossible for the two to disagree, which is why the matchers are
/// settled once, when the listener is attached.
/// <para>
/// The name is captured rather than read back from the listener, because a listener whose
/// <c>Name</c> is a settable property would otherwise stop answering to the name it was attached
/// under the moment the property changed.
/// </para>
/// </remarks>
/// <typeparam name="TListener"><see cref="IJobListener" /> or <see cref="ITriggerListener" />.</typeparam>
/// <typeparam name="TKey">The key the matchers select on.</typeparam>
internal readonly struct AttachedListener<TListener, TKey> where TKey : Key<TKey>
{
    public AttachedListener(string name, TListener listener, IMatcher<TKey>[] matchers)
    {
        Name = name;
        Listener = listener;
        Matchers = matchers;
    }

    /// <summary>
    /// The name the listener was attached under, and the name it is removed by.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The attached listener.
    /// </summary>
    public TListener Listener { get; }

    /// <summary>
    /// The matchers the listener was attached with. Empty means every notification reaches it.
    /// </summary>
    public IMatcher<TKey>[] Matchers { get; }

    /// <summary>
    /// Whether a notification about <paramref name="key" /> is one this listener asked for.
    /// </summary>
    /// <remarks>
    /// A listener attached with no matchers hears everything, and one attached with several hears a
    /// key that ANY of them matches.
    /// </remarks>
    public bool Matches(TKey key)
    {
        IMatcher<TKey>[] matchers = Matchers;
        if (matchers.Length == 0)
        {
            return true;
        }

        foreach (IMatcher<TKey> matcher in matchers)
        {
            if (matcher.IsMatch(key))
            {
                return true;
            }
        }

        return false;
    }
}
