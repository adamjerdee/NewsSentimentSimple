using NewsSentimentSimple.Interfaces;
using NewsSentimentSimple.Models;
using System.Globalization;
using System.Text.Json;

namespace NewsSentimentSimple.Services
{
    internal class FinnhubOptionChainService : IOptionChainService
    {
        private readonly HttpClient _http;
        private readonly string _key;

        // Flip to true if you want lightweight console debug
        private const bool DEBUG_LOG = false;

        public FinnhubOptionChainService(HttpClient http, string apiKey)
        {
            _http = http;
            _key = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
            _http.Timeout = TimeSpan.FromSeconds(20);
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
        }

        public double GetUnderlyingPrice(string ticker)
        {
            var url = $"https://finnhub.io/api/v1/quote?symbol={Uri.EscapeDataString(ticker)}&token={_key}";
            var json = _http.GetStringAsync(url).GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("c", out var cur) ? cur.GetDouble() : 0.0;
        }

        public List<OptionContract> GetMonthlyChain(string ticker)
        {
            var target = ThirdFridayTwoMonthsOut(DateTime.UtcNow.Date);
            var url = $"https://finnhub.io/api/v1/stock/option-chain?symbol={Uri.EscapeDataString(ticker)}&token={_key}";
            var json = _http.GetStringAsync(url).GetAwaiter().GetResult();

            if (DEBUG_LOG) Console.WriteLine($"[DEBUG {ticker}] option-chain JSON len={json.Length}");

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Gather candidate (expiration, optionsNode) pairs regardless of shape
            var candidates = new List<(DateTime Exp, JsonElement OptionsNode)>();

            // Shape A: { data: [ { expirationDate, options: [...] or {...} } ... ] }
            if (root.TryGetProperty("data", out var dataArr) && dataArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var block in dataArr.EnumerateArray())
                {
                    if (!TryParseExpiration(block, out var exp)) continue;

                    if (block.TryGetProperty("options", out var optionsNode) &&
                        (optionsNode.ValueKind == JsonValueKind.Array || optionsNode.ValueKind == JsonValueKind.Object))
                    {
                        candidates.Add((exp, optionsNode));
                    }
                    else if (TryGetCallsPutsNode(block, out var callsPutsObj))
                    {
                        candidates.Add((exp, callsPutsObj));
                    }
                }
            }

            // Shape B: { optionChain: [ { expirationDate, options: [...] or {...} } ... ] }
            if (candidates.Count == 0 && root.TryGetProperty("optionChain", out var ocArr) && ocArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var block in ocArr.EnumerateArray())
                {
                    if (!TryParseExpiration(block, out var exp)) continue;

                    if (block.TryGetProperty("options", out var optionsNode) &&
                        (optionsNode.ValueKind == JsonValueKind.Array || optionsNode.ValueKind == JsonValueKind.Object))
                    {
                        candidates.Add((exp, optionsNode));
                    }
                    else if (TryGetCallsPutsNode(block, out var callsPutsObj))
                    {
                        candidates.Add((exp, callsPutsObj));
                    }
                }
            }

            if (candidates.Count == 0)
            {
                if (DEBUG_LOG) Console.WriteLine($"[DEBUG {ticker}] No option blocks found.");
                return new List<OptionContract>();
            }

            // Choose expiration: nearest >= target; else nearest overall
            (DateTime Exp, JsonElement Node) chosen =
                candidates.Where(c => c.Exp >= target).OrderBy(c => c.Exp).FirstOrDefault();

            if (chosen.Exp == default)
                chosen = candidates.OrderBy(c => Math.Abs((c.Exp - target).TotalDays)).First();

            if (DEBUG_LOG) Console.WriteLine($"[DEBUG {ticker}] target={target:yyyy-MM-dd}, chosen={chosen.Exp:yyyy-MM-dd}");

            var list = new List<OptionContract>();
            ExtractOptionsIntoFlexible(list, chosen.Node, chosen.Exp);

            if (DEBUG_LOG) Console.WriteLine($"[DEBUG {ticker}] parsed contracts={list.Count}");
            return list;
        }

        // ---- JSON helpers ----
        private static bool TryParseExpiration(JsonElement block, out DateTime exp)
        {
            exp = default;
            foreach (var name in new[] { "expirationDate", "expiry", "expiration" })
            {
                if (block.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String)
                {
                    var s = p.GetString();
                    if (DateTime.TryParse(s, out exp)) return true;
                }
            }
            return false;
        }

        // Sometimes calls/puts are top-level on the block rather than in "options"
        private static bool TryGetCallsPutsNode(JsonElement block, out JsonElement node)
        {
            node = default;
            if (block.ValueKind == JsonValueKind.Object)
            {
                if (block.TryGetProperty("CALL", out _) || block.TryGetProperty("PUT", out _) ||
                    block.TryGetProperty("calls", out _) || block.TryGetProperty("puts", out _))
                {
                    node = block;
                    return true;
                }
            }
            return false;
        }

        // Accepts either array of options OR object with CALL/PUT arrays
        private static void ExtractOptionsIntoFlexible(List<OptionContract> list, JsonElement optionsNode, DateTime exp)
        {
            if (optionsNode.ValueKind == JsonValueKind.Array)
            {
                foreach (var opt in optionsNode.EnumerateArray())
                    TryAddOption(list, opt, exp, inferredType: null);
            }
            else if (optionsNode.ValueKind == JsonValueKind.Object)
            {
                foreach (var label in new[] { "CALL", "PUT", "calls", "puts" })
                {
                    if (optionsNode.TryGetProperty(label, out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        string inferred = (label.Equals("CALL", StringComparison.OrdinalIgnoreCase) ||
                                           label.Equals("calls", StringComparison.OrdinalIgnoreCase))
                                            ? "CALL" : "PUT";
                        foreach (var opt in arr.EnumerateArray())
                            TryAddOption(list, opt, exp, inferred);
                    }
                }
            }
        }

        private static void TryAddOption(List<OptionContract> list, JsonElement opt, DateTime exp, string inferredType)
        {
            // Type
            string type = inferredType;
            if (string.IsNullOrEmpty(type))
            {
                if (opt.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String)
                    type = (t.GetString() ?? "").ToUpperInvariant();
                else if (opt.TryGetProperty("contractType", out var ct) && ct.ValueKind == JsonValueKind.String)
                    type = (ct.GetString() ?? "").ToUpperInvariant();
            }
            if (type != "CALL" && type != "PUT") return;

            // Field variants
            double strike = GetDouble(opt, "strike", "strikePrice");
            double bid = GetDouble(opt, "bid", "b");
            double ask = GetDouble(opt, "ask", "a");
            double delta = GetDouble(opt, "delta", "Delta");
            double gamma = GetDouble(opt, "gamma", "Gamma");
            double vega = GetDouble(opt, "vega", "Vega");
            double theta = GetDouble(opt, "theta", "Theta");
            double iv = GetDouble(opt, "impliedVolatility", "impliedVol", "iv", "IV");

            // If everything's missing, skip
            if (double.IsNaN(strike) && double.IsNaN(delta) && double.IsNaN(gamma) &&
                double.IsNaN(vega) && double.IsNaN(theta) && double.IsNaN(iv))
                return;

            list.Add(new OptionContract
            {
                Type = type,
                Expiration = exp,
                Strike = strike,
                Bid = bid,
                Ask = ask,
                Delta = delta,
                Gamma = gamma,
                Vega = vega,
                Theta = theta,
                ImpliedVol = iv
            });
        }

        private static double GetDouble(JsonElement e, params string[] names)
        {
            foreach (var n in names)
            {
                if (!e.TryGetProperty(n, out var p)) continue;

                if (p.ValueKind == JsonValueKind.Number)
                    return p.GetDouble();

                if (p.ValueKind == JsonValueKind.String)
                {
                    var s = p.GetString();
                    if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                        return d;
                }
            }
            return double.NaN;
        }

        // Third Friday two months ahead
        private static DateTime ThirdFridayTwoMonthsOut(DateTime from)
        {
            var dt = new DateTime(from.Year, from.Month, 1).AddMonths(2);
            int fridays = 0;
            for (int d = 0; d < 31; d++)
            {
                var day = dt.AddDays(d);
                if (day.Month != dt.Month) break;
                if (day.DayOfWeek == DayOfWeek.Friday)
                {
                    fridays++;
                    if (fridays == 3) return day;
                }
            }
            // fallback (shouldn't hit)
            return dt;
        }
    }
}
