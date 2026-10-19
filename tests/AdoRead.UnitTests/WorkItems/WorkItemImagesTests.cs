using System.Security.Cryptography;
using AdoRead.AzureDevOps;
using AdoRead.WorkItems;

namespace AdoRead.UnitTests.WorkItems;

public sealed class WorkItemImagesTests
{
    private static readonly Uri OrganizationUrl = new("https://dev.azure.com/example/");

    [Fact]
    public void CandidateCollector_DiscoversDescriptionImageReferences()
    {
        var attachmentId = Guid.Parse("6d8e1ce4-1172-4d07-a83f-35f8d7f5889a");
        var workItem = new SourceWorkItem(
            123,
            1,
            null,
            new Dictionary<string, object?>
            {
                ["System.Description"] =
                    $"<DIV><IMG SRC=\"https://dev.azure.com/example/Payments/_apis/wit/attachments/{attachmentId:D}?fileName=description.png\"></DIV>"
            },
            []);

        WorkItemImageCandidate candidate = Assert.Single(WorkItemImageCandidateCollector.Collect(
            workItem,
            new Uri("https://dev.azure.com/example/")));

        Assert.Equal(attachmentId, candidate.AttachmentId);
        Assert.Equal("description.png", candidate.OriginalFileName);
        Assert.Equal("description", Assert.Single(candidate.Sources).Kind);
    }

    [Fact]
    public void CandidateCollector_DiscoversImagesInRichTextCustomFields()
    {
        var attachmentId = Guid.Parse("0f4b7a52-8e8b-4a0e-9d55-3b1f0c2b6a11");
        var workItem = new SourceWorkItem(
            123,
            1,
            null,
            new Dictionary<string, object?>
            {
                ["Custom.Notes"] =
                    $"<div><img src=\"https://dev.azure.com/example/Payments/_apis/wit/attachments/{attachmentId:D}?fileName=notes.png\"></div>",
                ["Custom.Plain"] =
                    $"<img src=\"https://dev.azure.com/example/Payments/_apis/wit/attachments/{Guid.NewGuid():D}?fileName=x.png\">"
            },
            [])
        {
            RichTextFields = new HashSet<string>(StringComparer.Ordinal) { "Custom.Notes" }
        };

        WorkItemImageCandidate candidate = Assert.Single(WorkItemImageCandidateCollector.Collect(workItem, OrganizationUrl));

        Assert.Equal(attachmentId, candidate.AttachmentId);
        Assert.Equal(new WorkItemMediaSource("customField", null, "Custom.Notes"), Assert.Single(candidate.Sources));
    }

    [Fact]
    public void CandidateCollector_DeduplicatesAcrossDescriptionAcceptanceCommentsAndRelations()
    {
        var attachmentId = Guid.Parse("6d8e1ce4-1172-4d07-a83f-35f8d7f5889a");
        string attachmentUrl =
            $"https://dev.azure.com/example/Payments/_apis/wit/attachments/{attachmentId:D}";
        var workItem = new SourceWorkItem(
            123,
            1,
            null,
            new Dictionary<string, object?>
            {
                ["System.Description"] = $"<img src=\"{attachmentUrl}?fileName=description.png\">",
                ["Microsoft.VSTS.Common.AcceptanceCriteria"] = $"<IMG SRC=\"{attachmentUrl}?fileName=acceptance.png\">"
            },
            [new SourceWorkItemRelation(
                "AttachedFile",
                new Uri($"{attachmentUrl}?fileName=relation.png"),
                new Dictionary<string, object?> { ["name"] = "relation.png" })]);
        WorkItemCommentDocument[] comments =
        [
            new(
                7,
                1,
                null,
                null,
                null,
                null,
                false,
                "html",
                null,
                $"<p><img src=\"{attachmentUrl}?fileName=comment.png\"></p>",
                null,
                [],
                [],
                null),
            new(
                8,
                1,
                null,
                null,
                null,
                null,
                false,
                "html",
                $"<img src=\"{attachmentUrl}?fileName=raw-comment.png\">",
                null,
                null,
                [],
                [],
                null)
        ];

        WorkItemImageCandidate candidate = Assert.Single(
            WorkItemImageCandidateCollector.Collect(workItem, comments, OrganizationUrl));

        Assert.Equal(attachmentId, candidate.AttachmentId);
        Assert.Equal("relation.png", candidate.OriginalFileName);
        Assert.Equal(
            ["description", "acceptanceCriteria", "comment", "comment", "attachmentRelation"],
            candidate.Sources.Select(source => source.Kind).ToArray());
        Assert.Equal([null, null, 7, 8, null], candidate.Sources.Select(source => source.CommentId).ToArray());
    }

    [Fact]
    public void CandidateCollector_ReportsUnsafeInlineReferencesWithoutRetainingUrls()
    {
        const string attachmentId = "6d8e1ce4-1172-4d07-a83f-35f8d7f5889a";
        string html = string.Join(
            string.Empty,
            [
                "<img src=\"http://dev.azure.com/example/Payments/_apis/wit/attachments/" + attachmentId + "\">",
                "<img src=\"https://other.visualstudio.com/Payments/_apis/wit/attachments/" + attachmentId + "\">",
                "<img src=\"https://user!@dev.azure.com/example/Payments/_apis/wit/attachments/" + attachmentId + "\">",
                "<img src=\"https://dev.azure.com/example/Payments/_apis/wit/attachments/" + attachmentId + "#fragment\">",
                "<img src=\"data:image/png;base64,secret\">",
                "<img src=\"file:///secret.png\">",
                "<img src=\"https://dev.azure.com/example/Payments/_apis/wit/attachments/" + attachmentId + "?redirect=https%3A%2F%2Fevil\">"
            ]);
        var workItem = new SourceWorkItem(
            123,
            1,
            null,
            new Dictionary<string, object?> { ["System.Description"] = html },
            []);

        IReadOnlyList<WorkItemImageCandidate> candidates =
            WorkItemImageCandidateCollector.Collect(workItem, OrganizationUrl);

        Assert.Equal(7, candidates.Count);
        Assert.All(candidates, candidate =>
        {
            Assert.Null(candidate.AttachmentId);
            Assert.Equal("unsupportedImageReference", candidate.SkipCode);
            Assert.Null(candidate.OriginalFileName);
        });
    }

    [Fact]
    public void CandidateCollector_ParsesEntitiesAndRepresentsMissingImageAttributes()
    {
        var attachmentId = Guid.Parse("6d8e1ce4-1172-4d07-a83f-35f8d7f5889a");
        var workItem = new SourceWorkItem(
            123,
            1,
            null,
            new Dictionary<string, object?>
            {
                ["System.Description"] =
                    $"<DIV><IMG SRC=\"https://dev.azure.com/example/Payments/_apis/wit/attachments/{attachmentId:D}?download=true&amp;fileName=entity.png\"><IMG><IMG SRC></DIV>"
            },
            []);

        IReadOnlyList<WorkItemImageCandidate> candidates =
            WorkItemImageCandidateCollector.Collect(workItem, OrganizationUrl);

        Assert.Equal(3, candidates.Count);
        Assert.Single(candidates, candidate => candidate.AttachmentId == attachmentId);
        Assert.Equal(2, candidates.Count(candidate => candidate.AttachmentId is null));
        Assert.All(
            candidates.Where(candidate => candidate.AttachmentId is null),
            candidate => Assert.Equal("unsupportedImageReference", candidate.SkipCode));
    }

    [Fact]
    public void CandidateCollector_UsesRelationExtensionForImageAndIgnoresNonImageRelation()
    {
        var imageId = Guid.Parse("6d8e1ce4-1172-4d07-a83f-35f8d7f5889a");
        var documentId = Guid.Parse("0d4c4d6b-5f05-4a63-8d09-0d2b0c2ca8dd");
        var workItem = new SourceWorkItem(
            123,
            1,
            null,
            new Dictionary<string, object?>(),
            [
                new SourceWorkItemRelation(
                    "AttachedFile",
                    new Uri($"https://dev.azure.com/example/Payments/_apis/wit/attachments/{imageId:D}"),
                    new Dictionary<string, object?> { ["name"] = "../checkout.JPEG" }),
                new SourceWorkItemRelation(
                    "AttachedFile",
                    new Uri($"https://dev.azure.com/example/Payments/_apis/wit/attachments/{documentId:D}"),
                    new Dictionary<string, object?> { ["name"] = "requirements.pdf" })
            ]);

        WorkItemImageCandidate candidate = Assert.Single(
            WorkItemImageCandidateCollector.Collect(workItem, OrganizationUrl));

        Assert.Equal(imageId, candidate.AttachmentId);
        Assert.Equal("../checkout.JPEG", candidate.OriginalFileName);
        Assert.Equal("attachmentRelation", Assert.Single(candidate.Sources).Kind);
    }

    [Fact]
    public void CandidateCollector_NormalizesBlankRelationNamesAndUsesUrlFallback()
    {
        var namedId = Guid.Parse("00000000-0000-0000-0000-000000000011");
        var nullNameId = Guid.Parse("00000000-0000-0000-0000-000000000012");
        var blankNameId = Guid.Parse("00000000-0000-0000-0000-000000000013");
        var workItem = new SourceWorkItem(
            123,
            1,
            null,
            new Dictionary<string, object?>
            {
                ["System.Description"] =
                    $"<img src=\"https://dev.azure.com/example/Payments/_apis/wit/attachments/{namedId:D}?fileName=inline.png\">"
            },
            [
                new SourceWorkItemRelation(
                    "AttachedFile",
                    new Uri($"https://dev.azure.com/example/Payments/_apis/wit/attachments/{namedId:D}?fileName=url.png"),
                    new Dictionary<string, object?> { ["name"] = "relation.png" }),
                new SourceWorkItemRelation(
                    "AttachedFile",
                    new Uri($"https://dev.azure.com/example/Payments/_apis/wit/attachments/{nullNameId:D}?fileName=null-name.png"),
                    new Dictionary<string, object?> { ["name"] = null }),
                new SourceWorkItemRelation(
                    "AttachedFile",
                    new Uri($"https://dev.azure.com/example/Payments/_apis/wit/attachments/{blankNameId:D}?fileName=blank-name.png"),
                    new Dictionary<string, object?> { ["name"] = "  " })
            ]);

        IReadOnlyList<WorkItemImageCandidate> candidates =
            WorkItemImageCandidateCollector.Collect(workItem, OrganizationUrl);

        Assert.Equal("relation.png", Assert.Single(candidates, candidate => candidate.AttachmentId == namedId).OriginalFileName);
        Assert.Equal("null-name.png", Assert.Single(candidates, candidate => candidate.AttachmentId == nullNameId).OriginalFileName);
        Assert.Equal("blank-name.png", Assert.Single(candidates, candidate => candidate.AttachmentId == blankNameId).OriginalFileName);
    }

    [Theory]
    [InlineData("https://example.visualstudio.com/Payments/_apis/wit/attachments/6d8e1ce4-1172-4d07-a83f-35f8d7f5889a", true)]
    [InlineData("https://dev.azure.com/example/Payments/_apis/wit/attachments/6d8e1ce4-1172-4d07-a83f-35f8d7f5889a?api-version=7.1&download=true", true)]
    [InlineData("https://dev.azure.com/example/Payments/_apis/wit/attachments/6d8e1ce4-1172-4d07-a83f-35f8d7f5889a?redirect=https%3A%2F%2Fevil", false)]
    [InlineData("https://dev.azure.com/example/Payments/_apis/wit/attachments/6d8e1ce4-1172-4d07-a83f-35f8d7f5889a/extra", false)]
    public void AttachmentReferenceParser_AcceptsOnlyKnownSameOrganizationRoutes(string value, bool expected)
    {
        bool parsed = AttachmentReferenceParser.TryParse(new Uri(value), OrganizationUrl, out _);

        Assert.Equal(expected, parsed);
    }

    [Fact]
    public async Task Downloader_UsesSignatureDerivedFormatsAndGuidOnlyPaths()
    {
        var jpegId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var webpId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        byte[] jpeg = JpegBytes();
        byte[] webp = WebpBytes();
        var source = new FakeImageSource(new Dictionary<Guid, byte[]> { [jpegId] = jpeg, [webpId] = webp });
        string directory = CreateEmptyDirectory();

        try
        {
            WorkItemMediaDocument result = await new WorkItemImageDownloader(source).DownloadAsync(
                [
                    Candidate(jpegId, "../unsafe.png"),
                    Candidate(webpId, "../unsafe.png")
                ],
                "Payments",
                directory,
                CancellationToken.None);

            Assert.Equal(["jpg", "webp"], result.Items.Select(item => item.Format!).ToArray());
            Assert.All(result.Items, item => Assert.Equal("downloaded", item.Status));
            Assert.Equal(
                [
                    Path.Combine(directory, $"{jpegId:N}.jpg"),
                    Path.Combine(directory, $"{webpId:N}.webp")
                ],
                result.Items.Select(item => item.LocalPath!).ToArray());
            Assert.Equal(jpeg, await File.ReadAllBytesAsync(result.Items[0].LocalPath!));
            Assert.Equal(webp, await File.ReadAllBytesAsync(result.Items[1].LocalPath!));
            Assert.Equal(
                Convert.ToHexString(SHA256.HashData(jpeg)).ToLowerInvariant(),
                result.Items[0].Sha256);
            Assert.Empty(Directory.EnumerateFiles(directory, ".partial-*"));
            Assert.Equal(
                [$"{jpegId:N}.jpg", $"{webpId:N}.webp"],
                Directory.EnumerateFiles(directory).Select(path => Path.GetFileName(path)!).OrderBy(name => name).ToArray());
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task Downloader_ContinuesAfterInvalidFormatAndSourceFailure()
    {
        var invalidId = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var failedId = Guid.Parse("00000000-0000-0000-0000-000000000004");
        var validId = Guid.Parse("00000000-0000-0000-0000-000000000005");
        var forbiddenId = Guid.Parse("00000000-0000-0000-0000-000000000010");
        var source = new FakeImageSource(
            new Dictionary<Guid, byte[]>
            {
                [invalidId] = "<svg>prompt</svg>"u8.ToArray(),
                [validId] = PngBytes()
            },
            new Dictionary<Guid, WorkItemSourceException>
            {
                [failedId] = new("notFound", "The Azure DevOps resource was not found.", false),
                [forbiddenId] = new("forbidden", "The PAT cannot read this Azure DevOps resource.", false)
            });
        string directory = CreateEmptyDirectory();

        try
        {
            WorkItemMediaDocument result = await new WorkItemImageDownloader(source).DownloadAsync(
                [
                    Candidate(invalidId, "bad.png"),
                    Candidate(failedId, "missing.png"),
                    Candidate(forbiddenId, "forbidden.png"),
                    Candidate(validId, "good.png")
                ],
                "Payments",
                directory,
                CancellationToken.None);

            Assert.Equal(
                ["unsupportedImageFormat", "notFound", "forbidden"],
                result.Items.Take(3).Select(item => item.Error?.Code ?? string.Empty).ToArray());
            Assert.Equal(["failed", "failed", "failed", "downloaded"], result.Items.Select(item => item.Status).ToArray());
            Assert.Single(Directory.EnumerateFiles(directory, $"{validId:N}.png"));
            Assert.Empty(Directory.EnumerateFiles(directory, ".partial-*"));
            Assert.Equal(4, source.AttachmentCalls.Count);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task Downloader_RemovesFailedStagingBeforeNextAttachmentCall()
    {
        var invalidId = Guid.Parse("00000000-0000-0000-0000-000000000014");
        var validId = Guid.Parse("00000000-0000-0000-0000-000000000015");
        string directory = CreateEmptyDirectory();
        var source = new ObservingImageSource(
            directory,
            invalidId,
            validId,
            "not-an-image"u8.ToArray(),
            PngBytes());

        try
        {
            WorkItemMediaDocument result = await new WorkItemImageDownloader(source).DownloadAsync(
                [Candidate(invalidId, "invalid.png"), Candidate(validId, "valid.png")],
                "Payments",
                directory,
                CancellationToken.None);

            Assert.Equal(["failed", "downloaded"], result.Items.Select(item => item.Status).ToArray());
            Assert.Equal(0, source.PartialFilesBeforeSecondCall);
            Assert.Empty(Directory.EnumerateFiles(directory, ".partial-*"));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task Downloader_RepresentsCandidatesBeyondBoundedAdmissionWithoutCallingSource()
    {
        var candidates = Enumerable.Range(1, WorkItemImageDownloader.MaximumCandidates + 1)
            .Select(index => Candidate(new Guid(index, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), $"{index}.png"))
            .ToArray();
        var source = new FakeImageSource(candidates.ToDictionary(candidate => candidate.AttachmentId!.Value, _ => PngBytes()));
        string directory = CreateEmptyDirectory();

        try
        {
            WorkItemMediaDocument result = await new WorkItemImageDownloader(source).DownloadAsync(
                candidates,
                "Payments",
                directory,
                CancellationToken.None);

            Assert.Equal(WorkItemImageDownloader.MaximumCandidates + 1, result.Items.Count);
            Assert.Equal("downloaded", result.Items[WorkItemImageDownloader.MaximumCandidates - 1].Status);
            Assert.Equal("skipped", result.Items[^1].Status);
            Assert.Equal("candidateLimitExceeded", result.Items[^1].Error?.Code);
            Assert.Equal(
                "1 image candidate was not considered because the candidate limit was reached.",
                result.Items[^1].Error?.Message);
            Assert.Equal(1, result.Items[^1].Error?.OmittedCount);
            Assert.Equal(candidates[WorkItemImageDownloader.MaximumCandidates].AttachmentId!.Value.ToString("D"), result.Items[^1].AttachmentId);
            Assert.Equal(WorkItemImageDownloader.MaximumCandidates, source.AttachmentCalls.Count);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task Downloader_BoundsDistinctUnresolvedProvenanceAndReportsOmittedSources()
    {
        var candidates = Enumerable.Range(0, 1000)
            .Select(commentId => new WorkItemImageCandidate(
                null,
                null,
                [new WorkItemMediaSource("comment", commentId)],
                "unsupportedImageReference"))
            .ToArray();
        string directory = CreateEmptyDirectory();

        try
        {
            WorkItemMediaDocument result = await new WorkItemImageDownloader(
                new FakeImageSource(new Dictionary<Guid, byte[]>())).DownloadAsync(
                candidates,
                "Payments",
                directory,
                CancellationToken.None);

            WorkItemMediaItem item = Assert.Single(result.Items);
            Assert.Null(item.AttachmentId);
            Assert.Equal("skipped", item.Status);
            Assert.Equal("unsupportedImageReference", item.Error!.Code);
            Assert.Equal(1000, item.Error.OmittedCount);
            Assert.Equal(WorkItemImageDownloader.MaximumProvenanceSources, item.Sources.Count);
            Assert.Equal(
                1000 - WorkItemImageDownloader.MaximumProvenanceSources,
                item.Error.OmittedSourceCount);
            Assert.Equal(
                Enumerable.Range(0, WorkItemImageDownloader.MaximumProvenanceSources)
                    .Select(commentId => (int?)commentId),
                item.Sources.Select(source => source.CommentId).ToArray());
            Assert.DoesNotContain("unresolvedMediaReferences", item.Error.Code, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task Downloader_UsesSingularMessageForOneUnresolvedReference()
    {
        string directory = CreateEmptyDirectory();

        try
        {
            WorkItemMediaDocument result = await new WorkItemImageDownloader(
                new FakeImageSource(new Dictionary<Guid, byte[]>())).DownloadAsync(
                [new WorkItemImageCandidate(
                    null,
                    null,
                    [new WorkItemMediaSource("description", null)],
                    "unsupportedImageReference")],
                "Payments",
                directory,
                CancellationToken.None);

            WorkItemMediaItem item = Assert.Single(result.Items);
            Assert.Equal(
                "1 image reference could not be mapped to a supported Azure DevOps attachment.",
                item.Error!.Message);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task Downloader_BoundsCandidateOverflowProvenanceAndReportsOmittedSources()
    {
        const int overflowCount = 100;
        var candidates = Enumerable.Range(0, WorkItemImageDownloader.MaximumCandidates + overflowCount)
            .Select(index => new WorkItemImageCandidate(
                Guid.NewGuid(),
                $"{index}.png",
                [new WorkItemMediaSource("comment", index)]))
            .ToArray();
        var source = new FakeImageSource(candidates.ToDictionary(candidate => candidate.AttachmentId!.Value, _ => PngBytes()));
        string directory = CreateEmptyDirectory();

        try
        {
            WorkItemMediaDocument result = await new WorkItemImageDownloader(source).DownloadAsync(
                candidates,
                "Payments",
                directory,
                CancellationToken.None);

            WorkItemMediaItem aggregate = Assert.Single(
                result.Items,
                item => item.Error?.Code == "candidateLimitExceeded");
            Assert.Equal(overflowCount, aggregate.Error!.OmittedCount);
            Assert.Equal(
                overflowCount - WorkItemImageDownloader.MaximumProvenanceSources,
                aggregate.Error.OmittedSourceCount);
            Assert.Equal(
                WorkItemImageDownloader.MaximumProvenanceSources,
                aggregate.Sources.Count);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task Downloader_CancellationRemovesOwnedStagingFile()
    {
        var attachmentId = Guid.Parse("00000000-0000-0000-0000-000000000006");
        using var cancellation = new CancellationTokenSource();
        var source = new CancellingImageSource(cancellation);
        string directory = CreateEmptyDirectory();

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new WorkItemImageDownloader(source).DownloadAsync(
                    [Candidate(attachmentId, "cancel.png")],
                    "Payments",
                    directory,
                    cancellation.Token));

            Assert.Empty(Directory.EnumerateFiles(directory, ".partial-*"));
            Assert.Empty(Directory.EnumerateFiles(directory, "*"));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task Downloader_CancellationAfterSuccessRemovesFinalizedInvocationFiles()
    {
        var firstId = Guid.Parse("00000000-0000-0000-0000-00000000000e");
        var secondId = Guid.Parse("00000000-0000-0000-0000-00000000000f");
        using var cancellation = new CancellationTokenSource();
        var source = new SequenceImageSource(
            new Dictionary<Guid, Func<Stream>>
            {
                [firstId] = () => new MemoryStream(PngBytes(), writable: false),
                [secondId] = () => new CancellingStream(cancellation)
            });
        string directory = CreateEmptyDirectory();

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new WorkItemImageDownloader(source).DownloadAsync(
                    [Candidate(firstId, "first.png"), Candidate(secondId, "second.png")],
                    "Payments",
                    directory,
                    cancellation.Token));

            Assert.Empty(Directory.EnumerateFiles(directory, "*"));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task Downloader_EnforcesPerImageAndTotalByteLimitsWhilePreservingEarlierSuccess()
    {
        var perImageId = Guid.Parse("00000000-0000-0000-0000-000000000007");
        Guid[] validIds =
        [
            Guid.Parse("00000000-0000-0000-0000-000000000008"),
            Guid.Parse("00000000-0000-0000-0000-000000000009"),
            Guid.Parse("00000000-0000-0000-0000-00000000000a"),
            Guid.Parse("00000000-0000-0000-0000-00000000000b"),
            Guid.Parse("00000000-0000-0000-0000-00000000000c"),
            Guid.Parse("00000000-0000-0000-0000-00000000000d")
        ];
        var source = new GeneratedImageSource(
            (new Dictionary<Guid, long>
            {
                [perImageId] = WorkItemImageDownloader.MaximumBytesPerImage + 1
            })
            .Concat(validIds.ToDictionary(id => id, _ => 9L * 1024 * 1024))
            .ToDictionary(pair => pair.Key, pair => pair.Value));
        string directory = CreateEmptyDirectory();

        try
        {
            WorkItemMediaDocument result = await new WorkItemImageDownloader(source).DownloadAsync(
                [
                    Candidate(perImageId, "per-image.png"),
                    ..validIds.Select((id, index) => Candidate(id, $"image-{index}.png"))
                ],
                "Payments",
                directory,
                CancellationToken.None);

            Assert.Equal(
                ["failed", "downloaded", "downloaded", "downloaded", "downloaded", "downloaded", "failed"],
                result.Items.Select(item => item.Status).ToArray());
            Assert.Equal("imageTooLarge", result.Items[0].Error!.Code);
            Assert.Equal("mediaBudgetExhausted", result.Items[^1].Error!.Code);
            Assert.Equal("The cumulative image download budget was exhausted.", result.Items[^1].Error!.Message);
            Assert.True(File.Exists(result.Items[1].LocalPath));
            Assert.Empty(Directory.EnumerateFiles(directory, ".partial-*"));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void CandidateCollector_RejectsAttachmentRoutesWithTrailingSegments()
    {
        var workItem = new SourceWorkItem(
            123,
            1,
            null,
            new Dictionary<string, object?>
            {
                ["Microsoft.VSTS.Common.AcceptanceCriteria"] =
                    "<img src=\"https://dev.azure.com/example/Payments/_apis/wit/attachments/6d8e1ce4-1172-4d07-a83f-35f8d7f5889a/extra?fileName=checkout-layout.png\" />"
            },
            []);

        WorkItemImageCandidate candidate = Assert.Single(WorkItemImageCandidateCollector.Collect(
            workItem,
            new Uri("https://dev.azure.com/example/")));
        Assert.Equal("unsupportedImageReference", candidate.SkipCode);
    }

    [Fact]
    public void SupportedImageFormat_RequiresStructurallyValidJpegHeader()
    {
        Assert.Equal("jpg", SupportedImageFormat.Detect(JpegBytes())?.Extension);
        Assert.Null(SupportedImageFormat.Detect([0xFF, 0xD8, 0xFF, 0xD9]));
        Assert.Null(SupportedImageFormat.Detect(
            [0xFF, 0xD8, 0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x00, 0x00, 0x01, 0x01, 0x11, 0x00]));
    }

    [Fact]
    public void SupportedImageFormat_RequiresStructurallyValidPngPayload()
    {
        byte[] valid = PngBytes();
        Assert.Equal("png", SupportedImageFormat.Detect(valid)?.Extension);
        Assert.Null(SupportedImageFormat.Detect(valid[..8]));
        Assert.Null(SupportedImageFormat.Detect(valid[..33]));

        byte[] zeroWidth = valid.ToArray();
        zeroWidth[19] = 0;
        Assert.Null(SupportedImageFormat.Detect(zeroWidth));
    }

    [Fact]
    public void SupportedImageFormat_RejectsWebpPrefixesAndArbitraryPayloads()
    {
        Assert.Null(SupportedImageFormat.Detect("RIFF"u8.ToArray().Concat(new byte[4]).Concat("WEBP"u8.ToArray()).ToArray()));
        Assert.Null(SupportedImageFormat.Detect("RIFF"u8.ToArray().Concat(new byte[4]).Concat("WEBP"u8.ToArray()).Concat("not-an-image"u8.ToArray()).ToArray()));
        Assert.Null(SupportedImageFormat.Detect(DeclaredRiffLengthBeyondFile()));
        Assert.Null(SupportedImageFormat.Detect(ChunkPaddedEndBeyondContainer()));
        Assert.Null(SupportedImageFormat.Detect(CorruptVp8StartCode()));
        Assert.Null(SupportedImageFormat.Detect(CorruptVp8LSignature()));
        Assert.Null(SupportedImageFormat.Detect(InvalidVp8xSize()));
        Assert.Null(SupportedImageFormat.Detect(Vp8xOnlyBytes()));
        Assert.Equal("webp", SupportedImageFormat.Detect(WebpBytes())?.Extension);
        Assert.Equal("webp", SupportedImageFormat.Detect(Vp8Bytes())?.Extension);
        Assert.Equal("webp", SupportedImageFormat.Detect(Vp8LBytes())?.Extension);
    }

    [Fact]
    public async Task Downloader_AcceptsJpegWithLargeMetadataBeforeFrameHeader()
    {
        var attachmentId = Guid.Parse("00000000-0000-0000-0000-000000000020");
        byte[] jpeg = LargeMetadataJpegBytes();
        var source = new FakeImageSource(new Dictionary<Guid, byte[]> { [attachmentId] = jpeg });
        string directory = CreateEmptyDirectory();

        try
        {
            WorkItemMediaDocument result = await new WorkItemImageDownloader(source).DownloadAsync(
                [Candidate(attachmentId, "large-metadata.jpg")],
                "Payments",
                directory,
                CancellationToken.None);

            WorkItemMediaItem item = Assert.Single(result.Items);
            Assert.Equal("downloaded", item.Status);
            Assert.Equal("jpg", item.Format);
            Assert.Equal(jpeg.LongLength, item.ByteLength);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    private static WorkItemImageCandidate Candidate(Guid id, string fileName) =>
        new(id, fileName, [new WorkItemMediaSource("attachmentRelation", null)]);

    private static byte[] PngBytes() =>
        Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private static byte[] JpegBytes() =>
    [
        0xFF, 0xD8,
        0xFF, 0xE0, 0x00, 0x10,
        0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
        0xFF, 0xC0, 0x00, 0x0B,
        0x08, 0x00, 0x01, 0x00, 0x01, 0x01, 0x01, 0x11, 0x00,
        0xFF, 0xD9
    ];

    private static byte[] LargeMetadataJpegBytes()
    {
        var bytes = new List<byte>
        {
            0xFF, 0xD8,
            0xFF, 0xE1, 0xFF, 0xFF
        };
        bytes.AddRange(new byte[65533]);
        bytes.AddRange(
        [
            0xFF, 0xC0, 0x00, 0x0B,
            0x08, 0x00, 0x01, 0x00, 0x01, 0x01, 0x01, 0x11, 0x00,
            0xFF, 0xD9
        ]);
        return bytes.ToArray();
    }

    private static byte[] WebpBytes() =>
    [
        0x52, 0x49, 0x46, 0x46, 0x34, 0x00, 0x00, 0x00,
        0x57, 0x45, 0x42, 0x50,
        0x56, 0x50, 0x38, 0x58, 0x0A, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x56, 0x50, 0x38, 0x20, 0x16, 0x00, 0x00, 0x00,
        0x30, 0x01, 0x00, 0x9D, 0x01, 0x2A, 0x01, 0x00, 0x01, 0x00,
        0x0E, 0xC0, 0xFE, 0x25, 0xA4, 0x00, 0x03, 0x70, 0x00, 0x00, 0x00, 0x00
    ];

    private static byte[] Vp8Bytes() =>
    [
        0x52, 0x49, 0x46, 0x46, 0x22, 0x00, 0x00, 0x00,
        0x57, 0x45, 0x42, 0x50,
        0x56, 0x50, 0x38, 0x20, 0x16, 0x00, 0x00, 0x00,
        0x30, 0x01, 0x00, 0x9D, 0x01, 0x2A, 0x01, 0x00, 0x01, 0x00,
        0x0E, 0xC0, 0xFE, 0x25, 0xA4, 0x00, 0x03, 0x70, 0x00, 0x00, 0x00, 0x00
    ];

    private static byte[] Vp8LBytes() =>
    [
        0x52, 0x49, 0x46, 0x46, 0x12, 0x00, 0x00, 0x00,
        0x57, 0x45, 0x42, 0x50,
        0x56, 0x50, 0x38, 0x4C, 0x05, 0x00, 0x00, 0x00,
        0x2F, 0x00, 0x00, 0x00, 0x00, 0x00
    ];

    private static byte[] Vp8xOnlyBytes() =>
    [
        0x52, 0x49, 0x46, 0x46, 0x16, 0x00, 0x00, 0x00,
        0x57, 0x45, 0x42, 0x50,
        0x56, 0x50, 0x38, 0x58, 0x0A, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
    ];

    private static byte[] DeclaredRiffLengthBeyondFile()
    {
        byte[] bytes = Vp8Bytes();
        bytes[4] = 0xFF;
        return bytes;
    }

    private static byte[] ChunkPaddedEndBeyondContainer()
    {
        byte[] bytes = Vp8Bytes();
        bytes[16] = 0x17;
        return bytes;
    }

    private static byte[] CorruptVp8StartCode()
    {
        byte[] bytes = Vp8Bytes();
        bytes[23] = 0x00;
        return bytes;
    }

    private static byte[] CorruptVp8LSignature()
    {
        byte[] bytes = Vp8LBytes();
        bytes[20] = 0x00;
        return bytes;
    }

    private static byte[] InvalidVp8xSize()
    {
        byte[] bytes = WebpBytes();
        bytes[16] = 0x09;
        return bytes;
    }

    private static string CreateEmptyDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"ado-read-image-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FakeImageSource(
        IReadOnlyDictionary<Guid, byte[]> images,
        IReadOnlyDictionary<Guid, WorkItemSourceException>? failures = null) : IWorkItemSource
    {
        private readonly IReadOnlyDictionary<Guid, WorkItemSourceException> _failures = failures ?? new Dictionary<Guid, WorkItemSourceException>();

        public List<Guid> AttachmentCalls { get; } = [];

        public Task<SourceWorkItem?> GetAsync(int id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<WorkItemCommentDocument>> GetAllCommentsAsync(
            string project,
            int id,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Stream> GetAttachmentContentAsync(
            string project,
            Guid attachmentId,
            string fileName,
            CancellationToken cancellationToken)
        {
            AttachmentCalls.Add(attachmentId);
            if (_failures.TryGetValue(attachmentId, out WorkItemSourceException? failure))
            {
                return Task.FromException<Stream>(failure);
            }

            return Task.FromResult<Stream>(new MemoryStream(images[attachmentId], writable: false));
        }
    }

    private sealed class ObservingImageSource(
        string directory,
        Guid invalidId,
        Guid validId,
        byte[] invalidBytes,
        byte[] validBytes) : IWorkItemSource
    {
        private int _attachmentCalls;

        public int PartialFilesBeforeSecondCall { get; private set; } = -1;

        public Task<SourceWorkItem?> GetAsync(int id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<WorkItemCommentDocument>> GetAllCommentsAsync(
            string project,
            int id,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Stream> GetAttachmentContentAsync(
            string project,
            Guid attachmentId,
            string fileName,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _attachmentCalls) == 2)
            {
                PartialFilesBeforeSecondCall = Directory.EnumerateFiles(directory, ".partial-*").Count();
            }

            return Task.FromResult<Stream>(new MemoryStream(
                attachmentId == invalidId ? invalidBytes : validId == attachmentId ? validBytes : [],
                writable: false));
        }
    }

    private sealed class CancellingImageSource(CancellationTokenSource cancellation) : IWorkItemSource
    {
        public Task<SourceWorkItem?> GetAsync(int id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<WorkItemCommentDocument>> GetAllCommentsAsync(
            string project,
            int id,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Stream> GetAttachmentContentAsync(
            string project,
            Guid attachmentId,
            string fileName,
            CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new CancellingStream(cancellation));
    }

    private sealed class GeneratedImageSource(IReadOnlyDictionary<Guid, long> lengths) : IWorkItemSource
    {
        public Task<SourceWorkItem?> GetAsync(int id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<WorkItemCommentDocument>> GetAllCommentsAsync(
            string project,
            int id,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Stream> GetAttachmentContentAsync(
            string project,
            Guid attachmentId,
            string fileName,
            CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new GeneratedImageStream(lengths[attachmentId]));
    }

    private sealed class SequenceImageSource(IReadOnlyDictionary<Guid, Func<Stream>> streams) : IWorkItemSource
    {
        public Task<SourceWorkItem?> GetAsync(int id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<WorkItemCommentDocument>> GetAllCommentsAsync(
            string project,
            int id,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Stream> GetAttachmentContentAsync(
            string project,
            Guid attachmentId,
            string fileName,
            CancellationToken cancellationToken) =>
            Task.FromResult(streams[attachmentId]());
    }

    private sealed class GeneratedImageStream(long length) : Stream
    {
        private static readonly byte[] PngHeader = PngBytes();
        private long _position;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_position >= length)
            {
                return ValueTask.FromResult(0);
            }

            int count = (int)Math.Min(buffer.Length, length - _position);
            buffer[..count].Span.Clear();
            if (_position == 0)
            {
                PngHeader.AsSpan(0, Math.Min(count, PngHeader.Length)).CopyTo(buffer.Span);
            }

            _position += count;
            return ValueTask.FromResult(count);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class CancellingStream(CancellationTokenSource cancellation) : Stream
    {
        private bool _firstRead = true;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_firstRead)
            {
                _firstRead = false;
                byte[] png = PngBytes();
                png.AsMemory().CopyTo(buffer);
                cancellation.Cancel();
                return png.Length;
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 0;
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
