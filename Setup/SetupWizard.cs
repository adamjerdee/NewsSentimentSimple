namespace NewsSentimentSimple.Setup
{
    public static class SetupWizard
    {
        public static void RunInteractive()
        {
            var configDir = System.IO.Path.Combine(AppContext.BaseDirectory, "Config");
            System.IO.Directory.CreateDirectory(configDir);
            var configPath = System.IO.Path.Combine(configDir, "AppSettings.json");

            // Ask for Finnhub API key
            System.Console.Write("Enter your Finnhub API key: ");
            var apiKey = System.Console.ReadLine()?.Trim() ?? "";

            // Ask for tickers
            System.Console.Write("Enter comma-separated tickers (e.g., MSFT,AAPL,NVDA): ");
            var tickers = (System.Console.ReadLine() ?? "")
                .Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries)
                .Select(t => t.ToUpperInvariant())
                .Distinct()
                .ToList();

            var json = System.Text.Json.JsonSerializer.Serialize(
                new
                {
                    Finnhub = new { ApiKey = apiKey },
                    tickers = tickers
                },
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
            );

            System.IO.File.WriteAllText(configPath, json);
            System.Console.WriteLine($"Saved config to {configPath}");
        }
    }
}