using ClosedXML.Excel;
using OfficeOpenXml;
using OfficeOpenXml.Drawing.Chart;
using CodeName399.Kite.API.Application.Services;
using CodeName399.Kite.API.Models.Indicator;
using CodeName399.Kite.API.Services;
using CodeName399.Kite.API.Services.Interface;

namespace CodeName399.Kite.API.Application.BackgroundServices;

/// <summary>Exports the 20 most active NSE equities and their one-minute indicators to an Excel workbook.</summary>
public sealed class TopStocksExcelExportBackgroundService : BackgroundService
{
    private static readonly TimeZoneInfo India = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "India Standard Time" : "Asia/Kolkata");
    private readonly ILogger<TopStocksExcelExportBackgroundService> _logger;
    private readonly CommonService _common;
    private readonly IInstrumentService _instruments;
    private readonly IAngelOneService _angel;
    private readonly IWebHostEnvironment _environment;

    public TopStocksExcelExportBackgroundService(
        ILogger<TopStocksExcelExportBackgroundService> logger,
        CommonService common,
        IInstrumentService instruments,
        IAngelOneService angel,
        IWebHostEnvironment environment)
    {
        _logger = logger;
        _common = common;
        _instruments = instruments;
        _angel = angel;
        _environment = environment;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // First export after startup, then refresh every 15 minutes during the NSE session.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                //var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, India);
                //if (now.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) &&
                //    now.TimeOfDay >= new TimeSpan(9, 15, 0) && now.TimeOfDay <= new TimeSpan(15, 30, 0))
                    await ExportAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Top-20 stock Excel export cycle failed."); }

            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
        }
    }

    private async Task ExportAsync(CancellationToken ct)
    {
        var login = await _angel.GetLoginDataAsync();
        var instruments = await _instruments.LoadInstruments();
        var quotes = await _common.GetQuotesAsync(instruments, login.JwtToken);
        var top = _common.GetTopQuotes(quotes, new Dictionary<string, CodeName399.Kite.API.Models.TradeOrder>(), 20);
        if (top.Count == 0)
        {
            _logger.LogWarning("Top-20 Excel export skipped: quote scan returned no active stocks.");
            return;
        }

        var nowIst = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, India);
        var from = new DateTime(nowIst.Year, nowIst.Month, nowIst.Day, 9, 15, 0, DateTimeKind.Unspecified);
        var to = new DateTime(nowIst.Year, nowIst.Month, nowIst.Day, nowIst.Hour, nowIst.Minute, 0, DateTimeKind.Unspecified);
        // CommonService globally rate-limits historical requests; use a concurrency of 3,
        // while its internal gate continues to enforce the provider's request spacing.
        var candlesByToken = await _common.DownloadCandlesInBatchesAsync(top, instruments, login.JwtToken,
            "ONE_MINUTE", from, to, batchSize: 3, batchDelay: 1);

        var outputDirectory = Path.Combine(_environment.ContentRootPath, "App_Data", "MarketResearch");
        Directory.CreateDirectory(outputDirectory);
        var filePath = Path.Combine(outputDirectory, $"Top20_OneMinute_{nowIst:yyyyMMdd_HHmmss}_IST.xlsx");

        using var workbook = new XLWorkbook();
        foreach (var quote in top)
        {
            ct.ThrowIfCancellationRequested();
            var token = quote.SymbolToken;
            candlesByToken.TryGetValue(token, out var source);
            var candles = (source ?? new List<Candle>()).Where(c => c.Close > 0 && c.High >= c.Low)
                .OrderBy(c => c.Timestamp).ToList();
            var sheetName = SafeSheetName(quote.TradingSymbol);
            var ws = workbook.Worksheets.Add(sheetName);
            WriteStockSheet(ws, candles);
        }

        workbook.SaveAs(filePath);
        AddCharts(filePath, top.Select(q => SafeSheetName(q.TradingSymbol)).ToList());
        _logger.LogInformation("Top-20 stock Excel workbook created. Stocks={StockCount}, File={FilePath}, Time={Time} IST",
            top.Count, filePath, nowIst);
    }

    private static void WriteStockSheet(IXLWorksheet ws, IReadOnlyList<Candle> candles)
    {
        var headers = new[]
        {
            "Timestamp (IST)", "Open", "High", "Low", "Close", "Volume",
            "EMA9", "EMA21", "EMA25", "VWAP", "ChoppinessIndex",
            "IsPullback", "IsPullbackRecovery", "IsCrossover",
            // 5-minute Opening Range Breakout (ORB) fields, derived from the first five 1-minute candles.
            "ORB5High", "ORB5Low", "ORB5Range", "ORB5Mid",
            "IsORB5RangeComplete", "IsAboveORB5High", "IsBelowORB5Low", "IsInsideORB5Range",
            "IsORB5BullishBreakout", "IsORB5BearishBreakout", "IsORB5Breakout", "ORB5BreakoutDirection"
        };
        for (var i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
        var closes = candles.Select(x => x.Close).ToList();
        var ema9 = Ema(closes, 9); var ema21 = Ema(closes, 21); var ema25 = Ema(closes, 25);
        decimal cumulativePv = 0, cumulativeVolume = 0;

        // NSE opening range: 09:15:00 through 09:19:59 IST (five one-minute candles).
        // The range is marked complete only when all five distinct minute candles exist.
        var openingRangeCandles = candles
            .Where(c =>
            {
                var timestamp = ToIndia(c.Timestamp);
                return timestamp.TimeOfDay >= new TimeSpan(9, 15, 0) &&
                       timestamp.TimeOfDay < new TimeSpan(9, 20, 0);
            })
            .GroupBy(c => ToIndia(c.Timestamp).Date.AddHours(ToIndia(c.Timestamp).Hour).AddMinutes(ToIndia(c.Timestamp).Minute))
            .Select(g => g.First())
            .OrderBy(c => c.Timestamp)
            .ToList();
        var orbRangeComplete = openingRangeCandles.Count == 5;
        decimal? orbHigh = orbRangeComplete ? openingRangeCandles.Max(c => c.High) : null;
        decimal? orbLow = orbRangeComplete ? openingRangeCandles.Min(c => c.Low) : null;
        decimal? orbRange = orbRangeComplete ? orbHigh!.Value - orbLow!.Value : null;
        decimal? orbMid = orbRangeComplete ? (orbHigh!.Value + orbLow!.Value) / 2m : null;

        for (var i = 0; i < candles.Count; i++)
        {
            var c = candles[i];
            cumulativePv += ((c.High + c.Low + c.Close) / 3m) * c.Volume;
            cumulativeVolume += c.Volume;
            var vwap = cumulativeVolume > 0 ? cumulativePv / cumulativeVolume : 0m;
            var pullback = i > 0 && ema9[i] > ema21[i] && c.Low <= ema9[i] && c.Close >= ema21[i];
            var recovery = i > 0 && ema9[i] > ema21[i] && candles[i - 1].Close < ema9[i - 1] && c.Close > ema9[i];
            // True on either bullish or bearish EMA9/EMA21 crossover. Equality on the
            // previous candle is treated as the start of a crossover when the spread opens.
            var isCrossover = i > 0 &&
                ((ema9[i - 1] <= ema21[i - 1] && ema9[i] > ema21[i]) ||
                 (ema9[i - 1] >= ema21[i - 1] && ema9[i] < ema21[i]));
            var row = i + 2;
            ws.Cell(row, 1).Value = ToIndia(c.Timestamp);
            ws.Cell(row, 1).Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
            ws.Cell(row, 2).Value = c.Open; ws.Cell(row, 3).Value = c.High; ws.Cell(row, 4).Value = c.Low;
            ws.Cell(row, 5).Value = c.Close; ws.Cell(row, 6).Value = c.Volume;
            ws.Cell(row, 7).Value = ema9[i]; ws.Cell(row, 8).Value = ema21[i]; ws.Cell(row, 9).Value = ema25[i];
            ws.Cell(row, 10).Value = vwap; ws.Cell(row, 11).Value = Choppiness(candles, i, 14);
            ws.Cell(row, 12).Value = pullback; ws.Cell(row, 13).Value = recovery;
            ws.Cell(row, 14).Value = isCrossover;

            var candleTimeIst = ToIndia(c.Timestamp);
            var afterOpeningRange = candleTimeIst.TimeOfDay >= new TimeSpan(9, 20, 0);
            var aboveOrbHigh = orbRangeComplete && afterOpeningRange && c.Close > orbHigh!.Value;
            var belowOrbLow = orbRangeComplete && afterOpeningRange && c.Close < orbLow!.Value;
            var insideOrb = orbRangeComplete && afterOpeningRange &&
                            c.Close >= orbLow!.Value && c.Close <= orbHigh!.Value;
            var previousClose = i > 0 ? candles[i - 1].Close : c.Close;
            var bullishOrbBreakout = orbRangeComplete && afterOpeningRange &&
                                     previousClose <= orbHigh!.Value && c.Close > orbHigh.Value;
            var bearishOrbBreakout = orbRangeComplete && afterOpeningRange &&
                                     previousClose >= orbLow!.Value && c.Close < orbLow.Value;

            if (orbRangeComplete)
            {
                ws.Cell(row, 15).Value = orbHigh!.Value;
                ws.Cell(row, 16).Value = orbLow!.Value;
                ws.Cell(row, 17).Value = orbRange!.Value;
                ws.Cell(row, 18).Value = orbMid!.Value;
            }
            ws.Cell(row, 19).Value = orbRangeComplete;
            ws.Cell(row, 20).Value = aboveOrbHigh;
            ws.Cell(row, 21).Value = belowOrbLow;
            ws.Cell(row, 22).Value = insideOrb;
            ws.Cell(row, 23).Value = bullishOrbBreakout;
            ws.Cell(row, 24).Value = bearishOrbBreakout;
            ws.Cell(row, 25).Value = bullishOrbBreakout || bearishOrbBreakout;
            ws.Cell(row, 26).Value = bullishOrbBreakout ? "Bullish" : bearishOrbBreakout ? "Bearish" : string.Empty;
        }

        var lastRow = Math.Max(2, candles.Count + 1);
        var table = ws.Range(1, 1, lastRow, headers.Length).CreateTable();
        table.Theme = XLTableTheme.TableStyleMedium2;
        ws.SheetView.FreezeRows(1);
        ws.Columns(1, headers.Length).AdjustToContents();
        ws.Column(1).Width = 21;
        if (candles.Count == 0) { ws.Cell(3, 1).Value = "No candle data available for this stock."; return; }

    }

    // ClosedXML does not expose worksheet chart APIs. Add native Excel charts in a
    // second pass with EPPlus, leaving the data/table generation in ClosedXML.
    private static void AddCharts(string filePath, IReadOnlyList<string> sheetNames)
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        using var package = new ExcelPackage(new FileInfo(filePath));
        foreach (var sheetName in sheetNames)
        {
            var ws = package.Workbook.Worksheets[sheetName];
            if (ws is null || ws.Dimension is null || ws.Dimension.End.Row < 3) continue;
            var lastRow = ws.Dimension.End.Row;

            var priceChart = ws.Drawings.AddChart("PriceIndicators", eChartType.Line);
            priceChart.Title.Text = "OHLC and indicators";
            priceChart.SetPosition(1, 0, lastRow + 2, 0);
            priceChart.SetSize(1100, 420);
            AddChartSeries(priceChart, ws, 2, lastRow, "Open");
            AddChartSeries(priceChart, ws, 3, lastRow, "High");
            AddChartSeries(priceChart, ws, 4, lastRow, "Low");
            AddChartSeries(priceChart, ws, 5, lastRow, "Close");
            AddChartSeries(priceChart, ws, 7, lastRow, "EMA9");
            AddChartSeries(priceChart, ws, 8, lastRow, "EMA21");
            AddChartSeries(priceChart, ws, 9, lastRow, "EMA25");
            AddChartSeries(priceChart, ws, 10, lastRow, "VWAP");

            var volumeChart = ws.Drawings.AddChart("VolumeChart", eChartType.Line);
            volumeChart.Title.Text = "Volume";
            volumeChart.SetPosition(1, 0, lastRow + 24, 0);
            volumeChart.SetSize(1100, 300);
            AddChartSeries(volumeChart, ws, 6, lastRow, "Volume");
        }
        package.Save();
    }

    private static void AddChartSeries(ExcelChart chart, ExcelWorksheet ws, int valueColumn, int lastRow, string name)
    {
        var column = ExcelCellAddress.GetColumnLetter(valueColumn);
        var series = chart.Series.Add(
            ws.Cells[$"{column}2:{column}{lastRow}"],
            ws.Cells[$"A2:A{lastRow}"]);
        series.Header = name;
    }

    private static List<decimal> Ema(IReadOnlyList<decimal> values, int period)
    {
        var result = new List<decimal>(values.Count); if (values.Count == 0) return result;
        var alpha = 2m / (period + 1m); decimal current = 0;
        for (var i = 0; i < values.Count; i++)
        {
            current = i == 0 ? values[i] : values[i] * alpha + current * (1m - alpha);
            result.Add(Math.Round(current, 6, MidpointRounding.AwayFromZero));
        }
        return result;
    }

    private static decimal Choppiness(IReadOnlyList<Candle> candles, int index, int period)
    {
        if (index + 1 < period) return 0;
        var slice = candles.Skip(index - period + 1).Take(period).ToList();
        decimal trSum = 0;
        for (var i = 0; i < slice.Count; i++)
        {
            var previousClose = i == 0 ? slice[i].Close : slice[i - 1].Close;
            trSum += Math.Max(slice[i].High - slice[i].Low,
                Math.Max(Math.Abs(slice[i].High - previousClose), Math.Abs(slice[i].Low - previousClose)));
        }
        var range = slice.Max(x => x.High) - slice.Min(x => x.Low);
        return range <= 0 || trSum <= 0 ? 0 : Math.Round(100m * (decimal)(Math.Log10((double)(trSum / range)) / Math.Log10(period)), 6);
    }

    private static DateTime ToIndia(DateTime timestamp)
    {
        if (timestamp.Kind == DateTimeKind.Utc) return TimeZoneInfo.ConvertTimeFromUtc(timestamp, India);
        if (timestamp.Kind == DateTimeKind.Local) return TimeZoneInfo.ConvertTime(timestamp, India);
        return DateTime.SpecifyKind(timestamp, DateTimeKind.Unspecified);
    }

    private static string SafeSheetName(string? symbol)
    {
        var name = string.IsNullOrWhiteSpace(symbol) ? "Unknown" : symbol;
        foreach (var invalid in new[] { ':', '\\', '/', '?', '*', '[', ']' }) name = name.Replace(invalid, '_');
        return name.Length > 31 ? name[..31] : name;
    }
}
