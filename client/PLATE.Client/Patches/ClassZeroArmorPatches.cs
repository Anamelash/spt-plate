using System;
using EFT.InventoryLogic;
using HarmonyLib;

namespace PLATE.Client.Patches
{
    /// <summary>
    /// Restores the engine components EFT omits from class-zero armour.
    ///
    /// <see cref="ArmoredEquipment"/> creates Repairable, Buff and Armor only when
    /// the template class is greater than zero. PLATE's class zero is intentional: it
    /// is the sub-Br1 anti-fragment rung, and it still has a material, thickness and
    /// durability. Without these components the item does not enter the armour hit
    /// pipeline, and <see cref="ArmorPlate.Prefab"/> dereferences a null Armor while an
    /// inspect window builds mandatory helmet panels.
    ///
    /// This postfix repeats the three component-construction lines from the vanilla
    /// constructor, but only for a durable class-zero template that actually names a
    /// protected collider, and only when vanilla did not already create them. Ordinary
    /// hats also carry dummy class, material and durability fields in EFT's database;
    /// their empty protection metadata keeps them ordinary hats. The patch is always
    /// applied: the server can normalize armour independently of every client module
    /// switch.
    /// </summary>
    internal static class ClassZeroArmorPatches
    {
        private const string PatchLabel = "armor0:BootstrapPostfix";

        private static readonly AccessTools.FieldRef<ArmoredEquipment, RepairableComponent>
            RepairableRef = AccessTools.FieldRefAccess<ArmoredEquipment, RepairableComponent>(
                nameof(ArmoredEquipment.Repairable));

        private static readonly AccessTools.FieldRef<ArmoredEquipment, ArmorComponent>
            ArmorRef = AccessTools.FieldRefAccess<ArmoredEquipment, ArmorComponent>(
                nameof(ArmoredEquipment.Armor));

        private static readonly AccessTools.FieldRef<ArmoredEquipment, BuffComponent>
            BuffRef = AccessTools.FieldRefAccess<ArmoredEquipment, BuffComponent>(
                nameof(ArmoredEquipment.Buff));

        public static void Apply(Harmony harmony)
        {
            var target = PatchTargets.ArmoredEquipment_Ctor;
            if (target == null)
            {
                PatchStats.MarkFailed(null, PatchLabel, "target not resolved");
                Plugin.Log.LogError(
                    "[PLATE] Class-zero armour: ArmoredEquipment constructor not resolved, skipped");
                return;
            }

            try
            {
                harmony.Patch(target,
                    postfix: new HarmonyMethod(typeof(ClassZeroArmorPatches),
                        nameof(BootstrapPostfix)));
                PatchStats.Track(harmony, target, PatchLabel);
            }
            catch (Exception ex)
            {
                PatchStats.MarkFailed(target, PatchLabel, ex.Message);
                Plugin.Log.LogError(
                    $"[PLATE] Class-zero armour: failed to patch {target.Name}: {ex}");
            }
        }

        private static void BootstrapPostfix(ArmoredEquipment __instance,
            ArmoredEquipmentTemplate template)
        {
            PatchStats.Hit(PatchLabel);
            RestoreComponents(__instance, template);
        }

        internal static bool RestoreComponents(ArmoredEquipment item,
            ArmoredEquipmentTemplate template)
        {
            if (item == null || template == null || template.MaxDurability <= 0 ||
                template.armorClass != 0 || item.Armor != null || item.Repairable != null)
            {
                return false;
            }

            var protectsBody = template.armorColliders?.Length > 0;
            var protectsPlateZone = template.armorPlateColliders?.Length > 0;
            if (!protectsBody && !protectsPlateZone)
            {
                return false;
            }

            var repairable = new RepairableComponent(item, template);
            RepairableRef(item) = repairable;
            item.Components.Add(repairable);

            var buff = new BuffComponent(item);
            BuffRef(item) = buff;
            item.Components.Add(buff);

            var armor = new CompositeArmorComponent(item, template, repairable,
                item.Togglable, buff);
            ArmorRef(item) = armor;
            item.Components.Add(armor);
            return true;
        }
    }
}
