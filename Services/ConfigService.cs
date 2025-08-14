using System.Text.Json;
using System.Text.Json.Serialization;

namespace NewsSentimentSimple.Services
{
    public class AppOptions
    {
        [JsonPropertyName("ApiKey")] public string? ApiKey { get; set; }
        [JsonPropertyName("Expiration")] public string? Expiration { get; set; } // optional ISO date (yyyy-MM-dd)
    }

    public class AppConfig
    {
        [JsonPropertyName("Finnhub")] public AppOptions Finnhub { get; set; } = new();
        [JsonPropertyName("tickers")] public List<string> Tickers { get; set; } = new();
    }

    public static class ConfigService
    {
        public static readonly string ConfigDir = Path.Combine(AppContext.BaseDirectory, "Config");
        public static readonly string ConfigPath = Path.Combine(ConfigDir, "AppSettings.json");
        public static readonly string SamplePath = Path.Combine(ConfigDir, "AppSettings.sample.json");

        public static AppConfig LoadOrDefault()
        {
            Directory.CreateDirectory(ConfigDir);
            if (!File.Exists(ConfigPath))
            {
                // If first run, create a minimal sample file to help users
                if (!File.Exists(SamplePath))
                {
                    var sample = new AppConfig
                    {
                        Finnhub = new AppOptions { ApiKey = "YOUR_API_KEY_HERE", Expiration = null },
                        Tickers = new List<string> { "MSFT", "AAPL", "NVDA" }
                    };
                    Save(sample, SamplePath);
                }
                return new AppConfig();
            }
            try
            {
                var json = File.ReadAllText(ConfigPath);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions()) ?? new AppConfig();
                // normalize tickers
                cfg.Tickers = cfg.Tickers.Where(t => !string.IsNullOrWhiteSpace(t))
                                         .Select(t => t.Trim().ToUpperInvariant())
                                         .Distinct()
                                         .ToList();
                return cfg;
            }
            catch
            {
                return new AppConfig();
            }
        }

        public static void Save(AppConfig config, string? path = null)
        {
            Directory.CreateDirectory(ConfigDir);
            var json = JsonSerializer.Serialize(config, JsonOptions(true));
            File.WriteAllText(path ?? ConfigPath, json);
        }

        private static JsonSerializerOptions JsonOptions(bool indented = false) => new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = indented,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }
}
