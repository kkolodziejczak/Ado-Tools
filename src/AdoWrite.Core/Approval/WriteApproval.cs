namespace AdoWrite.Approval;

/// <summary>
/// How a write that is not a dry run reaches Azure DevOps. By default it is queued in the outbox
/// and only the approval window, driven by the person at the keyboard, posts it. A machine whose
/// administrator turned approval off, and the approval window itself, post directly.
/// </summary>
public sealed class WriteApproval
{
    private WriteApproval(ApprovalOutbox? outbox, IApprovalWindow? window)
    {
        Outbox = outbox;
        Window = window;
    }

    /// <summary>Posts directly. Only code can choose it: no command-line option or user setting reaches it.</summary>
    public static WriteApproval Granted { get; } = new(null, null);

    /// <summary>Queues every write in <paramref name="outbox"/> and makes sure <paramref name="window"/> is open.</summary>
    public static WriteApproval QueueIn(ApprovalOutbox outbox, IApprovalWindow window) => new(outbox, window);

    /// <summary>Queues for the current user, unless this machine turned approval off.</summary>
    public static WriteApproval ForThisMachine()
    {
        if (MachineApprovalSetting.IsOff(MachineApprovalSetting.Read()))
        {
            return Granted;
        }

        ApprovalOutbox outbox = ApprovalOutbox.ForCurrentUser();
        return QueueIn(outbox, new ConsoleApprovalWindow(outbox.Directory));
    }

    internal ApprovalOutbox? Outbox { get; }

    internal IApprovalWindow? Window { get; }
}
