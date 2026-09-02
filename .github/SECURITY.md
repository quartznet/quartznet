# Security policy

## Supported versions

Security fixes are issued for the following lines:

| Version  | Supported          |
| -------- | ------------------ |
| 4.x      | :white_check_mark: |
| 3.x      | :white_check_mark: |
| < 3.0    | :x:                |

## Reporting a vulnerability

**Please do not report security vulnerabilities through public GitHub issues, discussions, or pull requests.**

Use GitHub's private vulnerability reporting:

- Open <https://github.com/quartznet/quartznet/security/advisories/new>
- Provide a clear description, the affected version(s), and a proof of concept or reproduction steps
- Indicate the impact (e.g. data loss, deadlock, information disclosure, remote code execution)

## What is not a vulnerability

These four come up regularly. Each is a deliberate design decision, documented where it applies, and a
report of one will be closed with a link back to this section — so please save your time and ours.

- **An authorized caller of the HTTP API or the dashboard is trusted fully — including with code
  execution on the host.** Quartz has no per-operation permission model: whoever passes the authorization
  you configured can schedule, trigger, pause, delete and shut down every scheduler they can see, and can
  read every job's data map. A job's type is a string the request carries; Quartz will only construct a
  type that implements `IJob`, but `NativeJob` implements `IJob` and starts the executable its job data
  names, so an authorized caller scheduling it is the API working as designed. `Quartz.Plugins` depends
  on `Quartz.Jobs`, so that type can be on the probing path of an application whose project file never
  names it. A job stored by one node is likewise resolved and constructed on **every** node that reads
  it, which is what a clustered scheduler is for. `QuartzDashboardOptions.ReadOnly` and the two
  `SchedulerAuthorizationPolicy` settings are the only narrowings on offer. Authorize these surfaces the
  way you would authorize a shell —
  [HTTP API](https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/http-api.html#production-hardening),
  [dashboard](https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/dashboard.html#production-hardening).
- **There is no rate limiting on any Quartz surface**, by design. ASP.NET Core's own rate limiter
  middleware applies to Quartz's endpoints like any others, and configuring one is the application's
  call rather than a scheduling library's.
- **The strong-name key pair is committed to this repository and is public.** Strong naming is assembly
  *identity*, not integrity — the .NET runtime does not verify strong-name signatures — so a key anyone
  can read is how an open-source library gives its consumers a stable identity to bind against.
  `InternalsVisibleTo` is therefore not a security boundary either, and nothing in Quartz treats it as
  one. Quartz author-signs nothing; what stands behind a published package is that nuget.org applies its
  own repository signature to everything it serves, and that a package can only reach nuget.org from
  this repository's `publish.yml` running on a `v*.*.*` tag, through
  [trusted publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing) — a GitHub OIDC
  token exchanged for a short-lived key, with no long-lived credential to steal.
- **Job data is not a secret store.** A `JobDataMap` is persisted in the job store, is readable through
  the HTTP API and the dashboard, and appears in logs and traces. A password or a connection string
  belongs in configuration or a secret manager and reaches the job through the container —
  [Keep the SMTP credential out of job data](https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/quartz-jobs.html#keep-the-smtp-credential-out-of-job-data)
  is the worked example.

## What to expect

- Acknowledgment of your report within a few business days.
- An initial assessment and, where applicable, a coordinated fix and release plan.
- Public disclosure via a GitHub Security Advisory once a fix is available, with credit to you (unless you prefer to remain anonymous).

If you do not receive a response within a reasonable time, you can ping the maintainers in the advisory thread or via the public discussion forums for a status check — but please continue to keep technical details private.
