using System;
using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders.Definitions;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.ModAPI;
using VRage.Utils;

namespace HydrogenBottleTiers
{
    // The bottle pips next to the jetpack fuel bar. The game's own counter (same Id) only counts the vanilla
    // hydrogen bottle; this one replaces it and counts every hydrogen bottle that isn't empty, of any tier or mod.
    // The game finds this class by itself and creates it only where there is a HUD, so never on a dedicated server.
    // It only reads the local player's inventory, once a second.
    public class HydrogenBottleHudStat : IMyHudStat
    {
        const int CheckIntervalFrames = 60;

        const string GasContainerTypeId = "MyObjectBuilder_GasContainerObject";
        static readonly MyDefinitionId HydrogenId = new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Hydrogen");

        // Whether an item type is a hydrogen bottle. Definitions don't change during a session.
        readonly Dictionary<MyDefinitionId, bool> isHydrogenBottle = new Dictionary<MyDefinitionId, bool>();

        int framesUntilCheck;
        float currentValue;
        string valueString = "0";
        bool failed;

        public MyStringHash Id { get; } = MyStringHash.GetOrCompute("player_hydrogen_bottles");
        public float CurrentValue => currentValue;
        public float MaxValue => 1f;
        public float MinValue => 0f;
        public string GetValueString() => valueString;

        public void Update()
        {
            if (failed || --framesUntilCheck > 0)
                return;
            framesUntilCheck = CheckIntervalFrames;

            try
            {
                SetValue(CountBottles());
            }
            catch (Exception e)
            {
                // Never break the HUD: stop counting and leave a line in the log.
                failed = true;
                SetValue(0);
                HydrogenBottleTiersSession.Log("HUD bottle count stopped after an error: " + e);
            }
        }

        int CountBottles()
        {
            IMyCharacter character = MyAPIGateway.Session?.Player?.Character;
            IMyInventory inventory = character?.GetInventory();
            if (inventory == null)
                return 0;

            int count = 0;
            for (int i = 0; i < inventory.ItemCount; i++)
            {
                // The light item info first; the full item (with its gas level) only for gas bottles.
                var info = inventory.GetItemAt(i);
                if (!info.HasValue || info.Value.Type.TypeId != GasContainerTypeId)
                    continue;
                IMyInventoryItem item = inventory.GetItemByID(info.Value.ItemId);
                var bottle = item?.Content as MyObjectBuilder_GasContainerObject;
                if (bottle != null && bottle.GasLevel > 1e-6f && IsHydrogenBottle(bottle.GetId()))
                    count += (int)item.Amount;
            }
            return count;
        }

        bool IsHydrogenBottle(MyDefinitionId id)
        {
            bool result;
            if (!isHydrogenBottle.TryGetValue(id, out result))
            {
                MyPhysicalItemDefinition definition;
                MyDefinitionManager.Static.TryGetPhysicalItemDefinition(id, out definition);
                var container = definition as MyOxygenContainerDefinition;
                result = container != null && container.StoredGasId == HydrogenId;
                isHydrogenBottle[id] = result;
            }
            return result;
        }

        void SetValue(int value)
        {
            if (value == currentValue)
                return;
            currentValue = value;
            valueString = value.ToString();
        }
    }
}
