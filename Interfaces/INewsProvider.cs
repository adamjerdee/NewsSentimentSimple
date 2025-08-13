using NewsSentimentSimple.Models;

namespace NewsSentimentSimple.Interfaces
{
    internal interface INewsProvider
    {
        List<NewsItem> GetHeadlines(string ticker, int max);
    }
}
