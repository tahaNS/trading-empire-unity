using System;
using System.Collections.Generic;
using System.Linq;

namespace TradingEmpire.Core
{
    public enum BuildingCategory { Residential, Commercial, Office, Recreation, Landmark }
    public enum DistrictId { Residential, Commercial, Business, Luxury, Recreation, Financial }

    /// <summary>Rent bonus a recreation building gives to the buildings around it.</summary>
    public sealed class NeighborBonus
    {
        /// <summary>Percent added to the income of every finished non-recreation building in range.</summary>
        public int Pct;
        /// <summary>Range in cells: the gap between the two footprints must be at most this.</summary>
        public int Radius;
    }

    /// <summary>
    /// What a trading record must show for a landmark's level. A level is reached when ALL its fields are met by CLOSED
    /// trades: net P/L must be strictly above MinNetPnl and the profit factor at least MinProfitFactor (when set).
    /// </summary>
    public sealed class TradeLevelRule
    {
        public int MinTrades;
        public Money? MinNetPnl;
        public double? MinProfitFactor;
    }

    public sealed class BuildingLevel
    {
        public string Name;
        public NeighborBonus NeighborBonus;
        /// <summary>Gross income per income interval at this level.</summary>
        public Money Income;
        /// <summary>Operating expense per income interval at this level.</summary>
        public Money Expense;
        /// <summary>Cost to upgrade to the NEXT level; null at the maximum level.</summary>
        public Money? UpgradeCost;
    }

    public sealed class BuildingDefinition
    {
        public string Id;
        /// <summary>If set, the building is locked until this unlock id is granted (by a mission).</summary>
        public string UnlockId;
        public string Name;
        public BuildingCategory Category;
        public DistrictId District;
        public string Description;
        public Money Cost;
        /// <summary>Days of simulated time to build. Income starts only once construction is finished.</summary>
        public double ConstructionDays;
        /// <summary>At most this many may stand at once (placing another is refused).</summary>
        public int? MaxCount;
        /// <summary>For landmarks: levels come from the player's trading record instead of upgrade payments.</summary>
        public TradeLevelRule[] TradeLevels;
        public Footprint Footprint;
        /// <summary>Placeholder model: a box of this colour (0xRRGGBB) and height.</summary>
        public int Color;
        public float Height;
        public int IncomeIntervalDays;
        /// <summary>Levels[0] is level 1. Max level = Levels.Length.</summary>
        public BuildingLevel[] Levels;
    }

    public static class Buildings
    {
        static Money D(double d) { return Money.FromDollars(d); }
        static BuildingLevel L(string name, double income, double expense, double? upgrade, NeighborBonus nb = null)
        {
            return new BuildingLevel { Name = name, Income = D(income), Expense = D(expense), UpgradeCost = upgrade.HasValue ? D(upgrade.Value) : (Money?)null, NeighborBonus = nb };
        }

        static BuildingDefinition Def(string id, string name, BuildingCategory cat, DistrictId district, string desc, double cost, double days, int w, int h,
            int color, float height, BuildingLevel[] levels, string unlock = null, int? max = null, TradeLevelRule[] trade = null)
        {
            return new BuildingDefinition
            {
                Id = id, Name = name, Category = cat, District = district, Description = desc, Cost = D(cost), ConstructionDays = days,
                Footprint = new Footprint(w, h), Color = color, Height = height, IncomeIntervalDays = 30, Levels = levels,
                UnlockId = unlock, MaxCount = max, TradeLevels = trade,
            };
        }

        // All numbers are balance values identical to the web version, tuned there.
        public static readonly IReadOnlyList<BuildingDefinition> All = new[]
        {
            Def("small-shop", "Small Shop", BuildingCategory.Commercial, DistrictId.Commercial, "A corner shop. Cheap to build with modest income.", 4000, 2, 1, 1, 0xe0a458, 1.2f,
                new[] { L("Small Shop", 135, 46, 2600), L("Corner Store", 245, 86, 4000), L("Mini Mall", 385, 135, null) }),
            Def("apartment", "Apartment Building", BuildingCategory.Residential, DistrictId.Residential, "Homes for residents. Steady rent income.", 20000, 5, 2, 2, 0x6c8ebf, 3f,
                new[] { L("Apartment Building", 780, 335, 12000), L("Apartment Complex", 1330, 540, 20000), L("Residential Block", 2000, 770, null) }),
            Def("small-office", "Small Office", BuildingCategory.Office, DistrictId.Business, "Office space for small firms. Higher costs, higher income.", 36000, 7, 2, 3, 0x9b7fb8, 4.2f,
                new[] { L("Small Office", 1550, 750, 21000), L("Business Center", 2450, 1070, 36000), L("Office Tower", 3600, 1450, null) }),
            Def("restaurant", "Restaurant", BuildingCategory.Commercial, DistrictId.Commercial, "A busy eatery. Better returns than a corner shop.", 15000, 4, 2, 2, 0xd96c4f, 1.8f,
                new[] { L("Restaurant", 760, 385, 9000), L("Grand Restaurant", 1250, 570, 15000), L("Dining Hall", 1850, 820, null) }, unlock: "restaurant"),
            Def("luxury-tower", "Luxury Tower", BuildingCategory.Residential, DistrictId.Luxury, "High-end living with premium rents.", 90000, 10, 3, 3, 0x3fa7a0, 7f,
                new[] { L("Luxury Tower", 3900, 1200, 55000), L("Grand Tower", 6000, 1700, 90000), L("Skyline Tower", 9000, 2500, null) }, unlock: "luxury-tower"),
            Def("corporate-office", "Corporate Office", BuildingCategory.Office, DistrictId.Business, "Headquarters space for large companies.", 150000, 10, 3, 3, 0x5b6fc9, 6.2f,
                new[] { L("Corporate Office", 6500, 2000, 90000), L("Corporate Campus", 10000, 3000, 150000), L("Corporate Skyscraper", 15000, 4500, null) }, unlock: "corporate-office"),
            Def("house", "House", BuildingCategory.Residential, DistrictId.Residential, "A family home. The cheapest way to start earning rent.", 3000, 2, 1, 1, 0xe8a07a, 1.3f,
                new[] { L("House", 105, 38, 2000), L("Family House", 190, 70, null) }),
            Def("park", "Park", BuildingCategory.Recreation, DistrictId.Recreation, "Green space. Earns nothing itself, but raises the rent of buildings near it.", 6000, 3, 2, 2, 0x5bb85a, 0.5f,
                new[] { L("Park", 0, 40, 5000, new NeighborBonus { Pct = 10, Radius = 3 }), L("Garden Park", 0, 75, null, new NeighborBonus { Pct = 15, Radius = 3 }) }),
            Def("plaza", "Fountain Plaza", BuildingCategory.Recreation, DistrictId.Recreation, "A paved square with a fountain. A wider and stronger rent boost than a park.", 20000, 5, 3, 3, 0xd8d2c4, 0.9f,
                new[] { L("Fountain Plaza", 0, 150, null, new NeighborBonus { Pct = 15, Radius = 4 }) }, unlock: "plaza"),
            Def("hotel", "Hotel", BuildingCategory.Commercial, DistrictId.Luxury, "Rooms for visitors. High income that grows with each upgrade.", 60000, 8, 2, 3, 0xc9617a, 4.6f,
                new[] { L("Hotel", 2900, 1100, 36000), L("Grand Hotel", 4400, 1500, 60000), L("Resort Hotel", 6400, 2100, null) }, unlock: "hotel"),
            Def("trading-hq", "Trading HQ", BuildingCategory.Landmark, DistrictId.Financial, "Your trading desk made visible. It earns no rent; its size follows your closed-trade record.", 40000, 6, 3, 3, 0x2f7d8c, 6f,
                new[] { L("Trading Desk", 0, 120, null), L("Trading Floor", 0, 120, null), L("Trading HQ Tower", 0, 120, null) }, unlock: "trading-hq", max: 1,
                trade: new[]
                {
                    new TradeLevelRule { MinTrades = 10 },
                    new TradeLevelRule { MinTrades = 40, MinNetPnl = D(0) },
                    new TradeLevelRule { MinTrades = 100, MinNetPnl = D(5000), MinProfitFactor = 1.2 },
                }),
        };

        public static BuildingDefinition Get(string id)
        {
            var d = All.FirstOrDefault(x => x.Id == id);
            if (d == null) throw new ArgumentException("Unknown building definition: " + id);
            return d;
        }

        /// <summary>Throws if the static definitions are malformed. Run in tests.</summary>
        public static void Validate(IEnumerable<BuildingDefinition> defs)
        {
            var seen = new HashSet<string>();
            foreach (var d in defs)
            {
                if (!seen.Add(d.Id)) throw new InvalidOperationException("Duplicate building id: " + d.Id);
                if (d.Footprint.W < 1 || d.Footprint.H < 1) throw new InvalidOperationException(d.Id + ": empty footprint");
                foreach (var l in d.Levels)
                    if (l.NeighborBonus != null && !(l.NeighborBonus.Pct > 0 && l.NeighborBonus.Radius >= 1)) throw new InvalidOperationException(d.Id + ": invalid neighbor bonus");
                if (!(d.ConstructionDays > 0)) throw new InvalidOperationException(d.Id + ": constructionDays must be positive");
                if (d.Cost.Cents <= 0) throw new InvalidOperationException(d.Id + ": cost must be positive");
                if (d.Levels.Length < 1) throw new InvalidOperationException(d.Id + ": needs at least one level");
                if (d.TradeLevels != null)
                {
                    if (d.TradeLevels.Length != d.Levels.Length) throw new InvalidOperationException(d.Id + ": tradeLevels must match the number of levels");
                    for (int i = 1; i < d.TradeLevels.Length; i++)
                        if (d.TradeLevels[i].MinTrades <= d.TradeLevels[i - 1].MinTrades) throw new InvalidOperationException(d.Id + ": tradeLevels must need more trades at each level");
                }
                for (int i = 0; i < d.Levels.Length; i++)
                {
                    bool last = d.TradeLevels != null || i == d.Levels.Length - 1;
                    if (last != !d.Levels[i].UpgradeCost.HasValue) throw new InvalidOperationException(d.Id + ": only the final level may omit upgradeCost");
                }
            }
        }
    }

    public sealed class LandTier
    {
        /// <summary>Tier number, 2 and up (tier 1 is the starting plot).</summary>
        public int Tier;
        public string Name;
        /// <summary>Unlock id granted by a mission; required before the land can be bought.</summary>
        public string UnlockId;
        public Money Cost;
        /// <summary>Buildable area after purchase; must contain the previous area.</summary>
        public RectI Area;
    }

    public static class Land
    {
        public const int WorldSize = 32;
        public static readonly RectI StartingArea = new RectI(10, 10, 12, 12);
        public static readonly IReadOnlyList<LandTier> Tiers = new[]
        {
            new LandTier { Tier = 2, Name = "Riverside District", UnlockId = "land-2", Cost = Money.FromDollars(15000), Area = new RectI(8, 8, 16, 16) },
            new LandTier { Tier = 3, Name = "Midtown District", UnlockId = "land-3", Cost = Money.FromDollars(60000), Area = new RectI(5, 5, 22, 22) },
            new LandTier { Tier = 4, Name = "Uptown District", UnlockId = "land-4", Cost = Money.FromDollars(300000), Area = new RectI(0, 0, 32, 32) },
        };
    }

    public static class Balance
    {
        /// <summary>Share of a building's construction cost paid back when it is demolished (upgrades are not refunded).</summary>
        public const int DemolishRefundPct = 50;
    }
}
