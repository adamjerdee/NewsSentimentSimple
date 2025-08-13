using NewsSentimentSimple.Models;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using VaderSharp2;
using NewsSentimentSimple.Interfaces;
using NewsSentimentSimple.Services;


class Program
{
    static void Main()
    {
        // ------------ Config ------------
        const int headlinesPerTicker = 15;
        const int daysLookback = 3;

        // Greeks weights (tune as needed)
        const double wDelta = 1.0, wGamma = 1.0, wVega = 1.0, wTheta = 1.0;

        // Output path
        var outDir = Path.Combine(AppContext.BaseDirectory, "out");
        Directory.CreateDirectory(outDir);
        var nowLocal = DateTime.Now;
        var outPath = Path.Combine(outDir, $"options_sentiment_{nowLocal:yyyy-MM-dd_HHmm}.html");

        // ------------ Load API key directly from JSON file ------------
        string finnhubKey = null;
        string[]? tickers = new string[] { };
        var configPath = Path.Combine(AppContext.BaseDirectory, "Config", "AppSettings.json");
        if (File.Exists(configPath))
        {
            try
            {
                var json = File.ReadAllText(configPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("Finnhub", out var fh) &&
                    fh.TryGetProperty("ApiKey", out var keyEl))
                    finnhubKey = keyEl.GetString();

                if (doc.RootElement.TryGetProperty("tickers", out var t))
                {
                    tickers = t.Deserialize<string[]>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Reading API key: {ex.Message}");
            }
        }

        bool useStub = string.IsNullOrWhiteSpace(finnhubKey);
        if (useStub)
        {
            Console.WriteLine("WARNING: Finnhub API key not found in Config/AppSettings.json (Finnhub:ApiKey).");
            Console.WriteLine("Using StubOptionChainService (no option scores will appear).");
        }
        else
        {
            Console.WriteLine($"[CONFIG] Finnhub key loaded ({finnhubKey.Length} chars).");
        }

        // ------------ Services ------------
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");

        INewsProvider news = new YahooRssProvider(http, daysLookback);
        var analyzer = new SentimentIntensityAnalyzer();

        IOptionChainService optionSvc = useStub
            ? new StubOptionChainService()
            : new FinnhubOptionChainService(new HttpClient(), finnhubKey!);

        // ------------ Collect ------------
        var rows = new List<SummaryRow>();
        var headlinesByTicker = new Dictionary<string, List<(string Headline, double Score, DateTime PublishedLocal)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var ticker in tickers)
        {
            // --- Best option via Greeks ---
            BestOption? best = null;
            try
            {
                var price = optionSvc.GetUnderlyingPrice(ticker);
                var chain = optionSvc.GetMonthlyChain(ticker);

                Console.WriteLine($"[{ticker}] price={price}, chainCount={chain?.Count ?? 0}");

                if (price > 0 && chain != null && chain.Count > 0)
                {
                    foreach (var o in chain)
                    {
                        if (double.IsNaN(o.Delta) || double.IsNaN(o.Gamma) || double.IsNaN(o.Vega) || double.IsNaN(o.Theta))
                            continue;

                        double normDelta = o.Delta;
                        double normGamma = o.Gamma * price;
                        double normVega = o.Vega / price;
                        double normTheta = o.Theta / price;

                        o.Score = (wDelta * Math.Abs(normDelta)) +
                                  (wGamma * normGamma) +
                                  (wVega * normVega) -
                                  (wTheta * Math.Abs(normTheta));
                    }

                    var pick = chain.Where(c => !double.IsNaN(c.Score) && c.Score != 0)
                                    .OrderByDescending(c => c.Score)
                                    .FirstOrDefault();

                    if (pick != null)
                    {
                        best = new BestOption(
                            ticker,
                            pick.Type,
                            pick.Expiration,
                            pick.Strike,
                            Mid: (pick.Bid + pick.Ask) / 2.0,
                            pick.Delta,
                            pick.Theta,
                            pick.ImpliedVol,
                            pick.Score
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Greeks {ticker}] {ex.Message}");
            }

            // --- Sentiment / headlines (last N days) ---
            List<NewsItem> items;
            try
            {
                items = news.GetHeadlines(ticker, headlinesPerTicker);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[News {ticker}] {ex.Message}");
                items = new List<NewsItem>();
            }

            var scored = items
                .Select(it => (it.Title, Score: analyzer.PolarityScores(it.Title).Compound, LocalTime: it.PublishedUtc.ToLocalTime()))
                .OrderByDescending(x => x.Score)
                .Select(x => (Headline: x.Title, Score: x.Score, PublishedLocal: x.LocalTime))
                .ToList();

            headlinesByTicker[ticker] = scored;

            double avgSent = scored.Count > 0 ? scored.Average(x => x.Score) : double.NaN;
            string label = double.IsNaN(avgSent) ? "NO DATA"
                          : avgSent > 0.05 ? "POSITIVE"
                          : avgSent < -0.05 ? "NEGATIVE" : "NEUTRAL";

            rows.Add(new SummaryRow(
                Ticker: ticker,
                Type: best?.Type ?? "-",
                Expiration: best?.Expiration.ToString("yyyy-MM-dd") ?? "-",
                Strike: best?.Strike ?? double.NaN,
                Mid: best?.Mid ?? double.NaN,
                Delta: best?.Delta ?? double.NaN,
                Theta: best?.Theta ?? double.NaN,
                IV: best?.IV ?? double.NaN,
                GreeksScore: best?.GreeksScore ?? double.NaN,
                HeadlineCount: scored.Count,
                AvgSentiment: avgSent,
                SentimentLabel: label
            ));
        }

        //    // ------------ HTML ------------
        var html = BuildHtml(nowLocal, rows, headlinesByTicker);
        File.WriteAllText(outPath, html, new UTF8Encoding(false));
        Console.WriteLine($"Saved HTML: {outPath}");
    }

    // =================== HTML builder ===================
    static string BuildHtml(
        DateTime ts,
        List<SummaryRow> rows,
        Dictionary<string, List<(string Headline, double Score, DateTime PublishedLocal)>> headlinesByTicker)
    {
        string Esc(string s) => WebUtility.HtmlEncode(s ?? "");
        string Num(double d, int decimals = 3) => double.IsNaN(d) ? "-" : d.ToString($"0.{new string('0', decimals)}", CultureInfo.InvariantCulture);
        string ColorLabel(string label)
            => label.Equals("POSITIVE", StringComparison.OrdinalIgnoreCase) ? "<span style=\"color:green;font-weight:bold;\">POSITIVE</span>"
             : label.Equals("NEGATIVE", StringComparison.OrdinalIgnoreCase) ? "<span style=\"color:red;font-weight:bold;\">NEGATIVE</span>"
             : label.Equals("NO DATA", StringComparison.OrdinalIgnoreCase) ? "NO DATA" : "NEUTRAL";

        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html>");
        sb.AppendLine("<html><head><meta charset='utf-8'><title>Options + Sentiment</title></head><body>");
        sb.AppendLine("<h1>Options + Sentiment</h1>");
        sb.AppendLine($"<p>Generated: {Esc(ts.ToString("yyyy-MM-dd HH:mm"))}. Headlines shown for last 3 days.</p>");
        string ColorType(string type)
            => type.Equals("CALL", StringComparison.OrdinalIgnoreCase) ? "<span style=\"color:green;font-weight:bold;\">CALL</span>"
                : type.Equals("PUT", StringComparison.OrdinalIgnoreCase) ? "<span style=\"color:red;font-weight:bold;\">PUT</span>"
                : type;

        // Summary table — sorted by Avg Sentiment high → low
        sb.AppendLine("<h2>Summary (Best Option per Ticker)</h2>");
        sb.AppendLine("<table border='1' cellpadding='5' cellspacing='0'>");
        sb.AppendLine("<tr><th>Ticker</th><th>Type</th><th>Expiration</th><th>Strike</th><th>Mid</th><th>Delta</th><th>Theta</th><th>IV</th><th>GreeksScore</th><th>HeadlineCount</th><th>AvgSentiment</th><th>Label</th></tr>");
        foreach (var r in rows.OrderByDescending(x => double.IsNaN(x.GreeksScore) ? double.NegativeInfinity : x.GreeksScore))
        {
            sb.Append("<tr>");
            sb.Append($"<td>{Esc(r.Ticker)}</td>");
            sb.Append($"<td>{ColorType(r.Type)}</td>");
            sb.Append($"<td>{Esc(r.Expiration)}</td>");
            sb.Append($"<td>{(double.IsNaN(r.Strike) ? "-" : r.Strike.ToString("0.00", CultureInfo.InvariantCulture))}</td>");
            sb.Append($"<td>{Num(r.Mid, 2)}</td>");
            sb.Append($"<td>{Num(r.Delta)}</td>");
            sb.Append($"<td>{Num(r.Theta)}</td>");
            sb.Append($"<td>{Num(r.IV)}</td>");
            sb.Append($"<td>{Num(r.GreeksScore)}</td>");
            sb.Append($"<td>{r.HeadlineCount}</td>");
            sb.Append($"<td>{Num(r.AvgSentiment)}</td>");
            sb.Append($"<td>{ColorLabel(r.SentimentLabel)}</td>");
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</table>");

        // Headlines by Ticker — sorted by sentiment, with date
        sb.AppendLine("<h2>Headlines by Ticker (Most Positive → Most Negative)</h2>");
        foreach (var kv in headlinesByTicker.OrderBy(k => k.Key))
        {
            sb.AppendLine($"<h3>{Esc(kv.Key)}:</h3>");
            var list = kv.Value;
            if (list == null || list.Count == 0) { sb.AppendLine("<p>No headlines found.</p>"); continue; }
            sb.AppendLine("<ol>");
            foreach (var (Headline, Score, PublishedLocal) in list)
                sb.AppendLine($"<li>{Esc(Headline)}, {Esc(PublishedLocal.ToString("yyyy-MM-dd HH:mm"))} (score {Score.ToString("0.000", CultureInfo.InvariantCulture)})</li>");
            sb.AppendLine("</ol>");
        }

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }
}