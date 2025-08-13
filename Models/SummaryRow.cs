using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewsSentimentSimple.Models
{
    record SummaryRow(
        string Ticker,
        string Type,
        string Expiration,
        double Strike,
        double Mid,
        double Delta,
        double Theta,
        double IV,
        double GreeksScore,
        int HeadlineCount,
        double AvgSentiment,
        string SentimentLabel
    );
}
