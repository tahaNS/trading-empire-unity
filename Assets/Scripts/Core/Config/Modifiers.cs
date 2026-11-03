using System.Collections.Generic;
using System.Linq;

namespace TradingEmpire.Core
{
    public enum Mood { Booming, Steady, Panic }

    public sealed class MoodEffect
    {
        public string Label;
        /// <summary>Added to the rent (income) of every earning building.</summary>
        public int IncomePct;
        /// <summary>Added to the upkeep of every building.</summary>
        public int ExpensePct;
        public string Text;
    }

    public sealed class PolicyEffect
    {
        /// <summary>Null = all buildings.</summary>
        public BuildingCategory? Target;
        public int IncomePct, ExpensePct;
    }

    public sealed class PolicyDefinition
    {
        public string Id, Name, Description;
        public PolicyEffect[] Effects;
        /// <summary>Multiplies the rent bonus of parks and plazas.</summary>
        public double ParkBonusMult = 1;
        /// <summary>Share (0 to 1) of bad events (fire, storm) this policy absorbs.</summary>
        public double EventShield;
        /// <summary>Unlock needed before the policy can be switched on (won in a trading challenge).</summary>
        public string UnlockId;
        public string LockedText;
    }

    public sealed class SynergyPart
    {
        public string Id;
        public BuildingCategory? Category;
        public bool Matches(BuildingDefinition def) { return (Id == null || Id == def.Id) && (!Category.HasValue || Category.Value == def.Category); }
    }

    public sealed class Synergy
    {
        public SynergyPart From, To;
        public int Radius, Pct;
        public string Label;
    }

    public static class Modifiers
    {
        // ---------- market mood (from your last closed trades) ----------
        public const int MoodWindow = 10;
        public const int MoodMinTrades = 5;
        public const double BoomWinRate = 0.6;
        public const double PanicWinRate = 0.3;

        public static MoodEffect EffectOf(Mood m)
        {
            switch (m)
            {
                case Mood.Booming: return new MoodEffect { Label = "Boom", IncomePct = 3, ExpensePct = 0, Text = "Your hot streak lifts the city: +3% rent." };
                case Mood.Panic: return new MoodEffect { Label = "Panic", IncomePct = 0, ExpensePct = 8, Text = "Your losing streak spooks the city: +8% upkeep." };
                default: return new MoodEffect { Label = "Steady", IncomePct = 0, ExpensePct = 0, Text = "The city is calm." };
            }
        }

        // ---------- policies ----------
        public static readonly IReadOnlyList<PolicyDefinition> Policies = new[]
        {
            new PolicyDefinition
            {
                Id = "marketing", Name = "Marketing push",
                Description = "Shops, restaurants and hotels earn more, but advertising raises their upkeep.",
                Effects = new[] { new PolicyEffect { Target = BuildingCategory.Commercial, IncomePct = 8 }, new PolicyEffect { Target = BuildingCategory.Commercial, ExpensePct = 12 } },
            },
            new PolicyDefinition
            {
                Id = "austerity", UnlockId = "policy-austerity", LockedText = "Win the Range day challenge in the terminal to unlock it.", Name = "Cost cutting",
                Description = "Every building costs less to run, but lower service quality trims its income. It pays off most for offices and towers with high upkeep.",
                Effects = new[] { new PolicyEffect { Target = null, ExpensePct = -10 }, new PolicyEffect { Target = null, IncomePct = -4 } },
            },
            new PolicyDefinition
            {
                Id = "green", UnlockId = "policy-green", LockedText = "Win the Spike survival challenge in the terminal to unlock it.", Name = "Green city",
                Description = "Parks and plazas boost neighbouring rents by half as much again, but cost 40% more to keep.",
                Effects = new[] { new PolicyEffect { Target = BuildingCategory.Recreation, ExpensePct = 40 } }, ParkBonusMult = 1.5,
            },
            new PolicyDefinition
            {
                Id = "insurance", Name = "Insurance cover",
                Description = "A premium on every building, in return for protection: fire repairs cost 80% less and storms hurt half as much.",
                Effects = new[] { new PolicyEffect { Target = null, ExpensePct = 4 } }, EventShield = 1,
            },
        };

        public static PolicyDefinition GetPolicy(string id) { return Policies.FirstOrDefault(p => p.Id == id); }

        // ---------- neighbour synergies ----------
        public static readonly IReadOnlyList<Synergy> Synergies = new[]
        {
            new Synergy { From = new SynergyPart { Category = BuildingCategory.Office }, To = new SynergyPart { Id = "restaurant" }, Radius = 3, Pct = 8, Label = "office workers nearby" },
            new Synergy { From = new SynergyPart { Category = BuildingCategory.Residential }, To = new SynergyPart { Id = "small-shop" }, Radius = 3, Pct = 6, Label = "customers living nearby" },
            new Synergy { From = new SynergyPart { Id = "hotel" }, To = new SynergyPart { Id = "restaurant" }, Radius = 3, Pct = 6, Label = "hotel guests nearby" },
            new Synergy { From = new SynergyPart { Id = "small-shop" }, To = new SynergyPart { Id = "apartment" }, Radius = 3, Pct = 4, Label = "shops on the doorstep" },
        };

        /// <summary>Park bonus plus synergies can lift a building's rent by at most this much.</summary>
        public const int MaxNeighborBonusPct = 25;
    }

    public enum EventKind { Fire, Storm, Festival }

    public sealed class EventDefinition
    {
        public EventKind Kind;
        public string Name;
        /// <summary>Relative chance among events.</summary>
        public int Weight;
        public int Days;
        /// <summary>Income change for every earning building while it lasts (storm, festival).</summary>
        public int IncomePct;
        /// <summary>Fire: the building earns nothing while repaired, and the repair costs this share of its price.</summary>
        public int RepairPct;
        public string Text;
    }

    public static class EventConfig
    {
        public static readonly IReadOnlyList<EventDefinition> Events = new[]
        {
            new EventDefinition { Kind = EventKind.Fire, Name = "Fire", Weight = 4, Days = 10, RepairPct = 12, Text = "closed for repairs" },
            new EventDefinition { Kind = EventKind.Storm, Name = "Storm", Weight = 3, Days = 6, IncomePct = -20, Text = "customers stay home: rent falls" },
            new EventDefinition { Kind = EventKind.Festival, Name = "Festival", Weight = 3, Days = 8, IncomePct = 20, Text = "visitors fill the streets: rent rises" },
        };

        /// <summary>Events start only once the city has this many finished buildings.</summary>
        public const int MinBuildings = 4;
        /// <summary>Chance per game day that something happens (when nothing is going on).</summary>
        public const double DailyChance = 0.04;
        /// <summary>Most days rolled in one tick, so a long pause never causes a flood.</summary>
        public const int MaxRollsPerTick = 3;

        /// <summary>How much of a bad event the Insurance policy absorbs (fire repair cost, storm loss).</summary>
        public static double InsuranceShield(EventKind kind)
        {
            return kind == EventKind.Fire ? 0.8 : kind == EventKind.Storm ? 0.5 : 0;
        }

        public static EventDefinition Get(EventKind kind) { return Events.FirstOrDefault(e => e.Kind == kind); }
    }
}
