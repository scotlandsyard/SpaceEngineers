using System.Collections.Generic;
using System.Xml.Serialization;
using ProtoBuf;

namespace TieredGasBottles
{
    // The server's settings file in the world's Storage folder.
    // Written with the defaults on first load; admins edit it and restart the server.
    public class TierConfig
    {
        public const int CurrentVersion = 1;

        public int Version = CurrentVersion;

        public string Notes =
            "Edit the values below, then restart the server (or reload the world). " +
            "Enabled: false means assemblers can't build that bottle; bottles that already exist keep working. " +
            "CapacityLitres: gas a full bottle holds (vanilla: oxygen 40, hydrogen 400). " +
            "MassKg: weight of one bottle (vanilla: 30). BuildTimeSeconds: assembler time at speed x1. " +
            "Recipe: any items, e.g. <Item Type=\"Ingot\" Subtype=\"Platinum\" Amount=\"2\" /> or Type=\"Component\". " +
            "Invalid or missing values fall back to the mod's defaults; check the server log for lines starting with [TieredGasBottles]. " +
            "Delete this file to get the defaults back.";

        [XmlArrayItem("Bottle")]
        public List<BottleConfig> Bottles = new List<BottleConfig>();

        public static TierConfig CreateDefault()
        {
            // Keep in step with Data\PhysicalItems_TieredGasBottles.sbc and Data\Blueprints_TieredGasBottles.sbc.
            var config = new TierConfig();
            foreach (string gas in new[] { "Oxygen", "Hydrogen" })
            {
                // Capacities are 1.5x, 2.5x and 4.5x the vanilla bottle; the rest is the same for both gases.
                float vanillaCapacity = gas == "Oxygen" ? 40 : 400;
                config.Bottles.Add(new BottleConfig
                {
                    Subtype = gas + "BottleTier2", CapacityLitres = vanillaCapacity * 1.5f, MassKg = 35, BuildTimeSeconds = 15,
                    Recipe = { Ingot("Iron", 105), Ingot("Silicon", 25), Ingot("Nickel", 40), Ingot("Cobalt", 5) },
                });
                config.Bottles.Add(new BottleConfig
                {
                    Subtype = gas + "BottleTier3", CapacityLitres = vanillaCapacity * 2.5f, MassKg = 50, BuildTimeSeconds = 30,
                    Recipe = { Ingot("Iron", 240), Ingot("Silicon", 40), Ingot("Nickel", 65), Ingot("Cobalt", 15),
                        Ingot("Silver", 5), Ingot("Magnesium", 0.5f) },
                });
                config.Bottles.Add(new BottleConfig
                {
                    Subtype = gas + "BottleTier4", CapacityLitres = vanillaCapacity * 4.5f, MassKg = 75, BuildTimeSeconds = 60,
                    Recipe = { Ingot("Iron", 400), Ingot("Silicon", 80), Ingot("Nickel", 100), Ingot("Cobalt", 22.5f),
                        Ingot("Silver", 12.5f), Ingot("Magnesium", 2.5f), Ingot("Gold", 5), Ingot("Platinum", 2) },
                });
            }
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

        public bool Enabled = true;
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

    // Server -> client: the server's config as XML, so every client shows the same bottles, capacities and recipes.
    [ProtoContract]
    public class ConfigMessage
    {
        [ProtoMember(1)]
        public string ConfigXml;
    }
}
