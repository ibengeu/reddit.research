namespace RedditCrawler.Configuration;

public sealed class CrawlerConfig
{
    public List<string> Subreddits { get; set; } = ["ResumesATS"];
    public int Limit { get; set; } = 100;
    public string Sort { get; set; } = "desc";
    public string OutputPath { get; set; } = "output";
    public int MaxTokensPerChunk { get; set; } = 512;
    public int ChunkOverlapTokens { get; set; } = 50;
    public bool EnableContextualEnrichment { get; set; } = false;
    public string ArcticShiftBaseUrl { get; set; } = "https://arctic-shift.photon-reddit.com";
    public int MinScore { get; set; } = 0;

    public static readonly string[] ValidSorts = ["asc", "desc"];
}
