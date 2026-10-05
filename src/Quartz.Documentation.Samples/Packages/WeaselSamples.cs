using JasperFx;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Quartz.Documentation.Samples.Packages;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/packages/weasel.md.
/// </summary>
public static class WeaselSamples
{
    public static async Task<int> Standalone(string[] args, string connectionString)
    {
        #region sample_weasel_postgres

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        builder.Services.AddQuartz(q => q.UsePersistentStore(store =>
        {
            store.UsePostgres(connectionString);
            store.UseSystemTextJsonSerializer();
            store.UseWeaselForPostgres();
        }));
        builder.Services.AddQuartzHostedService();

        IHost host = builder.Build();

        // db-apply, db-assert, db-patch, resources: JasperFx's command line, over this host
        return await host.RunJasperFxCommands(args);

        #endregion
    }

    public static void Options(IServiceCollection services, string connectionString)
    {
        services.AddQuartz(q => q.UsePersistentStore(store =>
        {
            store.UsePostgres(connectionString);

            #region sample_weasel_postgres_options

            store.UseWeaselForPostgres(weasel =>
            {
                // unset: the active JasperFx profile's ResourceAutoCreate, else CreateOrUpdate
                weasel.AutoCreate = AutoCreate.CreateOrUpdate;
                weasel.LockId = PostgresWeaselOptions.DefaultLockId;
                weasel.LockTimeout = TimeSpan.FromMinutes(1);
            });

            #endregion
        }));
    }

    public static void Sqlite(IServiceCollection services, string connectionString)
    {
        #region sample_weasel_sqlite

        services.AddQuartz(q => q.UsePersistentStore(store =>
        {
            store.UseSqlite(connectionString);
            store.UseWeaselForSqlite();
        }));

        #endregion
    }

    public static void SqlServer(IServiceCollection services, string connectionString)
    {
        #region sample_weasel_sqlserver

        services.AddQuartz(q => q.UsePersistentStore(store =>
        {
            store.UseSqlServer(connectionString);
            store.UseWeaselForSqlServer();
        }));

        #endregion
    }

    public static void SqlServerOptions(IServiceCollection services, string connectionString)
    {
        services.AddQuartz(q => q.UsePersistentStore(store =>
        {
            store.UseSqlServer(connectionString);

            #region sample_weasel_sqlserver_options

            // tables quartz.QRTZ_JOB_DETAILS, quartz.QRTZ_TRIGGERS, ...
            store.ConfigureStore(options => options.TablePrefix = "quartz.QRTZ_");
            store.UseWeaselForSqlServer(weasel =>
            {
                // unset: the active JasperFx profile's ResourceAutoCreate, else CreateOrUpdate
                weasel.AutoCreate = AutoCreate.CreateOrUpdate;
                weasel.LockResource = SqlServerWeaselOptions.DefaultLockResource;
                weasel.LockTimeout = TimeSpan.FromMinutes(1);
            });

            #endregion
        }));
    }

    public static void MySql(IServiceCollection services, string connectionString)
    {
        #region sample_weasel_mysql

        services.AddQuartz(q => q.UsePersistentStore(store =>
        {
            store.UseMySqlConnector(connectionString);
            store.UseWeaselForMySql();
        }));

        #endregion
    }

    public static void MySqlOptions(IServiceCollection services, string connectionString)
    {
        services.AddQuartz(q => q.UsePersistentStore(store =>
        {
            store.UseMySqlConnector(connectionString);

            #region sample_weasel_mysql_options

            store.UseWeaselForMySql(weasel =>
            {
                // unset: the active JasperFx profile's ResourceAutoCreate, else CreateOrUpdate
                weasel.AutoCreate = AutoCreate.CreateOrUpdate;
                // server-wide: give each database its own name to keep their applies apart
                weasel.LockName = MySqlWeaselOptions.DefaultLockName;
                weasel.LockTimeout = TimeSpan.FromMinutes(1);
            });

            #endregion
        }));
    }

    public static void Oracle(IServiceCollection services, string connectionString)
    {
        #region sample_weasel_oracle

        services.AddQuartz(q => q.UsePersistentStore(store =>
        {
            store.UseOracle(connectionString);
            store.UseWeaselForOracle();
        }));

        #endregion
    }

    public static void Firebird(IServiceCollection services, string connectionString)
    {
        #region sample_weasel_firebird

        services.AddQuartz(q => q.UsePersistentStore(store =>
        {
            store.UseFirebird(connectionString);
            store.UseWeaselForFirebird();
        }));

        #endregion
    }

    public static void FirebirdOptions(IServiceCollection services, string connectionString)
    {
        services.AddQuartz(q => q.UsePersistentStore(store =>
        {
            store.UseFirebird(connectionString);

            #region sample_weasel_firebird_options

            // IDX_QRTZ_REPORTING_FT_INST_JOB_REQ_RCVRY is 40 characters, past Firebird 3's 31 bytes
            store.ConfigureStore(options => options.TablePrefix = "QRTZ_REPORTING_");
            store.UseWeaselForFirebird(weasel =>
            {
                // unset: the active JasperFx profile's ResourceAutoCreate, else CreateOrUpdate
                weasel.AutoCreate = AutoCreate.CreateOrUpdate;
                // only for a database Firebird 3 never opens
                weasel.MaxIdentifierLength = 63;
            });

            #endregion
        }));
    }

    public static void Profile(IServiceCollection services)
    {
        #region sample_weasel_jasperfx_profile

        // Weasel applies nothing at startup in Production; db-apply does it at deploy time instead.
        services.AddJasperFx(options => options.Production.ResourceAutoCreate = AutoCreate.None);

        #endregion
    }

    public static void Prefix(IServiceCollection services, string connectionString)
    {
        services.AddQuartz(q => q.UsePersistentStore(store =>
        {
            store.UsePostgres(connectionString);

            #region sample_weasel_schema_prefix

            // tables quartz.qrtz_job_details, quartz.qrtz_triggers, ...
            store.ConfigureStore(options => options.TablePrefix = "quartz.qrtz_");
            store.UseWeaselForPostgres();

            #endregion
        }));
    }
}
