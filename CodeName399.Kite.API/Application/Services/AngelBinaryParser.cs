using CodeName399.Kite.API.Models;
using System.Buffers.Binary;
using System.Text;

namespace CodeName399.Kite.API.Services;

public class AngelBinaryParser
{
    public MarketTick? Parse(ReadOnlySpan<byte> packet)
    {
        try
        {
            if (packet.Length < 51)
                return null;

            byte mode = packet[0];
            if (mode == 0)
                return null;

            var tick = new MarketTick
            {
                MarketDataMode = mode,
                ExchangeType = packet[1],
                SymbolToken = ReadToken(packet),
                SequenceNumber = ReadInt64(packet, 27),
                ExchangeTime = DateTimeOffset.FromUnixTimeMilliseconds(ReadInt64(packet, 35)).UtcDateTime,
                Ltp = ReadInt64(packet, 43) / 100m
            };

            switch (mode)
            {
                case 2:
                case 3:
                    ParseQuote(packet, tick);
                    break;
                case 4:
                    ParseDepth(packet, tick);
                    break;
            }

            return tick;
        }
        catch
        {
            return null;
        }
    }

    private static void ParseQuote(ReadOnlySpan<byte> packet, MarketTick tick)
    {
        if (packet.Length < 123)
            return;

        tick.LastTradedQuantity = ReadInt64(packet, 51);
        tick.AveragePrice = ReadInt64(packet, 59) / 100m;
        tick.DayVolume = ReadInt64(packet, 67);
        tick.Volume = tick.DayVolume;
        tick.TotalBuyQuantity = BitConverter.Int64BitsToDouble(ReadInt64(packet, 75));
        tick.TotalSellQuantity = BitConverter.Int64BitsToDouble(ReadInt64(packet, 83));
        tick.Open = ReadInt64(packet, 91) / 100m;
        tick.High = ReadInt64(packet, 99) / 100m;
        tick.Low = ReadInt64(packet, 107) / 100m;
        tick.Close = ReadInt64(packet, 115) / 100m;
        tick.PreviousClose = tick.Close;

        // SNAP_QUOTE carries five buy and five sell levels starting at byte 147.
        if (packet.Length >= 347)
            ParseFiveLevelDepth(packet, tick, 147, 10);
    }

    private static void ParseDepth(ReadOnlySpan<byte> packet, MarketTick tick)
    {
        // Preserve the broker's depth packet in structured form where the standard
        // 20-byte depth records are present. We keep the first five levels on each
        // side because those are sufficient for the market-data model.
        if (packet.Length > 43)
            ParseFiveLevelDepth(packet, tick, 43, Math.Min(20, (packet.Length - 43) / 20));
    }

    private static void ParseFiveLevelDepth(ReadOnlySpan<byte> packet, MarketTick tick, int start, int count)
    {
        for (var i = 0; i < count && start + ((i + 1) * 20) <= packet.Length; i++)
        {
            var offset = start + (i * 20);
            var flag = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(offset, 2));
            var quantity = ReadInt64(packet, offset + 2);
            var price = ReadInt64(packet, offset + 10) / 100m;
            var orderCount = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(offset + 18, 2));

            if (price <= 0 || quantity < 0)
                continue;

            var level = new MarketDepthLevel
            {
                Price = price,
                Quantity = quantity,
                OrderCount = orderCount
            };

            // SmartAPI SNAP_QUOTE best-five payloads are returned with the
            // buy/sell lists swapped at the packet level. The official SDK
            // compensates for this before exposing best_5_buy_data/sell_data.
            // Keep MarketTick semantics conventional: Bid = buy, Ask = sell.
            if (flag == 0)
            {
                if (tick.AskDepth.Count < 5) tick.AskDepth.Add(level);
            }
            else
            {
                if (tick.BidDepth.Count < 5) tick.BidDepth.Add(level);
            }
        }

        if (tick.BidDepth.Count > 0) tick.Bid = tick.BidDepth.Max(x => x.Price);
        if (tick.AskDepth.Count > 0) tick.Ask = tick.AskDepth.Min(x => x.Price);
    }

    private static long ReadInt64(ReadOnlySpan<byte> packet, int offset)
        => packet.Length < offset + 8 ? 0 : BinaryPrimitives.ReadInt64LittleEndian(packet.Slice(offset, 8));

    private static string ReadToken(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 27) return string.Empty;
        return Encoding.ASCII.GetString(packet.Slice(2, 25)).TrimEnd('\0').Trim();
    }
}
