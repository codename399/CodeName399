using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Models.Configuration;
using CodeName399.Kite.API.Models.Enum;
using CodeName399.Kite.API.Models.Indicator;
using DatabaseRepository.Common.Utilities;
using DatabaseRepository.Model;
using Serilog;
using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Text;

namespace CodeName399.Kite.API.Helpers
{
    public static class EmailHelper
    {
        private static IConfiguration? _configuration;

        public static void Initialize(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private static string? GetSetting(string key, string environmentKey)
        {
            var value = _configuration?[key];
            if (!string.IsNullOrWhiteSpace(value)) return value;
            return Environment.GetEnvironmentVariable(environmentKey);
        }
        private static readonly object FailureSync = new();
        private static DateTime _lastFailureLogUtc = DateTime.MinValue;
        private static DateTime _lastMissingConfigurationLogUtc = DateTime.MinValue;

        public static bool SendEmail(
            string message,
            TradingConfiguration tradingConfiguration,
            string subject = "CodeName399 Notification",
            bool isHtml = false
        )
        {
            if (!tradingConfiguration.EnableNotification)
            {
                Log.Information("Email notification skipped because notifications are disabled. Subject={Subject}", subject);
                return false;
            }

            var smtpHost = GetSetting("SMTP_HOST", "CODENAME399_SMTP_HOST")
                ?? GetSetting("Email:SmtpHost", "CODENAME399_EMAIL__SMTPHOST");
            var smtpUser = GetSetting("SMTP_USER", "CODENAME399_SMTP_USER")
                ?? GetSetting("Email:SmtpUser", "CODENAME399_EMAIL__SMTPUSER");
            var smtpPassword = GetSetting("SMTP_PASSWORD", "CODENAME399_SMTP_PASSWORD")
                ?? GetSetting("Email:SmtpPassword", "CODENAME399_EMAIL__SMTPPASSWORD");
            var smtpTo = GetSetting("SMTP_TO", "CODENAME399_SMTP_TO")
                ?? GetSetting("Email:SmtpTo", "CODENAME399_EMAIL__SMTPTO");
            var smtpPortText = GetSetting("SMTP_PORT", "CODENAME399_SMTP_PORT")
                ?? GetSetting("Email:SmtpPort", "CODENAME399_EMAIL__SMTPPORT");
            var smtpPort = int.TryParse(smtpPortText, out var parsedPort) ? parsedPort : 587;

            if (string.IsNullOrWhiteSpace(smtpHost)
                || string.IsNullOrWhiteSpace(smtpUser)
                || string.IsNullOrWhiteSpace(smtpPassword)
                || string.IsNullOrWhiteSpace(smtpTo))
            {
                lock (FailureSync)
                {
                    var now = DateTime.UtcNow;
                    if ((now - _lastMissingConfigurationLogUtc) >= TimeSpan.FromMinutes(5))
                    {
                        _lastMissingConfigurationLogUtc = now;
                        Log.Warning(
                            "Email notification skipped because SMTP configuration is incomplete. HostConfigured={HostConfigured}, UserConfigured={UserConfigured}, PasswordConfigured={PasswordConfigured}, RecipientConfigured={RecipientConfigured}.",
                            !string.IsNullOrWhiteSpace(smtpHost),
                            !string.IsNullOrWhiteSpace(smtpUser),
                            !string.IsNullOrWhiteSpace(smtpPassword),
                            !string.IsNullOrWhiteSpace(smtpTo));
                    }
                }
                return false;
            }

            EmailNotificationConfig emailNotificationConfig = new()
            {
                Body = message,
                Subject = subject,
                To = smtpTo,
                SmtpUser = smtpUser,
                SmtpHost = smtpHost,
                SmtpPort = smtpPort,
                SmtpPassword = smtpPassword,
                IsHtml = isHtml,
            };

            try
            {
                Util.SendEmailNotification(emailNotificationConfig);
                Log.Information("Trade email notification sent. Subject={Subject}, Recipients={Recipients}", subject, smtpTo);
                return true;
            }
            catch (Exception ex)
            {
                lock (FailureSync)
                {
                    var now = DateTime.UtcNow;
                    if ((now - _lastFailureLogUtc) >= TimeSpan.FromMinutes(1))
                    {
                        _lastFailureLogUtc = now;
                        Log.Warning(ex, "Email notification failed. Trading operation continues without notification.");
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// Sends a BUY/SELL execution notification directly through SMTP.
        /// Trade notifications intentionally use the same resolved SMTP settings
        /// as the normal notification path, but do not depend on the shared
        /// utility's notification implementation. This makes execution emails
        /// deterministic and independently observable from reporting emails.
        /// </summary>
        public static bool SendTradeEmail(
            string message,
            TradingConfiguration tradingConfiguration,
            string subject,
            bool isHtml = true)
        {
            if (!tradingConfiguration.EnableNotification)
            {
                Log.Warning("Trade email skipped because EnableNotification=false. Subject={Subject}", subject);
                return false;
            }

            var smtpHost = GetSetting("SMTP_HOST", "CODENAME399_SMTP_HOST")
                ?? GetSetting("Email:SmtpHost", "CODENAME399_EMAIL__SMTPHOST");
            var smtpUser = GetSetting("SMTP_USER", "CODENAME399_SMTP_USER")
                ?? GetSetting("Email:SmtpUser", "CODENAME399_EMAIL__SMTPUSER");
            var smtpPassword = GetSetting("SMTP_PASSWORD", "CODENAME399_SMTP_PASSWORD")
                ?? GetSetting("Email:SmtpPassword", "CODENAME399_EMAIL__SMTPPASSWORD");
            var smtpTo = GetSetting("SMTP_TO", "CODENAME399_SMTP_TO")
                ?? GetSetting("Email:SmtpTo", "CODENAME399_EMAIL__SMTPTO");
            var smtpPortText = GetSetting("SMTP_PORT", "CODENAME399_SMTP_PORT")
                ?? GetSetting("Email:SmtpPort", "CODENAME399_EMAIL__SMTPPORT");
            var smtpPort = int.TryParse(smtpPortText, out var parsedPort) ? parsedPort : 587;

            if (string.IsNullOrWhiteSpace(smtpHost) ||
                string.IsNullOrWhiteSpace(smtpUser) ||
                string.IsNullOrWhiteSpace(smtpPassword) ||
                string.IsNullOrWhiteSpace(smtpTo))
            {
                Log.Error(
                    "BUY/SELL trade email could not be sent because SMTP configuration is incomplete. Subject={Subject}, HostConfigured={HostConfigured}, UserConfigured={UserConfigured}, PasswordConfigured={PasswordConfigured}, RecipientConfigured={RecipientConfigured}",
                    subject,
                    !string.IsNullOrWhiteSpace(smtpHost),
                    !string.IsNullOrWhiteSpace(smtpUser),
                    !string.IsNullOrWhiteSpace(smtpPassword),
                    !string.IsNullOrWhiteSpace(smtpTo));
                return false;
            }

            try
            {
                using var mail = new MailMessage
                {
                    From = new MailAddress(smtpUser),
                    Subject = subject,
                    Body = message,
                    IsBodyHtml = isHtml
                };

                foreach (var recipient in smtpTo.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                    mail.To.Add(recipient.Trim());

                using var client = new SmtpClient(smtpHost, smtpPort)
                {
                    EnableSsl = true,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(smtpUser, smtpPassword),
                    Timeout = 15000
                };

                Log.Information("Sending trade execution email. Subject={Subject}, Recipients={Recipients}", subject, smtpTo);
                client.Send(mail);
                Log.Information("Trade execution email sent successfully. Subject={Subject}, Recipients={Recipients}", subject, smtpTo);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Trade execution email failed. Subject={Subject}, Recipients={Recipients}", subject, smtpTo);
                return false;
            }
        }

        private static TradeIndicatorSnapshot ResolveEntryEmailIndicators(TradeOrder trade)
        {
            var candidates = new[] { trade.EntryIndicators, trade.ExitIndicators };
            foreach (var candidate in candidates)
            {
                if (candidate != null && !IsEmptyIndicatorSnapshot(candidate))
                    return candidate;
            }
            return new TradeIndicatorSnapshot();
        }

        private static TradeIndicatorSnapshot ResolveExitEmailIndicators(TradeOrder trade)
        {
            var candidates = new[] { trade.ExitIndicators, trade.EntryIndicators };
            foreach (var candidate in candidates)
            {
                if (candidate != null && !IsEmptyIndicatorSnapshot(candidate))
                    return candidate;
            }
            return new TradeIndicatorSnapshot();
        }

        private static bool IsEmptyIndicatorSnapshot(TradeIndicatorSnapshot snapshot)
            => snapshot.ATR <= 0m && snapshot.RecoveryScore <= 0m && snapshot.BreakoutStrength <= 0m && snapshot.PriceChangePercent == 0m;


        private static void HRow(StringBuilder sb, string field, object? value)
        {
            sb.Append("<tr><th>").Append(H(field)).Append("</th><td>").Append(H(FormatValue(value))).AppendLine("</td></tr>");
        }

        private static void HIRow(StringBuilder sb, string field, object? value, string purpose)
        {
            sb.Append("<tr><th>").Append(H(field)).Append("</th><td class='num'>").Append(H(FormatValue(value))).Append("</td><td>").Append(H(purpose)).AppendLine("</td></tr>");
        }

        private static void TD(StringBuilder sb, object? value)
        {
            sb.Append("<td>").Append(H(FormatValue(value))).Append("</td>");
        }

        private static string FormatValue(object? value)
        {
            if (value == null) return string.Empty;
            return value switch
            {
                decimal d => d.ToString("0.##########", CultureInfo.InvariantCulture),
                double d => d.ToString("0.##########", CultureInfo.InvariantCulture),
                float f => f.ToString("0.##########", CultureInfo.InvariantCulture),
                DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
                DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
                _ => value.ToString() ?? string.Empty
            };
        }

        private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

        private static decimal CalculateRiskReward(TradeOrder trade)
        {
            if (trade.Quantity <= 0 || trade.EntryPrice <= 0m || trade.StopLoss <= 0m)
                return 0m;

            // Use the actual stored gross reward estimate when available. The
            // current strategy uses a 1:2 risk/reward estimate for reporting;
            // this does not introduce a fixed-target exit.
            var riskPerShare = trade.Direction == TradeDirection.Short
                ? trade.StopLoss - trade.EntryPrice
                : trade.EntryPrice - trade.StopLoss;
            if (riskPerShare <= 0m)
                return 0m;

            var totalRisk = riskPerShare * trade.Quantity;
            var grossReward = trade.EstimatedGrossProfit > 0m
                ? trade.EstimatedGrossProfit
                : totalRisk * 2m;

            return totalRisk > 0m
                ? Math.Round(grossReward / totalRisk, 2, MidpointRounding.AwayFromZero)
                : 0m;
        }

        private static void CsvRow(StringBuilder sb, string field, object? value)
        {
            sb.Append(CsvValue(field)).Append(',').Append(CsvValue(value?.ToString() ?? string.Empty)).AppendLine();
        }

        private static string CsvValue(string? value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";

        private static string DecimalValue(decimal value) => value.ToString("0.##########", CultureInfo.InvariantCulture);

        public static string BuildBuyOrderEmail(TradeOrder trade, bool paperTrading = false)
        {
            var i = ResolveEntryEmailIndicators(trade);

            decimal investment = trade.EntryPrice * trade.Quantity;
            decimal risk =
                trade.Direction == TradeDirection.Short
                    ? trade.StopLoss - trade.EntryPrice
                    : trade.EntryPrice - trade.StopLoss;
            decimal rr = CalculateRiskReward(trade);

            string color = paperTrading ? "#0d6efd" : "#198754";
            string action = trade.Direction == TradeDirection.Short ? "SHORT" : "BUY";
            string title = paperTrading
                ? $"📝 PAPER {action} EXECUTED"
                : $"✅ {action} ORDER EXECUTED";

            return $$"""
<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<style>
body{
    font-family:'Segoe UI';
    background:#eef2f7;
}
table{
    border-collapse:collapse;
    width:100%;
}
td,th{
    border:1px solid #ddd;
    padding:8px;
}
.header{
    background:{{color}};
    color:white;
    padding:20px;
}
</style>
</head>

<body>

<div class="header">

<h2>{{title}}</h2>

<b>{{trade.Symbol}}</b>

<br/>

Instrument: {{trade.InstrumentType}} | Direction: {{trade.Direction}} | Entry: {{trade.EntryTransactionType}} | Exchange: {{trade.Exchange}} | Product: {{trade.ProductType}}

<br/>


</div>

<h3>Execution Details</h3>

<table>
<tr><th>Field</th><th>Value</th></tr>
<tr><td>Quantity</td><td>{{trade.Quantity}}</td></tr>
<tr><td>Entry Price (Market Fill)</td><td>₹{{trade.EntryPrice:N2}}</td></tr>
<tr><td>Stop Loss</td><td>₹{{trade.StopLoss:N2}}</td></tr>
<tr><td>Investment</td><td>₹{{investment:N2}}</td></tr>
<tr><td>Risk Reward</td><td>{{rr:F2}} : 1</td></tr>
<tr><td>Estimated Target Net Profit (after charges, 1:2 risk/reward estimate)</td><td>₹{{trade.EstimatedNetProfit:N2}}</td></tr>
<tr><td>Estimated Charges</td><td>₹{{trade.EstimatedCharges:N2}}</td></tr>
<tr><td>Mode</td><td>{{(paperTrading ? "Paper Trading" : "Live Trading")}}</td></tr>
</table>

<h3>Eligibility</h3>

<pre>

{{trade.EligibilityReason}}

</pre>

</body>

</html>
""";
        }

        public static string BuildSellOrderEmail(TradeOrder trade, bool paperTrade = false)
        {
            decimal investment = trade.EntryPrice * trade.Quantity;
            decimal grossProfit =
                (
                    trade.Direction == TradeDirection.Short
                        ? trade.EntryPrice - trade.ExitPrice
                        : trade.ExitPrice - trade.EntryPrice
                ) * trade.Quantity;
            decimal netProfit = trade.RealizedNetProfit;

            decimal returnPercent = investment > 0 ? (netProfit / investment) * 100m : 0m;

            decimal grossReturn = investment > 0 ? (grossProfit / investment) * 100m : 0m;

            decimal rr =
                trade.Direction == TradeDirection.Short
                    ? (
                        trade.StopLoss > trade.EntryPrice
                            ? (trade.EntryPrice - trade.ExitPrice)
                                / (trade.StopLoss - trade.EntryPrice)
                            : 0m
                    )
                    : (
                        trade.EntryPrice > trade.StopLoss
                            ? (trade.ExitPrice - trade.EntryPrice)
                                / (trade.EntryPrice - trade.StopLoss)
                            : 0m
                    );

            string pnlColor = netProfit >= 0 ? "#198754" : "#dc3545";

            string title = paperTrade
                ? (
                    trade.Direction == TradeDirection.Short
                        ? "📝 PAPER SHORT CLOSED"
                        : "📝 PAPER LONG CLOSED"
                )
                : (trade.Direction == TradeDirection.Short ? "💰 SHORT CLOSED" : "💰 LONG CLOSED");

            var sb = new StringBuilder();

            sb.AppendLine(
                $$"""
<!DOCTYPE html>

<html>

<head>

<meta charset="utf-8"/>

<meta name="viewport"
content="width=device-width, initial-scale=1"/>

<style>

body{
    margin:0;
    padding:15px;
    background:#eef2f7;
    font-family:Segoe UI,Arial,sans-serif;
}

.container{
    width:100%;
    max-width:700px;
    margin:0 auto;
    background:#ffffff;
    border:1px solid #dddddd;
}

.header{
    background:{{pnlColor}};
    color:#ffffff;
    padding:20px;
}

.title{
    font-size:26px;
    font-weight:bold;
}

.subtitle{
    margin-top:8px;
    font-size:13px;
}

.section{
    padding:18px;
}

.section-title{
    font-size:18px;
    font-weight:bold;
    color:#222;
    margin-bottom:10px;
    border-bottom:2px solid #eeeeee;
    padding-bottom:6px;
}

table{
    width:100%;
    border-collapse:collapse;
    table-layout:fixed;
}

th{
    background:#f7f7f7;
    border:1px solid #dddddd;
    padding:8px;
    text-align:left;
}

td{
    border:1px solid #dddddd;
    padding:8px;
    word-break:break-word;
    overflow-wrap:anywhere;
    vertical-align:top;
}

.metric{
    background:#fafafa;
    font-weight:bold;
    width:40%;
}

.green{color:#198754;}
.red{color:#dc3545;}
.blue{color:#0d6efd;}

</style>

</head>

<body>

<div class="container">

<div class="header">

<div class="title">

{{title}}

</div>

<div class="subtitle">

<b>{{trade.Symbol}}</b>

<br/>

Instrument: {{trade.InstrumentType}} | Direction: {{trade.Direction}} | Entry: {{trade.EntryTransactionType}} | Exchange: {{trade.Exchange}} | Product: {{trade.ProductType}}

<br/>


<br/>

{{(paperTrade ? "Paper Trading" : "Live Trading")}}

<br/>

{{DateTime.UtcNow:dd-MMM-yyyy hh:mm:ss tt}}

</div>

</div>

<div class="section">

<div class="section-title">

Trade Performance

</div>

<table role="presentation">

<tr>
<td class="metric">Net Profit</td>
<td style="color:{{pnlColor}};font-weight:bold;">
₹{{netProfit:N2}}
</td>
</tr>

<tr>
<td class="metric">Return</td>
<td style="color:{{pnlColor}};">
{{returnPercent:N2}}%
</td>
</tr>

<tr>
<td class="metric">Gross Profit</td>
<td>
₹{{grossProfit:N2}}
</td>
</tr>

<tr>
<td class="metric">Charges</td>
<td>
₹{{trade.EstimatedCharges:N2}}
</td>
</tr>

</table>

<br/>

<div class="section-title">

Trade Details

</div>

<table role="presentation">

<tr>
<td class="metric">Symbol</td>
<td>{{trade.Symbol}}</td>
</tr>

<tr>
<td class="metric">Quantity</td>
<td>{{trade.Quantity}}</td>
</tr>

<tr>
<td class="metric">Entry Price</td>
<td>₹{{trade.EntryPrice:N2}}</td>
</tr>

<tr>
<td class="metric">Exit Price</td>
<td>₹{{trade.ExitPrice:N2}}</td>
</tr>

<tr>
<td class="metric">Investment</td>
<td>₹{{investment:N2}}</td>
</tr>

<tr>
<td class="metric">Gross Return</td>
<td>{{grossReturn:N2}}%</td>
</tr>

<tr>
<td class="metric">Risk Reward</td>
<td>{{rr:F2}} : 1</td>
</tr>

<tr>
<td class="metric">Exit Reason</td>
<td>{{trade.ExitReason}}</td>
</tr>

<tr>
<td class="metric">Order Id</td>
<td>{{trade.ExitOrderId}}</td>
</tr>

<tr>
<td class="metric">Mode</td>
<td>{{(paperTrade ? "Paper Trading" : "Live Trading")}}</td>
</tr>

<tr>
<td class="metric">Buy Time</td>
<td>{{trade.EntryTime:dd-MMM-yyyy HH:mm:ss}}</td>
</tr>

<tr>
<td class="metric">Sell Time</td>
<td>{{trade.ExitTime:dd-MMM-yyyy HH:mm:ss}}</td>
</tr>

</table>
"""
            );

            TimeSpan holdingTime = (trade.ExitTime ?? DateTime.UtcNow) - trade.EntryTime;

            string holdingTimeText = holdingTime.ToString(@"hh\:mm\:ss");

            sb.AppendLine(
                $"""

<br/>

<div class="section-title">

Trade Lifecycle

</div>

<table role="presentation">

<tr>
<td class="metric">Highest Price</td>
<td>₹{trade.HighestPrice:N2}</td>
</tr>

<tr>
<td class="metric">Holding Time</td>
<td>{holdingTimeText}</td>
</tr>

<tr>
<td class="metric">Break Even Price</td>
<td>₹{trade.BreakEvenPrice:N2}</td>
</tr>

<tr>
<td class="metric">Minimum Exit Price</td>
<td>₹{trade.MinimumExitPrice:N2}</td>
</tr>

<tr>
<td class="metric">Trailing Stop Activated</td>
<td style="font-weight:bold;color:{(trade.TrailingActivated ? "#198754" : "#dc3545")};">
{(trade.TrailingActivated ? "YES" : "NO")}
</td>
</tr>

<tr>
<td class="metric">Final Stop Loss</td>
<td>₹{trade.StopLoss:N2}</td>
</tr>

</table>

<br/>

"""
            );

            decimal maxProfit = (trade.HighestPrice - trade.EntryPrice) * trade.Quantity;

            decimal capturedPercent = maxProfit > 0 ? (grossProfit / maxProfit) * 100m : 100m;

            decimal drawBack =
                trade.HighestPrice > 0
                    ? ((trade.HighestPrice - trade.ExitPrice) / trade.HighestPrice) * 100m
                    : 0m;

            sb.AppendLine(
                $"""

<br/>

<div class="section-title">

Trade Performance Analysis

</div>

<table role="presentation">

<tr>
<th style="width:40%;">Metric</th>
<th>Value</th>
</tr>

<tr>
<td>Gross Profit</td>
<td style="color:{(grossProfit >= 0 ? "#198754" : "#dc3545")};font-weight:bold;">
₹{grossProfit:N2}
</td>
</tr>

<tr>
<td>Net Profit</td>
<td style="color:{pnlColor};font-weight:bold;">
₹{netProfit:N2}
</td>
</tr>

<tr>
<td>Return</td>
<td style="color:{pnlColor};">
{returnPercent:N2}%
</td>
</tr>

<tr>
<td>Maximum Profit Available</td>
<td>
₹{maxProfit:N2}
</td>
</tr>

<tr>
<td>Highest Price Reached</td>
<td>
₹{trade.HighestPrice:N2}
</td>
</tr>

<tr>
<td>Profit Captured</td>
<td>
{capturedPercent:N2}%
</td>
</tr>

<tr>
<td>Profit Giveback Before Exit</td>
<td>
{drawBack:N2}%
</td>
</tr>

<tr>
<td>Exit Quality</td>
<td>
{(capturedPercent >= 90 ? "Excellent Exit"
            : capturedPercent >= 75 ? "Very Good Exit"
            : capturedPercent >= 60 ? "Good Exit"
            : "Exited Early")}
</td>
</tr>

<tr>
<td>Profit Giveback Assessment</td>
<td>
{(drawBack <= 0.5m ? "Minimal"
            : drawBack <= 2m ? "Healthy"
            : "Large Giveback")}
</td>
</tr>

<tr>
<td>Exit Reason</td>
<td>
<b>{trade.ExitReason}</b>
</td>
</tr>

</table>

<br/>

<div class="section-title">

Trade Summary

</div>

<table role="presentation">

<tr>
<td class="metric">Investment</td>
<td>₹{investment:N2}</td>
</tr>

<tr>
<td class="metric">Gross Profit</td>
<td>₹{grossProfit:N2}</td>
</tr>

<tr>
<td class="metric">Charges</td>
<td>₹{trade.EstimatedCharges:N2}</td>
</tr>

<tr>
<td class="metric">Net Profit</td>
<td style="font-size:18px;font-weight:bold;color:{pnlColor};">
₹{netProfit:N2}
</td>
</tr>

<tr>
<td class="metric">Return</td>
<td style="font-weight:bold;color:{pnlColor};">
{returnPercent:N2}%
</td>
</tr>

</table>

<br/>


<div style="
background:#1f2937;
color:#ffffff;
padding:18px;
text-align:center;
font-size:13px;">

<div style="
font-size:18px;
font-weight:bold;
margin-bottom:8px;">

CodeName399 Trading Bot

</div>

<div>

Automated Trade Closing Report

</div>

<br/>

Generated on

<b>

{DateTime.UtcNow:dddd, dd MMMM yyyy HH:mm:ss}

</b>

</div>

</div>

</body>

</html>

"""
            );

            return sb.ToString();
        }

        public static string BuildBrokerReconciliationEmail(
            TradeOrder trade,
            bool recoveredPosition
        )
        {
            var title = recoveredPosition
                ? "⚠️ BROKER POSITION RECOVERED"
                : "⚠️ POSITION CLOSED DURING RECONCILIATION";

            var color = recoveredPosition ? "#0d6efd" : "#dc3545";

            var message = recoveredPosition
                ? "A position existed at the broker but was missing from the database. The bot has recreated it and will continue managing Stop Loss."
                : "The position no longer existed at the broker. The bot has marked the trade as closed during reconciliation.";

            return $"""
<html>

<body style="font-family:Arial;background:#f5f5f5;padding:20px;">

<div style="max-width:750px;background:white;border-radius:8px;padding:20px;border:1px solid #ddd;">

<h2 style="color:{color};">
{title}
</h2>

<table cellpadding="8" cellspacing="0" border="1"
style="border-collapse:collapse;width:100%;">

<tr>
    <td><b>Symbol</b></td>
    <td>{trade.Symbol}</td>
</tr>

<tr>
    <td><b>Quantity</b></td>
    <td>{trade.Quantity}</td>
</tr>

<tr>
    <td><b>Entry Price</b></td>
    <td>₹{trade.EntryPrice:N2}</td>
</tr>

<tr>
    <td><b>Highest Price</b></td>
    <td>₹{trade.HighestPrice:N2}</td>
</tr>

<tr>
    <td><b>Stop Loss</b></td>
    <td>₹{trade.StopLoss:N2}</td>
</tr>


<tr>
    <td><b>Buy Order Id</b></td>
    <td>{trade.BuyOrderId}</td>
</tr>

<tr>
    <td><b>Break Even</b></td>
    <td>₹{trade.BreakEvenPrice:N2}</td>
</tr>

<tr>
    <td><b>Minimum Exit</b></td>
    <td>₹{trade.MinimumExitPrice:N2}</td>
</tr>

<tr>
    <td><b>Time</b></td>
    <td>{DateTime.UtcNow:dd-MMM-yyyy hh:mm:ss tt}</td>
</tr>

</table>

<br/>

<div style="background:#eef4ff;padding:12px;border-left:5px solid {color};">

{message}

</div>

</div>

</body>

</html>
""";
        }

        public static string BuildDailySummaryEmail(DailyTradingSummary summary)
        {
            var sb = new StringBuilder();

            string pnlColor = summary.TotalPnL >= 0 ? "#198754" : "#dc3545";

            sb.AppendLine(
                $$"""
<!DOCTYPE html>

<html>

<head>

<meta charset="utf-8"/>

<meta name="viewport"
content="width=device-width, initial-scale=1"/>

<style>

body{
    margin:0;
    padding:0;
    background:#f4f6f9;
    font-family:Segoe UI,Arial,sans-serif;
}

table{
    border-collapse:collapse;
}

td{
    word-break:break-word;
}

</style>

</head>

<body
style="
margin:0;
padding:0;
background:#f4f6f9;">

<table
role="presentation"
width="100%"
cellpadding="0"
cellspacing="0"
border="0"
style="background:#f4f6f9;">

<tr>

<td align="center"
style="padding:20px;">

<table
role="presentation"
width="700"
cellpadding="0"
cellspacing="0"
border="0"
style="
width:700px;
max-width:700px;
background:#ffffff;
border:1px solid #dddddd;">

<tr>

<td style="padding:20px;">

<h2 style="
margin:0;
color:#0d6efd;">

📈 Daily Trading Summary

</h2>

<br/>

<div style="
font-size:15px;
margin-bottom:20px;">

<b>Date :</b>

{{summary.Date:dd-MMM-yyyy}}

</div>

<h3 style="
margin-bottom:12px;">

Portfolio Summary

</h3>

<table
role="presentation"
width="100%"
cellpadding="8"
cellspacing="0"
border="1"
style="
width:100%;
border-collapse:collapse;
table-layout:fixed;">

<tr
style="background:#f8f9fa;">

<th align="left">
Metric
</th>

<th align="right">
Value
</th>

</tr>

<tr>

<td>
Opening Balance
</td>

<td align="right">
₹{{summary.OpeningBalance:N2}}
</td>

</tr>

<tr>

<td>
Closing Balance
</td>

<td align="right">
₹{{summary.ClosingBalance:N2}}
</td>

</tr>

<tr>

<td>
Total Investment
</td>

<td align="right">
₹{{summary.TotalInvestment:N2}}
</td>

</tr>

<tr>

<td>
Gross Profit
</td>

<td align="right">
₹{{summary.GrossProfit:N2}}
</td>

</tr>

<tr>

<td>
Total Charges
</td>

<td align="right">
₹{{summary.TotalCharges:N2}}
</td>

</tr>

<tr style="background:#eef8f0;">

<td>

<b>Net Profit</b>

</td>

<td
align="right"
style="
color:{{pnlColor}};
font-weight:bold;">

₹{{summary.TotalPnL:N2}}

</td>

</tr>

<tr>

<td>

ROI

</td>

<td align="right">

{{summary.Roi:N2}}%

</td>

</tr>

</table>

<br/>

<h3>

Trading Statistics

</h3>

<table
role="presentation"
width="100%"
cellpadding="8"
cellspacing="0"
border="1"
style="
width:100%;
border-collapse:collapse;
table-layout:fixed;">

<tr
style="background:#f8f9fa;">

<th align="left">

Statistic

</th>

<th align="right">

Value

</th>

</tr>

<tr>

<td>Total Trades</td>

<td align="right">

{{summary.TotalTrades}}

</td>

</tr>

<tr>

<td>Winning Trades</td>

<td
align="right"
style="color:#198754;">

{{summary.WinningTrades}}

</td>

</tr>

<tr>

<td>Losing Trades</td>

<td
align="right"
style="color:#dc3545;">

{{summary.LosingTrades}}

</td>

</tr>

<tr>

<td>Win Rate</td>

<td align="right">

{{summary.WinRate:N2}}%

</td>

</tr>

</table>

<br/>

<h3>

Trades Executed

</h3>

<table
role="presentation"
width="100%"
cellpadding="6"
cellspacing="0"
border="1"
style="
width:100%;
border-collapse:collapse;
table-layout:fixed;
font-size:13px;">

<tr
style="
background:#0d6efd;
color:white;">

<th>Symbol</th>
<th>Qty</th>
<th>Buy</th>
<th>Sell</th>
<th>Net</th>
<th>Exit Reason</th>

</tr>
"""
            );

            foreach (var trade in summary.Trades)
            {
                string color = trade.ProfitLoss >= 0 ? "#198754" : "#dc3545";

                sb.AppendLine(
                    $$"""

<tr>

<td>

{{trade.Symbol}}

</td>

<td align="center">

{{trade.Quantity}}

</td>

<td align="right">

₹{{trade.EntryPrice:N2}}

</td>

<td align="right">

₹{{trade.ExitPrice:N2}}

</td>

<td
align="right"
style="
font-weight:bold;
color:{{color}};">

₹{{trade.ProfitLoss:N2}}

</td>

<td>

{{trade.ExitReason}}

</td>

</tr>

"""
                );
            }

            sb.AppendLine(
                """

</table>

<br/>

<table
role="presentation"
width="100%"
cellpadding="0"
cellspacing="0"
border="0"
style="
width:100%;
table-layout:fixed;">

<tr>

"""
            );

            if (summary.BestTrade != null)
            {
                sb.AppendLine(
                    $$"""

<td
width="50%"
valign="top"
style="padding-right:8px;">

<table
role="presentation"
width="100%"
cellpadding="10"
cellspacing="0"
border="0"
style="
width:100%;
background:#eafaf1;
border-left:5px solid #198754;">

<tr>

<td>

<h3
style="
margin-top:0;
color:#198754;">

🏆 Best Trade

</h3>

<table
role="presentation"
width="100%"
cellpadding="6"
cellspacing="0"
border="1"
style="
width:100%;
border-collapse:collapse;">

<tr>

<td>

Symbol

</td>

<td align="right">

{{summary.BestTrade.Symbol}}

</td>

</tr>

<tr>

<td>

Net Profit

</td>

<td
align="right"
style="
color:#198754;
font-weight:bold;">

₹{{summary.BestTrade.ProfitLoss:N2}}

</td>

</tr>

<tr>

<td>

Return

</td>

<td align="right">

{{summary.BestTrade.ProfitLossPercent:N2}}%

</td>

</tr>

<tr>

<td>

Charges

</td>

<td align="right">

₹{{summary.BestTrade.Charges:N2}}

</td>

</tr>

</table>

</td>

</tr>

</table>

</td>

"""
                );
            }

            if (summary.WorstTrade != null)
            {
                sb.AppendLine(
                    $$"""

<td
width="50%"
valign="top"
style="padding-left:8px;">

<table
role="presentation"
width="100%"
cellpadding="10"
cellspacing="0"
border="0"
style="
width:100%;
background:#fff2f2;
border-left:5px solid #dc3545;">

<tr>

<td>

<h3
style="
margin-top:0;
color:#dc3545;">

📉 Worst Trade

</h3>

<table
role="presentation"
width="100%"
cellpadding="6"
cellspacing="0"
border="1"
style="
width:100%;
border-collapse:collapse;">

<tr>

<td>

Symbol

</td>

<td align="right">

{{summary.WorstTrade.Symbol}}

</td>

</tr>

<tr>

<td>

Net Profit

</td>

<td
align="right"
style="
color:#dc3545;
font-weight:bold;">

₹{{summary.WorstTrade.ProfitLoss:N2}}

</td>

</tr>

<tr>

<td>

Return

</td>

<td align="right">

{{summary.WorstTrade.ProfitLossPercent:N2}}%

</td>

</tr>

<tr>

<td>

Charges

</td>

<td align="right">

₹{{summary.WorstTrade.Charges:N2}}

</td>

</tr>

</table>

</td>

</tr>

</table>

</td>

"""
                );
            }

            sb.AppendLine(
                """

</tr>

</table>

<br/>

"""
            );

            sb.AppendLine(
                $$"""

<table
role="presentation"
width="100%"
cellpadding="0"
cellspacing="0"
border="0"
style="
width:100%;
background:#f8f9fa;
border-top:1px solid #dddddd;">

<tr>

<td style="padding:18px;">

<table
role="presentation"
width="100%"
cellpadding="4"
cellspacing="0"
border="0"
style="width:100%;">

<tr>

<td style="font-size:13px;color:#666666;">

Generated On

</td>

<td
align="right"
style="font-size:13px;">

<b>

{{DateTime.UtcNow:dd-MMM-yyyy hh:mm:ss tt}}

</b>

</td>

</tr>

<tr>

<td style="font-size:13px;">

Gross Profit

</td>

<td align="right">

₹{{summary.GrossProfit:N2}}

</td>

</tr>

<tr>

<td style="font-size:13px;">

Total Charges

</td>

<td align="right">

₹{{summary.TotalCharges:N2}}

</td>

</tr>

<tr>

<td
style="
font-size:15px;
font-weight:bold;">

Net Profit

</td>

<td
align="right"
style="
font-size:16px;
font-weight:bold;
color:{{pnlColor}};">

₹{{summary.TotalPnL:N2}}

</td>

</tr>

<tr>

<td>

ROI

</td>

<td
align="right"
style="
font-weight:bold;
color:{{pnlColor}};">

{{summary.Roi:N2}}%

</td>

</tr>

<tr>

<td>

Winning Trades

</td>

<td
align="right"
style="
font-weight:bold;
color:#198754;">

{{summary.WinningTrades}}

</td>

</tr>

<tr>

<td>

Losing Trades

</td>

<td
align="right"
style="
font-weight:bold;
color:#dc3545;">

{{summary.LosingTrades}}

</td>

</tr>

<tr>

<td>

Win Rate

</td>

<td
align="right"
style="
font-weight:bold;">

{{summary.WinRate:N2}}%

</td>

</tr>

</table>

<br/>

<div style="
text-align:center;
font-size:18px;
font-weight:bold;
color:#0d6efd;">

CodeName399 Trading Bot

</div>

<div style="
text-align:center;
font-size:13px;
color:#777777;
margin-top:6px;">

Daily Trading Summary Report

</div>

</td>

</tr>

</table>

</td>

</tr>

</table>

</td>

</tr>

</table>

</body>

</html>

"""
            );

            return sb.ToString();
        }
    }
}
