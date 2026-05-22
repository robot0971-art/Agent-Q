namespace AgentQ.Desktop.Services;

public enum LinkFetchStatus
{
    Succeeded,
    HttpError,
    UnsupportedContentType,
    EmptyContent,
    TimeoutOrCancellation,
    RequestFailed,
    InvalidUrl
}

public sealed class LinkFetchResult
{
    public required string Url { get; init; }

    public LinkFetchStatus Status { get; init; }

    public int? HttpStatusCode { get; init; }

    public string? HttpReasonPhrase { get; init; }

    public string? ContentType { get; init; }

    public string? Excerpt { get; init; }

    public string? FailureReason { get; init; }

    public bool Succeeded => Status is LinkFetchStatus.Succeeded;
}
