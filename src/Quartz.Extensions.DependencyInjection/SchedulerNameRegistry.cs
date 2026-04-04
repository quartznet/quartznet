using System;
using System.Collections.Generic;

namespace Quartz;

internal sealed class SchedulerNameRegistry
{
    private readonly List<string> names = new();

    public IReadOnlyList<string> Names => names;

    public void Add(string name)
    {
        if (names.Contains(name))
        {
            throw new ArgumentException($"A scheduler with name '{name}' has already been registered.", nameof(name));
        }

        names.Add(name);
    }
}
