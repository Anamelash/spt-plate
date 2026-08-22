using System.Text.RegularExpressions;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace PLATE.Server.Services;

/// <summary>What a weapon part is, as far as muzzle velocity is concerned.</summary>
public enum PartRole
{
    /// <summary>A weapon: it declares a chambering of its own.</summary>
    Weapon,

    /// <summary>A barrel item, whatever it is called.</summary>
    Barrel,

    /// <summary>
    /// Not a barrel item, but the barrel is built into it — an MP5SD upper receiver
    /// carries a ported 146 mm barrel that never exists as an item of its own.
    /// </summary>
    IntegratedBarrel,

    /// <summary>A brake, a flash hider, a suppressor: it screws onto a barrel.</summary>
    MuzzleDevice,

    /// <summary>Everything else that happens to carry a velocity modifier.</summary>
    Unknown,
}

/// <summary>
/// Tells a barrel from a muzzle device, by what the item database says rather than by
/// what the item is called.
///
/// The naming convention only holds for vanilla items: weapon packs register their
/// content through WTT's CustomItemService, which names a clone "[Pack]_(whatever the
/// locale calls it)", so every modded barrel in the game fails a "_name starts with
/// barrel_" test. Reading the role off the item's class, its place in the slot graph
/// and the properties only barrels carry works for both.
///
/// Deliberately asymmetric: a clamp — the thing that says "you are a muzzle brake, you
/// may not change velocity by 20%" — needs positive evidence, and anything unrecognized
/// is left alone and reported. Mistaking a barrel for a brake hands an 8.5 inch .300
/// BLK the ballistics of a 16 inch one and nothing says so; mistaking a handguard for
/// something unclassified costs a line in a report.
/// </summary>
public sealed class PartClassifier
{
    /// <summary>Class nodes items hang off. Vanilla ids; a mod's subclass is found by walking up.</summary>
    private static readonly MongoId BarrelClass = new("555ef6e44bdc2de9068b457e");

    private static readonly MongoId SilencerClass = new("550aa4cd4bdc2dd8348b456c");
    private static readonly MongoId FlashHiderClass = new("550aa4bf4bdc2dd6348b456b");
    private static readonly MongoId MuzzleComboClass = new("550aa4dd4bdc2dc9348b4569");

    private const string BarrelSlot = "mod_barrel";
    private const string MuzzleSlotPrefix = "mod_muzzle";
    private const string MagazineSlotPrefix = "mod_magazine";

    /// <summary>
    /// Slots that can lead to a barrel. Following anything else lets the walk escape
    /// into the shared accessory graph — a rail that fits fifty weapons reaches all of
    /// their barrels.
    /// </summary>
    private static readonly string[] BarrelPath =
        ["mod_barrel", "mod_reciever", "mod_receiver", "mod_handguard", "mod_gas_block"];

    /// <summary>
    /// A part whose name says the barrel is inside it. Read off the item's name only,
    /// never its description: the MPX-SD suppressors are described as going "over the
    /// custom ported barrel", which is a statement about the barrel item next to them.
    /// </summary>
    private static readonly Regex IntegralWord =
        new(@"integral|integrated|ported|интегр|портир", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BarrelWord =
        new(@"barrel|suppressor|silencer|ствол|глушител", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly Dictionary<MongoId, TemplateItem> _items;
    private readonly IReadOnlyDictionary<string, string>? _locale;

    /// <summary>Slot names each item is listed under, anywhere in the database.</summary>
    private readonly Dictionary<MongoId, HashSet<string>> _sitsIn = new();

    /// <summary>Slot names each item offers.</summary>
    private readonly Dictionary<MongoId, HashSet<string>> _owns = new();

    /// <summary>
    /// Parts that carry a weapon's barrel without being one, to the weapon that proves
    /// it. A default MongoId means several weapons claim the part and none of them can
    /// be held responsible for its number.
    /// </summary>
    private readonly Dictionary<MongoId, MongoId> _integratedHost = new();

    private readonly Dictionary<MongoId, bool> _isBarrel = new();

    public PartClassifier(Dictionary<MongoId, TemplateItem> items,
        IReadOnlyDictionary<string, string>? locale)
    {
        _items = items;
        _locale = locale;

        foreach (var item in items.Values)
        {
            var slots = item.Properties?.Slots;
            if (slots == null)
            {
                continue;
            }

            foreach (var slot in slots)
            {
                var slotName = slot.Name ?? "";
                Add(_owns, item.Id, slotName);

                foreach (var id in slot.Properties?.Filters?.SelectMany(f => f.Filter ?? []) ?? [])
                {
                    Add(_sitsIn, id, slotName);
                }
            }
        }

        FindIntegratedBarrels();
    }

    /// <summary>The role the item plays, in the order the evidence is worth trusting.</summary>
    public PartRole RoleOf(TemplateItem item)
    {
        if (!string.IsNullOrEmpty(item.Properties?.AmmoCaliber))
        {
            return PartRole.Weapon;
        }

        if (IsBarrel(item))
        {
            return PartRole.Barrel;
        }

        if (CarriesIntegratedBarrel(item))
        {
            return PartRole.IntegratedBarrel;
        }

        return IsMuzzleDevice(item) ? PartRole.MuzzleDevice : PartRole.Unknown;
    }

    /// <summary>
    /// Which of the barrel signals fired, for the report: the same verdict reached
    /// through the slot graph and through a naming convention are worth different
    /// amounts of trust, and only the report can say which one it was.
    /// </summary>
    public string BarrelEvidence(TemplateItem item)
    {
        if (InClass(item, BarrelClass))
        {
            return "by class";
        }

        if (SitsIn(item.Id, BarrelSlot))
        {
            return "by slot";
        }

        return HasBarrelProperties(item) ? "by props" : "by name";
    }

    /// <summary>
    /// A barrel item. Three independent witnesses, any one of which is enough:
    ///
    /// - its class is Barrel (all 174 vanilla barrels, and every pack barrel seen so
    ///   far — WTT clones keep the donor's class even when they rewrite every property);
    /// - some weapon lists it in a mod_barrel slot, and nothing but a barrel is ever
    ///   listed there;
    /// - it carries CenterOfImpact and ShotgunDispersion, which in the whole vanilla
    ///   database only barrels and whole weapons have.
    ///
    /// The name is checked last and separately (<see cref="LooksLikeBarrelByName"/>)
    /// because it is the one witness a pack breaks by construction.
    /// </summary>
    public bool IsBarrel(TemplateItem item)
    {
        if (_isBarrel.TryGetValue(item.Id, out var known))
        {
            return known;
        }

        var verdict = InClass(item, BarrelClass)
                      || SitsIn(item.Id, BarrelSlot)
                      || HasBarrelProperties(item)
                      || LooksLikeBarrelByName(item);

        _isBarrel[item.Id] = verdict;
        return verdict;
    }

    /// <summary>The vanilla naming convention, and nothing a weapon pack produces.</summary>
    public static bool LooksLikeBarrelByName(TemplateItem item) =>
        (item.Name ?? "").StartsWith("barrel_", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The weapon whose barrel this part is, or null when the part is not one or when
    /// several weapons claim it. Used to work out what the part itself has to
    /// contribute: the game adds the weapon's modifier to the part's.
    /// </summary>
    public TemplateItem? IntegratedHost(MongoId id) =>
        _integratedHost.TryGetValue(id, out var host) && host != default && _items.TryGetValue(host, out var weapon)
            ? weapon
            : null;

    /// <summary>
    /// Strings that might carry a barrel length or a caliber, best witness first. The
    /// name comes first because a WTT clone builds it out of the very locale entry that
    /// follows it, and because a locale may be missing altogether.
    /// </summary>
    public IEnumerable<string> TextOf(TemplateItem item)
    {
        foreach (var text in NameTextOf(item))
        {
            yield return text;
        }

        var description = Localized(item.Id, "Description");
        if (description.Length > 0)
        {
            yield return description;
        }
    }

    /// <summary>What the item is called, without the prose.</summary>
    public IEnumerable<string> NameTextOf(TemplateItem item)
    {
        var name = item.Name ?? "";
        if (name.Length > 0)
        {
            yield return name;
        }

        foreach (var key in new[] { "Name", "ShortName" })
        {
            var text = Localized(item.Id, key);
            if (text.Length > 0)
            {
                yield return text;
            }
        }
    }

    /// <summary>
    /// A part with a barrel inside it. Two structural signals and one from the name:
    ///
    /// - it owns the muzzle slot, and no weapon that can mount it has a barrel item
    ///   anywhere in its tree. Muzzle threading belongs to a barrel, so whatever owns
    ///   the slot is a barrel or has one inside — unless the barrel exists separately,
    ///   which is what the second half rules out. In vanilla this is exactly the two
    ///   MP5 upper receivers: Glock slides and 1911 frames own a muzzle slot too, but
    ///   their pistols hold the barrel in a mod_barrel slot;
    /// - it offers a magazine slot without being a weapon, which makes it a weapon in
    ///   everything but its class;
    /// - its name says the barrel is integral to it, as VSS and AS VAL both do.
    /// </summary>
    private bool CarriesIntegratedBarrel(TemplateItem item)
    {
        if (_integratedHost.ContainsKey(item.Id) || Owns(item.Id, MagazineSlotPrefix))
        {
            return true;
        }

        return NameTextOf(item).Any(t => IntegralWord.IsMatch(t) && BarrelWord.IsMatch(t));
    }

    /// <summary>
    /// A brake, a flash hider or a suppressor: it is one of their classes, or it is
    /// listed in a muzzle slot, where nothing else goes.
    /// </summary>
    private bool IsMuzzleDevice(TemplateItem item) =>
        InClass(item, SilencerClass) || InClass(item, FlashHiderClass) || InClass(item, MuzzleComboClass)
        || SitsInPrefixed(item.Id, MuzzleSlotPrefix);

    /// <summary>
    /// Properties only a barrel has among the parts: the whole vanilla database has 174
    /// items carrying both, and all 174 are barrels. Weapons carry them too and are
    /// separated before this is asked.
    /// </summary>
    private static bool HasBarrelProperties(TemplateItem item)
    {
        var p = item.Properties;
        return p?.CenterOfImpact != null && (p.ShotgunDispersion != null || p.shotgunDispersion != null);
    }

    /// <summary>
    /// Walks every weapon whose barrel does not come off and marks the part that owns
    /// its muzzle. The walk follows the barrel path only, so it cannot wander into the
    /// accessory graph and label a rail that fits fifty weapons.
    /// </summary>
    private void FindIntegratedBarrels()
    {
        foreach (var weapon in _items.Values)
        {
            if (string.IsNullOrEmpty(weapon.Properties?.AmmoCaliber))
            {
                continue;
            }

            var tree = TreeUnder(weapon);
            if (tree.Any(id => _items.TryGetValue(id, out var part) && IsBarrel(part)))
            {
                continue; // the barrel is an item of its own; nothing here carries it
            }

            foreach (var id in tree)
            {
                if (!Owns(id, MuzzleSlotPrefix) || !_items.TryGetValue(id, out var part) || IsMuzzleDevice(part))
                {
                    continue;
                }

                // two weapons owning the same carrier means neither can be held
                // responsible for its modifier; the default id records that
                _integratedHost[id] = _integratedHost.TryGetValue(id, out var seen) && seen != weapon.Id
                    ? default
                    : weapon.Id;
            }
        }
    }

    /// <summary>Everything reachable from an item along the barrel path, itself excluded.</summary>
    private List<MongoId> TreeUnder(TemplateItem root)
    {
        var seen = new HashSet<MongoId>();
        var queue = new Queue<TemplateItem>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var slot in current.Properties?.Slots ?? [])
            {
                if (!BarrelPath.Contains(slot.Name ?? "", StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var id in slot.Properties?.Filters?.SelectMany(f => f.Filter ?? []) ?? [])
                {
                    if (seen.Add(id) && _items.TryGetValue(id, out var child))
                    {
                        queue.Enqueue(child);
                    }
                }
            }
        }

        return seen.ToList();
    }

    /// <summary>Item ids reachable from a weapon along the barrel path that are barrels.</summary>
    public IEnumerable<MongoId> BarrelsUnder(TemplateItem weapon) =>
        TreeUnder(weapon).Where(id => _items.TryGetValue(id, out var part) && IsBarrel(part));

    /// <summary>
    /// Whether the item hangs off the given class, however many subclasses deep. A pack
    /// is free to register "PackBarrel" under Barrel, and one level of comparison would
    /// miss every item in it.
    /// </summary>
    private bool InClass(TemplateItem item, MongoId classId)
    {
        var current = item.Parent;
        var guard = 0;

        while (current != default && guard++ < 32)
        {
            if (current == classId)
            {
                return true;
            }

            if (!_items.TryGetValue(current, out var parent))
            {
                return false;
            }

            current = parent.Parent;
        }

        return false;
    }

    private string Localized(MongoId id, string key) =>
        _locale != null && _locale.TryGetValue($"{id} {key}", out var text) ? text ?? "" : "";

    private bool SitsIn(MongoId id, string slotName) =>
        _sitsIn.TryGetValue(id, out var slots) && slots.Contains(slotName);

    private bool SitsInPrefixed(MongoId id, string prefix) =>
        _sitsIn.TryGetValue(id, out var slots)
        && slots.Any(s => s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private bool Owns(MongoId id, string prefix) =>
        _owns.TryGetValue(id, out var slots)
        && slots.Any(s => s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static void Add(Dictionary<MongoId, HashSet<string>> into, MongoId id, string slotName)
    {
        if (!into.TryGetValue(id, out var set))
        {
            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            into[id] = set;
        }

        set.Add(slotName);
    }
}
