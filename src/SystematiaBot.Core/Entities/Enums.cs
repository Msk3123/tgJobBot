namespace SystematiaBot.Core.Entities;

public enum SourceKind
{
    Telegram,
    Rss,
    HtmlList
}

public enum NewsStatus
{
    New,
    Picked,
    Rejected,
    Done
}

public enum RejectReason
{
    Old,
    NotLocal,
    Unverified,
    Duplicate,
    Custom
}

public enum PostKind
{
    Social,
    Article
}

public enum SeoMode
{
    Quick,
    Full
}
