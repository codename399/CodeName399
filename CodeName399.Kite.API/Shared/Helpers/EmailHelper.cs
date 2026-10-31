using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Models.Configuration;
using CodeName399.Kite.API.Models.Enum;
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

        public static string BuildBuyOrderEmail(TradeOrder trade, bool autoTrading = false)
        {
            ArgumentNullException.ThrowIfNull(trade);

            var isPaper = string.Equals(
                trade.ExecutionMode,
                TradeExecutionMode.Paper.ToString(),
                StringComparison.OrdinalIgnoreCase);
            var color = isPaper ? "#0d6efd" : "#198754";
            var title = isPaper ? "PAPER BUY ORDER EXECUTED" : "BUY ORDER EXECUTED";
            var rows = new StringBuilder();

            // Reflect over TradeOrder so every persisted/public TradeOrder field is included,
            // including inherited audit properties, without maintaining a fragile field list.
            foreach (var property in typeof(TradeOrder).GetProperties(
                         System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
                     .OrderBy(p => p.MetadataToken))
            {
                if (!property.CanRead || property.GetIndexParameters().Length != 0)
                    continue;

                object? value;
                try
                {
                    value = property.GetValue(trade);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Unable to read TradeOrder property for buy email. Property={Property}", property.Name);
                    value = "<unavailable>";
                }

                rows.Append("<tr style=\\"background:")
                    .Append(tableOutcomeColor)
                    .Append(";\\"><th>")
                    .Append(H(property.Name))
                    .Append("</th><td>")
                    .Append(H(FormatValue(value)))
                    .AppendLine("</td></tr>");
            }

            return $$"""
<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<style>
body { font-family: Arial, Helvetica, sans-serif; background:#eef2f7; color:#1f2937; margin:0; padding:20px; }
.container { max-width:760px; margin:0 auto; background:#fff; border-radius:8px; overflow:hidden; }
.header { background:{{color}}; color:#fff; padding:20px 24px; }
.header h2 { margin:0 0 6px; }
.header p { margin:0; opacity:.95; }
table { border-collapse:collapse; width:100%; }
th, td { border:1px solid #e5e7eb; padding:10px 12px; text-align:left; vertical-align:top; overflow-wrap:anywhere; }
th { width:38%; background:transparent; font-weight:600; }
thead th { background:#0d6efd; color:#fff; }
.content { padding:20px 24px 24px; }
</style>
</head>
<body>
<div class="container">
  <div class="header">
    <h2>{{title}}</h2>
    <p>{{H(trade.Symbol)}} · {{H(trade.Exchange)}} · {{H(trade.ExecutionMode)}}</p>
  </div>
  <div class="content">
    <h3>Trade Order Details</h3>
    <table>
      <thead><tr><th>Field</th><th>Value</th></tr></thead>
      <tbody>
        {{rows}}
      </tbody>
    </table>
  </div>
</div>
</body>
</html>
""";
        }

        public static string BuildSellOrderEmail(TradeOrder trade, bool autoTrading = false)
        {
            ArgumentNullException.ThrowIfNull(trade);

            var isPaper = string.Equals(
                trade.ExecutionMode,
                TradeExecutionMode.Paper.ToString(),
                StringComparison.OrdinalIgnoreCase);
            var color = "#0d6efd";
            var tableOutcomeColor = trade.NetProfit > 0m
                ? "#d1e7dd" // Net profitable after charges.
                : trade.GrossProfit > 0m
                    ? "#fff3cd" // Gross profit, but charges consumed the profit.
                    : "#f8d7da"; // Gross loss.
            var title = isPaper ? "PAPER LONG CLOSED" : "LONG CLOSED";
            var rows = new StringBuilder();

            // Include every readable public TradeOrder property, including inherited audit fields.
            foreach (var property in typeof(TradeOrder).GetProperties(
                         System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
                     .OrderBy(p => p.MetadataToken))
            {
                if (!property.CanRead || property.GetIndexParameters().Length != 0)
                    continue;

                object? value;
                try
                {
                    value = property.GetValue(trade);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Unable to read TradeOrder property for sell email. Property={Property}", property.Name);
                    value = "<unavailable>";
                }

                rows.Append("<tr style=\\"background:")
                    .Append(tableOutcomeColor)
                    .Append(";\\"><th>")
                    .Append(H(property.Name))
                    .Append("</th><td>")
                    .Append(H(FormatValue(value)))
                    .AppendLine("</td></tr>");
            }

            return $$"""
<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<style>
body { font-family: Arial, Helvetica, sans-serif; background:#eef2f7; color:#1f2937; margin:0; padding:20px; }
.container { max-width:760px; margin:0 auto; background:#fff; border-radius:8px; overflow:hidden; }
.header { background:{{color}}; color:#fff; padding:20px 24px; }
.header h2 { margin:0 0 6px; }
.header p { margin:0; opacity:.95; }
table { border-collapse:collapse; width:100%; }
th, td { border:1px solid #e5e7eb; padding:10px 12px; text-align:left; vertical-align:top; overflow-wrap:anywhere; }
th { width:38%; background:transparent; font-weight:600; }
thead th { background:#0d6efd; color:#fff; }
.content { padding:20px 24px 24px; }
</style>
</head>
<body>
<div class="container">
  <div class="header">
    <h2>{{title}}</h2>
    <p>{{H(trade.Symbol)}} · {{H(trade.Exchange)}} · {{H(trade.ExecutionMode)}}</p>
  </div>
  <div class="content">
    <h3>Trade Order Details</h3>
    <table>
      <thead><tr><th>Field</th><th>Value</th></tr></thead>
      <tbody>
        {{rows}}
      </tbody>
    </table>
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
