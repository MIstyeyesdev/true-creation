using System;
using System.Collections.Generic;
using System.Linq;
using PalItemgen.Lookup;

namespace PalItemgen.Model
{
    /// <summary>
    /// The true producers the chain station tracks: every buildable bench whose
    /// model class is PalMapObjectConvertItemModel (the player chooses what it
    /// makes), grouped by the game's build-wheel family (TypeUIDisplay) and
    /// ordered strongest first. Ranches, plantations, quarries and logging
    /// sites are not producers. Pure C#: the harness compiles this file.
    /// </summary>
    public static class ChainCatalog
    {
        public struct Family
        {
            public string Id;
            public string Label;
        }

        /// <summary>The seven families, in the order the screen and the config list them.</summary>
        public static readonly Family[] Families =
        {
            new Family { Id = "Product_Repair", Label = "Workbenches & assembly lines" },
            new Family { Id = "PalCaptureItem", Label = "Sphere stations" },
            new Family { Id = "Weapon", Label = "Weapon stations" },
            new Family { Id = "Medicine", Label = "Medicine benches" },
            new Family { Id = "Refine", Label = "Furnaces" },
            new Family { Id = "Cooking", Label = "Kitchens" },
            new Family { Id = "Milling_Crusher", Label = "Mills & crushers" },
        };

        /// <summary>
        /// Child blueprints whose model class and work spot live on their parent
        /// blueprint (the lookup exporter does not follow SuperStruct): the JSON
        /// says model = None and workSpots = 0, the game says ConvertItemModel with
        /// one spot.
        /// </summary>
        public static readonly string[] ModelFixes = { "SphereFactory_Black_01", "WeaponFactory_Dirty_01" };

        /// <summary>Index of a family id in Families, or -1.</summary>
        public static int Order(string id)
        {
            for (var i = 0; i < Families.Length; i++)
                if (Families[i].Id == id) return i;
            return -1;
        }

        /// <summary>Screen label of a family id, or the id itself when unknown.</summary>
        public static string Label(string id)
        {
            var i = Order(id);
            return i >= 0 ? Families[i].Label : id;
        }

        public static bool IsTrueProducer(ProducerInfo p) =>
            p != null && p.Buildable && p.MapObjectId != null
            && (p.Model == "PalMapObjectConvertItemModel" || ModelFixes.Contains(p.MapObjectId));

        public static string FamilyOf(ProducerInfo p) => p?.Build?.TypeUIDisplay ?? "None";

        /// <summary>Fixed Pal work spots: buildings.json carries workSpots, producers.json does not.</summary>
        public static int WorkSpots(GameData d, string id) =>
            ModelFixes.Contains(id) ? 1 : (d?.Building(id)?.WorkSpots ?? 0);

        /// <summary>Static data work speed; 0 = unknown, the basic tier of a family.</summary>
        public static float Speed(ProducerInfo p) => p?.WorkSpeed ?? 0f;

        public static int Tier(ProducerInfo p) => p?.Tech?.LevelCap ?? 0;

        public static int RankMax(ProducerInfo p) => p?.RankMax ?? 0;

        /// <summary>First assignment row that is not "Anyone", else "None" (same rule as LuaWriter.Benches).</summary>
        public static string Suitability(ProducerInfo p)
        {
            if (p == null || p.Assignments == null) return "None";
            var a = p.Assignments.FirstOrDefault(x => x.WorkSuitability != "Anyone") ?? p.Assignments.FirstOrDefault();
            return a?.WorkSuitability ?? "None";
        }

        /// <summary>Family order (unknown last), then rank max, speed and tier descending, then name and id.</summary>
        public static int Compare(ProducerInfo a, ProducerInfo b)
        {
            var oa = Order(FamilyOf(a));
            var ob = Order(FamilyOf(b));
            if (oa < 0) oa = int.MaxValue;
            if (ob < 0) ob = int.MaxValue;
            var c = oa.CompareTo(ob);
            if (c != 0) return c;
            c = RankMax(b).CompareTo(RankMax(a));
            if (c != 0) return c;
            c = Speed(b).CompareTo(Speed(a));
            if (c != 0) return c;
            c = Tier(b).CompareTo(Tier(a));
            if (c != 0) return c;
            c = string.CompareOrdinal(a?.Name ?? "", b?.Name ?? "");
            if (c != 0) return c;
            return string.CompareOrdinal(a?.MapObjectId ?? "", b?.MapObjectId ?? "");
        }

        /// <summary>Every true producer, grouped by family and strongest first.</summary>
        public static IEnumerable<ProducerInfo> Producers(GameData d)
        {
            if (d == null || d.Producers == null) return Enumerable.Empty<ProducerInfo>();
            var list = d.Producers.Where(IsTrueProducer).ToList();
            list.Sort(Compare);
            return list;
        }

        public static IEnumerable<ProducerInfo> InFamily(GameData d, string familyId) =>
            Producers(d).Where(p => FamilyOf(p) == familyId);

        /// <summary>The true producers that can make an item, strongest first.</summary>
        public static IEnumerable<ProducerInfo> MakersOf(GameData d, string itemId)
        {
            if (d == null || string.IsNullOrEmpty(itemId)) return Enumerable.Empty<ProducerInfo>();
            var makers = Producers(d).Where(p => d.ItemsFor(p).Any(it => it.Id == itemId)).ToList();
            // strongest first regardless of family: the planner does not care which family makes it
            makers.Sort((a, b) =>
            {
                var c = RankMax(b).CompareTo(RankMax(a));
                if (c != 0) return c;
                c = Speed(b).CompareTo(Speed(a));
                if (c != 0) return c;
                c = Tier(b).CompareTo(Tier(a));
                if (c != 0) return c;
                c = string.CompareOrdinal(a.Name ?? "", b.Name ?? "");
                return c != 0 ? c : string.CompareOrdinal(a.MapObjectId ?? "", b.MapObjectId ?? "");
            });
            return makers;
        }
    }
}
