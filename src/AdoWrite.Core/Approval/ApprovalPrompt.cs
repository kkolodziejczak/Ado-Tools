using System.Text.Json;

namespace AdoWrite.Approval;

/// <summary>The console the approval window runs in: keys typed by a person, and the screen.</summary>
public interface IApprovalConsole
{
    /// <summary>Whether a person types the keys and sees the screen: neither is redirected.</summary>
    bool IsInteractive { get; }

    /// <summary>Waits for the next key.</summary>
    ConsoleKeyInfo ReadKey();

    /// <summary>Drops keys typed before the screen now shown, so a key meant for the last screen cannot decide this one.</summary>
    void DiscardTypedKeys();

    void Draw(IReadOnlyList<ScreenLine> lines);
}

/// <summary>
/// The approval window for one write: it shows the dry run, and posts only when the person presses
/// Enter and a second dry run still matches the one they read. Any other key cancels. The write is
/// deleted when the window closes, whatever happened, so it is never offered again.
/// </summary>
public sealed class ApprovalPrompt(PendingWrites pending, ApprovalRunner runner, IApprovalConsole console)
{
    public async Task RunAsync(string id, CancellationToken cancellationToken)
    {
        if (pending.Claim(id) is not { } request)
        {
            Show(ApprovalScreen.Message(
                "This write isn't waiting here: it was withdrawn, opened in another window, or can't be read. Nothing was posted from this window."));
            console.ReadKey();
            return;
        }

        try
        {
            await ApproveAsync(request, cancellationToken);
        }
        finally
        {
            pending.Delete(id);
        }
    }

    private async Task ApproveAsync(ApprovalRequest request, CancellationToken cancellationToken)
    {
        Show(ApprovalScreen.Render(request, PromptStage.Loading, null));
        JsonElement read = await RunAsync(request, preview: true, cancellationToken);
        PromptStage stage = PromptStage.Ready;
        while (ApprovalPreview.Error(read) is null)
        {
            Show(ApprovalScreen.Render(request, stage, read));
            if (ReadFreshKey().Key != ConsoleKey.Enter)
            {
                return;
            }

            Show(ApprovalScreen.Render(request, PromptStage.Posting, read));
            JsonElement now = await RunAsync(request, preview: true, cancellationToken);
            if (!ApprovalPreview.SameWrite(read, now))
            {
                (read, stage) = (now, PromptStage.Changed);
                continue;
            }

            if (request.Pinned(read) is not { } approved)
            {
                Show(ApprovalScreen.Render(request, PromptStage.Posted, read, "nothing to send, so nothing was sent"));
                ReadFreshKey();
                return;
            }

            JsonElement result = await RunAsync(approved, preview: false, cancellationToken);
            Show(ApprovalPreview.Error(result) is { } error
                ? ApprovalScreen.Render(request, PromptStage.NotPosted, read, error)
                : ApprovalScreen.Render(request, PromptStage.Posted, read, ApprovalPreview.Done(result)));
            ReadFreshKey();
            return;
        }

        Show(ApprovalScreen.Render(request, PromptStage.PreviewFailed, read, ApprovalPreview.Error(read)));
        ReadFreshKey();
    }

    private ConsoleKeyInfo ReadFreshKey()
    {
        console.DiscardTypedKeys();
        return console.ReadKey();
    }

    /// <summary>A run that throws becomes an error envelope, so the window says what went wrong instead of closing.</summary>
    private async Task<JsonElement> RunAsync(ApprovalRequest request, bool preview, CancellationToken cancellationToken)
    {
        try
        {
            return await runner.RunAsync(request, preview, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return ApprovalRunner.Error("approvalRunFailed", exception.Message);
        }
    }

    private void Show(IReadOnlyList<ScreenLine> lines) => console.Draw(lines);
}
