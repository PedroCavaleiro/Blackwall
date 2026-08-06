namespace Blackwall.Core.Services;

public enum SafeBrowsingResult {
    Safe,
    Unsafe,
    Unsure
}

public interface ISafeBrowsingService {
    Task<SafeBrowsingResult> CheckUrlAsync(string url);
}
