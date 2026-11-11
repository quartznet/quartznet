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

using Quartz.Dashboard.Components.Shared;

namespace Quartz.Dashboard.Services.Timeline;

/// <summary>
/// Trigger mutations for the Timeline page: API call, toast, action log, then reload.
/// </summary>
internal sealed class TimelineActions
{
    private readonly IQuartzApiClient _api;
    private readonly ToastService _toasts;
    private readonly DashboardActionLog _actionLog;
    private readonly QuartzDashboardOptions _options;
    private readonly Func<Task> _reloadTimeline;

    public TimelineActions(
        IQuartzApiClient api,
        ToastService toasts,
        DashboardActionLog actionLog,
        QuartzDashboardOptions options,
        Func<Task> reloadTimeline)
    {
        _api = api;
        _toasts = toasts;
        _actionLog = actionLog;
        _options = options;
        _reloadTimeline = reloadTimeline;
    }

    public Task ResumeTriggerAsync(string schedulerName, string triggerGroup, string triggerName)
    {
        TriggerKeyDto key = new(triggerGroup, triggerName);
        string label = DisplayValueHelper.FormatKey(triggerGroup, triggerName);
        return ExecuteBoolMutationAsync(
            schedulerName,
            () => _api.ResumeTrigger(schedulerName, key),
            "Resumed trigger " + label + ".",
            "Trigger " + label + " was not resumed - it no longer exists or was not paused.",
            "ResumeTrigger",
            label);
    }

    public Task ResetTriggerAsync(string schedulerName, string triggerGroup, string triggerName)
    {
        TriggerKeyDto key = new(triggerGroup, triggerName);
        string label = DisplayValueHelper.FormatKey(triggerGroup, triggerName);
        return ExecuteBoolMutationAsync(
            schedulerName,
            () => _api.ResetTriggerFromErrorState(schedulerName, key),
            "Reset error state for trigger " + label + ".",
            "Trigger " + label + " was not reset - it no longer exists or is not in the error state.",
            "ResetTriggerFromErrorState",
            label);
    }

    public Task RunNowAsync(string schedulerName, string jobGroup, string jobName)
    {
        JobKeyDto jobKey = new(jobGroup, jobName);
        string label = DisplayValueHelper.FormatKey(jobGroup, jobName);
        return ExecuteVoidMutationAsync(
            schedulerName,
            () => _api.TriggerJob(schedulerName, jobKey),
            "Triggered job " + label + ".",
            "TriggerJob",
            label);
    }

    public Task PauseTriggerAsync(string schedulerName, string triggerGroup, string triggerName, PauseDetails details)
    {
        TriggerKeyDto key = new(triggerGroup, triggerName);
        string label = DisplayValueHelper.FormatKey(triggerGroup, triggerName);
        return ExecuteBoolMutationAsync(
            schedulerName,
            () => _api.PauseTriggerWith(schedulerName, key, details),
            "Paused trigger " + label + ".",
            "Trigger " + label + " was not paused - it no longer exists.",
            "PauseTrigger",
            label,
            DashboardActionLog.PauseNote(details));
    }

    public Task UnscheduleTriggerAsync(string schedulerName, string triggerGroup, string triggerName)
    {
        TriggerKeyDto key = new(triggerGroup, triggerName);
        string label = DisplayValueHelper.FormatKey(triggerGroup, triggerName);
        return ExecuteBoolMutationAsync(
            schedulerName,
            () => _api.UnscheduleJob(schedulerName, key),
            "Unscheduled trigger " + label + ".",
            "Trigger " + label + " was not unscheduled - it no longer exists.",
            "UnscheduleTrigger",
            label);
    }

    private async Task ExecuteBoolMutationAsync(
        string? schedulerName,
        Func<ValueTask<bool>> operation,
        string successMessage,
        string noOpMessage,
        string actionName,
        string target,
        string? note = null)
    {
        if (_options.ReadOnly || string.IsNullOrWhiteSpace(schedulerName))
        {
            return;
        }

        try
        {
            bool changed = await operation().ConfigureAwait(true);
            if (changed)
            {
                _actionLog.Record(schedulerName, actionName, target, succeeded: true, message: note);
                _toasts.Success(successMessage);
                await _reloadTimeline().ConfigureAwait(true);
            }
            else
            {
                _toasts.Error(noOpMessage);
            }
        }
        catch (Exception ex)
        {
            _actionLog.Failed(schedulerName, actionName, target, ex);
            _toasts.Error(ex.Message);
        }
    }

    private async Task ExecuteVoidMutationAsync(
        string? schedulerName,
        Func<ValueTask> operation,
        string successMessage,
        string actionName,
        string target)
    {
        if (_options.ReadOnly || string.IsNullOrWhiteSpace(schedulerName))
        {
            return;
        }

        try
        {
            await operation().ConfigureAwait(true);
            _actionLog.Record(schedulerName, actionName, target, succeeded: true);
            _toasts.Success(successMessage);
            await _reloadTimeline().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _actionLog.Failed(schedulerName, actionName, target, ex);
            _toasts.Error(ex.Message);
        }
    }
}
