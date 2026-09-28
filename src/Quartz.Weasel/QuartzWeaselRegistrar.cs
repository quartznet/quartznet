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

using JasperFx.CommandLine.Descriptions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Weasel.Core.Migrations;

namespace Quartz.Weasel;

/// <summary>
/// What every dialect's registration does to the container, whichever dialect it is.
/// </summary>
internal static class QuartzWeaselRegistrar
{
    /// <summary>
    /// Records that a scheduler's schema is Weasel's to manage, and registers the parts that act on the
    /// record once per container.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One record per scheduler: a second call for the same scheduler replaces the first, so the last
    /// options win the way they do everywhere else on the store builder. A second call naming a different
    /// dialect is refused, because a store has one database.
    /// </para>
    /// <para>
    /// The system part is both JasperFx's <see cref="ISystemPart" />, which is what <c>resources</c> and
    /// <c>describe</c> walk, and Weasel's <see cref="IDatabaseSource" />, which is what <c>db-apply</c>,
    /// <c>db-assert</c> and <c>db-patch</c> walk — one instance, so both see the same databases.
    /// </para>
    /// </remarks>
    public static void Register(IPersistentStoreBuilder store, QuartzWeaselRegistration registration)
    {
        IServiceCollection services = store.Services;

        // ServiceType and IsKeyedService before ImplementationInstance, which throws on a keyed descriptor
        // and a named scheduler registers plenty of those.
        for (int i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType != typeof(QuartzWeaselRegistration)
                || services[i].IsKeyedService
                || services[i].ImplementationInstance is not QuartzWeaselRegistration existing
                || !string.Equals(existing.SchedulerKey, registration.SchedulerKey, StringComparison.Ordinal))
            {
                continue;
            }

            if (!ReferenceEquals(existing.Dialect, registration.Dialect))
            {
                throw new SchedulerConfigException(
                    $"{registration.Dialect.RegistrationMethod}() was called for a scheduler whose schema"
                    + $" {existing.Dialect.RegistrationMethod}() already handed to Weasel. A store has one database,"
                    + " so call the one that matches it.");
            }

            services.RemoveAt(i);
        }

        services.AddSingleton(registration);

        string optionsName = registration.SchedulerKey ?? Options.DefaultName;
        if (!services.Any(x => x.ServiceType == typeof(IValidateOptions<AdoJobStoreOptions>)
                               && !x.IsKeyedService
                               && x.ImplementationInstance is SingleSchemaOwnerValidator validator
                               && validator.OptionsName == optionsName))
        {
            services.AddSingleton<IValidateOptions<AdoJobStoreOptions>>(
                new SingleSchemaOwnerValidator(optionsName, registration.Dialect.RegistrationMethod));
        }

        if (services.Any(x => x.ServiceType == typeof(QuartzWeaselSystemPart)))
        {
            return;
        }

        services.AddSingleton<QuartzWeaselSystemPart>();
        services.AddSingleton<ISystemPart>(provider => provider.GetRequiredService<QuartzWeaselSystemPart>());
        services.AddSingleton<IDatabaseSource>(provider => provider.GetRequiredService<QuartzWeaselSystemPart>());
        services.AddHostedService<QuartzWeaselStartup>();
    }
}

/// <summary>
/// Refuses a store that asked both Weasel and itself to create its schema.
/// </summary>
/// <remarks>
/// <c>ProvisionSchema()</c> runs Quartz's own guarded script at startup; Weasel migrates towards its
/// model. Either can create the schema, and running both only means two owners with two answers to what
/// the schema should be. Validation (<see cref="SchemaProvisioning.Validate" />, the default) and
/// <see cref="SchemaProvisioning.None" /> both stay available: neither creates anything.
/// </remarks>
internal sealed class SingleSchemaOwnerValidator : IValidateOptions<AdoJobStoreOptions>
{
    private readonly string registrationMethod;

    public SingleSchemaOwnerValidator(string optionsName, string registrationMethod)
    {
        OptionsName = optionsName;
        this.registrationMethod = registrationMethod;
    }

    public string OptionsName { get; }

    public ValidateOptionsResult Validate(string? name, AdoJobStoreOptions options)
    {
        if (!string.Equals(name ?? Options.DefaultName, OptionsName, StringComparison.Ordinal)
            || options.SchemaProvisioning != SchemaProvisioning.CreateIfMissing)
        {
            return ValidateOptionsResult.Skip;
        }

        return ValidateOptionsResult.Fail(
            $"{registrationMethod}() hands this store's schema to Weasel, and ProvisionSchema() hands it to the store;"
            + " a schema has one owner. Remove ProvisionSchema() - or set AdoJobStoreOptions.SchemaProvisioning back"
            + " to Validate - and Weasel creates the schema and moves it forward.");
    }
}
