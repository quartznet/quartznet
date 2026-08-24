namespace Quartz.Documentation.Samples.Tutorial;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/tutorial/node-affinity.md.
/// </summary>
public static class NodeAffinitySamples
{
    public static void PinToANode(IJobDetail job)
    {
        #region sample_node_affinity_pin

        // Pin to a specific node
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("myTrigger")
            .ForJob(job)
            .WithPreferredNode(PreferredNode.For("production-node-1"))
            .WithCronSchedule("0 0/5 * * * ?")
            .Build();

        #endregion
    }

    public static void AutoPin(IJobDetail job)
    {
        #region sample_node_affinity_auto_pin

        // Auto-pin: the first node to fire it claims it
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("myTrigger")
            .ForJob(job)
            .WithPreferredNode(PreferredNode.Auto)
            .WithCronSchedule("0 0/5 * * * ?")
            .Build();

        #endregion
    }

    public static async ValueTask ReadingThePin(IScheduler scheduler)
    {
        #region sample_node_affinity_reading_the_pin

        ITrigger? t = await scheduler.GetTrigger(new TriggerKey("myTrigger"));
        PreferredNode pin = t!.PreferredNode;   // GetTrigger returns null when there is no such trigger
        string? node = pin.Node;         // "production-node-1"; null when unpinned or an unclaimed auto-pin
        bool auto = pin.IsAutomatic;     // false for a pin you named
        bool unpinned = pin.IsNone;

        #endregion
    }

    public static void RebuildingKeepsThePin(ITrigger trigger)
    {
        #region sample_node_affinity_rebuild

        // The rebuilt trigger is still auto-pinned, so it will still fail over if that node dies
        ITrigger rebuilt = trigger.GetTriggerBuilder().WithDescription("updated").Build();

        #endregion
    }

    public static void ChangingThePinWhileRebuilding(ITrigger trigger)
    {
        #region sample_node_affinity_rebuild_with_new_pin

        // a pin you named; IsAutomatic is false
        ITrigger named = trigger.GetTriggerBuilder()
            .WithPreferredNode(PreferredNode.For("node-2"))
            .Build();

        // no preference at all
        ITrigger unpinned = trigger.GetTriggerBuilder()
            .WithPreferredNode(PreferredNode.None)
            .Build();

        #endregion
    }

    public static async ValueTask MovingAPin(IScheduler scheduler)
    {
        #region sample_node_affinity_move_pin

        await scheduler.UpdateTriggerDetails(
            new TriggerKey("myTrigger"),
            new TriggerDetailsUpdate().WithPreferredNode(PreferredNode.For("node-2")));

        #endregion
    }

    public static async ValueTask ClearingAPin(IScheduler scheduler)
    {
        #region sample_node_affinity_clear_pin

        await scheduler.UpdateTriggerDetails(
            new TriggerKey("myTrigger"),
            new TriggerDetailsUpdate().WithPreferredNode(PreferredNode.None));

        #endregion
    }
}
