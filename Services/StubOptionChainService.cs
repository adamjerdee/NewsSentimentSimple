using NewsSentimentSimple.Interfaces;
using NewsSentimentSimple.Models;

namespace NewsSentimentSimple.Services
{
    internal class StubOptionChainService : IOptionChainService
    {
        public double GetUnderlyingPrice(string ticker) => 100.0;
        public List<OptionContract> GetMonthlyChain(string ticker) => new(); // no data
    }
}
