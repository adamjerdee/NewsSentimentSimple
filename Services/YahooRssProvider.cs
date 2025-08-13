using NewsSentimentSimple.Interfaces;
using NewsSentimentSimple.Models;
using System.ServiceModel.Syndication;
using System.Xml;

namespace NewsSentimentSimple.Services
{
    internal class YahooRssProvider : INewsProvider
    {
        private readonly HttpClient _http;
        private readonly int _days;

        public YahooRssProvider(HttpClient http, int daysLookback)
        {
            _http = http;
            _days = Math.Max(1, daysLookback);
        }

        public List<NewsItem> GetHeadlines(string ticker, int max)
        {
            var url = $"https://feeds.finance.yahoo.com/rss/2.0/headline?s={Uri.EscapeDataString(ticker)}&region=US&lang=en-US";
            using var resp = _http.GetAsync(url).GetAwaiter().GetResult();
            resp.EnsureSuccessStatusCode();
            using var stream = resp.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
            using var reader = XmlReader.Create(stream);
            var feed = SyndicationFeed.Load(reader);

            var cutoffUtc = DateTime.UtcNow.AddDays(-_days);

            return feed?.Items?
                .Select(i =>
                {
                    var pubUtc = i.PublishDate.UtcDateTime;
                    if (pubUtc == default || pubUtc.Year < 2000)
                    {
                        var upd = i.LastUpdatedTime.UtcDateTime;
                        pubUtc = (upd != default && upd.Year >= 2000) ? upd : DateTime.UtcNow;
                    }
                    return new NewsItem((i.Title?.Text ?? "").Trim(), pubUtc);
                })
                .Where(n => !string.IsNullOrWhiteSpace(n.Title))
                .Where(n => n.PublishedUtc >= cutoffUtc)
                .OrderByDescending(n => n.PublishedUtc)
                .Take(max)
                .ToList()
                ?? new List<NewsItem>();
        }
    }
}
