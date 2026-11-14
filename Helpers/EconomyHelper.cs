using System.Collections.Generic;
using System.Text;
using ATS_API.Helpers;
using Eremite;
using Eremite.Model;

namespace ATS_ImperialLogistics.Helpers
{
    public readonly struct GoodCost
    {
        public readonly string GoodName;
        public readonly string ShortLabel;
        public readonly int Amount;

        public GoodCost(string goodName, string shortLabel, int amount)
        {
            GoodName = goodName;
            ShortLabel = shortLabel;
            Amount = amount;
        }
    }

    public static class EconomyHelper
    {
        public static string BuildPacksName => GoodsTypes.Packs_Pack_Of_Building_Materials.ToName();
        public static string TradePacksName => GoodsTypes.Packs_Pack_Of_Trade_Goods.ToName();
        public static string WildfireEssenceName => GoodsTypes.Hearth_Parts.ToName();

        public static GoodCost BuildPacks(int amount) => new GoodCost(BuildPacksName, "Build Packs", amount);
        public static GoodCost TradePacks(int amount) => new GoodCost(TradePacksName, "Trade Packs", amount);
        public static GoodCost WildfireEssence(int amount) => new GoodCost(WildfireEssenceName, "Wildfire", amount);

        public static int GetStored(string goodName)
        {
            if (string.IsNullOrEmpty(goodName) || Svc.Buildings?.Storages == null || Svc.Buildings.Storages.Count == 0 || Svc.Storage?.Main == null)
            {
                return 0;
            }
            return Svc.Storage.Main.GetAmount(goodName);
        }

        public static int GetUpgradeGoodsSignature()
        {
            unchecked
            {
                int h = 17;
                h = (h * 31) + GetStored(BuildPacksName);
                h = (h * 31) + GetStored(TradePacksName);
                h = (h * 31) + GetStored(WildfireEssenceName);
                return h;
            }
        }

        public static bool CanAfford(IList<GoodCost> costs)
        {
            if (costs == null || Svc.Buildings?.Storages == null || Svc.Buildings.Storages.Count == 0 || Svc.Storage?.Main == null)
            {
                return false;
            }

            for (int i = 0; i < costs.Count; i++)
            {
                var c = costs[i];
                if (c.Amount > 0 && GetStored(c.GoodName) < c.Amount)
                {
                    return false;
                }
            }
            return true;
        }

        public static bool TrySpend(string ownerModel, int ownerId, IList<GoodCost> costs)
        {
            if (costs == null || !CanAfford(costs))
            {
                return false;
            }

            for (int i = 0; i < costs.Count; i++)
            {
                var c = costs[i];
                if (c.Amount > 0)
                {
                    Svc.Storage.Remove(
                        new Good(c.GoodName, c.Amount),
                        ownerModel ?? "ImperialLogistics",
                        ownerId,
                        StorageOperationType.BuildingUpgrade);
                }
            }
            return true;
        }

        public static string FormatCostSummary(IList<GoodCost> costs)
        {
            if (costs == null || costs.Count == 0)
            {
                return "Free";
            }

            var sb = new StringBuilder();
            bool first = true;
            for (int i = 0; i < costs.Count; i++)
            {
                var c = costs[i];
                if (c.Amount <= 0) continue;
                if (!first) sb.Append(" + ");
                first = false;

                int have = GetStored(c.GoodName);
                string color = have >= c.Amount ? "#A8E6A1" : "#FF9494";
                sb.Append($"<color={color}>{have}/{c.Amount}</color> {c.ShortLabel}");
            }
            return sb.Length > 0 ? sb.ToString() : "Free";
        }
    }
}
