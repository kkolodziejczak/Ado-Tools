namespace AdoWrite.Approval;

/// <summary>
/// How a write that is not a dry run reaches Azure DevOps. By default it is saved and opened in an
/// approval window of its own, where only the person at the keyboard posts it. A machine whose
/// administrator turned approval off, and the approval window itself, post directly.
/// </summary>
public sealed class WriteApproval
{
    private WriteApproval(PendingWrites? pending, IApprovalWindow? window)
    {
        Pending = pending;
        Window = window;
    }

    /// <summary>Posts directly. Only code can choose it: no command-line option or user setting reaches it.</summary>
    public static WriteApproval Granted { get; } = new(null, null);

    /// <summary>Saves every write in <paramref name="pending"/> and opens it in <paramref name="window"/>.</summary>
    public static WriteApproval Required(PendingWrites pending, IApprovalWindow window) => new(pending, window);

    /// <summary>Required for the current user, unless this machine turned approval off.</summary>
    public static WriteApproval ForThisMachine()
    {
        if (MachineApprovalSetting.IsOff(MachineApprovalSetting.Read()))
        {
            return Granted;
        }

        PendingWrites pending = PendingWrites.ForCurrentUser();
        return Required(pending, new ConsoleApprovalWindow(pending));
    }

    internal PendingWrites? Pending { get; }

    internal IApprovalWindow? Window { get; }
}
