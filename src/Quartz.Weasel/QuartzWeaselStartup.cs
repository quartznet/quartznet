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

using JasperFx;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Weasel.Core;

namespace Quartz.Weasel;

/// <summary>
/// Applies each Weasel-managed scheduler's schema as the host starts, before any scheduler is built.
/// </summary>
/// <remarks>
/// <para>
/// In <see cref="IHostedLifecycleService.StartingAsync" /> because the host finishes every hosted
/// service's <c>StartingAsync</c> before it calls any <c>StartAsync</c>, and <c>QuartzHostedService</c>
/// builds its schedulers — and each store validates its schema — in <c>StartAsync</c>. Registered after
/// the hosted service or before it, the schema is in place first.
/// </para>
/// <para>
/// How far it goes is <see cref="AutoCreate" />: the application's own choice when it made one, the
/// active JasperFx profile's <c>ResourceAutoCreate</c> when the application registered JasperFx — the way
/// Marten and Wolverine decide — and <see cref="AutoCreate.CreateOrUpdate" /> otherwise.
/// <see cref="AutoCreate.None" /> applies nothing, and the store's own validation is what then stands
/// between an empty database and a scheduler.
/// </para>
/// </remarks>
internal sealed class QuartzWeaselStartup : IHostedLifecycleService
{
    private readonly QuartzWeaselSystemPart part;
    private readonly JasperFxOptions? jasperFx;

    public QuartzWeaselStartup(QuartzWeaselSystemPart part, IServiceProvider services)
    {
        this.part = part;
        jasperFx = services.GetService<JasperFxOptions>();
    }

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        foreach (IQuartzWeaselDatabase database in part.Databases)
        {
            QuartzWeaselDatabaseContext context = database.Context;
            AutoCreate autoCreate = context.AutoCreate ?? jasperFx?.ActiveProfile.ResourceAutoCreate ?? AutoCreate.CreateOrUpdate;

            if (autoCreate == AutoCreate.None)
            {
                context.Logger.SchemaApplySkipped(context.SchedulerName, autoCreate);
                continue;
            }

            ResourceMigrationFailureMode failureMode = jasperFx?.ActiveProfile.ResourceMigrationFailureMode
                                                       ?? ResourceMigrationFailureMode.FailFast;
            database.ResourceMigrationFailureMode = failureMode;

            try
            {
                SchemaPatchDifference difference = await database
                    .ApplyAllConfiguredChangesToDatabaseAsync(autoCreate, ct: cancellationToken)
                    .ConfigureAwait(false);

                if (difference == SchemaPatchDifference.None)
                {
                    context.Logger.SchemaUnchanged(context.SchedulerName, database.Describe().DatabaseUri());
                }
                else
                {
                    context.Logger.SchemaApplied(context.SchedulerName, database.Describe().DatabaseUri(), difference);
                }
            }
            catch (Exception e) when (failureMode == ResourceMigrationFailureMode.ContinueOnFailures
                                      && !cancellationToken.IsCancellationRequested)
            {
                context.Logger.SchemaApplyFailedContinuing(context.SchedulerName, e);
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
