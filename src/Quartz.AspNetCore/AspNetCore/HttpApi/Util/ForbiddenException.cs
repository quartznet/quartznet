namespace Quartz.AspNetCore.HttpApi.Util;

/// <summary>
/// A decision to refuse, raised from inside a handler rather than in front of one.
/// </summary>
/// <remarks>
/// The sibling of <see cref="NotFoundException" />: <see cref="ExceptionHandler" /> turns it into the
/// <c>403</c> problem details <see cref="SchedulerAuthorization" /> answers with, and it carries no
/// <c>Quartz-ExceptionType</c> for the same reason that one does not — nothing failed, a rule said no.
/// <see cref="QuartzHttpApiOptions.SchedulerAuthorizationPolicy" /> is checked in front of the endpoint
/// because the scheduler is named in the route; a job type is named in the body, so it can only be
/// refused once the body has been read.
/// </remarks>
internal sealed class ForbiddenException : Exception
{
    public ForbiddenException(string message) : base(message)
    {
    }

    /// <summary>
    /// The refusal of a job type <see cref="QuartzHttpApiOptions.IsJobTypeAllowed" /> did not allow.
    /// </summary>
    /// <remarks>
    /// The detail names the type and nothing else. That name is the caller's own input, so repeating it
    /// tells them which of the jobs they sent was refused and tells them nothing they did not write.
    /// </remarks>
    public static ForbiddenException ForJobType(string jobType) => new($"Job type {jobType} is not allowed");

    /// <summary>
    /// The refusal of a mutating route while
    /// <see cref="QuartzHttpApiOptions.ReadOnly" /> is set.
    /// </summary>
    /// <remarks>
    /// Raised in front of the handler rather than inside one: nothing about the request decides it, so
    /// there is no reason to read a body or look a scheduler up before saying no. The detail is the
    /// operator's configuration and nothing about the caller.
    /// </remarks>
    public static ForbiddenException ForReadOnlyApi() => new(ReadOnlyDetail);

    /// <summary>
    /// What a read-only refusal says, as one constant: a client matching on the detail matches on
    /// something fixed, and the test that pins the wire shape names it rather than repeating it.
    /// </summary>
    public const string ReadOnlyDetail = "The Quartz HTTP API is configured as read-only.";
}
