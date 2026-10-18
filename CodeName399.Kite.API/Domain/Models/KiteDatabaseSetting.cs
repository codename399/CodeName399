using DatabaseRespository.Model;

namespace CodeName399.Model
{
    public class KiteDatabaseSetting : MongoDatabaseSetting
    {
        public string? TradeOrderCollection { get; set; }
        public string? TradingConfigurationCollection { get; set; }
        public string? TradingKeyCollection { get; set; }
    }
}
