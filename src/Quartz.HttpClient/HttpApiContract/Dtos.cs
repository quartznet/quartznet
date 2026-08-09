#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract - Can be null when received from Web API

namespace Quartz.HttpApiContract;

internal record KeyDto(string Name, string Group) : IValidatable
{
    public static KeyDto Create(JobKey jobKey)
    {
        ArgumentNullException.ThrowIfNull(jobKey);

        return new KeyDto(jobKey.Name, jobKey.Group);
    }

    public static KeyDto Create(TriggerKey triggerKey)
    {
        ArgumentNullException.ThrowIfNull(triggerKey);

        return new KeyDto(triggerKey.Name, triggerKey.Group);
    }

    public JobKey AsJobKey() => new(Name, Group);

    public TriggerKey AsTriggerKey() => new(Name, Group);

    public IEnumerable<string> Validate()
    {
        if (Name is null)
        {
            yield return "Key is missing name";
        }

        if (Group is null)
        {
            yield return "Key is missing group";
        }
    }

    public override string ToString() => Group + '.' + Name;
}

internal record SchedulerContextDto(Dictionary<string, string?> Context)
{
    public static SchedulerContextDto Create(SchedulerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Values.Any(x => x is not string))
        {
            throw new NotSupportedException("Only string values are supported in SchedulerContext");
        }

        var data = context.ToDictionary(x => x.Key, x => (string?) x.Value);
        return new SchedulerContextDto(data);
    }

    public SchedulerContext AsContext()
    {
        return new SchedulerContext(Context.ToDictionary(x => x.Key, x => (object?) x.Value));
    }
}

internal record TriggerStateDto(TriggerState State);