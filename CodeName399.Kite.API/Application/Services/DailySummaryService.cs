using CodeName399.Kite.API.Repository;
using CodeName399.Kite.API.Helpers;
using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Services.Interface;
using CodeName399.Kite.API.Models.Enum;

namespace CodeName399.Kite.API.Services
{
    public class DailySummaryService : IDailySummaryService
    {
        private readonly IAngelOneService _angel;
        private readonly TradingSettingsProvider _settings;
        private readonly ILogger<DailySummaryService> _logger;
        private readonly TradeCloseHistoryStore _closeHistory;
        private readonly TradeManager _tradeManager;

        public DailySummaryService(
            IAngelOneService angel,
            TradingSettingsProvider settings,
            ILogger<DailySummaryService> logger,
            TradeCloseHistoryStore closeHistory,
            TradeManager tradeManager
        )
        {
            _angel = angel;
            _settings = settings;
            _logger = logger;
            _closeHistory = closeHistory;
            _tradeManager = tradeManager;
        }

        public async Task<DailyTradingSummary> GetDailySummaryAsync(DateOnly date)
        {
            var ist = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
            var startUtc = TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue), ist);
            var endUtc = TimeZoneInfo.ConvertTimeToUtc(date.AddDays(1).ToDateTime(TimeOnly.MinValue), ist);

            var closedTrades = await _closeHistory.GetAsync(startUtc, endUtc);
            var openTrades = date == DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ist))
                ? _tradeManager.GetOpen().Where(x => x.InstrumentType == InstrumentType.Equity && !x.Closed).ToList()
                : [];

            var summary = BuildSummary(date, closedTrades, openTrades);
            return summary;
        }

        private static DailyTradingSummary BuildSummary(DateOnly date, List<TradeOrder> trades, List<TradeOrder> openTrades)
        {
            var summary = new DailyTradingSummary { Date = date, TotalTrades = trades.Count };

            foreach (var trade in trades)
            {
                var investment = trade.EntryPrice * trade.Quantity;
                var gross = trade.RealizedGrossProfit;
                var charges = trade.RealizedCharges > 0 ? trade.RealizedCharges : trade.EstimatedCharges;
                var net = trade.RealizedNetProfit != 0 || gross != 0 ? trade.RealizedNetProfit : gross - charges;
                if (net >= 0) summary.WinningTrades++; else summary.LosingTrades++;
                summary.GrossProfit += Math.Max(0, gross);
                summary.GrossLoss += Math.Min(0, gross);
                summary.TotalCharges += charges;
                summary.TotalPnL += net;
                summary.TotalInvestment += investment;
                summary.Trades.Add(ToDailyTrade(trade, net, charges, false));

                if (trade.EntryDecision.Contains("PREDICTION_BUY", StringComparison.OrdinalIgnoreCase)) summary.PredictionBuySignals++;
                if ((trade.ExitCategory + " " + trade.ExitReason).Contains("PREDICTION", StringComparison.OrdinalIgnoreCase)) summary.PredictionDrivenExits++;
                else summary.SafetyExits++;
            }

            foreach (var trade in openTrades)
            {
                var current = trade.CurrentPrice > 0 ? trade.CurrentPrice : trade.EntryPrice;
                var unrealized = (current - trade.EntryPrice) * trade.Quantity;
                summary.UnrealizedPnL += unrealized;
                summary.OpenTrades.Add(ToDailyTrade(trade, unrealized, trade.EstimatedCharges, true));
            }

            summary.WinRate = summary.TotalTrades == 0 ? 0 : summary.WinningTrades * 100m / summary.TotalTrades;
            summary.Roi = summary.TotalInvestment == 0 ? 0 : summary.TotalPnL * 100m / summary.TotalInvestment;
            summary.NetPnlIncludingOpen = summary.TotalPnL + summary.UnrealizedPnL;
            summary.BestTrade = summary.Trades.OrderByDescending(x => x.ProfitLoss).FirstOrDefault();
            summary.WorstTrade = summary.Trades.OrderBy(x => x.ProfitLoss).FirstOrDefault();
            return summary;
        }

        private static DailyTradeSummary ToDailyTrade(TradeOrder trade, decimal pnl, decimal charges, bool open) => new()
        {
            TradeId = trade.Id,
            Symbol = trade.Symbol,
            Quantity = trade.Quantity,
            EntryPrice = trade.EntryPrice,
            ExitPrice = trade.ExitPrice,
            Investment = trade.EntryPrice * trade.Quantity,
            Charges = charges,
            GrossProfit = trade.RealizedGrossProfit,
            ProfitLoss = pnl,
            ProfitLossPercent = trade.EntryPrice * trade.Quantity == 0 ? 0 : pnl * 100m / (trade.EntryPrice * trade.Quantity),
            ExitReason = trade.ExitReason,
            ExitCategory = trade.ExitCategory,
            ExitTrigger = trade.ExitTrigger,
            EntryDecision = trade.EntryDecision,
            EntryReason = trade.EntryReason,
            EntryPredictionProbability = trade.EntryPredictionProbability,
            ExitPredictionProbability = trade.ExitPredictionProbability,
            PredictionModelVersion = trade.PredictionModelVersion,
            EntryTime = trade.EntryTime,
            ExitTime = trade.ExitTime ?? DateTime.MinValue,
            IsOpen = open
        };

        public async Task<bool> SendDailySummaryAsync()
        {
            try
            {
                var ist = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

                var nowIst = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ist);

                var todayIst = nowIst.Date;

                if (_settings.Current.LastDailySummarySent == DateOnly.FromDateTime(todayIst))
                    return true;

                var startUtc = TimeZoneInfo.ConvertTimeToUtc(todayIst, ist);
                var endUtc = TimeZoneInfo.ConvertTimeToUtc(todayIst.AddDays(1), ist);

                _logger.LogInformation(
                    "Generating daily summary. IST={NowIst}, UTC Range={StartUtc} -> {EndUtc}",
                    nowIst,
                    startUtc,
                    endUtc
                );

                var trades = await _closeHistory.GetAsync(startUtc, endUtc);

                decimal totalInvestment = 0;
                decimal grossProfit = 0;
                decimal totalCharges = 0;
                decimal netProfit = 0;

                int winningTrades = 0;
                int losingTrades = 0;

                foreach (var trade in trades)
                {
                    decimal investment = trade.EntryPrice * trade.Quantity;

                    decimal sellValue = trade.ExitPrice * trade.Quantity;

                    decimal gross = sellValue - investment;

                    decimal charges = trade.EstimatedCharges;

                    decimal net = gross - charges;

                    totalInvestment += investment;
                    grossProfit += gross;
                    totalCharges += charges;
                    netProfit += net;

                    if (net >= 0)
                        winningTrades++;
                    else
                        losingTrades++;
                }

                decimal closingBalance = 0;

                try
                {
                    closingBalance = await _angel.GetAvailableBalanceAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Unable to fetch available balance.");
                }

                decimal openingBalance = closingBalance - netProfit;

                var summary = new DailyTradingSummary
                {
                    Date = DateOnly.FromDateTime(todayIst),

                    OpeningBalance = openingBalance,

                    ClosingBalance = closingBalance,

                    TotalInvestment = totalInvestment,

                    GrossProfit = grossProfit,

                    TotalCharges = totalCharges,

                    TotalPnL = netProfit,

                    Roi = totalInvestment == 0 ? 0 : netProfit * 100 / totalInvestment,

                    TotalTrades = trades.Count,

                    WinningTrades = winningTrades,

                    LosingTrades = losingTrades,

                    WinRate = trades.Count == 0 ? 0 : winningTrades * 100m / trades.Count,
                };

                foreach (var trade in trades)
                {
                    decimal investment = trade.EntryPrice * trade.Quantity;

                    decimal sellValue = trade.ExitPrice * trade.Quantity;

                    decimal gross = sellValue - investment;

                    decimal charges = trade.EstimatedCharges;

                    decimal net = gross - charges;

                    summary.Trades.Add(
                        new DailyTradeSummary
                        {
                            Symbol = trade.Symbol,

                            Quantity = trade.Quantity,

                            EntryPrice = trade.EntryPrice,

                            ExitPrice = trade.ExitPrice,

                            Investment = investment,

                            Charges = charges,

                            GrossProfit = gross,

                            ProfitLoss = net,

                            ProfitLossPercent = investment == 0 ? 0 : net * 100 / investment,

                            ExitReason = trade.ExitReason,

                            EntryTime = trade.EntryTime,

                            ExitTime = trade.ExitTime ?? DateTime.MinValue,
                        }
                    );
                }

                summary.BestTrade = summary
                    .Trades.OrderByDescending(x => x.ProfitLoss)
                    .FirstOrDefault();

                summary.WorstTrade = summary.Trades.OrderBy(x => x.ProfitLoss).FirstOrDefault();

                var emailSent = EmailHelper.SendEmail(
                    EmailHelper.BuildDailySummaryEmail(summary),
                    _settings.Current,
                    $"Daily Trading Summary - {todayIst:dd-MMM-yyyy}",
                    true
                );

                if (!emailSent)
                {
                    _logger.LogWarning(
                        "Daily summary email was not sent. LastDailySummarySent will not be updated so the scheduler can retry. Trades={Trades}",
                        trades.Count
                    );
                    return false;
                }

                _settings.Current.LastDailySummarySent = DateOnly.FromDateTime(todayIst);

                await _settings.UpdateAsync(_settings.Current);

                _logger.LogInformation(
                    "Daily summary email sent successfully. Trades={Trades}",
                    trades.Count
                );

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate/send daily summary. The scheduler will retry.");
                return false;
            }
        }
    }
}
