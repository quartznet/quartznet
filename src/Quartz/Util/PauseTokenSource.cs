namespace Quartz.Util;

/// <summary>
/// The source (controller) of a "pause token", which can be used to cooperatively pause and unpause operations.
/// Adapted from: https://github.com/StephenCleary/AsyncEx/blob/0361015459938f2eb8f3c1ad1021d19ee01c93a4/src/Nito.AsyncEx.Coordination/PauseToken.cs
/// </summary>
public sealed class PauseTokenSource
{
    /// <summary>
    /// The MRE that manages the "pause" logic. When the MRE is set, the token is not paused; when the MRE is not set, the token is paused.
    /// </summary>
    private readonly AsyncManualResetEvent mre = new(true);

    /// <summary>
    /// Whether this source (and its tokens) are in the paused state.
    /// Reading this member has a high possibility of race conditions.
    /// </summary>
    public bool IsPaused
    {
        get => !mre.IsSet;
        set
        {
            if (value)
            {
                mre.Reset();
            }
            else
            {
                mre.Set();
            }
        }
    }

    /// <summary>
    /// Gets a pause token controlled by this source.
    /// </summary>
    public PauseToken Token => new(mre);

    /// <summary>
    /// New pause token defaults to unpaused state.
    /// </summary>
    /// <param name="startPaused">Pass true to start the controller in the paused state.</param>
    public PauseTokenSource(bool startPaused = false)
    {
        IsPaused = startPaused;
    }
}

/// <summary>
/// A type that allows an operation to be cooperatively paused.
/// </summary>
public readonly struct PauseToken
{
    /// <summary>
    /// The MRE that manages the "pause" logic, or <c>null</c> if this token can never be paused. When the MRE is set, the token is not paused; when the MRE is not set, the token is paused.
    /// </summary>
    private readonly AsyncManualResetEvent mre;

    internal PauseToken(AsyncManualResetEvent mre)
    {
        this.mre = mre;
    }

    /// <summary>
    /// Whether this token can ever possibly be paused.
    /// </summary>
    public bool CanBePaused => mre != null;

    /// <summary>
    /// Whether this token is in the paused state.
    /// </summary>
    public bool IsPaused => mre is { IsSet: false };

    /// <summary>
    /// Asynchronously waits until the pause token is not paused.
    /// </summary>
    public Task WaitWhilePausedAsync()
    {
        return mre is null ? 
            Task.CompletedTask : 
            mre.WaitAsync();
    }

    /// <summary>
    /// Asynchronously waits until the pause token is not paused, or until this wait is canceled by the cancellation token.
    /// </summary>
    /// <param name="token">
    /// The cancellation token to observe.
    /// If the token is already canceled, this method will first check if the pause token is unpaused,
    /// and will return without an exception in that case.
    /// </param>
    public Task WaitWhilePausedAsync(CancellationToken token)
    {
        return mre is null ? 
            Task.CompletedTask : 
            mre.WaitAsync(token);
    }
}