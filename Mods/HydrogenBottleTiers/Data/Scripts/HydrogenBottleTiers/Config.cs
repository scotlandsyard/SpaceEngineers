using System.Collections.Generic;
using System.Xml.Serialization;
using ProtoBuf;

namespace HydrogenBottleTiers
{
    // The server's settings file: <world>\Storage\<mod>\HydrogenBottleTiers.xml.
    // Written with the defaults on first load; admins edit it and restart the server.
    public class TierConfig
    {
        public const int CurrentVersion = 1;

        public int Version = CurrentVersion;

        public string Notes =
            "Edit the values below, then restart the server (or reload the world). " +
            "CapacityLitres: hydrogen a full bottle holds (vanilla bottle: 400). " +
            "MassKg: weight of one bottle (vanilla: 30). BuildTimeSeconds: assembler time at speed x1. " +
            "Recipe: any items, e.g. <Item Type=\"Ingot\" Subtype=\"Platinum\" Amount=\"2\" /> or Type=\"Component\". " +
            "Invalid or missing values fall back to the mod's defaults; check the server log for lines starting with [HydrogenBottleTiers]. " +
            "Delete this file to get the defaults back.";

        [XmlArrayItem("Bottle")]
        public List<BottleConfig> Bottles = new List<BottleConfig>();

        public static TierConfig CreateDefault()
        {
            // Keep in step with Data\PhysicalItems_HydrogenBottleTiers.sbc and Data\Blueprints_HydrogenBottleTiers.sbc.
            var config = new TierConfig();
            config.Bottles.Add(new BottleConfig
            {
                Subtype = "HydrogenBottleTier2", CapacityLitres = 600, MassKg = 35, BuildTimeSeconds = 15,
                Recipe = { Ingot("Iron", 105), Ingot("Silicon", 25), Ingot("Nickel", 40), Ingot("Cobalt", 5) },
            });
            config.Bottles.Add(new BottleConfig
            {
                Subtype = "HydrogenBottleTier3", CapacityLitres = 1000, MassKg = 50, BuildTimeSeconds = 30,
                Recipe = { Ingot("Iron", 240), Ingot("Silicon", 40), Ingot("Nickel", 65), Ingot("Cobalt", 15),
                    Ingot("Silver", 5), Ingot("Magnesium", 0.5f) },
            });
            config.Bottles.Add(new BottleConfig
            {
                Subtype = "HydrogenBottleTier4", CapacityLitres = 1800, MassKg = 75, BuildTimeSeconds = 60,
                Recipe = { Ingot("Iron", 400), Ingot("Silicon", 80), Ingot("Nickel", 100), Ingot("Cobalt", 22.5f),
                    Ingot("Silver", 12.5f), Ingot("Magnesium", 2.5f), Ingot("Gold", 5), Ingot("Platinum", 2) },
            });
            return config;
        }

        static ItemConfig Ingot(string subtype, float amount)
        {
            return new ItemConfig { Type = "Ingot", Subtype = subtype, Amount = amount };
        }
    }

    public class BottleConfig
    {
        [XmlAttribute]
        public string Subtype;

        public float CapacityLitres;
        public float MassKg;
        public float BuildTimeSeconds;

        [XmlArrayItem("Item")]
        public List<ItemConfig> Recipe = new List<ItemConfig>();
    }

    public class ItemConfig
    {
        [XmlAttribute]
        public string Type = "Ingot";

        [XmlAttribute]
        public string Subtype;

        [XmlAttribute]
        public float Amount;
    }

    // Server -> client: the server's config as XML, so every client shows the same capacities and recipes.
    [ProtoContract]
    public class ConfigMessage
    {
        [ProtoMember(1)]
        public string ConfigXml;
    }
}
