using System.Collections.Generic;

namespace Quartz;

internal sealed class SchedulerNameRegistry
{
    private readonly List<string> names = new();

    public IReadOnlyList<string> Names => names;

    public void Add(string name) => names.Add(name);
}
