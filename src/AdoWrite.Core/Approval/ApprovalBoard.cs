namespace AdoWrite.Approval;

public enum ApprovalItemStatus
{
    Waiting,
    Posted,
    Rejected,
    Failed
}

/// <summary>A queued write as the window shows it. <see cref="Note"/> is the posted URL or why it failed.</summary>
public sealed record ApprovalItem(ApprovalRequest Request, ApprovalItemStatus Status, string? Note = null)
{
    /// <summary>Still to decide: waiting, or failed and so ready to post again or reject.</summary>
    public bool IsOpen => Status is ApprovalItemStatus.Waiting or ApprovalItemStatus.Failed;
}

/// <summary>
/// What the approval window shows: every write queued since it opened, in order, and the selected one.
/// It holds no I/O, so the keys' effects are tested without a console.
/// </summary>
public sealed class ApprovalBoard
{
    private readonly List<ApprovalItem> _items = [];

    public IReadOnlyList<ApprovalItem> Items => _items;

    public int Selected { get; private set; }

    public ApprovalItem? Current => _items.Count == 0 ? null : _items[Selected];

    public int OpenCount => _items.Count(item => item.IsOpen);

    public int Count(ApprovalItemStatus status) => _items.Count(item => item.Status == status);

    /// <summary>Adds the requests queued since the last call. Returns whether any was added.</summary>
    public bool Add(IReadOnlyList<ApprovalRequest> pending)
    {
        ApprovalRequest[] added = [.. pending.Where(request => _items.All(item => item.Request.Id != request.Id))];
        _items.AddRange(added.Select(request => new ApprovalItem(request, ApprovalItemStatus.Waiting)));
        return added.Length > 0;
    }

    public void Move(int delta)
    {
        if (_items.Count > 0)
        {
            Selected = Math.Clamp(Selected + delta, 0, _items.Count - 1);
        }
    }

    /// <summary>Records what happened to a write. Deciding the selected one moves on to the next open one, like <c>git add -p</c>, wrapping to the first.</summary>
    public void Mark(string id, ApprovalItemStatus status, string? note = null)
    {
        int index = _items.FindIndex(item => item.Request.Id == id);
        if (index < 0)
        {
            return;
        }

        _items[index] = _items[index] with { Status = status, Note = note };
        if (index == Selected && !_items[index].IsOpen)
        {
            int next = _items.FindIndex(index, item => item.IsOpen);
            next = next >= 0 ? next : _items.FindIndex(item => item.IsOpen);
            Selected = next >= 0 ? next : index;
        }
    }
}
