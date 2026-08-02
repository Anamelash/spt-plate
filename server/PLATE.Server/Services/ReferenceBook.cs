using System.Text.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Utils;

namespace PLATE.Server.Services;

/// <summary>
/// In-mod reference book of real ammunition prototype specs (ammo-reference.jsonc).
/// Masses/velocities/charges from open sources — anchors for buckshot
/// normalization and grenade fragment physics. The file sits next to the config
/// and is hand-editable; a default is created when missing.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class ReferenceBook(ISptLogger<ReferenceBook> logger)
{
    public const string FileName = "ammo-reference.jsonc";

    public class ShotshellRef
    {
        /// <summary>Prototype name (for the report/locale).</summary>
        public string Prototype { get; set; } = "";

        /// <summary>Mass of a single pellet/dart, g.</summary>
        public double PelletMassG { get; set; }

        /// <summary>Muzzle velocity, m/s (0 = leave the in-game value).</summary>
        public double V0 { get; set; }

        /// <summary>Expansiveness index X (lead shot ~0.7, steel dart ~0.05).</summary>
        public double X { get; set; } = 0.7;

        /// <summary>Pellet count per shell (charge mass / pellet mass). 0 = leave the in-game value.</summary>
        public int PelletCount { get; set; }
    }

    public class GrenadeRef
    {
        public string Prototype { get; set; } = "";

        /// <summary>Lethal fragment mass, g (spec average).</summary>
        public double FragMassG { get; set; }

        /// <summary>Initial fragment velocity, m/s.</summary>
        public double FragV0 { get; set; }

        /// <summary>Explosive mass in TNT equivalent, g (for the blast).</summary>
        public double TntG { get; set; }
    }

    public class BlastAnchorRef
    {
        /// <summary>Anchor grenade: its vanilla Strength is taken as "correct".</summary>
        public string Name { get; set; } = "RGD-5";

        public double Strength { get; set; } = 100;

        public double TntG { get; set; } = 110;
    }

    public class BarrelRef
    {
        /// <summary>Weapon whose barrel the cartridge's InitialSpeed is quoted for.</summary>
        public string Prototype { get; set; } = "";

        /// <summary>That weapon's barrel length, mm. A barrel this long changes nothing.</summary>
        public double RefMm { get; set; }

        /// <summary>
        /// Le Duc constant, mm. Fitted to a published barrel-length ladder where one
        /// exists; 0 means "work it out from the case", which is good to about ±35%.
        /// </summary>
        public double C { get; set; }

        /// <summary>Case capacity, mm³ (1 grain of water = 64.8 mm³). Only used when C is 0.</summary>
        public double CaseMm3 { get; set; }

        /// <summary>Bore diameter, mm. Only used when C is 0.</summary>
        public double BoreMm { get; set; }
    }

    public class WeaponBarrelRef
    {
        public string Prototype { get; set; } = "";

        /// <summary>Barrel length of the real weapon, mm.</summary>
        public double LengthMm { get; set; }
    }

    /// <summary>
    /// Physical properties of an armour material. Which of them matter depends on how
    /// the material fails, so each carries only what its own class of penetration
    /// mechanics consumes — a shear strength means nothing for a ceramic and a
    /// compressive strength means nothing for a woven fibre.
    /// </summary>
    public class ArmorMaterialRef
    {
        /// <summary>Ductile | Brittle | Fibrous — decides which penetration model applies.</summary>
        public string Class { get; set; } = "Ductile";

        /// <summary>g/cm³.</summary>
        public double DensityGCm3 { get; set; }

        /// <summary>Ductile: yield strength, MPa.</summary>
        public double YieldMPa { get; set; }

        /// <summary>Ductile: ultimate shear strength, MPa — the plug-punching term.</summary>
        public double ShearMPa { get; set; }

        /// <summary>Brittle: compressive strength, MPa — what a ceramic actually resists with.</summary>
        public double CompressiveMPa { get; set; }

        /// <summary>Fibrous: fibre tensile strength, MPa.</summary>
        public double FibreTensileMPa { get; set; }

        /// <summary>Fibrous: strain to failure, fraction — how far the cone stretches.</summary>
        public double FailureStrain { get; set; }

        /// <summary>
        /// Share of a plate's mass that is the hard element. A steel plate is all steel;
        /// a ceramic one is a strike face on a fibre backer, and counting the backer as
        /// ceramic would make every plate read thicker than it is.
        /// </summary>
        public double HardMassFraction { get; set; } = 1.0;

        public string Source { get; set; } = "";
    }

    /// <summary>
    /// What a piece of armour is really made of. Keyed by the product, not by the
    /// in-game class: the class is a consequence of the construction, not its cause,
    /// and mods move it around freely. One entry covers every zone of the product —
    /// front, back, side, groin are the same plate.
    /// </summary>
    public class ArmorPlateRef
    {
        public string Prototype { get; set; } = "";

        /// <summary>Overrides the game's material only when it is wrong; empty = keep it.</summary>
        public string Material { get; set; } = "";

        /// <summary>Thickness of the hard element, mm. This is what the game has nowhere.</summary>
        public double ThicknessMm { get; set; }

        /// <summary>Backing package behind the plate, mm of fibre (0 = none).</summary>
        public double BackingMm { get; set; }

        public string Source { get; set; } = "";
    }

    public class AmmoReference
    {
        /// <summary>Key — the cartridge template's _name in the DB.</summary>
        public Dictionary<string, ShotshellRef> Shotshells { get; set; } = new();

        /// <summary>Key — the grenade template's _name in the DB.</summary>
        public Dictionary<string, GrenadeRef> Grenades { get; set; } = new();

        /// <summary>Key — the caliber id (ammoCaliber on the weapon template).</summary>
        public Dictionary<string, BarrelRef> Barrels { get; set; } = new();

        /// <summary>Key — the game's ArmorMaterial id.</summary>
        public Dictionary<string, ArmorMaterialRef> ArmorMaterials { get; set; } = new();

        /// <summary>Key — the armour product, the item name up to "_level".</summary>
        public Dictionary<string, ArmorPlateRef> ArmorPlates { get; set; } = new();

        /// <summary>
        /// Key — "Material/Class". The plate a real one of that rating would be, used for
        /// the ones the game invented.
        /// </summary>
        public Dictionary<string, ArmorPlateRef> ArmorByClass { get; set; } = new();

        /// <summary>
        /// Key — the weapon template's _name. Only for weapons whose barrel does not
        /// come off, so its length cannot be read from a barrel item.
        /// </summary>
        public Dictionary<string, WeaponBarrelRef> Weapons { get; set; } = new();

        public BlastAnchorRef BlastAnchor { get; set; } = new();
    }

    private AmmoReference _cached;

    public AmmoReference Load(string modPath)
    {
        if (_cached != null)
        {
            return _cached;
        }

        var path = System.IO.Path.Combine(modPath, FileName);
        if (!File.Exists(path))
        {
            File.WriteAllText(path, DefaultReferenceJsonc);
            logger.Debug($"[PLATE] Ammo reference written: {path}");
        }

        try
        {
            _cached = Parse(File.ReadAllText(path)) ?? new AmmoReference();
        }
        catch (Exception ex)
        {
            logger.Error($"[PLATE] Failed to parse {FileName}, reference disabled: {ex.Message}");
            _cached = new AmmoReference();
        }

        FillMissingSections(_cached);
        return _cached;
    }

    private static AmmoReference? Parse(string json)
    {
        return JsonSerializer.Deserialize<AmmoReference>(json, new JsonSerializerOptions
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
        });
    }

    /// <summary>
    /// The file is written once and then only read, so a section added in a later
    /// version would never reach anyone who already has one — the feature behind it
    /// would silently do nothing on every existing install. Sections the file does not
    /// have are taken from the shipped defaults; sections it does have are left alone,
    /// including whatever the user edited into them.
    /// </summary>
    private void FillMissingSections(AmmoReference loaded)
    {
        var filled = MergeShippedDefaults(loaded);
        if (filled.Count > 0)
        {
            logger.Debug($"[PLATE] {FileName} predates these sections, using the shipped ones: " +
                         string.Join(", ", filled));
        }
    }

    /// <summary>Fills empty sections from the shipped book; returns what it filled.</summary>
    public static List<string> MergeShippedDefaults(AmmoReference loaded)
    {
        AmmoReference? shipped = null;
        AmmoReference Shipped() => shipped ??= Parse(DefaultReferenceJsonc) ?? new AmmoReference();

        var filled = new List<string>();

        if (loaded.Shotshells.Count == 0)
        {
            loaded.Shotshells = Shipped().Shotshells;
            filled.Add(nameof(loaded.Shotshells));
        }

        if (loaded.Grenades.Count == 0)
        {
            loaded.Grenades = Shipped().Grenades;
            filled.Add(nameof(loaded.Grenades));
        }

        if (loaded.Barrels.Count == 0)
        {
            loaded.Barrels = Shipped().Barrels;
            filled.Add(nameof(loaded.Barrels));
        }

        if (loaded.Weapons.Count == 0)
        {
            loaded.Weapons = Shipped().Weapons;
            filled.Add(nameof(loaded.Weapons));
        }

        if (loaded.ArmorMaterials.Count == 0)
        {
            loaded.ArmorMaterials = Shipped().ArmorMaterials;
            filled.Add(nameof(loaded.ArmorMaterials));
        }

        if (loaded.ArmorPlates.Count == 0)
        {
            loaded.ArmorPlates = Shipped().ArmorPlates;
            filled.Add(nameof(loaded.ArmorPlates));
        }

        if (loaded.ArmorByClass.Count == 0)
        {
            loaded.ArmorByClass = Shipped().ArmorByClass;
            filled.Add(nameof(loaded.ArmorByClass));
        }

        return filled;
    }

    /// <summary>
    /// Default reference book. Lead pellet masses: m = ρ·π·d³/6, ρ=11.34 g/cm³.
    /// Grenade fragments/charges: open-source specs (Soviet manuals, Jane's, wikis).
    /// </summary>
    private const string DefaultReferenceJsonc =
        """
        {
          // ===== Birdshot and buckshot: pellet mass (g), velocity (m/s), X, pellet count =====
          // Lead pellet mass: ρ·π·d³/6 (ρ=11.34). V0=0 — leave the in-game velocity alone.
          // X: a lead ball deforms (~0.7), a steel dart keeps its shape (~0.05).
          // PelletCount = charge mass / pellet mass (12/70 ~32 g, 20/70 ~24 g, 23x75 ~40 g);
          // vanilla puts 8 into almost everything — small buckshot is underloaded 2-4.5x.
          "Shotshells": {
            "patron_12x70_buckshot":       { "Prototype": "12/70 buckshot 7.0mm",  "PelletMassG": 2.04, "V0": 400, "X": 0.7, "PelletCount": 16 },
            "patron_12x70_buckshot_525":   { "Prototype": "12/70 buckshot 5.25mm", "PelletMassG": 0.86, "V0": 390, "X": 0.7, "PelletCount": 36 },
            "patron_12x70_buckshot_65":    { "Prototype": "12/70 buckshot 6.5mm",  "PelletMassG": 1.63, "V0": 400, "X": 0.7, "PelletCount": 20 },
            "patron_12x70_buckshot_85":    { "Prototype": "12/70 magnum 8.5mm",   "PelletMassG": 3.65, "V0": 385, "X": 0.7, "PelletCount": 9 },
            "patron_12x70_flechette":      { "Prototype": "12/70 flechette (steel)", "PelletMassG": 0.65, "V0": 550, "X": 0.05, "PelletCount": 20 },
            "patron_12x70_piranha":        { "Prototype": "12/70 darts 2mm",    "PelletMassG": 0.50, "V0": 550, "X": 0.05, "PelletCount": 0 },
            "patron_20x70_buckshot":       { "Prototype": "20/70 buckshot 7.5mm",  "PelletMassG": 2.50, "V0": 390, "X": 0.7, "PelletCount": 10 },
            "patron_20x70_buckshot_56":    { "Prototype": "20/70 buckshot 5.6mm",  "PelletMassG": 1.03, "V0": 400, "X": 0.7, "PelletCount": 23 },
            "patron_20x70_buckshot_62":    { "Prototype": "20/70 buckshot 6.2mm",  "PelletMassG": 1.41, "V0": 400, "X": 0.7, "PelletCount": 17 },
            "patron_20x70_buckshot_73":    { "Prototype": "20/70 buckshot 7.3mm",  "PelletMassG": 2.31, "V0": 405, "X": 0.7, "PelletCount": 10 },
            "patron_20x70_flechette":      { "Prototype": "20/70 flechette (steel)", "PelletMassG": 0.65, "V0": 520, "X": 0.05, "PelletCount": 14 },
            "patron_23x75_shrapnel_10":    { "Prototype": "23x75 Shrapnel-10",    "PelletMassG": 3.40, "V0": 270, "X": 0.7, "PelletCount": 14 },
            "patron_23x75_shrapnel_25":    { "Prototype": "23x75 Shrapnel-25",    "PelletMassG": 3.40, "V0": 375, "X": 0.7, "PelletCount": 18 }
          },

          // ===== Grenades: fragment (g, m/s) and explosive charge in TNT equivalent (g) =====
          // Fragment damage = E/EnergyPerHp (ammo normalizer config), penetration from E/A.
          // In-game fragment count is left alone (performance); the physics is brought to spec.
          "Grenades": {
            "weapon_grenade_f1":                { "Prototype": "F-1",     "FragMassG": 1.50, "FragV0": 730,  "TntG": 60 },
            "weapon_grenade_f1_event":          { "Prototype": "F-1",     "FragMassG": 1.50, "FragV0": 730,  "TntG": 60 },
            "RGD-5":                            { "Prototype": "RGD-5",   "FragMassG": 0.40, "FragV0": 1000, "TntG": 110 },
            "weapon_grenade_rgn":               { "Prototype": "RGN",     "FragMassG": 0.42, "FragV0": 700,  "TntG": 112 },
            "weapon_grenade_rgo":               { "Prototype": "RGO",     "FragMassG": 0.46, "FragV0": 1200, "TntG": 106 },
            "weapon_grenade_m67":               { "Prototype": "M67",     "FragMassG": 0.35, "FragV0": 1400, "TntG": 250 },
            "weapon_grenade_chattabka_vog17":   { "Prototype": "VOG-17M", "FragMassG": 0.50, "FragV0": 1100, "TntG": 36 },
            "weapon_grenade_chattabka_vog25":   { "Prototype": "VOG-25",  "FragMassG": 0.40, "FragV0": 1100, "TntG": 48 },
            "weapon_grenade_v40":               { "Prototype": "V40",     "FragMassG": 0.15, "FragV0": 800,  "TntG": 20 }
          },

          // ===== Barrels: muzzle velocity by barrel length =====
          // v(L) = v_inf*L/(L+C) — Le Duc. RefMm is the barrel the cartridge's in-game
          // InitialSpeed belongs to (the service weapon of the caliber), so a barrel of
          // that length gets a 0% modifier and everything else is relative to it.
          // C comes from a published barrel-length ladder where one exists; C=0 means
          // "derive it from the case" (1.67*CaseMm3/bore area, good to about +-35%).
          // CaseMm3: 1 grain of water = 64.8 mm3.
          "Barrels": {
            // --- C measured against published ladders ---
            "Caliber762x51":     { "Prototype": "M14, 559 mm",        "RefMm": 559, "C": 129, "CaseMm3": 3640, "BoreMm": 7.82 },
            "Caliber556x45NATO": { "Prototype": "M16A2, 508 mm",      "RefMm": 508, "C": 134, "CaseMm3": 1850, "BoreMm": 5.70 },
            "Caliber762x39":     { "Prototype": "AKM, 415 mm",        "RefMm": 415, "C": 68,  "CaseMm3": 2310, "BoreMm": 7.92 },
            "Caliber762x35":     { "Prototype": ".300 BLK, 406 mm",   "RefMm": 406, "C": 58,  "CaseMm3": 1670, "BoreMm": 7.82 },
            "Caliber9x19PARA":   { "Prototype": "pistol, 120 mm",     "RefMm": 120, "C": 24,  "CaseMm3": 860,  "BoreMm": 9.01 },
            "Caliber9x33R":      { "Prototype": "revolver, 152 mm",   "RefMm": 152, "C": 56,  "CaseMm3": 1620, "BoreMm": 9.07 },

            // --- C derived from the case: case volumes below are approximate ---
            "Caliber545x39":     { "Prototype": "AK-74, 415 mm",      "RefMm": 415, "C": 0, "CaseMm3": 1850, "BoreMm": 5.60 },
            "Caliber762x54R":    { "Prototype": "SVD, 620 mm",        "RefMm": 620, "C": 0, "CaseMm3": 4150, "BoreMm": 7.92 },
            "Caliber9x39":       { "Prototype": "AS Val, 200 mm",     "RefMm": 200, "C": 0, "CaseMm3": 1600, "BoreMm": 9.25 },
            "Caliber366TKM":     { "Prototype": "VPO-209, 415 mm",    "RefMm": 415, "C": 0, "CaseMm3": 2200, "BoreMm": 9.50 },
            "Caliber1143x23ACP": { "Prototype": "M1911, 127 mm",      "RefMm": 127, "C": 0, "CaseMm3": 1620, "BoreMm": 11.50 },
            "Caliber762x25TT":   { "Prototype": "TT, 116 mm",         "RefMm": 116, "C": 0, "CaseMm3": 1170, "BoreMm": 7.87 },
            "Caliber9x18PM":     { "Prototype": "PM, 93 mm",          "RefMm": 93,  "C": 0, "CaseMm3": 840,  "BoreMm": 9.27 },
            // PMM is the hot 9x18 load in the same case, so the same geometry
            "Caliber9x18PMM":    { "Prototype": "PMM, 93 mm",         "RefMm": 93,  "C": 0, "CaseMm3": 840,  "BoreMm": 9.27 },
            "Caliber9x21":       { "Prototype": "SR-1, 120 mm",       "RefMm": 120, "C": 0, "CaseMm3": 1100, "BoreMm": 9.00 },
            "Caliber57x28":      { "Prototype": "P90, 263 mm",        "RefMm": 263, "C": 0, "CaseMm3": 1430, "BoreMm": 5.70 },
            "Caliber46x30":      { "Prototype": "MP7, 180 mm",        "RefMm": 180, "C": 0, "CaseMm3": 970,  "BoreMm": 4.65 },
            "Caliber68x51":      { "Prototype": "XM7, 330 mm",        "RefMm": 330, "C": 0, "CaseMm3": 3890, "BoreMm": 7.00 },
            "Caliber86x70":      { "Prototype": ".338 LM, 690 mm",    "RefMm": 690, "C": 0, "CaseMm3": 7390, "BoreMm": 8.60 },
            "Caliber127x55":     { "Prototype": "ASh-12, 420 mm",     "RefMm": 420, "C": 0, "CaseMm3": 2590, "BoreMm": 12.70 },
            "Caliber127x33":     { "Prototype": "SR-2 class, 400 mm", "RefMm": 400, "C": 0, "CaseMm3": 3050, "BoreMm": 12.70 },
            "Caliber12g":        { "Prototype": "shotgun, 660 mm",    "RefMm": 660, "C": 0, "CaseMm3": 4500, "BoreMm": 18.50 },
            "Caliber20g":        { "Prototype": "shotgun, 660 mm",    "RefMm": 660, "C": 0, "CaseMm3": 3600, "BoreMm": 15.60 },
            "Caliber23x75":      { "Prototype": "KS-23, 510 mm",      "RefMm": 510, "C": 0, "CaseMm3": 5000, "BoreMm": 23.00 },

            // --- calibers added by weapon packs; absent installs simply skip them ---
            "Caliber102x22":     { "Prototype": ".40 S&W, 102 mm",         "RefMm": 102, "C": 0, "CaseMm3": 1030,  "BoreMm": 10.16 },
            "Caliber11x33R":     { "Prototype": ".44 Magnum, 152 mm",      "RefMm": 152, "C": 0, "CaseMm3": 1720,  "BoreMm": 10.90 },
            "Caliber792x33":     { "Prototype": "StG-44, 419 mm",          "RefMm": 419, "C": 0, "CaseMm3": 2200,  "BoreMm": 8.20 },
            "Caliber792x57":     { "Prototype": "Kar98k, 600 mm",          "RefMm": 600, "C": 0, "CaseMm3": 4340,  "BoreMm": 8.20 },
            "Caliber65x52":      { "Prototype": "Carcano M91, 780 mm",     "RefMm": 780, "C": 0, "CaseMm3": 3170,  "BoreMm": 6.70 },
            "Caliber762x63":     { "Prototype": ".30-06, 610 mm",          "RefMm": 610, "C": 0, "CaseMm3": 4430,  "BoreMm": 7.82 },
            "Caliber762x67B":    { "Prototype": ".300 Win Mag, 610 mm",    "RefMm": 610, "C": 0, "CaseMm3": 5570,  "BoreMm": 7.82 },
            "Caliber6ARC":       { "Prototype": "6mm ARC, 460 mm",         "RefMm": 460, "C": 0, "CaseMm3": 2200,  "BoreMm": 6.17 },
            "Caliber784x49":     { "Prototype": ".308 Marlin Express, 610 mm", "RefMm": 610, "C": 0, "CaseMm3": 3200, "BoreMm": 7.82 },
            "Caliber86x63":      { "Prototype": ".338 Norma, 660 mm",      "RefMm": 660, "C": 0, "CaseMm3": 6280,  "BoreMm": 8.60 },
            "Caliber93x64":      { "Prototype": "9.3x64 Brenneke, 600 mm", "RefMm": 600, "C": 0, "CaseMm3": 5570,  "BoreMm": 9.30 },
            "Caliber1036x77":    { "Prototype": ".408 CheyTac, 740 mm",    "RefMm": 740, "C": 0, "CaseMm3": 7970,  "BoreMm": 10.36 },
            "Caliber127x99":     { "Prototype": ".50 BMG, 737 mm",         "RefMm": 737, "C": 0, "CaseMm3": 19000, "BoreMm": 12.95 },
            "Caliber127x108":    { "Prototype": "12.7x108, 1000 mm",       "RefMm": 1000, "C": 0, "CaseMm3": 21000, "BoreMm": 12.98 },
            // note the multiplication sign in the key: that is how the pack spells it
            "Caliber17.8×89":    { "Prototype": ".700 Nitro Express, 610 mm", "RefMm": 610, "C": 0, "CaseMm3": 11000, "BoreMm": 17.80 }
          },

          // Weapons whose barrel does not come off, so its length cannot be read from a
          // barrel item. Lengths are the real prototype's. Anything not listed here keeps
          // its own velocity modifier, clamped, and is printed in the normalization report.
          "Weapons": {
            // --- Kalashnikov pattern: the barrel is part of the weapon, the gas block bolts on ---
            "weapon_izhmash_ak74_545x39":     { "Prototype": "AK-74", "LengthMm": 415 },
            "weapon_izhmash_ak74m_545x39":    { "Prototype": "AK-74M", "LengthMm": 415 },
            "weapon_izhmash_ak74n_545x39":    { "Prototype": "AK-74N", "LengthMm": 415 },
            "weapon_izhmash_aks74_545x39":    { "Prototype": "AKS-74", "LengthMm": 415 },
            "weapon_izhmash_aks74n_545x39":   { "Prototype": "AKS-74N", "LengthMm": 415 },
            "weapon_izhmash_aks74u_545x39":   { "Prototype": "AKS-74U", "LengthMm": 206.5 },
            "weapon_izhmash_aks74un_545x39":  { "Prototype": "AKS-74UN", "LengthMm": 206.5 },
            "weapon_izhmash_aks74ub_545x39":  { "Prototype": "AKS-74UB", "LengthMm": 206.5 },
            "weapon_izhmash_ak12_545x39":     { "Prototype": "AK-12", "LengthMm": 415 },
            "weapon_izhmash_ak105_545x39":    { "Prototype": "AK-105", "LengthMm": 314 },
            "weapon_izhmash_ak101_556x45":    { "Prototype": "AK-101", "LengthMm": 415 },
            "weapon_izhmash_ak102_556x45":    { "Prototype": "AK-102", "LengthMm": 314 },
            "weapon_izhmash_ak103_762x39":    { "Prototype": "AK-103", "LengthMm": 415 },
            "weapon_izhmash_ak104_762x39":    { "Prototype": "AK-104", "LengthMm": 314 },
            "weapon_izhmash_akm_762x39":      { "Prototype": "AKM", "LengthMm": 415 },
            "weapon_izhmash_akmn_762x39":     { "Prototype": "AKMN", "LengthMm": 415 },
            "weapon_izhmash_akms_762x39":     { "Prototype": "AKMS", "LengthMm": 415 },
            "weapon_izhmash_akmsn_762x39":    { "Prototype": "AKMSN", "LengthMm": 415 },
            "weapon_sag_ak545_545x39":        { "Prototype": "SAG AK-545", "LengthMm": 415 },
            "weapon_sag_ak545_short_545x39":  { "Prototype": "SAG AK-545 Short", "LengthMm": 314 },
            "weapon_rifle_dynamics_704_762x39": { "Prototype": "RD-704", "LengthMm": 409 },
            "weapon_molot_akm_vpo_209_366_TKM": { "Prototype": "VPO-209", "LengthMm": 415 },
            "weapon_molot_vepr_km_vpo_136_762x39": { "Prototype": "VPO-136", "LengthMm": 415 },
            "weapon_molot_vepr_hunter_vpo-101_762x51": { "Prototype": "VPO-101", "LengthMm": 520 },
            "weapon_molot_op_sks_762x39":     { "Prototype": "OP-SKS", "LengthMm": 520 },

            // --- submachine guns and machine pistols ---
            "weapon_izhmash_pp-19-01_9x19":   { "Prototype": "PP-19-01 Vityaz", "LengthMm": 237 },
            "weapon_izhmash_saiga_9_9x19":    { "Prototype": "Saiga-9", "LengthMm": 237 },
            "weapon_zis_ppsh41_762x25":       { "Prototype": "PPSh-41", "LengthMm": 269 },
            "weapon_zmz_pp-91_9x18pm":        { "Prototype": "PP-91 Kedr", "LengthMm": 120 },
            // the Klin is a Kedr rechambered for PMM and keeps its barrel
            "weapon_zmz_pp-9_9x18pmm":        { "Prototype": "PP-9 Klin", "LengthMm": 120 },
            "weapon_zmz_pp-91-01_9x18pm":     { "Prototype": "PP-91-01 Kedr-B", "LengthMm": 120 },
            "weapon_tochmash_sr2m_veresk_9x21": { "Prototype": "SR-2M Veresk", "LengthMm": 172 },
            "weapon_hk_mp5_navy3_9x19":       { "Prototype": "MP5", "LengthMm": 225 },
            "weapon_hk_mp5_kurtz_9x19":       { "Prototype": "MP5K", "LengthMm": 115 },
            "weapon_hk_mp7a1_46x30":          { "Prototype": "MP7A1", "LengthMm": 180 },
            "weapon_hk_mp7a2_46x30":          { "Prototype": "MP7A2", "LengthMm": 180 },
            "weapon_bt_mp9_9x19":             { "Prototype": "B&T MP9", "LengthMm": 130 },
            "weapon_bt_mp9n_9x19":            { "Prototype": "B&T MP9-N", "LengthMm": 130 },
            "weapon_iwi_uzi_9x19":            { "Prototype": "Uzi", "LengthMm": 260 },
            "weapon_iwi_uzi_pro_pistol_9x19": { "Prototype": "Uzi Pro Pistol", "LengthMm": 114 },

            // --- pistols and revolvers ---
            "weapon_izhmeh_pm_9x18pm":        { "Prototype": "PM", "LengthMm": 93.5 },
            "weapon_izhmeh_pm_treaded_9x18pm": { "Prototype": "PM threaded", "LengthMm": 93.5 },
            "weapon_izhmeh_mp443_9x19":       { "Prototype": "MP-443 Grach", "LengthMm": 112.4 },
            "weapon_tochmash_pb_9x18pm":      { "Prototype": "PB", "LengthMm": 105 },
            "weapon_tochmash_sr1mp_9x21":     { "Prototype": "SR-1MP Gyurza", "LengthMm": 120 },
            "weapon_molot_aps_9x18pm":        { "Prototype": "APS Stechkin", "LengthMm": 140 },
            "weapon_toz_apb_9x18pm":          { "Prototype": "APB", "LengthMm": 140 },
            "weapon_tula_tt_762x25":          { "Prototype": "TT-33", "LengthMm": 116 },
            "weapon_kbp_rsh_12_127x55":       { "Prototype": "RSh-12", "LengthMm": 165 },
            "weapon_chiappa_rhino_50ds_9x33R": { "Prototype": "Chiappa Rhino 50DS", "LengthMm": 127 },

            // --- rifles, shotguns, machine guns ---
            "weapon_izhmash_mosin_infantry_762x54": { "Prototype": "Mosin M91/30", "LengthMm": 730 },
            "weapon_izhmash_sv-98_762x54r":   { "Prototype": "SV-98", "LengthMm": 650 },
            "weapon_remington_model_700_762x51": { "Prototype": "Remington 700", "LengthMm": 660 },
            "weapon_accuracy_inernational_axmc_86x70": { "Prototype": "AI AXMC", "LengthMm": 686 },
            "weapon_ckib_ash_12_127x55":      { "Prototype": "ASh-12.7", "LengthMm": 305 },
            "weapon_ckib_nsv_utes_127x108":   { "Prototype": "NSV Utes", "LengthMm": 1100 },
            "weapon_zid_pkm_762x54r":         { "Prototype": "PKM", "LengthMm": 645 },
            "weapon_zid_rpd_762x39":          { "Prototype": "RPD", "LengthMm": 520 },
            "weapon_izhmash_saiga12k_10_12g": { "Prototype": "Saiga-12K", "LengthMm": 430 },
            "weapon_kiba_saiga12k_fa_12g":    { "Prototype": "Saiga-12K FA", "LengthMm": 430 },
            "weapon_toz_toz-106_20g":         { "Prototype": "TOZ-106", "LengthMm": 200 },
            "weapon_aklys_defense_velociraptor_762x35": { "Prototype": "Velociraptor 9\"", "LengthMm": 229 }
          },

          // ===== Armour materials =====
          // Class decides which penetration mechanics apply, and therefore which of the
          // numbers below are even meaningful. A shear strength says nothing about a
          // ceramic; a compressive strength says nothing about a woven fibre.
          //   Ductile — metals: the projectile punches a plug and pushes material aside
          //   Brittle — ceramics: a fracture conoid forms and erodes the projectile
          //   Fibrous — aramid/UHMWPE: a cone of fibres stretches until it fails
          "ArmorMaterials": {
            "ArmoredSteel": { "Class": "Ductile", "DensityGCm3": 7.85, "YieldMPa": 1250, "ShearMPa": 750,
                              "Source": "RHA / armour steel ~500 HB" },
            "Titan":        { "Class": "Ductile", "DensityGCm3": 4.43, "YieldMPa": 880,  "ShearMPa": 550,
                              "Source": "Ti-6Al-4V, 334 HB, UTS 950 MPa" },
            "Aluminium":    { "Class": "Ductile", "DensityGCm3": 2.70, "YieldMPa": 300,  "ShearMPa": 190,
                              "Source": "5083/7039 armour plate" },
            "Ceramic":      { "Class": "Brittle", "DensityGCm3": 3.90, "CompressiveMPa": 2500,
                              "HardMassFraction": 0.65, "Source": "Al2O3 on a fibre backer" },
            "Combined":     { "Class": "Brittle", "DensityGCm3": 3.20, "CompressiveMPa": 2600,
                              "HardMassFraction": 0.60, "Source": "ceramic face, composite backing" },
            "Glass":        { "Class": "Brittle", "DensityGCm3": 2.50, "CompressiveMPa": 1000,
                              "Source": "laminated ballistic glass" },
            "Aramid":       { "Class": "Fibrous", "DensityGCm3": 1.44, "FibreTensileMPa": 2900, "FailureStrain": 0.034,
                              "Source": "Kevlar 29 / TSVM-DZh" },
            "UHMWPE":       { "Class": "Fibrous", "DensityGCm3": 0.97, "FibreTensileMPa": 3400, "FailureStrain": 0.035,
                              "Source": "Dyneema HB grade" }
          },

          // ===== Armour construction =====
          // Keyed by the product — the item name up to "_level" — so one entry covers
          // every zone of it. Thickness is the number the game has nowhere and the whole
          // reason this table exists. Material is only set where the game has it wrong.
          // Anything absent keeps the game's own material and falls back to the class.
          "ArmorPlates": {
            "6b5-16":     { "Prototype": "6B5-16, ADU 605T-83", "ThicknessMm": 6.5, "BackingMm": 8,
                            "Source": "titanium 6.5 mm + 30-layer TSVM-DZh package" },
            "6b5-15":     { "Prototype": "6B5-15, ADU 14.20.00.000", "ThicknessMm": 13, "BackingMm": 8,
                            "Source": "boron carbide 13 mm + fabric package" },
            "6b3TM":      { "Prototype": "6B3TM, ADU 605-80", "ThicknessMm": 1.25, "BackingMm": 8,
                            "Source": "titanium 1.25 mm anti-fragmentation element" },
            "kora_kulon": { "Prototype": "Kora-Kulon", "ThicknessMm": 4.3, "BackingMm": 6,
                            "Source": "steel plate, Br3" }
          },

          // ===== Reference plate per material and class =====
          // Most of the armour in the game is invented for it, so there is no product to
          // look up. What there is, for every rating, is a real plate that does the same
          // job — and that is what an invented one is standing in for. Keyed
          // "Material/Class"; a documented product always wins over this.
          "ArmorByClass": {
            "ArmoredSteel/3": { "Prototype": "thin armour steel, Br3", "ThicknessMm": 4.5 },
            "ArmoredSteel/4": { "Prototype": "armour steel, Br4",      "ThicknessMm": 5.5 },
            "ArmoredSteel/5": { "Prototype": "AR500, NIJ III",         "ThicknessMm": 6.35,
                                "Source": "0.25 in is the standard Level III steel plate" },
            "ArmoredSteel/6": { "Prototype": "armour steel, III+",     "ThicknessMm": 8.0 },

            "Ceramic/4":      { "Prototype": "alumina, SAPI class",    "ThicknessMm": 7.0 },
            "Ceramic/5":      { "Prototype": "SAPI, silicon carbide",  "ThicknessMm": 8.5 },
            "Ceramic/6":      { "Prototype": "ESAPI, boron carbide",   "ThicknessMm": 10.0,
                                "Source": "ESAPI is 10 mm of boron carbide on a UHMWPE backer" },

            "Combined/3":     { "Prototype": "ceramic face, Br3",      "ThicknessMm": 6.0 },
            "Combined/4":     { "Prototype": "ceramic face, Br4",      "ThicknessMm": 7.0 },
            "Combined/5":     { "Prototype": "ceramic face, Br5",      "ThicknessMm": 8.5 },
            "Combined/6":     { "Prototype": "ceramic face, Br6",      "ThicknessMm": 10.0 },

            "Titan/4":        { "Prototype": "titanium, Br4",          "ThicknessMm": 8.0 },
            "Titan/5":        { "Prototype": "titanium, Br5",          "ThicknessMm": 10.0 },
            "Titan/6":        { "Prototype": "titanium, Br6",          "ThicknessMm": 13.0 },

            // polyethylene stops by encapsulating the round, so it needs far more of
            // itself than a hard face does
            "UHMWPE/3":       { "Prototype": "UHMWPE monolith, Br3",   "ThicknessMm": 20.0 },
            "UHMWPE/4":       { "Prototype": "UHMWPE monolith, Br4",   "ThicknessMm": 25.0 },
            "UHMWPE/5":       { "Prototype": "UHMWPE monolith, NIJ III", "ThicknessMm": 33.0,
                                "Source": "standalone Level III polyethylene plate, 1.3 in" },
            "UHMWPE/6":       { "Prototype": "UHMWPE monolith, Br6",   "ThicknessMm": 38.0 },

            "Aluminium/4":    { "Prototype": "aluminium armour, Br4",  "ThicknessMm": 20.0 },
            "Glass/4":        { "Prototype": "laminated ballistic glass", "ThicknessMm": 25.0 },
            "Glass/5":        { "Prototype": "laminated ballistic glass", "ThicknessMm": 30.0 },
            "Glass/6":        { "Prototype": "laminated ballistic glass", "ThicknessMm": 35.0 },
            "Aramid/3":       { "Prototype": "aramid pack, Br3",       "ThicknessMm": 12.0 },
            "Aramid/4":       { "Prototype": "aramid pack, Br4",       "ThicknessMm": 16.0 },
            "Aramid/5":       { "Prototype": "aramid pack, Br5",       "ThicknessMm": 20.0 },
            "Aramid/6":       { "Prototype": "aramid pack, Br6",       "ThicknessMm": 24.0 }
          },

          // Blast anchor: Strength_i = Strength_anchor * (TntG_i / TntG_anchor)^(1/3)
          "BlastAnchor": { "Name": "RGD-5", "Strength": 100, "TntG": 110 }
        }
        """;
}
