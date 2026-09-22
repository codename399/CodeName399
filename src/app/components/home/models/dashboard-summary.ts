export interface DashboardSummary {
  availableCash: number;

  autoTradingEnabled: boolean;

  paperTrading: boolean;

  dailyTrades: number;

  dailyLoss: number;

  killSwitch: boolean;

  marketStatus: string;
}
