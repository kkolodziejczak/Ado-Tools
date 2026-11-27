using System.Text.Json;

namespace AdoWrite.Approval;

/// <summary>The console the approval window runs in: keys typed by a person, and the screen.</summary>
public interface IApprovalConsole
{
    /// <summary>Whether a person types the keys and sees the screen: neither is redirected.</summary>
    bool IsInteractive { get; }

    /// <summary>The next key, or null when none was pressed within <paramref name="wait"/>.</summary>
    ConsoleKeyInfo? ReadKey(TimeSpan wait);

    void Draw(IReadOnlyList<ScreenLine> lines);
}

/// <summary>
/// The approval window's loop: shows every queued write with its dry run, and on the person's key
/// posts or rejects it. It picks up writes queued while it is open. Posted and rejected writes leave the
/// outbox; quitting leaves the rest queued for the next window.
/// </summary>
public sealed class ApprovalReview(ApprovalOutbox outbox, ApprovalRunner runner, IApprovalConsole console, TimeZoneInfo timeZone)
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);

    private readonly ApprovalBoard _board = new();
    private readonly Dictionary<string, JsonElement> _previews = new(StringComparer.Ordinal);
    private IReadOnlyList<ScreenLine> _drawn = [];

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _board.Add(outbox.List());
        while (true)
        {
            await LoadPreviewAsync(_board.Current, cancellationToken);
            Draw();
            ConsoleKeyInfo? key = console.ReadKey(RefreshInterval);
            switch (key?.Key)
            {
                case null:
                    _board.Add(outbox.List());
                    break;
                case ConsoleKey.UpArrow:
                    _board.Move(-1);
                    break;
                case ConsoleKey.DownArrow:
                    _board.Move(1);
                    break;
                case ConsoleKey.Y when _board.Current is { IsOpen: true } current:
                    await PostAsync(current, cancellationToken);
                    break;
                case ConsoleKey.N when _board.Current is { IsOpen: true } current:
                    outbox.Remove(current.Request.Id);
                    _board.Mark(current.Request.Id, ApprovalItemStatus.Rejected);
                    break;
                case ConsoleKey.A:
                    foreach (ApprovalItem item in _board.Items.Where(item => item.IsOpen).ToArray())
                    {
                        await PostAsync(item, cancellationToken);
                        Draw();
                    }

                    break;
                case ConsoleKey.Q or ConsoleKey.Escape:
                    return;
            }
        }
    }

    private async Task LoadPreviewAsync(ApprovalItem? item, CancellationToken cancellationToken)
    {
        if (item is not { IsOpen: true } || _previews.ContainsKey(item.Request.Id))
        {
            return;
        }

        Draw();
        _previews[item.Request.Id] = await RunAsync(item.Request, preview: true, cancellationToken);
    }

    private async Task PostAsync(ApprovalItem item, CancellationToken cancellationToken)
    {
        JsonElement result = await RunAsync(item.Request, preview: false, cancellationToken);
        if (ApprovalPreview.Error(result) is { } error)
        {
            _board.Mark(item.Request.Id, ApprovalItemStatus.Failed, error);
            return;
        }

        outbox.Remove(item.Request.Id);
        _board.Mark(item.Request.Id, ApprovalItemStatus.Posted, ApprovalPreview.PostedUrl(result));
    }

    /// <summary>A run that throws becomes an error envelope, so one broken request cannot close the window.</summary>
    private async Task<JsonElement> RunAsync(ApprovalRequest request, bool preview, CancellationToken cancellationToken)
    {
        try
        {
            return await runner.RunAsync(request, preview, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return JsonSerializer.SerializeToElement(new
            {
                status = "error",
                errors = new[] { new { code = "approvalRunFailed", message = exception.Message } }
            });
        }
    }

    private void Draw()
    {
        IReadOnlyList<ScreenLine> lines = ApprovalBoardView.Render(_board, _previews, timeZone);
        if (!lines.SequenceEqual(_drawn))
        {
            console.Draw(lines);
            _drawn = lines;
        }
    }
}
