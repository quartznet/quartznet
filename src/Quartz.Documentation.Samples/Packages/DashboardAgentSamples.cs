using System.Net;
using System.Security.Cryptography.X509Certificates;

using Microsoft.Extensions.Hosting;

namespace Quartz.Documentation.Samples.Packages;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/packages/dashboard-agent.md and the package's README.
/// </summary>
public static class DashboardAgentSamples
{
    /// <summary>Stands in for whatever the host authenticates machines with: a managed identity, an OIDC client.</summary>
    public interface IMachineCredential
    {
        ValueTask<string?> GetAccessToken(string scope, CancellationToken cancellationToken = default);
    }

    public static void Readme(HostApplicationBuilder builder)
    {
        #region sample_readme_dashboard_agent

        builder.AddQuartz(q =>
        {
            q.UseDashboardAgent(agent =>
            {
                agent.Endpoint = new Uri("https://ops.example.com/quartz/agents");
                agent.Token = builder.Configuration["Dashboard:AgentToken"];
                agent.Target = "worker-1";

                // Refused until said: an agent crosses a trust boundary, and a job type is a string
                // the dashboard sends.
                agent.IsJobTypeAllowed = name => name.StartsWith("Acme.Jobs.", StringComparison.Ordinal);
            });
        });

        #endregion
    }

    public static void Worker(string[] args)
    {
        #region sample_dashboard_agent_worker

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        builder.AddQuartz(q =>
        {
            q.UseDashboardAgent(agent =>
            {
                // The dashboard's path followed by /agents: AcceptAgents maps the hub there.
                agent.Endpoint = new Uri("https://ops.example.com/quartz/agents");

                // Sent as an Authorization: Bearer header on every connection, never in the URL.
                agent.Token = builder.Configuration["Dashboard:AgentToken"];

                // The first half of the scheduler's key on the dashboard: worker-1/QuartzScheduler.
                // Defaults to the machine name.
                agent.Target = "worker-1";

                // Which job types a request through this agent may name. Unset, every one is refused.
                agent.IsJobTypeAllowed = name => name.StartsWith("Acme.Jobs.", StringComparison.Ordinal);
            });
        });

        builder.AddQuartzHostedService();

        #endregion
    }

    public static void Narrowed(HostApplicationBuilder builder)
    {
        #region sample_dashboard_agent_narrowed

        builder.AddQuartz(q =>
        {
            q.UseDashboardAgent(agent =>
            {
                agent.Endpoint = new Uri("https://ops.example.com/quartz/agents");
                agent.Token = builder.Configuration["Dashboard:AgentToken"];

                // Every operation that changes something is 403; the dashboard shows the refusal.
                agent.ReadOnly = true;

                // Or keep writes and refuse by route name — the operation ids the HTTP API publishes.
                agent.IsOperationAllowed = operation => operation is not ("Shutdown" or "Clear" or "DeleteJobs");

                // The job types the agent will store, matched on the name as the request wrote it.
                agent.IsJobTypeAllowed = name => name.StartsWith("Acme.Jobs.", StringComparison.Ordinal);
            });
        });

        #endregion
    }

    public static void HostAuthentication(HostApplicationBuilder builder, IMachineCredential credential)
    {
        #region sample_dashboard_agent_host_auth

        builder.AddQuartz(q =>
        {
            q.UseDashboardAgent(agent =>
            {
                agent.Endpoint = new Uri("https://ops.example.com/quartz/agents");

                // The host's own credential instead of a shared secret: asked before every connection,
                // so an expiring token is renewed. The dashboard holds the hub to a policy that
                // accepts it: AcceptAgents(a => a.AuthorizationPolicy = "agents").
                agent.AccessTokenProvider = cancellationToken =>
                    credential.GetAccessToken("api://quartz-dashboard/.default", cancellationToken);

                agent.IsJobTypeAllowed = name => name.StartsWith("Acme.Jobs.", StringComparison.Ordinal);
            });
        });

        #endregion
    }

    public static void Connection(IQuartzBuilder q)
    {
        #region sample_dashboard_agent_connection

        q.UseDashboardAgent(agent =>
        {
            agent.Endpoint = new Uri("https://ops.example.com/quartz/agents");
            agent.Token = "…";
            agent.IsJobTypeAllowed = name => name.StartsWith("Acme.Jobs.", StringComparison.Ordinal);

            // The connection is the SignalR client's: a proxy, a client certificate, the transports.
            agent.ConfigureConnection = connection =>
            {
                connection.Proxy = new WebProxy("http://proxy.internal:3128");
                connection.ClientCertificates = [X509CertificateLoader.LoadPkcs12FromFile("worker-1.pfx", password: null)];
            };
        });

        #endregion
    }
}
