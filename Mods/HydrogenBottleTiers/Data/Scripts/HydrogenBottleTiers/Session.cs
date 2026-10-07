using System;
using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders.Definitions;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.Components;
using VRage.Utils;

namespace HydrogenBottleTiers
{
    // Applies the server's bottle settings (capacity, mass, build time, recipe) to the definitions, on the
    // server and on every client. Runs once at world load and never updates, so it costs nothing while playing.
    // If this script ever fails (for example after a game update), the bottles still work with the defaults
    // from the .sbc files; only the config file and the HUD bottle count are lost.
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class HydrogenBottleTiersSession : MySessionComponentBase
    {
        public const string LogPrefix = "[HydrogenBottleTiers] ";

        const string ConfigFileName = "HydrogenBottleTiers.xml";
        const ushort MessageId = 47213;
        const byte RequestConfig = 1;
        const int RequestCooldownFrames = 300; // a client is answered at most once every 5 seconds

        // Our bottles and their assembly blueprints. The config can only change these.
        static readonly Dictionary<string, string> BlueprintByBottle = new Dictionary<string, string>
        {
            { "HydrogenBottleTier2", "Position0021_HydrogenBottleTier2" },
            { "HydrogenBottleTier3", "Position0022_HydrogenBottleTier3" },
            { "HydrogenBottleTier4", "Position0023_HydrogenBottleTier4" },
        };

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
            Type owner = typeof(HydrogenBottleTiersSession);
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
                using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(ConfigFileName, typeof(HydrogenBottleTiersSession)))
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
                string blueprintSubtype;
                if (bottle == null || bottle.Subtype == null || !BlueprintByBottle.TryGetValue(bottle.Subtype, out blueprintSubtype))
                {
                    if (logProblems)
                        Log("Unknown bottle in the config, ignored: " + (bottle == null ? "(empty)" : bottle.Subtype));
                    continue;
                }

                var itemId = new MyDefinitionId(typeof(MyObjectBuilder_GasContainerObject), bottle.Subtype);
                MyPhysicalItemDefinition itemDefinition;
                MyDefinitionManager.Static.TryGetPhysicalItemDefinition(itemId, out itemDefinition);
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
                            .Append("Capacity: ").Append(bottle.CapacityLitres).Append(" L of hydrogen");
                }
                else if (logProblems)
                    Log(bottle.Subtype + ": CapacityLitres must be between 1 and 1000000, using the default.");

                if (IsValid(bottle.MassKg, 0, 100000))
                    container.Mass = bottle.MassKg;
                else if (logProblems)
                    Log(bottle.Subtype + ": MassKg must be between 0 and 100000, using the default.");

                MyBlueprintDefinitionBase blueprint = MyDefinitionManager.Static.GetBlueprintDefinition(
                    new MyDefinitionId(typeof(MyObjectBuilder_BlueprintDefinition), blueprintSubtype));
                if (blueprint == null)
                {
                    if (logProblems)
                        Log("Blueprint missing for " + bottle.Subtype + ", is the mod installed correctly?");
                    continue;
                }

                if (IsValid(bottle.BuildTimeSeconds, 0.1f, 86400))
                    blueprint.BaseProductionTimeInSeconds = bottle.BuildTimeSeconds;
                else if (logProblems)
                    Log(bottle.Subtype + ": BuildTimeSeconds must be between 0.1 and 86400, using the default.");

                MyBlueprintDefinitionBase.Item[] recipe = BuildRecipe(bottle, logProblems);
                if (recipe != null)
                    blueprint.Prerequisites = recipe;
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
