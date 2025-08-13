using NewsSentimentSimple.Models;

namespace NewsSentimentSimple.Interfaces
{
    internal interface IOptionChainService
    {
        double GetUnderlyingPrice(string ticker);
        List<OptionContract> GetMonthlyChain(string ticker); // include both calls & puts
    }
}
