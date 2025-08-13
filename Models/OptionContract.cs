
namespace NewsSentimentSimple.Models
{
    public class OptionContract
    {
        public string Type { get; set; } = ""; // "CALL" or "PUT"
        public DateTime Expiration { get; set; }
        public double Strike { get; set; }
        public double Bid { get; set; }
        public double Ask { get; set; }
        public double Delta { get; set; }
        public double Gamma { get; set; }
        public double Vega { get; set; }
        public double Theta { get; set; }
        public double ImpliedVol { get; set; }
        public double Score { get; set; } // computed
    }
}
