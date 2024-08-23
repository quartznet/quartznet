---

title: 'Lesson 5: SimpleTrigger'
---

SimpleTrigger should meet your scheduling needs if you need to have a job execute exactly once at a specific moment in time,
or at a specific moment in time followed by repeats at a specific interval. Or plainer English, if you want the trigger to
fire at exactly 11:23:54 AM on January 13, 2005, and then fire five more times, every ten seconds.

With this description, you may not find it surprising to find that the properties of a SimpleTrigger include: a start-time,
and end-time, a repeat count, and a repeat interval. All of these properties are exactly what you'd expect them to be, with
only a couple special notes related to the end-time property.

The repeat count can be zero, a positive integer, or the constant value SimpleTrigger.RepeatIndefinitely.
The repeat interval property must be TimeSpan.Zero, or a positive TimeSpan value.
Note that a repeat interval of zero will cause 'repeat count' firings of the trigger to happen concurrently
(or as close to concurrently as the scheduler can manage).

If you're not already familiar with the DateTime class, you may find it helpful for computing your trigger fire-times,
depending on the startTimeUtc (or endTimeUtc) that you're trying to create. The TriggerUtils class is also helpful in this respect.

The EndTimeUtc property (if it is specified) over-rides the repeat count property. This can be useful if you wish to create a trigger
such as one that fires every 10 seconds until a given moment in time - rather than having to compute the number of times it would
repeat between the start-time and the end-time, you can simply specify the end-time and then use a repeat count of RepeatIndefinitely
(you could even specify a repeat count of some huge number that is sure to be more than the number of times the trigger will actually
fire before the end-time arrives).

SimpleTrigger has a few different constructors, but we'll examine this one, and use it in the few examples that follow:

__One of SimpleTrigger's Constructors__

```csharp
    public SimpleTrigger(
        string name,
        string group,
        DateTime startTimeUtc,
        NullableDateTime endTime endTimeUtc,
        int repeatCount,
        TimeSpan repeatInterval)
```

__SimpleTrigger Example 1 - Create a trigger that fires exactly once, ten seconds from now__

```csharp
    SimpleTrigger trigger = new SimpleTrigger(
        "myTrigger",
        null,
        DateTime.UtcNow.AddSeconds(10),
        null,
        0,
        TimeSpan.Zero);
```

__SimpleTrigger Example 2 - Create a trigger that fires immediately, then repeats every 60 seconds, forever__

```csharp
   SimpleTrigger trigger2 = new SimpleTrigger(
        "myTrigger",
        null,
        DateTime.UtcNow,
        null,
        SimpleTrigger.RepeatIndefinitely,
        TimeSpan.FromSeconds(60));
```

__SimpleTrigger Example 3 - Create a trigger that fires immediately, then repeats every 10 seconds until 40 seconds from now__

```csharp
TimeSpan.FromSeconds(60));
     SimpleTrigger trigger = new SimpleTrigger(
         "myTrigger",
         "myGroup",
         DateTime.UtcNow,
         DateTime.UtcNow.AddSeconds(40),
         SimpleTrigger.RepeatIndefinitely,
         TimeSpan.FromSeconds(10));
```

__SimpleTrigger Example 4 - Create a trigger that fires on March 17 of the year 2002 at precisely 10:30 am, and repeats 5 times
(for a total of 6 firings) - with a 30 second delay between each firing__

```csharp
     DateTime startTime = new DateTime(2002, 3, 17, 10, 30, 0).ToUniversalTime();

     SimpleTrigger trigger = new SimpleTrigger(
          "myTrigger",
          null,
          startTime,
          null,
          5,
          TimeSpan.FromSeconds(30));
```

Spend some time looking at the other constructors (and property setters) available on SimpleTrigger, so that you can use the
one most convenient to what you want to accomplish.

### SimpleTrigger Misfire Instructions

SimpleTrigger has several instructions that can be used to inform Quartz what it should do when a misfire occurs.
(Misfire situations were introduced in the More About Triggers section of this tutorial).
These instructions are defined as constants on MisfirePolicy.SimpleTrigger (including API documentation describing their behavior).
The instructions include:

__Misfire Instruction Constants of SimpleTrigger__

* MisfirePolicy.SimpleTrigger.FireNow
* MisfirePolicy.SimpleTrigger.RescheduleNowWithExistingRepeatCount
* MisfirePolicy.SimpleTrigger.RescheduleNowWithRemainingRepeatCount
* MisfirePolicy.SimpleTrigger.RescheduleNextWithRemainingCount
* MisfirePolicy.SimpleTrigger.RescheduleNextWithExistingCount

You should recall from the earlier lessons that all triggers have the MisfirePolicy.SmartPolicy instruction available for use,
and this instruction is also the default for all trigger types.

If the 'smart policy' instruction is used, SimpleTrigger dynamically chooses between its various MISFIRE instructions, based on the configuration
and state of the given SimpleTrigger instance. The documentation for the SimpleTrigger.UpdateAfterMisfire() method explains the exact details of
this dynamic behavior.
