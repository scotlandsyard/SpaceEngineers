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
    // The oxygen and hydrogen bottle pips next to the suit's oxygen and fuel bars. The game's own counters
    // (same Ids) only count the vanilla bottles; these replace them and count every bottle of that gas that
    // isn't empty, of any tier or mod. The game finds these classes by itself and creates them only where there
    // is a HUD, so never on a dedicated server. They only read the local player's inventory, once a second.
    public class OxygenBottleHudStat : BottleHudStat
    {
        public OxygenBottleHudStat() : base("player_oxygen_bottles", "Oxygen") { }
    }

    public class HydrogenBottleHudStat : BottleHudStat
    {
        public HydrogenBottleHudStat() : base("player_hydrogen_bottles", "Hydrogen") { }
    }

    public abstract class BottleHudStat : IMyHudStat
    {
        const int CheckIntervalFrames = 60;

        // Oxygen bottles are OxygenContainerObject, hydrogen bottles GasContainerObject (its base type).
        const string GasContainerTypeId = "MyObjectBuilder_GasContainerObject";
        const string OxygenContainerTypeId = "MyObjectBuilder_OxygenContainerObject";

        readonly MyDefinitionId gasId;

        // Whether an item type is a bottle of this gas. Definitions don't change during a session.
        readonly Dictionary<MyDefinitionId, bool> isBottleOfGas = new Dictionary<MyDefinitionId, bool>();

        int framesUntilCheck;
        float currentValue;
        string valueString = "0";
        bool failed;

        protected BottleHudStat(string statId, string gas)
        {
            Id = MyStringHash.GetOrCompute(statId);
            gasId = new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), gas);
        }

        public MyStringHash Id { get; }
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
                HydrogenBottleTiersSession.Log("HUD bottle count (" + Id.String + ") stopped after an error: " + e);
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
                if (!info.HasValue)
                    continue;
                string typeId = info.Value.Type.TypeId;
                if (typeId != GasContainerTypeId && typeId != OxygenContainerTypeId)
                    continue;
                IMyInventoryItem item = inventory.GetItemByID(info.Value.ItemId);
                var bottle = item?.Content as MyObjectBuilder_GasContainerObject;
                if (bottle != null && bottle.GasLevel > 1e-6f && IsBottleOfGas(bottle.GetId()))
                    count += (int)item.Amount;
            }
            return count;
        }

        bool IsBottleOfGas(MyDefinitionId id)
        {
            bool result;
            if (!isBottleOfGas.TryGetValue(id, out result))
            {
                MyPhysicalItemDefinition definition;
                MyDefinitionManager.Static.TryGetPhysicalItemDefinition(id, out definition);
                var container = definition as MyOxygenContainerDefinition;
                result = container != null && container.StoredGasId == gasId;
                isBottleOfGas[id] = result;
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
