namespace Quartz.Impl.Triggers;

internal sealed class TriggerComparer : IComparer<ITrigger>
{
    internal static readonly TriggerComparer Instance = new();

    private TriggerComparer()
    {
    }

    public int Compare(ITrigger? x, ITrigger? y)
    {
        if (y?.Key is null && x?.Key is null)
        {
            return 0;
        }

        if (y?.Key is null)
        {
            return -1;
        }

        if (x?.Key is null)
        {
            return 1;
        }

        return x.Key.CompareTo(y.Key);
    }
}