using System;
using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders.Definitions;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.Components;
using VRage.Utils;

namespace TieredGasBottles
{
    // Applies the server's bottle settings (enabled, capacity, mass, build time, recipe) to the definitions, on the
    // server and on every client. Runs once at world load and never updates, so it costs nothing while playing.
    // If this script ever fails (for example after a game update), the bottles still work with the defaults
    // from the .sbc files; only the config file and the HUD bottle count are lost.
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class TieredGasBottlesSession : MySessionComponentBase
    {
        public const string LogPrefix = "[TieredGasBottles] ";

        const string ConfigFileName = "TieredGasBottles.xml";
        const ushort MessageId = 47213;
        const byte RequestConfig = 1;
        const int RequestCooldownFrames = 300; // a client is answered at most once every 5 seconds

        // Our bottles: item type, assembly blueprint, and the assembler classes it's in
        // (Data\BlueprintClasses_TieredGasBottles.sbc). The config can only change these.
        class BottleInfo
        {
            public MyDefinitionId ItemId;
            public MyDefinitionId BlueprintId;
            public string[] AssemblerClasses;
            public string GasName;
        }

        static readonly Dictionary<string, BottleInfo> Bottles = CreateBottleInfo();

        static Dictionary<string, BottleInfo> CreateBottleInfo()
        {
            var bottles = new Dictionary<string, BottleInfo>();
            Add(bottles, typeof(MyObjectBuilder_OxygenContainerObject), "Oxygen", "001");
            Add(bottles, typeof(MyObjectBuilder_GasContainerObject), "Hydrogen", "002");
            return bottles;
        }

        static void Add(Dictionary<string, BottleInfo> bottles, Type itemType, string gas, string position)
        {
            for (int tier = 2; tier <= 4; tier++)
            {
                string subtype = gas + "BottleTier" + tier;
                bottles[subtype] = new BottleInfo
                {
                    ItemId = new MyDefinitionId(itemType, subtype),
                    BlueprintId = new MyDefinitionId(typeof(MyObjectBuilder_BlueprintDefinition), "Position" + position + (tier - 1) + "_" + subtype),
                    AssemblerClasses = tier == 2 ? new[] { "SimpleEquipment", "EliteEquipment" } : new[] { "EliteEquipment" },
                    GasName = gas == "Oxygen" ? "oxygen" : "hydrogen",
                };
            }
        }

        bool handlerRegistered;
        byte[] configForClients;
        readonly Dictionary<ulong, int> lastRequestFrame = new Dictionary<ulong, int>();

        public override void LoadData()
        {
            try
            {
                // Start from the defaults every time: definitions can survive from an earlier world in the same game run.
                Apply(TierConfig.CreateDefault(), false);

                if (MyAPIGateway.Session.IsServer)
                {
                    TierConfig config = LoadServerConfig();
                    Apply(config, true);
                    configForClients = MyAPIGateway.Utilities.SerializeToBinary(
                        new ConfigMessage { ConfigXml = MyAPIGateway.Utilities.SerializeToXML(config) });
                }

                if (MyAPIGateway.Multiplayer.MultiplayerActive)
                {
                    MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(MessageId, OnMessage);
                    handlerRegistered = true;
                }
            }
            catch (Exception e)
            {
                Log("Loading failed, using the default bottles: " + e);
            }
        }

        public override void BeforeStart()
        {
            try
            {
                if (handlerRegistered && !MyAPIGateway.Session.IsServer)
                    MyAPIGateway.Multiplayer.SendMessageToServer(MessageId, new[] { RequestConfig });
            }
            catch (Exception e)
            {
                Log("Asking the server for its settings failed: " + e);
            }
        }

        protected override void UnloadData()
        {
            try
            {
                if (handlerRegistered)
                    MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(MessageId, OnMessage);
            }
            catch (Exception e)
            {
                Log("Unload failed: " + e);
            }
            handlerRegistered = false;
        }

        void OnMessage(ushort id, byte[] data, ulong sender, bool fromServer)
        {
            try
            {
                if (data == null || data.Length == 0)
                    return;

                if (MyAPIGateway.Session.IsServer)
                {
                    // A client asking for the settings. Nothing it sends is used, so there is nothing to validate.
                    if (fromServer || configForClients == null || data[0] != RequestConfig)
                        return;
                    int frame = MyAPIGateway.Session.GameplayFrameCounter;
                    int last;
                    if (lastRequestFrame.TryGetValue(sender, out last) && frame - last < RequestCooldownFrames)
                        return;
                    lastRequestFrame[sender] = frame;
                    MyAPIGateway.Multiplayer.SendMessageTo(MessageId, configForClients, sender);
                }
                else if (fromServer)
                {
                    var message = MyAPIGateway.Utilities.SerializeFromBinary<ConfigMessage>(data);
                    if (message == null || string.IsNullOrEmpty(message.ConfigXml))
                        return;
                    Apply(TierConfig.CreateDefault(), false);
                    Apply(MyAPIGateway.Utilities.SerializeFromXML<TierConfig>(message.ConfigXml), false);
                }
            }
            catch (Exception e)
            {
                Log("Handling a network message failed: " + e);
            }
        }

        // Reads the world's config file, or writes one with the defaults. A file that can't be read is left alone
        // (so the admin can fix it) and the defaults are used.
        TierConfig LoadServerConfig()
        {
            Type owner = typeof(TieredGasBottlesSession);
            if (!MyAPIGateway.Utilities.FileExistsInWorldStorage(ConfigFileName, owner))
            {
                TierConfig defaults = TierConfig.CreateDefault();
                SaveServerConfig(defaults);
                Log("Wrote the default settings to the world's Storage folder: " + ConfigFileName);
                return defaults;
            }

            TierConfig config;
            try
            {
                string text;
                using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(ConfigFileName, owner))
                    text = reader.ReadToEnd();
                config = MyAPIGateway.Utilities.SerializeFromXML<TierConfig>(text);
            }
            catch (Exception e)
            {
                Log(ConfigFileName + " could not be read, using the defaults until it is fixed: " + e.Message);
                return TierConfig.CreateDefault();
            }
            if (config == null)
                return TierConfig.CreateDefault();
            if (config.Bottles == null)
                config.Bottles = new List<BottleConfig>();

            // A newer version of the mod may add bottles: add their defaults so the admin sees them in the file.
            bool added = false;
            foreach (BottleConfig bottle in TierConfig.CreateDefault().Bottles)
            {
                if (!config.Bottles.Exists(b => b != null && b.Subtype == bottle.Subtype))
                {
                    config.Bottles.Add(bottle);
                    added = true;
                }
            }
            if (added || config.Version < TierConfig.CurrentVersion)
            {
                config.Version = TierConfig.CurrentVersion;
                SaveServerConfig(config);
                Log("Updated " + ConfigFileName + " with settings added in this version of the mod.");
            }
            return config;
        }

        static void SaveServerConfig(TierConfig config)
        {
            try
            {
                using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(ConfigFileName, typeof(TieredGasBottlesSession)))
                    writer.Write(MyAPIGateway.Utilities.SerializeToXML(config));
            }
            catch (Exception e)
            {
                Log("Could not write " + ConfigFileName + ": " + e.Message);
            }
        }

        // Applies every valid value; anything invalid keeps whatever was applied before (the defaults).
        static void Apply(TierConfig config, bool logProblems)
        {
            if (config == null || config.Bottles == null)
                return;

            foreach (BottleConfig bottle in config.Bottles)
            {
                BottleInfo info;
                if (bottle == null || bottle.Subtype == null || !Bottles.TryGetValue(bottle.Subtype, out info))
                {
                    if (logProblems)
                        Log("Unknown bottle in the config, ignored: " + (bottle == null ? "(empty)" : bottle.Subtype));
                    continue;
                }

                MyPhysicalItemDefinition itemDefinition;
                MyDefinitionManager.Static.TryGetPhysicalItemDefinition(info.ItemId, out itemDefinition);
                var container = itemDefinition as MyOxygenContainerDefinition;
                if (container == null)
                {
                    if (logProblems)
                        Log("Definition missing for " + bottle.Subtype + ", is the mod installed correctly?");
                    continue;
                }

                if (IsValid(bottle.CapacityLitres, 1, 1000000))
                {
                    container.Capacity = bottle.CapacityLitres;
                    if (container.ExtraInventoryTooltipLine != null)
                        container.ExtraInventoryTooltipLine.Clear().Append('\n')
                            .Append("Capacity: ").Append(bottle.CapacityLitres).Append(" L of ").Append(info.GasName);
                }
                else if (logProblems)
                    Log(bottle.Subtype + ": CapacityLitres must be between 1 and 1000000, using the default.");

                if (IsValid(bottle.MassKg, 0, 100000))
                    container.Mass = bottle.MassKg;
                else if (logProblems)
                    Log(bottle.Subtype + ": MassKg must be between 0 and 100000, using the default.");

                MyBlueprintDefinitionBase blueprint = MyDefinitionManager.Static.GetBlueprintDefinition(info.BlueprintId);
                if (blueprint == null)
                {
                    if (logProblems)
                        Log("Blueprint missing for " + bottle.Subtype + ", is the mod installed correctly?");
                    continue;
                }

                SetBuildable(blueprint, info.AssemblerClasses, bottle.Enabled);
                if (!bottle.Enabled && logProblems)
                    Log(bottle.Subtype + " is disabled: assemblers won't build it.");

                if (IsValid(bottle.BuildTimeSeconds, 0.1f, 86400))
                    blueprint.BaseProductionTimeInSeconds = bottle.BuildTimeSeconds;
                else if (logProblems)
                    Log(bottle.Subtype + ": BuildTimeSeconds must be between 0.1 and 86400, using the default.");

                MyBlueprintDefinitionBase.Item[] recipe = BuildRecipe(bottle, logProblems);
                if (recipe != null)
                    blueprint.Prerequisites = recipe;
            }
        }

        // Adds the blueprint to, or takes it out of, the assembler classes. A blueprint class has no Remove, so a
        // removal rebuilds the class without it. The game checks these classes before queueing anything, on the
        // server too, so a disabled bottle can't be queued even by a client that still shows it.
        static void SetBuildable(MyBlueprintDefinitionBase blueprint, string[] classNames, bool buildable)
        {
            foreach (string className in classNames)
            {
                MyBlueprintClassDefinition blueprintClass = MyDefinitionManager.Static.GetBlueprintClass(className);
                if (blueprintClass == null || blueprintClass.ContainsBlueprint(blueprint) == buildable)
                    continue;
                if (buildable)
                {
                    blueprintClass.AddBlueprint(blueprint);
                    continue;
                }
                var keep = new List<MyBlueprintDefinitionBase>();
                foreach (MyBlueprintDefinitionBase other in blueprintClass)
                {
                    if (other != blueprint)
                        keep.Add(other);
                }
                blueprintClass.ClearBlueprints();
                foreach (MyBlueprintDefinitionBase other in keep)
                    blueprintClass.AddBlueprint(other);
            }
        }

        // The whole recipe or nothing: one bad line keeps the default recipe, so a typo can't make a bottle cheap.
        static MyBlueprintDefinitionBase.Item[] BuildRecipe(BottleConfig bottle, bool logProblems)
        {
            if (bottle.Recipe == null || bottle.Recipe.Count == 0)
            {
                if (logProblems)
                    Log(bottle.Subtype + ": the recipe is empty, using the default.");
                return null;
            }

            var items = new List<MyBlueprintDefinitionBase.Item>();
            foreach (ItemConfig entry in bottle.Recipe)
            {
                MyDefinitionId id;
                MyPhysicalItemDefinition definition;
                string type = entry == null ? null : entry.Type;
                if (type != null && !type.StartsWith("MyObjectBuilder_"))
                    type = "MyObjectBuilder_" + type;
                if (entry == null || !MyDefinitionId.TryParse(type, entry.Subtype, out id)
                    || !MyDefinitionManager.Static.TryGetPhysicalItemDefinition(id, out definition) || definition == null)
                {
                    if (logProblems)
                        Log(bottle.Subtype + ": unknown recipe item " + (entry == null ? "(empty)" : entry.Type + "/" + entry.Subtype)
                            + ", using the default recipe.");
                    return null;
                }
                if (!IsValid(entry.Amount, 0.001f, 1000000))
                {
                    if (logProblems)
                        Log(bottle.Subtype + ": amount of " + entry.Subtype + " must be between 0.001 and 1000000, using the default recipe.");
                    return null;
                }
                items.Add(new MyBlueprintDefinitionBase.Item { Id = id, Amount = (MyFixedPoint)entry.Amount });
            }
            return items.ToArray();
        }

        static bool IsValid(float value, float min, float max)
        {
            return !float.IsNaN(value) && value >= min && value <= max;
        }

        public static void Log(string message)
        {
            MyLog.Default.WriteLineAndConsole(LogPrefix + message);
        }
    }
}
