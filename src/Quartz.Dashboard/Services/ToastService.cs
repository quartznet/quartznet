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

namespace Quartz.Dashboard.Services;

internal enum ToastLevel
{
    Info,
    Success,
    Error
}

internal sealed record ToastMessage(int Id, string Message, ToastLevel Level);

internal sealed class ToastService
{
    private readonly Lock gate = new();
    private readonly List<ToastMessage> messages = [];
    private int nextId;

    public event EventHandler? Changed;

    public IReadOnlyList<ToastMessage> Messages
    {
        get
        {
            lock (gate)
            {
                return messages.ToArray();
            }
        }
    }

    public void Info(string message)
    {
        Add(message, ToastLevel.Info);
    }

    public void Success(string message)
    {
        Add(message, ToastLevel.Success);
    }

    public void Error(string message)
    {
        Add(message, ToastLevel.Error);
    }

    public void Dismiss(int id)
    {
        bool removed;
        lock (gate)
        {
            removed = messages.RemoveAll(x => x.Id == id) > 0;
        }

        if (removed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Add(string message, ToastLevel level)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        int id = Interlocked.Increment(ref nextId);

        lock (gate)
        {
            messages.Add(new ToastMessage(id, message, level));
            while (messages.Count > 5)
            {
                messages.RemoveAt(0);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
        _ = RemoveAfterDelay(id, TimeSpan.FromSeconds(4));
    }

    private async Task RemoveAfterDelay(int id, TimeSpan delay)
    {
        await Task.Delay(delay).ConfigureAwait(false);
        Dismiss(id);
    }
}
