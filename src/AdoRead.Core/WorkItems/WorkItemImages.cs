using System.Security.Cryptography;
using HtmlAgilityPack;
using AdoRead.AzureDevOps;

namespace AdoRead.WorkItems;

internal sealed record WorkItemImageCandidate(
    Guid? AttachmentId,
    string? OriginalFileName,
    IReadOnlyList<WorkItemMediaSource> Sources,
    string? SkipCode = null)
{
    public bool IsDownloadable => AttachmentId is not null;
}

internal static class WorkItemImageCandidateCollector
{
    public static IReadOnlyList<WorkItemImageCandidate> Collect(
        SourceWorkItem workItem,
        Uri organizationUrl) =>
        Collect(workItem, [], organizationUrl);

    public static IReadOnlyList<WorkItemImageCandidate> Collect(
        SourceWorkItem workItem,
        IReadOnlyList<WorkItemCommentDocument> comments,
        Uri organizationUrl)
    {
        var candidates = new Dictionary<Guid, MutableCandidate>();
        var skipped = new List<WorkItemImageCandidate>();
        var inlineAttachmentIds = new HashSet<Guid>();

        CollectHtmlField(
            workItem.Fields,
            "System.Description",
            "description",
            commentId: null,
            organizationUrl,
            candidates,
            skipped,
            inlineAttachmentIds);
        CollectHtmlField(
            workItem.Fields,
            "Microsoft.VSTS.Common.AcceptanceCriteria",
            "acceptanceCriteria",
            commentId: null,
            organizationUrl,
            candidates,
            skipped,
            inlineAttachmentIds);

        foreach (WorkItemCommentDocument comment in comments)
        {
            string? html = !string.IsNullOrWhiteSpace(comment.RenderedHtml)
                ? comment.RenderedHtml
                : string.Equals(comment.SourceFormat, "html", StringComparison.OrdinalIgnoreCase)
                    ? comment.RawText
                    : null;
            if (html is null)
            {
                continue;
            }

            CollectHtml(
                html,
                "comment",
                comment.Id,
                organizationUrl,
                candidates,
                skipped,
                inlineAttachmentIds);
        }

        foreach (SourceWorkItemRelation relation in workItem.Relations)
        {
            if (!string.Equals(relation.RelationType, "AttachedFile", StringComparison.Ordinal))
            {
                continue;
            }

            string? relationFileName = GetAttributeString(relation.Attributes, "name");
            bool isImageHint = IsSupportedDisplayFileName(relationFileName);
            if (relation.TargetUrl is null
                || !AttachmentReferenceParser.TryParse(
                    relation.TargetUrl,
                    organizationUrl,
                    out AttachmentReference reference))
            {
                if (isImageHint)
                {
                    skipped.Add(Skipped(
                        new WorkItemMediaSource("attachmentRelation", null),
                        "unsupportedImageReference"));
                }

                continue;
            }

            string? originalFileName = relationFileName ?? reference.OriginalFileName;
            if (isImageHint
                || IsSupportedDisplayFileName(reference.OriginalFileName)
                || inlineAttachmentIds.Contains(reference.AttachmentId))
            {
                Add(
                    candidates,
                    reference,
                    originalFileName,
                    new WorkItemMediaSource("attachmentRelation", null),
                    relationFileName is not null
                        ? 2
                        : reference.OriginalFileName is not null ? 1 : 0);
            }
        }

        return candidates.Values
            .Select(candidate => candidate.ToImmutable())
            .Concat(skipped)
            .OrderBy(candidate => candidate.IsDownloadable ? 0 : 1)
            .ThenBy(candidate => candidate.AttachmentId?.ToString("D"), StringComparer.Ordinal)
            .ThenBy(candidate => SourceOrder(candidate.Sources[0].Kind))
            .ThenBy(candidate => candidate.Sources[0].CommentId)
            .ToArray();
    }

    private static void CollectHtmlField(
        IReadOnlyDictionary<string, object?> fields,
        string fieldName,
        string sourceKind,
        int? commentId,
        Uri organizationUrl,
        Dictionary<Guid, MutableCandidate> candidates,
        List<WorkItemImageCandidate> skipped,
        HashSet<Guid> inlineAttachmentIds)
    {
        if (fields.TryGetValue(fieldName, out object? value)
            && value is string html)
        {
            CollectHtml(
                html,
                sourceKind,
                commentId,
                organizationUrl,
                candidates,
                skipped,
                inlineAttachmentIds);
        }
    }

    private static void CollectHtml(
        string html,
        string sourceKind,
        int? commentId,
        Uri organizationUrl,
        Dictionary<Guid, MutableCandidate> candidates,
        List<WorkItemImageCandidate> skipped,
        HashSet<Guid> inlineAttachmentIds)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        HtmlNodeCollection? images = document.DocumentNode.SelectNodes("//img");
        if (images is null)
        {
            return;
        }

        foreach (HtmlNode image in images)
        {
            string source = HtmlEntity.DeEntitize(image.GetAttributeValue("src", string.Empty));
            var mediaSource = new WorkItemMediaSource(sourceKind, commentId);
            if (!Uri.TryCreate(source, UriKind.Absolute, out Uri? sourceUri)
                || !AttachmentReferenceParser.TryParse(
                    sourceUri,
                    organizationUrl,
                    out AttachmentReference reference))
            {
                skipped.Add(Skipped(
                    mediaSource,
                    "unsupportedImageReference"));
                continue;
            }

            inlineAttachmentIds.Add(reference.AttachmentId);
            Add(
                candidates,
                reference,
                reference.OriginalFileName,
                mediaSource,
                reference.OriginalFileName is not null ? 1 : 0);
        }
    }

    private static WorkItemImageCandidate Skipped(
        WorkItemMediaSource source,
        string code) =>
        new(null, null, [source], code);

    private static void Add(
        Dictionary<Guid, MutableCandidate> candidates,
        AttachmentReference reference,
        string? originalFileName,
        WorkItemMediaSource source,
        int originalFileNamePriority)
    {
        if (!candidates.TryGetValue(reference.AttachmentId, out MutableCandidate? candidate))
        {
            candidate = new MutableCandidate(
                reference.AttachmentId,
                originalFileName,
                originalFileNamePriority);
            candidates.Add(reference.AttachmentId, candidate);
        }

        if (originalFileNamePriority > candidate.OriginalFileNamePriority)
        {
            candidate.OriginalFileName = originalFileName;
            candidate.OriginalFileNamePriority = originalFileNamePriority;
        }

        if (!candidate.Sources.Contains(source))
        {
            candidate.Sources.Add(source);
        }
    }

    private static string? GetAttributeString(
        IReadOnlyDictionary<string, object?> attributes,
        string name) =>
        attributes.TryGetValue(name, out object? value)
            ? NormalizeDisplayFileName(value)
            : null;

    private static string? NormalizeDisplayFileName(object? value)
    {
        string? text = Convert.ToString(value);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static bool IsSupportedDisplayFileName(string? fileName) =>
        fileName is not null
        && SupportedImageFormat.FromFileName(fileName) is not null;

    private static int SourceOrder(string kind) => kind switch
    {
        "description" => 0,
        "acceptanceCriteria" => 1,
        "comment" => 2,
        "attachmentRelation" => 3,
        _ => 4
    };

    private sealed class MutableCandidate(
        Guid attachmentId,
        string? originalFileName,
        int originalFileNamePriority)
    {
        public Guid AttachmentId { get; } = attachmentId;

        public string? OriginalFileName { get; set; } = originalFileName;

        public int OriginalFileNamePriority { get; set; } = originalFileNamePriority;

        public List<WorkItemMediaSource> Sources { get; } = [];

        public WorkItemImageCandidate ToImmutable() =>
            new(
                AttachmentId,
                OriginalFileName,
                Sources
                    .OrderBy(source => SourceOrder(source.Kind))
                    .ThenBy(source => source.CommentId)
                    .ToArray());
    }
}

internal sealed record AttachmentReference(Guid AttachmentId, string? OriginalFileName);

internal static class AttachmentReferenceParser
{
    public static bool TryParse(
        Uri source,
        Uri organizationUrl,
        out AttachmentReference reference)
    {
        reference = null!;
        if (source.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(source.UserInfo)
            || !string.IsNullOrEmpty(source.Fragment)
            || source.Port != organizationUrl.Port
            || !TryGetPathPrefix(source, organizationUrl, out int endpointStart))
        {
            return false;
        }

        if (!TryGetSegments(source.AbsolutePath, out string[] segments)
            || endpointStart > segments.Length - 4
            || !string.Equals(segments[^4], "_apis", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(segments[^3], "wit", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(segments[^2], "attachments", StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParseExact(segments[^1], "D", out Guid attachmentId)
            || !TryReadQuery(source, out string? originalFileName))
        {
            return false;
        }

        reference = new AttachmentReference(attachmentId, originalFileName);
        return true;
    }

    private static bool TryGetPathPrefix(Uri source, Uri organizationUrl, out int endpointStart)
    {
        endpointStart = 0;
        if (source.Port != 443
            || !TryGetSegments(source.AbsolutePath, out string[] sourceSegments)
            || !TryGetSegments(organizationUrl.AbsolutePath, out string[] organizationSegments))
        {
            return false;
        }

        string organization = organizationSegments.SingleOrDefault() ?? string.Empty;
        bool isCanonicalHost = string.Equals(
            source.IdnHost,
            organizationUrl.IdnHost,
            StringComparison.OrdinalIgnoreCase);
        bool isLegacyHost = string.Equals(
            source.IdnHost,
            $"{organization}.visualstudio.com",
            StringComparison.OrdinalIgnoreCase);
        if (!isCanonicalHost && !isLegacyHost)
        {
            return false;
        }

        if (isCanonicalHost)
        {
            if (sourceSegments.Length < organizationSegments.Length
                || !StartsWith(sourceSegments, organizationSegments))
            {
                return false;
            }

            endpointStart = organizationSegments.Length;
        }

        return sourceSegments.Length >= endpointStart + 4;
    }

    private static bool TryGetSegments(string path, out string[] segments)
    {
        try
        {
            segments = path
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.UnescapeDataString)
                .ToArray();
            return true;
        }
        catch (UriFormatException)
        {
            segments = [];
            return false;
        }
    }

    private static bool StartsWith(string[] value, string[] prefix) =>
        prefix
            .Select((segment, index) => string.Equals(value[index], segment, StringComparison.OrdinalIgnoreCase))
            .All(match => match);

    private static bool TryReadQuery(Uri source, out string? fileName)
    {
        fileName = null;
        string query = source.Query.TrimStart('?');
        if (query.Length == 0)
        {
            return true;
        }

        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split('=', 2);
            if (parts.Length != 2)
            {
                return false;
            }

            string key;
            string value;
            try
            {
                key = Uri.UnescapeDataString(parts[0]);
                value = Uri.UnescapeDataString(parts[1].Replace('+', ' '));
            }
            catch (UriFormatException)
            {
                return false;
            }

            if (!seenKeys.Add(key)
                || !string.Equals(key, "fileName", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(key, "download", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(key, "api-version", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.Equals(key, "fileName", StringComparison.OrdinalIgnoreCase))
            {
                fileName = string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }

        return true;
    }
}

internal sealed class WorkItemImageDownloader(IWorkItemSource source)
{
    public const int MaximumCandidates = 20;
    public const int MaximumProvenanceSources = 64;
    public const long MaximumBytesPerImage = 10 * 1024 * 1024;
    public const long MaximumTotalBytes = 50 * 1024 * 1024;

    public async Task<WorkItemMediaDocument> DownloadAsync(
        IReadOnlyList<WorkItemImageCandidate> candidates,
        string project,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        if (!WorkItemImageOutputDirectory.TryValidateExisting(outputDirectory))
        {
            throw new IOException("The image output directory is not ready for image downloads.");
        }
        var items = new List<WorkItemMediaItem>(Math.Min(candidates.Count, MaximumCandidates + 2));
        long totalBytes = 0;
        int admittedCandidates = 0;
        CandidateOverflow? overflow = null;
        SkippedAggregate? unresolved = null;
        using var transaction = new ImageOutputTransaction(outputDirectory);

        foreach (WorkItemImageCandidate candidate in candidates)
        {
            if (!candidate.IsDownloadable)
            {
                unresolved ??= new SkippedAggregate();
                unresolved.Add(candidate);
                continue;
            }

            if (admittedCandidates >= MaximumCandidates)
            {
                overflow ??= new CandidateOverflow(candidate);
                overflow.Add(candidate);
                continue;
            }

            admittedCandidates++;
            WorkItemMediaItem item = await DownloadOneAsync(
                candidate,
                project,
                outputDirectory,
                totalBytes,
                transaction,
                cancellationToken);
            if (item.ByteLength is long byteLength)
            {
                totalBytes += byteLength;
            }

            items.Add(item);
        }

        if (overflow is not null)
        {
            items.Add(overflow.ToItem());
        }

        if (unresolved is not null)
        {
            items.Add(unresolved.ToItem());
        }

        var document = new WorkItemMediaDocument(
            outputDirectory,
            new WorkItemMediaLimits(MaximumCandidates, MaximumBytesPerImage, MaximumTotalBytes),
            items);
        transaction.Commit();
        return document;
    }

    private async Task<WorkItemMediaItem> DownloadOneAsync(
        WorkItemImageCandidate candidate,
        string project,
        string outputDirectory,
        long totalBytes,
        ImageOutputTransaction transaction,
        CancellationToken cancellationToken)
    {
        try
        {
            await using Stream input = await source.GetAttachmentContentAsync(
                project,
                candidate.AttachmentId!.Value,
                candidate.OriginalFileName ?? "attachment",
                cancellationToken);
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[81920];
            long byteLength = 0;
            string? stagingPath = null;
            SupportedImageFormat? format;
            try
            {
                stagingPath = transaction.CreateStagingPath();
                await using (var output = new FileStream(
                    stagingPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: buffer.Length,
                    options: FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    int read;
                    while ((read = await input.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
                    {
                        byteLength += read;
                        if (byteLength > MaximumBytesPerImage)
                        {
                            throw new ImageDownloadException(
                                "imageTooLarge",
                                "The image exceeds the configured download limit.",
                                transient: false);
                        }

                        if (byteLength > MaximumTotalBytes - totalBytes)
                        {
                            throw new ImageDownloadException(
                                "mediaBudgetExhausted",
                                "The cumulative image download budget was exhausted.",
                                transient: false);
                        }

                        hash.AppendData(buffer, 0, read);
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    }

                    await output.FlushAsync(cancellationToken);
                }

                using (var stagedInput = new FileStream(
                    stagingPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: buffer.Length,
                    options: FileOptions.SequentialScan))
                {
                    format = SupportedImageFormat.Detect(stagedInput);
                }

                if (format is null)
                {
                    throw new ImageDownloadException(
                        "unsupportedImageFormat",
                        "The attachment is not a supported PNG, JPEG, or WebP image.",
                        transient: false);
                }

                string finalPath = Path.Combine(
                    outputDirectory,
                    $"{candidate.AttachmentId.Value:N}.{format.Extension}");
                if (!IsWithinDirectory(finalPath, outputDirectory))
                {
                    throw new ImageDownloadException(
                        "imageOutputError",
                        "The image could not be saved.",
                        transient: false);
                }

                transaction.Finalize(stagingPath, finalPath);
                return new WorkItemMediaItem(
                    candidate.AttachmentId.Value.ToString("D"),
                    candidate.OriginalFileName,
                    "downloaded",
                    format.Extension,
                    format.ContentType,
                    byteLength,
                    Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(),
                    Path.GetFullPath(finalPath),
                    candidate.Sources,
                    Error: null);
            }
            finally
            {
                if (stagingPath is not null)
                {
                    transaction.DeleteStaging(stagingPath);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (WorkItemSourceException exception)
        {
            return Failed(candidate, exception.Code, exception.Message, exception.Transient);
        }
        catch (ImageDownloadException exception)
        {
            return Failed(candidate, exception.Code, exception.Message, exception.Transient);
        }
        catch (IOException)
        {
            return Failed(candidate, "imageOutputError", "The image could not be saved.", transient: false);
        }
        catch (UnauthorizedAccessException)
        {
            return Failed(candidate, "imageOutputError", "The image could not be saved.", transient: false);
        }
    }

    private sealed class CandidateOverflow(WorkItemImageCandidate representative)
    {
        private readonly BoundedSourceAccumulator _sources = new();

        public int Count { get; private set; }

        public void Add(WorkItemImageCandidate candidate)
        {
            Count++;
            _sources.Add(candidate.Sources);
        }

        public WorkItemMediaItem ToItem() =>
            new(
                representative.AttachmentId!.Value.ToString("D"),
                representative.OriginalFileName,
                "skipped",
                Format: null,
                ContentType: null,
                ByteLength: null,
                Sha256: null,
                LocalPath: null,
                _sources.ToArray(),
                new WorkItemMediaError(
                    "candidateLimitExceeded",
                    Count == 1
                        ? "1 image candidate was not considered because the candidate limit was reached."
                        : $"{Count} image candidates were not considered because the candidate limit was reached.",
                    Transient: false,
                    OmittedCount: Count,
                    OmittedSourceCount: _sources.OmittedCount == 0 ? null : _sources.OmittedCount));
    }

    private sealed class SkippedAggregate
    {
        private readonly BoundedSourceAccumulator _sources = new();

        public int Count { get; private set; }

        public void Add(WorkItemImageCandidate candidate)
        {
            Count++;
            _sources.Add(candidate.Sources);
        }

        public WorkItemMediaItem ToItem() =>
            new(
                AttachmentId: null,
                OriginalFileName: null,
                "skipped",
                Format: null,
                ContentType: null,
                ByteLength: null,
                Sha256: null,
                LocalPath: null,
                _sources.ToArray(),
                new WorkItemMediaError(
                    "unsupportedImageReference",
                    Count == 1
                        ? "1 image reference could not be mapped to a supported Azure DevOps attachment."
                        : $"{Count} image references could not be mapped to a supported Azure DevOps attachment.",
                    Transient: false,
                    OmittedCount: Count,
                    OmittedSourceCount: _sources.OmittedCount == 0 ? null : _sources.OmittedCount));
    }

    private sealed class BoundedSourceAccumulator
    {
        private readonly HashSet<WorkItemMediaSource> _seen = [];
        private readonly List<WorkItemMediaSource> _retained = [];

        public int OmittedCount { get; private set; }

        public void Add(IReadOnlyList<WorkItemMediaSource> sources)
        {
            foreach (WorkItemMediaSource source in sources)
            {
                if (!_seen.Add(source))
                {
                    continue;
                }

                if (_retained.Count < MaximumProvenanceSources)
                {
                    _retained.Add(source);
                }
                else
                {
                    OmittedCount++;
                }
            }
        }

        public IReadOnlyList<WorkItemMediaSource> ToArray() =>
            _retained
                .OrderBy(source => source.Kind, StringComparer.Ordinal)
                .ThenBy(source => source.CommentId)
                .ToArray();
    }

    private static WorkItemMediaItem Failed(
        WorkItemImageCandidate candidate,
        string code,
        string message,
        bool transient) =>
        new(
            candidate.AttachmentId?.ToString("D"),
            candidate.OriginalFileName,
            "failed",
            Format: null,
            ContentType: null,
            ByteLength: null,
            Sha256: null,
            LocalPath: null,
            candidate.Sources,
            new WorkItemMediaError(code, message, transient));

    private static bool IsWithinDirectory(string path, string directory)
    {
        string relative = Path.GetRelativePath(Path.GetFullPath(directory), Path.GetFullPath(path));
        return relative is not (".." or ".")
            && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
    }

    private sealed class ImageDownloadException(string code, string message, bool transient) : Exception(message)
    {
        public string Code { get; } = code;

        public bool Transient { get; } = transient;
    }

    private sealed class ImageOutputTransaction(string outputDirectory) : IDisposable
    {
        private readonly HashSet<string> _stagingPaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _finalizedPaths = new(StringComparer.OrdinalIgnoreCase);
        private bool _committed;

        public string CreateStagingPath()
        {
            string path = Path.Combine(outputDirectory, $".partial-{Guid.NewGuid():N}");
            _stagingPaths.Add(path);
            return path;
        }

        public void Finalize(string stagingPath, string finalPath)
        {
            File.Move(stagingPath, finalPath, overwrite: false);
            _stagingPaths.Remove(stagingPath);
            _finalizedPaths.Add(finalPath);
        }

        public void DeleteStaging(string stagingPath)
        {
            if (_stagingPaths.Remove(stagingPath))
            {
                TryDelete(stagingPath);
            }
        }

        public void Commit() => _committed = true;

        public void Dispose()
        {
            foreach (string stagingPath in _stagingPaths)
            {
                TryDelete(stagingPath);
            }

            if (!_committed)
            {
                foreach (string finalizedPath in _finalizedPaths)
                {
                    TryDelete(finalizedPath);
                }
            }

            _stagingPaths.Clear();
            _finalizedPaths.Clear();
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
