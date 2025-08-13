using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewsSentimentSimple.Models
{
    record BestOption(
        string Ticker,
        string Type,      // "CALL" or "PUT"
        DateTime Expiration,
        double Strike,
        double Mid,
        double Delta,
        double Theta,
        double IV,
        double GreeksScore
    );
}
