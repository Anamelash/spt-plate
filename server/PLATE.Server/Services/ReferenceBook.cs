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

    /// <summary>
    /// Construction of a single bullet. Three numbers, because one cannot describe
    /// both a lead ball and a tungsten dart: how much of the bullet can deform, how
    /// much of its frontal area the hard core takes, and how much of its mass.
    /// Nothing here is a game class or a game stat — it is what the bullet is made of.
    /// </summary>
    public class BulletRef
    {
        /// <summary>Prototype name (for the report).</summary>
        public string Prototype { get; set; } = "";

        /// <summary>
        /// Deformable fraction 0..1: the share of the projectile that flows on impact.
        /// Not the same as "soft metal by mass" — a lead core inside a closed jacket
        /// upsets and no more, the same core behind an open nose peels back. -1 leaves
        /// the statistical estimate in place.
        /// </summary>
        public double X { get; set; } = -1;

        /// <summary>
        /// Core frontal area / bullet frontal area. The armour sees this, not the
        /// calibre: a 5.6 mm core in a 7.85 mm bullet meets the plate at twice the
        /// energy density. 0 = no separable core, the whole bullet strikes.
        /// </summary>
        public double CoreAreaFrac { get; set; }

        /// <summary>
        /// Core mass / bullet mass. What carries on after the plate; the jacket stays
        /// in the hole. 0 = no separable core.
        /// </summary>
        public double CoreMassFrac { get; set; }

        /// <summary>
        /// Vickers hardness of the core. Published as HRC by everyone who publishes it
        /// at all, converted here: 40 HRC is 392 HV, 60 is 697, 65 is 832, and tungsten
        /// carbide runs 1200-1500. 0 = lead and copper, which is 40 HV and never wins
        /// an argument with a plate.
        /// </summary>
        public double CoreHardnessHv { get; set; }

        /// <summary>
        /// Mass of what actually flies, g. 0 = keep the game's figure, which is the normal
        /// case: the card and the prototype agree for an ordinary bullet. It exists for the
        /// ones where they cannot — a sabot round leaves the barrel as its penetrator alone,
        /// and a card that lists the whole projectile is describing something that never
        /// arrives.
        /// </summary>
        public double MassG { get; set; }

        public string Source { get; set; } = "";
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

        /// <summary>
        /// For a Ductile material: "ShearPlugging" or "HoleExpansion" — which of the
        /// two ductile laws the alloy obeys. A metallurgical property, decided by the
        /// alloy's strain-hardening reserve (UTS over yield): exhausted hardening
        /// localises shear and plugs, reserve flows the material aside. Every armour
        /// alloy in this book plugs — hence the empty default — and the field exists
        /// so that a soft, high-hardening metal (structural steel at UTS/yield 1.8)
        /// added by data is read by the law its metallurgy dictates, not by ours.
        /// </summary>
        public string FailureMode { get; set; } = "";

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
        /// Vickers hardness. Not a strength: the question it answers is which of the two
        /// pieces of metal gives way first. A mild steel core at 400 HV upsets on the
        /// face of a 580 HV plate and stops being a punch; the same plate against a
        /// tungsten carbide core at 1300 HV is the one losing. 0 leaves the term out,
        /// which is right for fibre — a woven pack has no hardness worth the name.
        /// </summary>
        public double HardnessHv { get; set; }

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

        /// <summary>
        /// The rating the manufacturer certifies, when they publish one but no
        /// construction. Sometimes that is all there is: Fort say the Kiver-M stops a
        /// Stechkin and nothing about what it is made of. With no thickness to use, the
        /// reference is at least read at the real rating instead of the game's.
        /// 0 = not stated.
        /// </summary>
        public int Rating { get; set; }

        /// <summary>
        /// The item is a hard element, whatever slot the game files it under. A class
        /// ceiling is a statement about a form — loose plies stitched together, prepreg
        /// pressed into a shell — and a form cannot be rated past what that much of it
        /// stops. A plate is not one of those forms, so nothing caps its rating: the
        /// Velocity SLAAP is 18 mm of polyethylene where the thickest shell anyone
        /// fields is 7.3, a rifle-rated applique that happens to bolt onto a helmet.
        /// Set this only where the maker's own specification says so, never to lift a
        /// rating that looks low.
        /// </summary>
        public bool Plate { get; set; }

        /// <summary>
        /// The alloy's own strength, overriding the material's, MPa. 0 = inherit.
        ///
        /// The game names eight materials and "ArmoredSteel" is one of them, so every
        /// steel plate in it — a Russian 44S panel at 2000 MPa yield and an American
        /// AR500 at 1250 — arrives under the same name and used to get the same numbers.
        /// The material entry cannot be split, since its key IS the game's enum; the
        /// grade therefore lives on the product, which is where the rest of the
        /// construction already lives.
        /// </summary>
        public double ShearMPa { get; set; }

        public double YieldMPa { get; set; }

        /// <summary>The alloy's own hardness, HV. 0 = inherit from the material.</summary>
        public double HardnessHv { get; set; }

        /// <summary>Backing package behind the plate, mm of fibre (0 = none).</summary>
        public double BackingMm { get; set; }

        /// <summary>
        /// What the backing is made of — a key into ArmorMaterials, fibrous. Empty
        /// means aramid, the dominant case: Russian packages and helmet liners are
        /// TSVM/Kevlar fabric. Western composite plates say "UHMWPE" explicitly.
        /// </summary>
        public string BackingMaterial { get; set; } = "";

        /// <summary>
        /// Density of the hard element, g/cm³, when it is not what the material table
        /// says. The game has one Ceramic and the table reads it as alumina, but an
        /// ESAPI is boron carbide at 2.52 — so 10 mm of it weighs two thirds of what
        /// 10 mm of alumina would. Without this the thickness would have to be fudged
        /// to keep the areal density right, and the geometry would stop being real.
        /// 0 = the material table is correct for this one.
        /// </summary>
        public double DensityGCm3 { get; set; }

        /// <summary>
        /// For ArmorByClass rungs only: the key of the REAL product in ArmorPlates
        /// whose construction this rung borrows. A class rung used to be a thickness
        /// solved from the class threshold — a number the model owed to itself. Where
        /// a certified product of the same material and class exists, the rung is now
        /// a reference to it: not a solved number, a real plate's construction with a
        /// real certificate behind it. Empty = the rung carries its own (computed,
        /// last-resort) figures, and says so in its Source.
        /// </summary>
        public string SameAs { get; set; } = "";

        public string Source { get; set; } = "";
    }

    public class AmmoReference
    {
        /// <summary>Key — the cartridge template's _name in the DB.</summary>
        public Dictionary<string, ShotshellRef> Shotshells { get; set; } = new();

        /// <summary>
        /// Key — the cartridge template's _name in the DB. A table by name is the whole
        /// point: six packs of M61 are one bullet, and a statistic run over whatever
        /// cohort a mod happens to install gave them six different characters.
        /// </summary>
        public Dictionary<string, BulletRef> Bullets { get; set; } = new();

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
        /// the ones the game invented. Read through ResolveByClass, never directly:
        /// a rung that names a representative (SameAs) is that product's construction.
        /// </summary>
        public Dictionary<string, ArmorPlateRef> ArmorByClass { get; set; } = new();

        /// <summary>
        /// The construction a "Material/Class" rung actually stands for: the real
        /// product it names, or its own last-resort figures when no product of that
        /// material and class exists to borrow from. Null — no such rung.
        /// </summary>
        public ArmorPlateRef ResolveByClass(string key)
        {
            if (!ArmorByClass.TryGetValue(key, out var rung))
            {
                return null;
            }

            return rung.SameAs.Length > 0 && ArmorPlates.TryGetValue(rung.SameAs, out var product)
                ? product
                : rung;
        }

        /// <summary>
        /// Key — "Material/Class". The package sewn into a carrier: layers of fabric,
        /// held together by the stitching and nothing else. Read at a rating of 2 at
        /// most — carriers are sold as Br1 or Br2 and the rifle protection lives in the
        /// plates.
        /// </summary>
        public Dictionary<string, ArmorPlateRef> SoftArmor { get; set; } = new();

        /// <summary>
        /// Key — "Material/Class". A rigid shell: a helmet, a visor, a mask. Aramid in
        /// one of these is not the aramid of a vest package — it is pressed under heat
        /// into a resin-bonded laminate and behaves as a solid, so it belongs in its own
        /// table with its own figures.
        /// </summary>
        public Dictionary<string, ArmorPlateRef> HelmetShells { get; set; } = new();

        /// <summary>
        /// Key — the same product or item name as ArmorPlates; value — why the search
        /// came back empty. A headstone, not a setting: nothing here changes a figure.
        /// It records that somebody has already gone looking, so the next pass over the
        /// report spends its time on the entries nobody has looked at yet.
        /// </summary>
        public Dictionary<string, string> NoRealSpecs { get; set; } = new();

        /// <summary>
        /// Key — the weapon template's _name. Only for weapons whose barrel does not
        /// come off, so its length cannot be read from a barrel item.
        /// </summary>
        public Dictionary<string, WeaponBarrelRef> Weapons { get; set; } = new();

        public BlastAnchorRef BlastAnchor { get; set; } = new();

        /// <summary>
        /// Bumped when a shipped figure CHANGES rather than when one is added. Adding is
        /// handled by the per-entry merge, which never writes over what is already
        /// there; correcting is not, and a correction that reaches nobody is not a
        /// correction. On a bump the old file is renamed aside and rewritten.
        /// </summary>
        public int Version { get; set; }
    }

    /// <summary>
    /// The version the mod ships, read out of the book itself: raising it means editing
    /// the "Version" line at the end of <see cref="DefaultReferenceJsonc"/>, next to the
    /// note saying what was corrected, and nothing else. It used to be a second number
    /// here, and the two drifted — this said 13 while the text still said 7, so every
    /// start found the file it had just written outdated, renamed it aside and rewrote
    /// it, taking whatever the user had edited into it. A book that will not parse
    /// reports version 0, which makes every file current: refusing to refresh is a
    /// missed correction, refreshing on a number nobody wrote is a deleted file.
    ///
    /// What each version corrected is written where the number is — in the book, which
    /// is also where the user reads it.
    /// </summary>
    private static readonly int ShippedVersion = Parse(DefaultReferenceJsonc)?.Version ?? 0;

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

        if (_cached.Version < ShippedVersion)
        {
            _cached = Refresh(path, _cached.Version);
        }

        FillMissingSections(_cached);
        return _cached;
    }

    /// <summary>
    /// Replaces a reference book written by an older version of the mod, keeping the old
    /// one alongside it. The per-entry merge cannot do this: it deliberately never
    /// overwrites, so a figure that was wrong stays wrong for everyone who has already
    /// run the mod once.
    /// </summary>
    private AmmoReference Refresh(string path, int was)
    {
        try
        {
            var kept = path + $".v{was}.bak";
            File.Copy(path, kept, overwrite: true);
            File.WriteAllText(path, DefaultReferenceJsonc);
            logger.Info($"[PLATE] {FileName} was version {was}, the mod ships {ShippedVersion}. " +
                        $"Rewritten; your previous copy is at {System.IO.Path.GetFileName(kept)}");
        }
        catch (Exception ex)
        {
            logger.Warning($"[PLATE] Could not refresh {FileName}: {ex.Message}");
        }

        return Parse(DefaultReferenceJsonc) ?? new AmmoReference();
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
            logger.Debug($"[PLATE] {FileName} is missing entries the mod ships, added: " +
                         string.Join(", ", filled));
        }
    }

    /// <summary>
    /// Adds every shipped entry the file on disk does not already have, and returns
    /// what was added. Entry by entry rather than section by section: the file on a
    /// user's machine was written by an older version of the mod, and a whole-section
    /// check means a table they already have — plates, calibers — never sees anything
    /// added to it again. What they have written themselves always wins.
    /// </summary>
    public static List<string> MergeShippedDefaults(AmmoReference loaded)
    {
        AmmoReference? shipped = null;
        AmmoReference Shipped() => shipped ??= Parse(DefaultReferenceJsonc) ?? new AmmoReference();

        var filled = new List<string>();

        void Fill<T>(string name, Dictionary<string, T> into, Func<AmmoReference, Dictionary<string, T>> from)
        {
            var added = 0;
            foreach (var (key, value) in from(Shipped()))
            {
                added += into.TryAdd(key, value) ? 1 : 0;
            }

            if (added > 0)
            {
                filled.Add($"{name} +{added}");
            }
        }

        Fill(nameof(loaded.Shotshells), loaded.Shotshells, s => s.Shotshells);
        Fill(nameof(loaded.Bullets), loaded.Bullets, s => s.Bullets);
        Fill(nameof(loaded.Grenades), loaded.Grenades, s => s.Grenades);
        Fill(nameof(loaded.Barrels), loaded.Barrels, s => s.Barrels);
        Fill(nameof(loaded.Weapons), loaded.Weapons, s => s.Weapons);
        Fill(nameof(loaded.ArmorMaterials), loaded.ArmorMaterials, s => s.ArmorMaterials);
        Fill(nameof(loaded.ArmorPlates), loaded.ArmorPlates, s => s.ArmorPlates);
        Fill(nameof(loaded.ArmorByClass), loaded.ArmorByClass, s => s.ArmorByClass);
        Fill(nameof(loaded.SoftArmor), loaded.SoftArmor, s => s.SoftArmor);
        Fill(nameof(loaded.HelmetShells), loaded.HelmetShells, s => s.HelmetShells);
        Fill(nameof(loaded.NoRealSpecs), loaded.NoRealSpecs, s => s.NoRealSpecs);

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

          // ===== Bullets: what the projectile is made of =====
          //
          // X is the deformable fraction. It is not "how much lead is in there": the
          // same lead core behind a closed gilding jacket upsets and stops, and behind
          // an open nose peels back to twice the calibre. Read as construction:
          // hard core 0.05-0.10, FMJ rifle 0.25-0.30, pistol round-nose 0.35,
          // soft point 0.70, hollow point 0.90, prefragmented 0.95.
          //
          // CoreAreaFrac / CoreMassFrac describe the hard core: its frontal area and its
          // mass as fractions of the whole bullet. Area is what the plate meets, mass is
          // what carries on out the back once the jacket has been stripped in the hole.
          //
          // TWO RULES DECIDE WHETHER A ROUND GETS THEM AT ALL.
          //
          // 1. A core softer than about 55 HRC does not survive the face of a plate — it
          //    upsets and spreads to full calibre. So M855's 40-45 HRC tip and the mild
          //    steel of 7N6 and PS are recorded with their mass fraction and an AREA
          //    fraction of 1: no concentration. M855A1's 58-60 HRC tip in the same
          //    cartridge case does concentrate. That single line is the difference
          //    between a round that bounces off AR500 and one that goes through it.
          // 2. No core is entered without a published core mass or diameter. Lead-cored
          //    ball is one piece of metal and says so by having no entry.
          //
          // Core diameters come from the Adept Armor threat survey, which publishes them
          // alongside core weight and hardness for the standard-issue rounds. Where only
          // a mass and a length are published, the area is that mass over the core alloy's
          // density over 0.78 of the length — the 0.78 is read off the one core with all
          // three published, the 7N26 BP at 5.55 g of tool steel, 30.5 mm long and 6.14 mm
          // across. Cross-check: the two tungsten-carbide cores of different calibre and
          // different maker, M993 and 7N37, land at 0.49 and 0.53 of their bullet's area.
          "Bullets": {
            // --- 5.45x39. Core masses: ru.wikipedia, sourced to the GRAU indices;
            // core diameters and hardness: Adept Armor threat survey, except the PS,
            // which is the one round in the calibre where the survey and the Russian
            // sources describe different cartridges - see its own Source line ---
            "patron_545x39_PS":   { "Prototype": "7N6M PS",       "X": 0.25, "CoreAreaFrac": 0.51, "CoreMassFrac": 0.42, "CoreHardnessHv": 697,
                                    "Source": "core 1.43 g of Steel 65G in a 3.4 g bullet, 4.0 mm, 60 HRC. The 1987 modernisation changed the core steel and its heat treatment without changing the bullet, the marking or the index, so 7N6 names both this and the untreated Steel 10 original it replaced - which has not been produced since. The survey that reads this core at 40-45 HRC gives no year for its sample, and 40-45 HRC is what the literature gives for that original" },
            "patron_545x39_PP":   { "Prototype": "7N10 PP",       "X": 0.15, "CoreAreaFrac": 0.532, "CoreMassFrac": 0.478, "CoreHardnessHv": 697,
                                    "Source": "core 1.72-1.80 g of Steel 70/75 in a 3.62-3.74 g bullet, 4.1 mm, 60 HRC" },
            "patron_545x39_BP":   { "Prototype": "7N22 BP",       "X": 0.08, "CoreAreaFrac": 0.507, "CoreMassFrac": 0.477, "CoreHardnessHv": 765,
                                    "Source": "core 1.75 g of U12A tool steel in a 3.67 g bullet, 4.0 mm, 60-65 HRC" },
            "patron_545x39_BS":   { "Prototype": "7N24 BS",       "X": 0.05, "CoreAreaFrac": 0.507, "CoreMassFrac": 0.512, "CoreHardnessHv": 1300,
                                    "Source": "core 2.1 g of VK-8 tungsten-cobalt in a 4.1 g bullet, 4.0 mm (Adept read the core at 1.8 g)" },
            "patron_545x39_7n39": { "Prototype": "7N39 Igolnik",  "X": 0.05, "CoreAreaFrac": 0.507, "CoreMassFrac": 0.463, "CoreHardnessHv": 1300,
                                    "Source": "core 1.9 g of 92% tungsten carbide on cobalt in a 4.1 g bullet, 4.0 mm, pressed and sintered" },
            "patron_545x39_7n40": { "Prototype": "7N40",          "X": 0.12,
                                    "Source": "the enhanced-penetration development of the PP; no core figures published" },
            "patron_545x39_BT":   { "Prototype": "7T3M tracer",   "X": 0.25 },
            "patron_545x39_T":    { "Prototype": "7T3 tracer",    "X": 0.25 },
            "patron_545x39_FMJ":  { "Prototype": "5.45 FMJ",      "X": 0.30 },
            "patron_545x39_SP":   { "Prototype": "5.45 soft point","X": 0.70 },
            "patron_545x39_HP":   { "Prototype": "5.45 hollow point","X": 0.90 },
            "patron_545x39_PRS":  { "Prototype": "7N40 PRS reduced ricochet","X": 0.80 },
            "patron_545x39_US":   { "Prototype": "7U1 US subsonic","X": 0.20,
                                    "Source": "heavy subsonic on a blunt VK8 core; too slow to do anything with it" },

            // --- 5.56x45 ---
            "patron_556x45_M855":     { "Prototype": "M855 / SS109", "X": 0.25, "CoreAreaFrac": 1.0, "CoreMassFrac": 0.162, "CoreHardnessHv": 410,
                                        "Source": "10 gr steel tip over a 32 gr lead rear in a 62 gr bullet, 4.6 mm, 40-45 HRC - the tip is not hard enough to hold its shape, so the area fraction stays 1. It still arrives on the far side as 0.65 g of steel" },
            "patron_556x45_M855A1":   { "Prototype": "M855A1 EPR",   "X": 0.10, "CoreAreaFrac": 0.569, "CoreMassFrac": 0.306, "CoreHardnessHv": 670,
                                        "Source": "19 gr exposed hardened steel over a copper slug, 4.3 mm, 58-60 HRC - the same 62 gr as the M855 and a different weapon against steel" },
            "patron_556x45_M856A1":   { "Prototype": "M856A1 tracer EPR", "X": 0.10, "CoreAreaFrac": 0.569, "CoreMassFrac": 0.306, "CoreHardnessHv": 670,
                                        "Source": "the tracer built on the M855A1's penetrator" },
            "patron_556x45_M995":     { "Prototype": "M995 AP",      "X": 0.05, "CoreAreaFrac": 0.492, "CoreMassFrac": 0.615, "CoreHardnessHv": 1300,
                                        "Source": "32 gr WC-Co core in an aluminium cup, 4.0 mm, in a 52 gr bullet. Two sources agree on the core: 2.07 g and 2.08 g" },
            "patron_556x45_ssa_ap":   { "Prototype": "SSA AP",       "X": 0.05,
                                        "Source": "same mass and velocity as the M995 in the game; nobody publishes a construction for it" },
            "patron_556x45_M856":     { "Prototype": "M856 tracer",  "X": 0.25 },
            "patron_556x45_55_FMJ":   { "Prototype": "M193",         "X": 0.30 },
            "patron_556x45_55_HP":    { "Prototype": "55 gr HP",     "X": 0.90 },
            "patron_556x45_mk_318_mod_0": { "Prototype": "Mk318 SOST", "X": 0.60,
                                        "Source": "open-tip barrier round: a lead front over a solid copper rear, meant to upset without coming apart" },
            "patron_556x45_MK_255_Mod_0": { "Prototype": "Mk255 reduced ricochet", "X": 0.85 },
            "patron_556x45_varmageddon":  { "Prototype": "Varmageddon", "X": 0.95 },

            // --- 7.62x39 ---
            "patron_762x39_PS":     { "Prototype": "57-N-231 PS",  "X": 0.25, "CoreAreaFrac": 0.50, "CoreMassFrac": 0.468, "CoreHardnessHv": 697,
                                      "Source": "core 55-60 gr of 65G/70/75 spring steel in a 7.9 g bullet, 5.6 mm, heat-treated. The 1989 modernisation changed the core steel and its heat treatment without changing the index - the same story as the 5.45 PS, and the penetration moved with it: a helmet at 1000 m rather than 900, a fragmentation vest at 700 rather than 600, and a rifle-rated vest at 100 m, which the mild core could not do at any range. The geometry is the survey's and is not in dispute; its 35-45 HRC is, and is what the literature gives for the pre-1989 steel 10 it evidently sampled" },
            "patron_762x39_BP":     { "Prototype": "7N23 BP",      "X": 0.07, "CoreAreaFrac": 0.399, "CoreMassFrac": 0.492, "CoreHardnessHv": 697,
                                      "Source": "60 gr hardened core, 5.0 mm, 60 HRC, in the same 123 gr bullet as the PS" },
            "patron_762x39_pp":     { "Prototype": "7N27 PP",      "X": 0.15 },
            "patron_762x39_mai_ap": { "Prototype": "MAI AP",       "X": 0.05, "CoreAreaFrac": 0.125, "MassG": 2.0, "CoreHardnessHv": 1300,
                                      "Source": "no published prototype - not in the Russian ammunition literature, on the forums or in the patent record, and evidently the game's own invention. What the game does state is a construction: a two-part projectile, a sabot carrying a tungsten carbide penetrator. Hardness is that material at the 1300 HV this book already carries for it (5.45 BS on VK-8, 7N39 on 92% WC). The rest follows from one assumption, that a sub-calibre penetrator is half the width of the calibre's ordinary core: 2.8 mm against the PS core's 5.6, which is 0.125 of the bullet's face. Mass is then not free - 2.8 mm of tungsten carbide at 14.8 g/cm3 over a 22 mm rod, the longest that fits inside a bullet of this calibre, is 2.0 g. The card's 7.9 g is the whole projectile including the sabot that never arrives, and at the card's 875 m/s it claims 3024 J, half again what this case can deliver at all" },
            "patron_762x39_T45M":   { "Prototype": "T-45M tracer", "X": 0.25 },
            "patron_762x39_fmj":    { "Prototype": "7.62x39 FMJ",  "X": 0.30 },
            "patron_762x39_sp":     { "Prototype": "7.62x39 SP",   "X": 0.70 },
            "patron_762x39_HP":     { "Prototype": "7.62x39 HP",   "X": 0.90 },
            "patron_762x39_US":     { "Prototype": "57-N-231U US", "X": 0.25 },

            // --- 7.62x51 ---
            "patron_762x51_M80":    { "Prototype": "M80 ball",     "X": 0.25,
                                      "Source": "147 gr of lead alloy in a jacket; one piece of metal" },
            "patron_762x51_m80a1":  { "Prototype": "M80A1 EPR",    "X": 0.12, "CoreAreaFrac": 0.491, "CoreMassFrac": 0.347, "CoreHardnessHv": 550,
                                      "Source": "45 gr hardened steel tip over a copper slug, 5.5 mm, 50-55 HRC, in a 130 gr bullet" },
            "patron_762x51_M61":    { "Prototype": "M61 AP",       "X": 0.10, "CoreAreaFrac": 0.491, "CoreMassFrac": 0.365, "CoreHardnessHv": 730,
                                      "Source": "55 gr hardened core at 60-63 HRC with a lead filler, in a 150.5 gr bullet. Core diameter is not published; read at the M80A1's 5.5 mm, the same calibre and the same kind of core" },
            "patron_762x51_m993":   { "Prototype": "M993 AP",      "X": 0.05, "CoreAreaFrac": 0.491, "CoreMassFrac": 0.712, "CoreHardnessHv": 1300,
                                      "Source": "91 gr WC-Co core in an aluminium cup under a tombac-clad steel jacket, 5.5 mm, in a 128 gr bullet. Bofors FFV design, 58-degree tip" },
            "patron_762x51_M62":    { "Prototype": "M62 tracer",   "X": 0.25 },
            "patron_762x51_bpz_fmj":{ "Prototype": "BPZ FMJ",      "X": 0.28 },
            "patron_762x51_tpz_sp": { "Prototype": "TPZ soft point","X": 0.70 },
            "patron_762x51_ultra_nosler": { "Prototype": "Nosler Ballistic Tip", "X": 0.90 },

            // --- 7.62x54R ---
            "patron_762x54R_LPS_Gzh": { "Prototype": "57-N-323S LPS", "X": 0.25,
                                        "Source": "mild steel core; a lead substitute, no hard element" },
            "patron_762x54R_7N1":     { "Prototype": "7N1 sniper",    "X": 0.30,
                                        "Source": "steel nose and lead base with an air cavity at the tip - an open tip that is not there to expand" },
            "patron_762x54R_SNB":     { "Prototype": "7N14 SNB",      "X": 0.08, "CoreAreaFrac": 0.673, "CoreMassFrac": 0.463, "CoreHardnessHv": 720,
                                        "Source": "pointed U12A core over 60 HRC. Dimensions are not published for the 7N14 itself; read at the 7N13 BP's 70 gr and 6.5 mm, the same U12A core in the same case" },
            "patron_762x54r_7n37":    { "Prototype": "7N37",          "X": 0.05, "CoreAreaFrac": 0.531, "CoreMassFrac": 0.510, "CoreHardnessHv": 1300,
                                        "Source": "core 6.22 g of VK8, 20.9 mm long, in a 12.2 g bullet; 426 mm3 over 0.78 of that length is 26 mm2, or 5.8 mm across" },
            "patron_762x54r_7bt1":    { "Prototype": "7BT1 AP-tracer","X": 0.10 },
            "patron_762x54r_bthp":    { "Prototype": "BTHP match",    "X": 0.35 },
            "patron_762x54r_spbt":    { "Prototype": "SPBT hunting",  "X": 0.70 },
            "patron_762x54r_fmj":     { "Prototype": "7.62x54R FMJ",  "X": 0.28 },
            "patron_762x54r_t46m":    { "Prototype": "T-46M tracer",  "X": 0.25 },

            // --- 6.8x51 ---
            "patron_68x51":     { "Prototype": "XM1186 GP", "X": 0.12, "CoreAreaFrac": 0.675, "CoreMassFrac": 0.26, "CoreHardnessHv": 700,
                                  "Source": "30-40 gr hardened steel penetrator over a copper slug, 5.5-6.0 mm, 58-62 HRC, in a 135 gr bullet" },
            "patron_68x51_fmj": { "Prototype": "6.8x51 FMJ", "X": 0.28 },

            // --- .338 Lapua Magnum ---
            "patron_86x70_lapua_ap":          { "Prototype": ".338 AP (AP485/AP529)", "X": 0.05, "CoreAreaFrac": 0.666, "CoreMassFrac": 0.587, "CoreHardnessHv": 1300,
                                                "Source": "WC-Co core 7.0 mm across, 120-200 gr, in a 248-300 gr bullet" },
            "patron_86x70_lapua_magnum":      { "Prototype": "Lock Base B408",  "X": 0.35 },
            "patron_86x70_lapua_magnum_upz":  { "Prototype": ".338 UPZ",        "X": 0.40 },
            "patron_86x70_lapua_tac_x":       { "Prototype": "Barnes TAC-X",    "X": 0.80,
                                                "Source": "solid copper hollow point, made to open into petals" },

            // --- 9x19 ---
            "patron_9x19_7n31":       { "Prototype": "7N31 PBP",  "X": 0.08, "CoreAreaFrac": 0.563, "CoreMassFrac": 0.687, "CoreHardnessHv": 700,
                                        "Source": "hardened carbon steel core 2.7-3.0 g in a 4.1-4.2 g bullet, exposed at the tip under an aluminium alloy jacket. Diameter is not published: 363 mm3 of steel over 0.78 of the bullet's own 13 mm gives 6.8 mm" },
            "patron_9x19_ap_63":      { "Prototype": "AP 6.3",     "X": 0.15 },
            "patron_9x19_PST_gzh":    { "Prototype": "7N21 PST",   "X": 0.20,
                                        "Source": "hardened steel core; core figures not published" },
            "patron_9x19_m882":       { "Prototype": "M882 ball",  "X": 0.30 },
            "patron_9x19_PSO_gzh":    { "Prototype": "PSO subsonic","X": 0.35 },
            "patron_9x19_luger_cci":  { "Prototype": "CCI Luger FMJ","X": 0.35 },
            "patron_9x19_GT":         { "Prototype": "Green Tracer","X": 0.35 },
            "patron_9x19_quakemaker": { "Prototype": "QuakeMaker JHP","X": 0.90 },
            "patron_9x19_rip":        { "Prototype": "G2 RIP",     "X": 0.95 },

            // --- 9x21 ---
            "patron_9x21_7n42":  { "Prototype": "7N42 BP",   "X": 0.08 },
            "patron_9x21_sp10":  { "Prototype": "SP-10",     "X": 0.10,
                                   "Source": "hardened steel core exposed at the tip; core figures not published" },
            "patron_9x21_sp13":  { "Prototype": "SP-13 tracer AP", "X": 0.12 },
            "patron_9x21_7u4":   { "Prototype": "7U4 subsonic",    "X": 0.30 },
            "patron_9x21_sp11":  { "Prototype": "SP-11 ball",      "X": 0.30 },
            "patron_9x21_sp12":  { "Prototype": "SP-12 expanding", "X": 0.85 },

            // --- 9x39 ---
            "patron_9x39_sp6":  { "Prototype": "SP-6",  "X": 0.08,
                                  "Source": "heat-treated steel core protruding from the jacket, filling its whole cavity so that the core's energy is not spent breaking the jacket. Core mass and diameter are not published" },
            "patron_9x39_bp":   { "Prototype": "BP 7N12", "X": 0.07,
                                  "Source": "the SP-6 core reworked for 10% more penetration" },
            "patron_9x39_pab9": { "Prototype": "PAB-9", "X": 0.10 },
            "patron_9x39_sp5":  { "Prototype": "SP-5",  "X": 0.30,
                                  "Source": "steel nose over a lead base - the sniper load, not the armour one" },
            "patron_9x39_spp":  { "Prototype": "SPP 7N9", "X": 0.25 },
            "patron_9x39_fmj":  { "Prototype": "9x39 FMJ", "X": 0.30 },

            // --- 9x18 PM ---
            "patron_9x18pm_PST_gzh":  { "Prototype": "57-N-181S PST", "X": 0.30 },
            "patron_9x18pm_PBM":      { "Prototype": "PBM 7N25",      "X": 0.15,
                                        "Source": "hardened steel core in a light bullet driven fast; core figures not published" },
            "patron_9x18pm_PMM":      { "Prototype": "PMM 57-N-181SM","X": 0.25 },
            "patron_9x18pm_BZT_gzh":  { "Prototype": "BZT AP-tracer",  "X": 0.20 },
            "patron_9x18pm_RG028_gzh":{ "Prototype": "RG028",          "X": 0.20 },
            "patron_9x18pm_P_gzh":    { "Prototype": "P gzh",          "X": 0.35 },
            "patron_9x18pm_PSO_gzh":  { "Prototype": "PSO",            "X": 0.35 },
            "patron_9x18pm_PPT_gzh":  { "Prototype": "PPT tracer",     "X": 0.35 },
            "patron_9x18pm_PPE_gzh":  { "Prototype": "PPE",            "X": 0.35 },
            "patron_9x18pm_PRS_gs":   { "Prototype": "PRS reduced ricochet", "X": 0.80 },
            "patron_9x18pm_PS_gs_PPO":{ "Prototype": "PS gs PPO",      "X": 0.35 },
            "patron_9x18pm_PSV":      { "Prototype": "PSV",            "X": 0.90 },
            "patron_9x18pm_SP7_gzh":  { "Prototype": "SP-7",           "X": 0.90 },
            "patron_9x18pm_SP8_gzh":  { "Prototype": "SP-8",           "X": 0.95 },

            // --- 5.7x28 ---
            "patron_57x28_ss190":   { "Prototype": "SS190",   "X": 0.10,
                                      "Source": "steel penetrator over an aluminium core in a reinforced copper jacket; neither the penetrator's mass nor its diameter is published" },
            "patron_57x28_l191":    { "Prototype": "L191 AP", "X": 0.08 },
            "patron_57x28_sb193":   { "Prototype": "SB193 subsonic", "X": 0.30 },
            "patron_57x28_ss197sr": { "Prototype": "SS197SR V-Max",  "X": 0.85 },
            "patron_57x28_ss198lf": { "Prototype": "SS198LF",        "X": 0.80 },
            "patron_57x28_r37f":    { "Prototype": "R37.F",          "X": 0.95 },
            "patron_57x28_r37x":    { "Prototype": "R37.X",          "X": 0.90 },

            // --- 4.6x30 ---
            "patron_46x30_ap_sx":       { "Prototype": "4.6x30 AP SX", "X": 0.05, "CoreAreaFrac": 0.85, "CoreMassFrac": 0.92, "CoreHardnessHv": 700,
                                          "Source": "the bullet IS the core: 2 g of hardened steel with a copper plating and nothing else. Fractions are the plating's share, which is why this one punches so far above its energy" },
            "patron_46x30_fmj_sx":      { "Prototype": "4.6x30 FMJ SX", "X": 0.20 },
            "patron_46x30_subsonic_sx": { "Prototype": "4.6x30 subsonic","X": 0.25 },
            "patron_46x30_jsp":         { "Prototype": "4.6x30 JSP",     "X": 0.70 },
            "patron_46x30_action_sx":   { "Prototype": "4.6x30 Action SX","X": 0.90 },

            // --- .45 ACP, 7.62x25, .357 ---
            "patron_1143x23_acp_ap":         { "Prototype": ".45 ACP AP", "X": 0.10 },
            "patron_1143x23_acp":            { "Prototype": ".45 ACP ball", "X": 0.30 },
            "patron_1143x23_acp_lasermatch_fmj": { "Prototype": ".45 Lasermatch FMJ", "X": 0.30 },
            "patron_1143x23_acp_hydra_shok": { "Prototype": ".45 Hydra-Shok", "X": 0.90 },
            "patron_1143x23_rip":            { "Prototype": ".45 RIP",     "X": 0.95 },
            "patron_762x25tt_Pst_gzh":       { "Prototype": "7.62x25 Pst", "X": 0.20 },
            "patron_762x25tt_P_Gl":          { "Prototype": "7.62x25 P gl","X": 0.30 },
            "patron_762x25tt_T_Gzh":         { "Prototype": "7.62x25 tracer","X": 0.30 },
            "patron_762x25tt_FMJ43":         { "Prototype": "7.62x25 FMJ43", "X": 0.30 },
            "patron_762x25tt_akbs":          { "Prototype": "7.62x25 AKBS",  "X": 0.30 },
            "patron_762x25tt_LRN":           { "Prototype": "7.62x25 LRN",   "X": 0.60 },
            "patron_762x25tt_LRNPC":         { "Prototype": "7.62x25 LRNPC", "X": 0.50 },
            "patron_9x33r_fmj":  { "Prototype": ".357 FMJ", "X": 0.30 },
            "patron_9x33r_sp":   { "Prototype": ".357 SP",  "X": 0.70 },
            "patron_9x33r_hp":   { "Prototype": ".357 HP",  "X": 0.90 },
            "patron_9x33r_jhp":  { "Prototype": ".357 JHP", "X": 0.90 },

            // --- .300 BLK, .366 TKM ---
            "patron_762x35_blackout_ap": { "Prototype": ".300 BLK AP", "X": 0.10 },
            "patron_762x35_cbj":         { "Prototype": ".300 BLK CBJ", "X": 0.05,
                                           "Source": "a tungsten sub-projectile in a discarding sabot; CBJ do not publish the penetrator's dimensions" },
            "patron_762x35_m62":         { "Prototype": ".300 BLK M62 tracer", "X": 0.25 },
            "patron_762x35_blackout":    { "Prototype": ".300 BLK ball", "X": 0.30 },
            "patron_762x35_whisper":     { "Prototype": ".300 Whisper",  "X": 0.35 },
            "patron_762x35_vmax":        { "Prototype": ".300 BLK V-Max","X": 0.95 },
            "patron_366_custom_ap":      { "Prototype": ".366 AP-M",     "X": 0.10 },
            "patron_366_TKM_EKO":        { "Prototype": ".366 EKO",      "X": 0.25,
                                           "Source": "a solid copper bullet - light, fast and lead-free, which is not the same as expanding" },
            "patron_366_TKM_FMJ":        { "Prototype": ".366 FMJ",      "X": 0.30 },
            "patron_366_TKM_Geksa":      { "Prototype": ".366 Geksa",    "X": 0.80 },

            // --- 12.7 ---
            "patron_127x55_ps12b": { "Prototype": "PS12B",  "X": 0.08,
                                     "Source": "the armour-piercing load of the ASh-12; core figures not published" },
            "patron_127x55_ps12":  { "Prototype": "PS12",   "X": 0.30 },
            "patron_127x55_ps12a": { "Prototype": "PS12A",  "X": 0.20 },
            "patron_127x108":      { "Prototype": "B-32 API", "X": 0.10 },
            "patron_127x108_bzt":  { "Prototype": "BZT-44 API-T", "X": 0.10 },
            "patron_127x99_m903":  { "Prototype": "M903 SLAP", "X": 0.05, "CoreAreaFrac": 0.55, "CoreMassFrac": 1.0, "CoreHardnessHv": 1300,
                                     "Source": "the sabot is gone by the time it arrives: a .223 in tungsten penetrator, 5.66 mm, and the game already gives the round that penetrator's 7.62 mm as a calibre, so the area fraction is against that" },
            "patron_127x99_m33":   { "Prototype": "M33 ball", "X": 0.25 },
            "patron_127x99_m21":   { "Prototype": "M21 tracer","X": 0.20 },
            "patron_127x99_hp":    { "Prototype": ".50 BMG HP","X": 0.90 },

            // --- shotgun slugs ---
            "patron_12x70_slug_ap_20":  { "Prototype": "AP-20 slug", "X": 0.10,
                                          "Source": "a hardened steel slug; no jacket to strip and no separate core" },
            "patron_20x70_slug_ap":     { "Prototype": "20/70 AP slug", "X": 0.10 },
            "patron_12x70_slug_poleva_3": { "Prototype": "Poleva-3",  "X": 0.60 },
            "patron_12x70_slug_poleva_6u":{ "Prototype": "Poleva-6u", "X": 0.55 },
            "patron_12x70_rip":         { "Prototype": "12/70 RIP",   "X": 0.95 },
            "patron_12x70_slug_hp_copper": { "Prototype": "copper HP slug", "X": 0.90 }
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
          // HardnessHv decides which of two pieces of metal gives way first, which is a
          // separate question from how strong either of them is. Without it no single
          // constant fits both the rolled-armour V50 ladder and the GOST classes: they
          // sit a factor of 3.8 apart in energy per mm2, and most of that gap is a 40 HRC
          // core meeting a 580 HV plate.
          // Every Source names where its numbers came from: either a derivation rule
          // written with a "·" (0.45·UTS and its kin) or a named document — datasheet,
          // handbook, MIL spec. A strength without a provenance is indistinguishable
          // from a fitted one, and fitted strengths are what the calibration rule
          // forbids: the free constants live in the mode constants, never here.
          "ArmorMaterials": {
            "ArmoredSteel": { "Class": "Ductile", "DensityGCm3": 7.85, "YieldMPa": 1250, "ShearMPa": 750,
                              "HardnessHv": 580,
                              "Source": "AR500-grade armour steel per maker datasheets (SSAB 500-class): ~550 HB -> 580 HV, yield 1250 MPa, UTS ~1650; shear 750 = 0.45·UTS, the through-hardened-steel rule. Rolled homogeneous armour is softer - 300 HB, UTS ~1000, shear 450 = 0.45·UTS, 320 HV - and the V50 ladder in the fixture is RHA, not this" },
            "Titan":        { "Class": "Ductile", "DensityGCm3": 4.43, "YieldMPa": 880,  "ShearMPa": 550,
                              "HardnessHv": 350,
                              "Source": "Ti-6Al-4V per ASM handbook / MMPDS: yield 880 MPa, UTS 950, ultimate shear 550 as tabulated (=0.58·UTS - titanium shears higher than steel's 0.45 rule), 334 HB -> 350 HV" },
            "Aluminium":    { "Class": "Ductile", "DensityGCm3": 2.70, "YieldMPa": 300,  "ShearMPa": 190,
                              "HardnessHv": 120,
                              "Source": "5083-H131 armour plate (MIL-DTL-46027): yield ~300 MPa, UTS ~317, shear 190 = 0.6·UTS, the aluminium rule; 120 HV is the 7039 (MIL-DTL-46063) end of the pair" },
            "Ceramic":      { "Class": "Brittle", "DensityGCm3": 3.90, "CompressiveMPa": 2500,
                              "HardnessHv": 1500, "HardMassFraction": 0.65,
                              "Source": "94-96% alumina per CoorsTek AD-series datasheets: compressive 2000-2600 MPa, 1400-1600 HV; 2500/1500 read mid-band. Al2O3 on a fibre backer; the hardness is why it beats a carbide core" },
            "Combined":     { "Class": "Brittle", "DensityGCm3": 3.20, "CompressiveMPa": 2600,
                              "HardnessHv": 1600, "HardMassFraction": 0.60,
                              "Source": "ceramic face on composite backing; face read one grade above the Ceramic entry, at 99%-alumina figures (CoorsTek AD-995 class): compressive ~2600 MPa, ~1600 HV" },
            "Glass":        { "Class": "Brittle", "DensityGCm3": 2.50, "CompressiveMPa": 1000,
                              "HardnessHv": 550, "Source": "laminated soda-lime float glass: compressive 1000 MPa per Saint-Gobain float-glass data, Vickers ~5.4 GPa -> 550 HV (Ashby, Engineering Materials)" },
            "Aramid":       { "Class": "Fibrous", "DensityGCm3": 1.44, "FibreTensileMPa": 2900, "FailureStrain": 0.034,
                              "Source": "DuPont Kevlar 29 datasheet: 2920 MPa tensile, 3.6% break elongation; strain read just under single-fibre break because a woven pack fails at the weave. TSVM-DZh is the Russian equivalent. No hardness - a woven pack has none worth the name" },
            "UHMWPE":       { "Class": "Fibrous", "DensityGCm3": 0.97, "FibreTensileMPa": 3400, "FailureStrain": 0.035,
                              "Source": "DSM Dyneema SK-grade fibre datasheet: 3400-3700 MPa tensile, 3-4% break strain; read at the bottom of the band for the pressed HB laminates" }
          },

          // ===== Armour construction =====
          // Keyed by the item name first and by the product — the item name up to
          // "_level" — second, so one entry covers every zone of a product but a product
          // whose zones really do differ can name them one at a time. Thickness is the
          // number the game has nowhere and the whole reason this table exists. Material
          // is only set where the game has it wrong. Anything absent keeps the game's own
          // material and falls back to the class.
          //
          // Where a manufacturer publishes a thickness it is used as published. Where one
          // publishes a mass instead — the usual case for helmets and for every Russian
          // maker — the hard element is t = m / (rho * A): the mass over the density of
          // the form it is in and the area it covers.
          //
          // TWO THINGS DECIDE WHETHER THAT ARITHMETIC IS RIGHT.
          //
          // The area must be the RATED area, not the outer rectangle. Russian plates
          // carry a frame the certificate does not count, and the makers publish both
          // side by side: Tekhinkom rate 7.14 / 7.56 / 8.46 dm2 where the outer
          // dimensions suggest 8.3, and Kora-1MK lists 40-50 dm2 of vest against 15.0 of
          // certified plate. Read over the rated areas the whole Granit line collapses
          // onto one areal density per class; read over the rectangles it does not.
          //
          // The density must be the density of the FORM, not of the fibre. Four makers
          // publish thickness and areal density for the same object, and they line up by
          // how hard the thing was pressed:
          //     sewn package        630 kg/m3   IOTV, 7.6 mm at 4.79 kg/m2
          //     UD laminate panel   870         HighCom Trooper SA3920
          //     pressed PE shell   1050         Galvion Caiman TL, 6.00 mm at 6.35
          //     pressed aramid     1350         ACH patent, 8.13 mm at 10.94
          // Set DensityGCm3 wherever the form differs from the material table's fibre
          // figure. The check on all of it is the PASGT, the one helmet with both numbers
          // published: 11.2 kg/m2 at 1350 gives 8.3 mm against a measured 7.3 +/- 0.8.
          "ArmorPlates": {
            // --- vests ---
            // The Soviet vests all carry 7.6 mm of package, and that number is borrowed
            // rather than computed. Their own specification gives a layer count, not a
            // thickness — 30 layers of TSVM-DZh in the 6B5, 30 of TSVM-2 in the 6B23 —
            // and the fabric's areal density is not published anywhere reachable. What IS
            // published is what those 30 layers are worth: NIJ IIIA. So the thickness
            // comes from a certified IIIA package that does publish one, the IOTV Gen4
            // torso at 7.6 mm and 4.79 kg/m2. Two published facts, no arithmetic of ours.
            // It replaced a flat 8 mm that nobody had sourced, and moved nothing by 5%.
            //
            // Only the old vests carry it. The 6B23 is modelled by the game as separate
            // soft-armour zones plus separate steel plates, so its package is already a
            // layer of its own and putting one on the plate as well would count it twice;
            // the 6B5 and 6B3TM are single items per zone, titanium and fabric together,
            // and for them this field is the only way the fabric exists at all.
            //
            // 6B5-16 is differential like the 6B3TM-01: a rifle-class front and a
            // fragment-class back. Eight 6.5 mm tiles in front plus three to five thin
            // ones, seven 1.25 mm tiles behind
            "6b5-16":     { "Prototype": "6B5-16, ADU 605T-83", "Material": "Titan", "ThicknessMm": 6.5, "BackingMm": 7.6,
                            "Source": "8 tiles of 6.5 mm titanium in front plus 3-5 of 1.25; 30-layer TSVM-DZh; 7.5 kg" },
            "6b5-16_level3_soft_armor_back": { "Prototype": "6B5-16 back, ADU 605-80", "Material": "Titan", "ThicknessMm": 1.25, "BackingMm": 7.6,
                            "Source": "the back is 7 tiles of 1.25 mm, a class below the front" },

            // 6B2 / Zh-81: nineteen thin titanium tiles, doubled over the heart
            "6b2":        { "Prototype": "6B2 / Zh-81, ADU-605-80", "Material": "Titan", "ThicknessMm": 1.25, "BackingMm": 7.6,
                            "Source": "VT-14 titanium 1.25 mm (to 1.4 with tolerance), 19 tiles in three rows of three with the heart area DOUBLED to two layers. 4.2-4.8 kg over a published 28-30 dm2, class 2" },
            "6b5-15":     { "Prototype": "6B5-15, ADU 14.20.00.000", "Material": "Ceramic", "ThicknessMm": 13, "BackingMm": 7.6, "BackingMaterial": "UHMWPE",
                            "DensityGCm3": 2.52,
                            "Source": "boron carbide 13 mm, 17-20 tiles a side, on a fabric package" },
            "kora_kulon": { "Prototype": "Kora-Kulon", "Material": "ArmoredSteel", "ThicknessMm": 4.3, "BackingMm": 6,
                            "Source": "steel plate, Br3" },

            // the 6B3TM-01 is two different vests front and back, and the game splits it
            // the same way: class 4 in front, class 2 behind. One entry for the pair gave
            // the front the back's plate and made it five times too thin
            "6b3TM":      { "Prototype": "6B3TM-01, VT-23", "Material": "Titan", "ThicknessMm": 6.5, "BackingMm": 7.6,
                            "Source": "12-15 tiles of 6.5 mm VT-23 titanium" },
            "6b3TM_level2_soft_armor_back": { "Prototype": "6B3TM-01 back, VT-14", "Material": "Titan", "ThicknessMm": 1.25, "BackingMm": 7.6,
                            "Source": "the -01 swaps the back for 7 tiles of 1.25 mm VT-14" },
            "6b3TM_level2_soft_armor_groin_back": { "Prototype": "6B3TM-01 back, VT-14", "Material": "Titan", "ThicknessMm": 1.25, "BackingMm": 7.6,
                            "Source": "the -01 swaps the back for 7 tiles of 1.25 mm VT-14" },

            // --- plates ---
            "sapi_6_frontback":         { "Prototype": "ESAPI", "Material": "Ceramic", "ThicknessMm": 10, "BackingMm": 12, "BackingMaterial": "UHMWPE",
                                          "DensityGCm3": 2.52,
                                          "Source": "boron carbide 10 mm on a UHMWPE backer; 5.5 lb over a 9.5x12.5 in medium is 3.26 g/cm2" },
            "SSAPI_ESBI_6_side":        { "Prototype": "ESBI side insert", "Material": "Ceramic", "ThicknessMm": 10, "BackingMm": 10, "BackingMaterial": "UHMWPE",
                                          "DensityGCm3": 2.52,
                                          "Source": "the ESAPI construction in a side cut; 2.25 lb over 6x8 in is the same 3.30 g/cm2" },
            // A plate's outer rectangle is not the armour. Tekhinkom's panels carry a
            // frame the certificate does not count, and the rated area is what the
            // ceramic actually covers: 7.14, 7.56 and 8.46 dm² against outer dimensions
            // that would suggest 7.5, 8.3 and 9.0. Reading the mass over the rectangle
            // spread it too thin, and every Granit here was about a tenth light for it.
            //
            // The rated areas are the check on themselves. Over them the whole line
            // comes out at one areal density per class — Br4 at 3.57, 3.57 and 3.61
            // g/cm² across the three sizes, Br5 at 4.13, 4.10 and 4.10. Over the outer
            // rectangles that agreement falls apart.
            "granitBr4":                { "Prototype": "Granit Br4 (Granit-5A)", "Material": "Ceramic", "ThicknessMm": 6, "BackingMm": 12, "BackingMaterial": "UHMWPE",
                                          "Source": "2.55 / 2.70 / 3.05 kg over 7.14 / 7.56 / 8.46 dm² rated - 3.6 g/cm² at every size" },
            "granitBr5":                { "Prototype": "Granit Br5", "Material": "Ceramic", "ThicknessMm": 6.8, "BackingMm": 14, "BackingMaterial": "UHMWPE",
                                          "Source": "2.95 / 3.10 / 3.47 kg over 7.14 / 7.56 / 8.46 dm² rated - 4.1 g/cm² at every size" },
            "granit4_5class_front":     { "Prototype": "Granit-4, Br5", "Material": "Ceramic", "ThicknessMm": 6.8, "BackingMm": 14, "BackingMaterial": "UHMWPE",
                                          "Source": "the Br5 of the line, 3.10 kg over 7.56 dm² rated" },
            "granit4_5class_back":      { "Prototype": "Granit-4, Br5", "Material": "Ceramic", "ThicknessMm": 6.8, "BackingMm": 14, "BackingMaterial": "UHMWPE",
                                          "Source": "the Br5 of the line, 3.10 kg over 7.56 dm² rated" },
            "granit4rs":                { "Prototype": "Granit-4RS", "Material": "Ceramic", "ThicknessMm": 6.8, "BackingMm": 13, "BackingMaterial": "UHMWPE",
                                          "Source": "365x290x20 mm and 3.8 kg outside; at the line's 4.1 g/cm² that is 9.3 dm² rated" },
            "granit":                   { "Prototype": "Granit Br5, first execution", "Material": "Ceramic", "ThicknessMm": 7.7, "BackingMm": 15, "BackingMaterial": "UHMWPE",
                                          "Source": "305x263x22 mm, 3.45 kg - the heavy execution, above the rest of the line" },
            "granit4_zhukBr3_3class_front": { "Prototype": "Zhuk-3", "Material": "UHMWPE", "ThicknessMm": 23,
                                          "Source": "300x250 mm SAPI cut, 23 mm, 1.70 kg - all polyethylene, and the mass over that face comes to the 23" },
            // one size only, and KlASS and its owners agree on the rated 6.0 dm²
            "korund_vmk_6class_front":  { "Prototype": "Korund-VM-K", "Material": "Ceramic", "ThicknessMm": 7.2, "BackingMm": 16, "BackingMaterial": "UHMWPE",
                                          "Source": "2.6 kg over 6.0 dm² rated; 260x260x23 mm outside" },
            "korund_vm_k_6class_back":  { "Prototype": "Korund-VM-K", "Material": "Ceramic", "ThicknessMm": 7.2, "BackingMm": 16, "BackingMaterial": "UHMWPE",
                                          "Source": "2.6 kg over 6.0 dm² rated; 260x260x23 mm outside" },
            // Russian steel panels are 44S unless the product says otherwise: NII Stali's
            // ultra-high-strength grade, 55-57 HRC, UTS 2250-2350, yield 2000-2100, which
            // the maker puts level with MARS-300 and ARMOX-600 and which is nothing like
            // the AR500 datasheet the material entry carries. Shear is 0.45*UTS by the same
            // through-hardened rule the material uses; hardness 56 HRC -> 613 HV.
            "korund_back_6b23_2":       { "Prototype": "6B23 steel panel, 44S", "Material": "ArmoredSteel", "ThicknessMm": 6.3,
                                          "ShearMPa": 1035, "YieldMPa": 2050, "HardnessHv": 613,
                                          "Source": "6.3 mm of 44S steel, rated against the heat-hardened AKM core. The 6B23 certificate names its whole schedule: 57-N-231 heat-hardened core at 10 m, 7N22 and M193/M855 at 25, 7N24 and 57-N-323S at 50" },
            "korund":                   { "Prototype": "Korund-VM steel panel", "Material": "ArmoredSteel", "ThicknessMm": 6.3,
                                          "ShearMPa": 1035, "YieldMPa": 2050, "HardnessHv": 613,
                                          "Source": "Br4 steel; 15.9 dm2 of panel at 6.3 mm is 7.9 kg of the vest's 9.8" },
            "korund_vm":                { "Prototype": "Korund-VM steel panel", "Material": "ArmoredSteel", "ThicknessMm": 6.3,
                                          "ShearMPa": 1035, "YieldMPa": 2050, "HardnessHv": 613,
                                          "Source": "Br4 steel; 15.9 dm2 of panel at 6.3 mm is 7.9 kg of the vest's 9.8" },
            "korund_6b12":              { "Prototype": "6B12 chest plate", "Material": "ArmoredSteel", "ThicknessMm": 6,
                                          "ShearMPa": 1035, "YieldMPa": 2050, "HardnessHv": 613,
                                          "Source": "the 6B12 chest and abdomen plates are 6 mm of steel; the back one is 2" },

            // --- Western plates, nearly all with a published thickness ---
            // Several in-game brands are lightly disguised real makers: NESCO is HESCO,
            // TallCom is HighCom, SPRTN is Spartan, GAC is HighCom's Guardian line,
            // Monoclete is Paraclete, PRTCTR is DFNDR, Cult Locust is the Adept Mantis
            "SAPI_SPRTN_Elaphros":      { "Prototype": "Spartan Elaphros", "Material": "UHMWPE", "ThicknessMm": 30.48,
                                          "Source": "1.2 in published; a monolithic UHMWPE hybrid with NO ceramic face at all. 1.45 kg, 10x12 shooters. NIJ III certified - the game's class 4 does not match" },
            "SAPI_Monoclete_PE":        { "Prototype": "Paraclete 10260", "Material": "UHMWPE", "ThicknessMm": 25.4,
                                          "Source": "1 in published, 1.36 kg, 10x12 shooters cut, NIJ III standalone. The game's 1.35 kg is a near-exact match" },
            // NOTE on the ceramic composites below: what these makers publish is the
            // TOTAL plate thickness, ceramic face plus polyethylene backer together —
            // the split itself is not published for any plate anywhere; that was hunted
            // specifically, including patent search and sectioned-plate photographs,
            // and came back empty. But the split is not free to invent either: with the
            // total thickness, the total areal density and the face material known, the
            // layer thicknesses are DETERMINED —
            //     t_face = T · (ρ_avg − ρ_backer) / (ρ_face − ρ_backer)
            // with the backer at consolidated-UHMWPE density (0.97). Each entry below
            // carries that derivation in its source line; the derived split preserves
            // the plate's real areal density exactly, which is the one number the maker
            // did publish.
            "SAPI_GAC_4sss2":           { "Prototype": "HighCom Guardian 4sss2", "Material": "Ceramic", "ThicknessMm": 9.7,
                                          "BackingMm": 14.9, "BackingMaterial": "UHMWPE", "DensityGCm3": 3.21,
                                          "Source": "0.97 in published TOTAL. SILICON CARBIDE on a UHMWPE backer - the game tags it plain UHMWPE and omits the ceramic. NIJ IV to the older 0101.04. Mass not published; avg density read at the Level IV band 4.55 g/cm2. Split: 24.6·(1.85−0.97)/(3.21−0.97) = 9.7 mm SiC, rest backer; areal density preserved" },
            "SAPI_GAC_3s15m":           { "Prototype": "HighCom Guardian 3s15m", "Material": "UHMWPE", "ThicknessMm": 21,
                                          "Source": "0.83 in published, 100% Dyneema, XTclave-consolidated, NIJ III standalone" },
            "SAPI_NESCO_4400":          { "Prototype": "HESCO 4400SA-MC", "Material": "Ceramic", "ThicknessMm": 9.1,
                                          "BackingMm": 11.9, "BackingMaterial": "UHMWPE",
                                          "Source": "0.83 in published TOTAL; 3.6 kg over a 9.5x12.5 SAPI cut is 4.70 g/cm2 average. Ceramic type not published - read as alumina (3.9), the heavy Level IV construction the weight itself argues for. Split: 21·(2.24−0.97)/(3.9−0.97) = 9.1 mm, rest backer; 9.1·3.9 + 11.9·0.97 = 4.70 g/cm2 exactly. NIJ IV, but the 4400 is the NON-certified variant - the 4401 is the listed one" },
            "SAPI_TallCom_Guardian":    { "Prototype": "HighCom Guardian 4sas4", "Material": "Ceramic", "ThicknessMm": 9.0,
                                          "BackingMm": 10.1, "BackingMaterial": "UHMWPE",
                                          "Source": "0.75 in published TOTAL. Ceramic type not published - read as alumina; mass not published either - HighCom pulled the page - so avg density is the Level IV band (2.36 whole-plate). Split: 19.05·(2.36−0.97)/(3.9−0.97) = 9.0 mm, rest backer" },
            "SAPI_Cult_Locust":         { "Prototype": "Adept Mantis", "Material": "Titan", "ThicknessMm": 4.7,
                                          "BackingMm": 13.1, "BackingMaterial": "UHMWPE",
                                          "Source": "0.7 in published TOTAL, 2.47 kg - forged multi-curve titanium bonded to a polyethylene backer. III+ / Adept 'RF2'. The game's 2.56 kg nearly matches. Split: 17.8·(1.88−0.97)/(4.43−0.97) = 4.7 mm of titanium, rest backer; areal density 3.35 g/cm2 preserved" },
            "SAPI_AR500_legacy":        { "Prototype": "AR500 Armor Heritage", "Material": "ArmoredSteel", "ThicknessMm": 6.35,
                                          "Source": "0.34 in total published including the FragLock coat; the steel core is the industry-standard 0.25 in and the rest is non-ballistic. 'Legacy' was never a SKU - the 3.85 kg in game matches the Heritage's 8.5 lb" },
            "SAPI_SPRTN_Omega":         { "Prototype": "Spartan Omega AR500", "Material": "ArmoredSteel", "ThicknessMm": 6.35,
                                          "Source": "0.25 in of AR500 published. Full Coat takes the OVERALL thickness to 0.5 in but the extra is polyurea, not armour" },
            "sapi":                     { "Prototype": "SAPI", "Material": "Ceramic", "ThicknessMm": 6.1, "BackingMm": 10, "BackingMaterial": "UHMWPE",
                                          "DensityGCm3": 2.52,
                                          "Source": "medium 1.82 kg over a published 241x318 mm = 7.66 dm2. Boron or silicon carbide on Spectra. The game's 1.82 kg is exact. DoD protocol, not NIJ" },
            "SSAPI_5_side":             { "Prototype": "SAPI side insert", "Material": "Ceramic", "ThicknessMm": 5.6, "BackingMm": 7.6, "BackingMaterial": "UHMWPE",
                                          "Source": "~1 kg over the 6x8 in cut = 3 dm². Not a disguise - SSAPI is the real DoD designation for the plain-family side plate" },

            // material corrections where nobody publishes an amount
            "SAPI_NewSphereTech":       { "Prototype": "invented, but its own description says aluminium OXIDE", "Material": "Ceramic",
                                          "Source": "the game's material field reads 'Aluminium' out of the phrase 'An Aluminum Oxide ballistic plate' - alumina is a ceramic, not a metal" },
            "SAPI_PRTCTR_Lightweight":  { "Prototype": "DFNDR Level IIIA", "Material": "UHMWPE",
                                          "Source": "0.44 kg in medium, thickness never published. The real donor is IIIA - HANDGUN ONLY, no rifle rating at all, where the game puts it on the rifle scale" },

            // --- helmets: aramid shells ---
            "Untar":            { "Prototype": "PASGT", "Material": "Aramid", "ThicknessMm": 7.8,
                                  "Source": "11.2 kg/m2, 19 layers of Kevlar 29; the shell measures 7.3 +/- 0.8 mm" },
            "msa_tc2001":       { "Prototype": "MSA ACH TC-2001, full ear cutout", "Material": "Aramid", "ThicknessMm": 8.13,
                                  "DensityGCm3": 1.35,
                                  "Source": "8.13 mm at 10.94 kg/m2, both published - patent US10448695. Kevlar 129 or Twaron T2000, 25-27 plies, 12-14% PVB-phenolic" },
            "msa_tc2002":       { "Prototype": "MSA ACH TC-2002, partial ear cutout", "Material": "Aramid", "ThicknessMm": 8.13,
                                  "DensityGCm3": 1.35,
                                  "Source": "the same TC-2000 shell as the 2001, cut differently" },
            "msa_gallet_tc800": { "Prototype": "MSA Gallet TC 801, high cut", "Material": "Aramid", "ThicknessMm": 8.3,
                                  "DensityGCm3": 1.35,
                                  "Source": "1129 g over a published 1005 cm2 = 11.23 kg/m2; the series holds 11.1 across all three cuts" },
            "Untar":            { "Prototype": "PASGT", "Material": "Aramid", "ThicknessMm": 8.3,
                                  "DensityGCm3": 1.35,
                                  "Source": "11.2 kg/m2, 19 plies of Kevlar 29 at 16-18% resin; the shell measures 7.3 +/- 0.8 mm" },
            "ronin":            { "Prototype": "DevTac Ronin", "Material": "Aramid", "ThicknessMm": 7,
                                  "DensityGCm3": 1.35,
                                  "Source": "7 mm of ballistic Kevlar, published; 2.2-2.7 kg against the 1.6 the game gives it" },
            "ratnik_6b47":      { "Prototype": "6B47 Ratnik", "Material": "Aramid", "ThicknessMm": 6.8,
                                  "DensityGCm3": 1.10,
                                  "Source": "1 kg over a published 11.0-11.5 dm2. Read at 1.10 not 1.35: the shell is two resin-bonded skins around a DRY, unimpregnated aramid pack, not one laminate" },
            "highcom_striker_achhc": { "Prototype": "HighCom Striker ACHHC", "Material": "Aramid", "ThicknessMm": 7.3,
                                  "DensityGCm3": 1.20,
                                  "Source": "842 g shell in medium, published; hybrid Kevlar and Spectra, so between the aramid and PE densities" },
            "crye_precision_airframe":  { "Prototype": "Crye AirFrame", "Material": "Aramid", "ThicknessMm": 7,
                                  "DensityGCm3": 1.35,
                                  "Source": "2.30 lb complete in medium less pads and retention; Crye publish no material and no thickness" },
            "item_equipment_helmet_crye_airframe_chops": { "Prototype": "Crye AirFrame chops", "Material": "Aramid", "ThicknessMm": 6,
                                  "DensityGCm3": 1.20,
                                  "Source": "~165 g each; certified to full NIJ IIIA including .44 Magnum, which the game under-rates at class 3" },
            "item_equipment_helmet_crye_airframe_ears":  { "Prototype": "Crye AirFrame ears", "Material": "Aramid", "ThicknessMm": 6,
                                  "DensityGCm3": 1.20,
                                  "Source": "same ballistic table as the chops; Crye publish no weight for the ears" },
            "bnti_lshz_2dtm":   { "Prototype": "LShZ-2DTM, Armocom (not BNTI)", "Material": "Aramid", "ThicknessMm": 10.2,
                                  "DensityGCm3": 1.25,
                                  "Source": "corpus 1.9 kg over a published 15.0 dm2; the LShZ-2 independently gives the same 10.6-11.2" },
            "class_tor2":       { "Prototype": "TOR-2, NPP KlASS", "Material": "Aramid", "ThicknessMm": 11,
                                  "DensityGCm3": 1.25,
                                  "Source": "2.2-2.55 kg over a published 11.2-13.5 dm2; Br2, fragment V50 >= 720 m/s" },
            "lshz":             { "Prototype": "LShZ, Armocom", "Material": "Aramid", "ThicknessMm": 5,
                                  "DensityGCm3": 1.25,
                                  "Source": "0.7-1.3 kg over a published 9.5-14.5 dm2; Br1 S per GOST R 57560-2017" },
            "fort_kiver_m":     { "Prototype": "Fort Kiver-M", "Material": "Aramid", "ThicknessMm": 6.4,
                                  "DensityGCm3": 1.25,
                                  "Source": "1.6 kg over a published 16 dm2, BNTI catalogue. Fort certify class 1+ - Stechkin, 9x19 and fragments at 570 m/s - where the game says 3" },
            "ballisticarmorco_bastion": { "Prototype": "Ballistic Armor Co Bastion", "Material": "Aramid", "ThicknessMm": 8,
                                  "DensityGCm3": 1.35,
                                  "Source": "epoxy-impregnated Kevlar, 3 lb 4 oz complete, NIJ IIIA. A DIFFERENT product from the Adept Bastion despite the name" },
            "item_equipment_helmet_lshz2dtm_aventail": { "Prototype": "LShZ-2DTM aventail", "Material": "Aramid", "ThicknessMm": 12,
                                  "DensityGCm3": 0.90,
                                  "Source": "0.6 kg over a published 5.5 dm2, Br2. Discrete fabric, so read between a sewn pack and a pressed shell" },

            // --- helmets: polyethylene shells ---
            "exfil":            { "Prototype": "Team Wendy EXFIL Ballistic", "Material": "UHMWPE", "ThicknessMm": 7.3,
                                  "DensityGCm3": 1.05,
                                  "Source": "0.79 kg shell over 10.25 dm2, both from Team Wendy's own arithmetic. Cross-checked: the SL is 6.3 mm and they call it 15% lighter at the same geometry" },
            "mtek_flux":        { "Prototype": "MTEK FLUX", "Material": "UHMWPE", "ThicknessMm": 4.6,
                                  "DensityGCm3": 1.05,
                                  "Source": "0.5 kg shell over a published 164 in2; satisfies MTEK's own 'less than 0.25 in'. NOTE 4.73 kg/m2 is light for a .44 Mag IIIA claim - the outlier of the set" },
            "mtek_strike":      { "Prototype": "MTEK Strike", "Material": "UHMWPE", "ThicknessMm": 5.4,
                                  "DensityGCm3": 1.05,
                                  "Source": "0.5 kg seamless shell, no darts or bolt holes anywhere; coverage not published" },
            "nfm_hjelm":        { "Prototype": "NFM HJELM HC 160F", "Material": "UHMWPE", "ThicknessMm": 4.8,
                                  "DensityGCm3": 1.02,
                                  "Source": "4.8 mm at 4.9 kg/m2, both published. NFM never name the fibre; the density is what identifies it as polyethylene" },
            "diamond_age_bastion": { "Prototype": "Diamond Age / Adept Bastion", "Material": "Aramid", "ThicknessMm": 8.55,
                                  "DensityGCm3": 1.20,
                                  "Source": "MEASURED at four points - 8.48/8.56/8.51/8.64 mm, 1.075 kg in XL. Chesapeake Testing CD01-2018-R04BRT-135172" },

            // the game's parent item is the Caiman HYBRID, not the TL. Its shell is
            // carbon and carries no ballistic rating at all - only AR/PD 10-02 blunt
            // impact. The polyethylene is in the applique, which the game models
            // separately as item_equipment_helmet_galvion_applique
            "galvion_caiman":   { "Prototype": "Galvion Caiman Hybrid, carbon bump shell", "Material": "Aramid", "ThicknessMm": 2.1,
                                  "DensityGCm3": 1.55,
                                  "Source": "3.37 kg/m2 of carbon; NO ballistic rating, blunt impact only" },
            "item_equipment_helmet_galvion_applique": { "Prototype": "Caiman Hybrid ballistic applique", "Material": "UHMWPE", "ThicknessMm": 6.2,
                                  "DensityGCm3": 1.05,
                                  "Source": "6.59 kg/m2 confirmed from two carriers of Galvion's sheet; 0.59 kg in medium" },

            "helmet_team_wendy_exfil_ear_covers":        { "Prototype": "EXFIL ear covers", "Material": "UHMWPE", "ThicknessMm": 7.3,
                                  "DensityGCm3": 1.05,
                                  "Source": "68 cm2 each, published. The 318 g/pair on retail pages is the whole assembly with housings - the panels are ~105 g" },
            "helmet_team_wendy_exfil_ear_covers_coyote": { "Prototype": "EXFIL ear covers", "Material": "UHMWPE", "ThicknessMm": 7.3,
                                  "DensityGCm3": 1.05,
                                  "Source": "identical item in the database, only the prefab differs" },

            // a rifle-rated applique, not a shell - and lighter than the figure I first
            // used, which included the hook-and-loop hardware. "Plate" is that sentence
            // made into data: 18 mm against the 7.3 of the thickest shell anyone fields,
            // so the shell ceiling has nothing to say about the class it is sold at
            "item_equipment_helmet_gentex_slaap_gray":   { "Prototype": "Velocity SLAAP", "Material": "UHMWPE", "ThicknessMm": 18,
                                  "DensityGCm3": 1.00, "Plate": true,
                                  "Source": "0.45 kg in large, 0.39 in small. Defeats 7.62x39 mild steel core at 2400 fps. Gentex's sheet has no thickness and no area field at all" },
            "item_equipment_helmet_gentex_slaap_green":  { "Prototype": "Velocity SLAAP", "Material": "UHMWPE", "ThicknessMm": 18,
                                  "DensityGCm3": 1.00, "Plate": true, "Source": "as the gray" },
            "item_equipment_helmet_gentex_slaap_tan":    { "Prototype": "Velocity SLAAP", "Material": "UHMWPE", "ThicknessMm": 18,
                                  "DensityGCm3": 1.00, "Plate": true, "Source": "as the gray" },
            "ulach":            { "Prototype": "HighCom Striker ULACH", "Material": "UHMWPE", "ThicknessMm": 5.4,
                                  "DensityGCm3": 1.05,
                                  "Source": "950 g as configured in medium. UHMWPE Spectra by HighCom's own words - the game's aramid is wrong" },

            // the FAST MT shell is a hybrid of carbon, unidirectional polyethylene and
            // woven aramid - not the plain aramid the game and Wikipedia both give it
            "ops_core_fastMT":  { "Prototype": "Ops-Core FAST MT Super High Cut", "Material": "Aramid", "ThicknessMm": 6.43,
                                  "DensityGCm3": 1.055,
                                  "Source": "6.43 mm at 1.39 lb/ft2, both published by Gentex. Certified to SOCOM Maritime, not NIJ IIIA" },
            "helmet_ops_core_fast_tan": { "Prototype": "Ops-Core FAST MT Super High Cut", "Material": "Aramid", "ThicknessMm": 6.43,
                                  "DensityGCm3": 1.055, "Source": "the same shell" },

            // --- helmets: metal shells ---
            "altin":            { "Prototype": "Altyn", "Material": "Titan", "ThicknessMm": 3, "BackingMm": 7.6,
                                  "Source": "3 mm titanium on a 15-30 layer TSVM-DZh backing; 4.1 kg with the visor" },
            "helmet_altyn_face_shield": { "Prototype": "Altyn visor", "Material": "Titan", "ThicknessMm": 3,
                                  "Source": "3 mm titanium, as the shell" },
            "maska1sha":        { "Prototype": "Maska-1Sch", "Material": "ArmoredSteel", "ThicknessMm": 3,
                                  "Source": "4.3 kg of armour steel over 13 dm2, GOST class 2" },
            "item_equipment_helmet_maska_1sh_shield":       { "Prototype": "Maska-1Sch visor", "Material": "ArmoredSteel", "ThicknessMm": 3.5,
                                  "Source": "steel plate with a vision slit, class 2" },
            "item_equipment_helmet_maska_1sh_shield_killa": { "Prototype": "Maska-1Sch visor", "Material": "ArmoredSteel", "ThicknessMm": 3.5,
                                  "Source": "steel plate with a vision slit, class 2" },
            "sferaS_SSSh94":    { "Prototype": "Sfera SSSh-94", "Material": "ArmoredSteel", "ThicknessMm": 2.0,
                                  "Source": "PUBLISHED - manufacturer passport, 'листов стали толщиной 2.0+0.3 мм', three plates. 3.5 kg over 10 dm2, class 2" },
            "ssh68":            { "Prototype": "SSh-68", "Material": "ArmoredSteel", "ThicknessMm": 1.8,
                                  "Source": "1.3 kg over 8 dm2. Steel 38KhS3NMFA, factory code K-1. Rated for a 1 g fragment at 250 m/s, not for bullets" },
            "Rys_T":            { "Prototype": "Rys-T, NII Stali", "Material": "Titan", "ThicknessMm": 3.6,
                                  "Source": "2.5 kg without the visor over a published 13 dm2, GOST class 2. Alloy grade not published - VT-23 belongs to the K6-3, not to this" },
            "adept_neosteel":   { "Prototype": "Adept NovaSteel", "Material": "ArmoredSteel", "ThicknessMm": 1.7,
                                  "Source": "1293 g over a published >7.10 dm2. 'Carapace' is a non-martensitic UHSS that work-hardens, not a quenched armour plate. VPAM 3 - handgun and PDW, not the rifle class the game gives it" },
            "item_equipment_helmet_neosteel_mandible": { "Prototype": "Adept NovaSteel mandible", "Material": "ArmoredSteel", "ThicknessMm": 2,
                                  "Source": "same alloy; the NovaSteel Buckler measured 2.79-2.90 mm for flat-plate NIJ IIIA and is the upper bound. Rated 9 mm at >1400 fps" },
            "zsh_1_2m":         { "Prototype": "ZSh-1-2M", "Material": "Aluminium", "ThicknessMm": 3.8, "BackingMm": 2,
                                  "Source": "shell 2.2 kg over a published 13.6 dm2, less its aramid backer. Br2, V50 750 m/s - the game says class 4" },
            "lshz5_vulkan5":    { "Prototype": "Vulkan-5 (LShZ-5)", "Material": "Combined", "ThicknessMm": 6, "BackingMm": 10,
                                  "Source": "4.5 kg over 13 dm2 - a ceramic screen on a composite shell, and the heaviest helmet worn" },

            // --- visors, mandibles and appliques ---
            "item_equipment_glasses_6B34": { "Prototype": "6B34 Permyachka goggles", "Material": "Glass", "ThicknessMm": 6,
                                  "DensityGCm3": 1.20,
                                  "Source": "6 mm lens and 1.3 dm2 of coverage, both published by three retailers; V50 350 m/s against a 1 g, 6 mm ball. Every Russian source says only 'steklo' - polycarbonate is not confirmed" },
            "item_equipment_helmet_galvion_mandible": { "Prototype": "Batlskin Viper mandible", "Material": "UHMWPE", "ThicknessMm": 16,
                                  "DensityGCm3": 1.05,
                                  "Source": "0.42 kg in medium over the ~2.5 dm2 a mandible wraps. UHMWPE per Galvion - the wire version is the steel one. NIJ IIIA level, V50 671 m/s vs 1.1 g FSP" },
            "helmet_ops_core_fast_gunsight_mandible": { "Prototype": "Ops-Core FAST ballistic mandible", "Material": "Aramid", "ThicknessMm": 7,
                                  "DensityGCm3": 0.90,
                                  "Source": "567 g. Gentex split the functions explicitly: the carbon frame is blunt impact, the ballistic element is a FLEXIBLE aramid pack. 9 mm V0 at 1195 fps - below IIIA, and they quote no NIJ level" },

            // published thickness, and the areas turn out to differ by 40%
            "helmet_ops_core_handgun_face_shield": { "Prototype": "Ops-Core multi-hit handgun shield", "Material": "Glass", "ThicknessMm": 20.3,
                                  "DensityGCm3": 1.19,
                                  "Source": "PUBLISHED 0.8 in viewport over a published 4.51 dm2, 1315 g, acrylic and polycarbonate. NIJ 0108.01 IIIA. The mass closes on the thickness to the gram" },
            "item_equipment_helmet_team_wendy_exfil_face_shield": { "Prototype": "EXFIL face shield", "Material": "Glass", "ThicknessMm": 3,
                                  "DensityGCm3": 1.20,
                                  "Source": "263 g over a published 3.24 dm2. Rated BS EN 166 class 2B - a 0.86 g ball at 120 m/s. NOT ballistic, and the game's class 3 is fiction" },
            "item_equipment_helmet_team_wendy_exfil_face_shield_coyote": { "Prototype": "EXFIL face shield", "Material": "Glass", "ThicknessMm": 3,
                                  "DensityGCm3": 1.20, "Source": "identical item, only the colour differs" },

            // Russian Br1 visors. None of the makers publish a thickness, but the
            // construction is standardised and patented: patent RU209135U1 specifies
            // three polycarbonate panels of 5.5-6.1 mm bonded with 0.9-1.0 mm of
            // polyurethane. Every one of these certifies a class BELOW its own shell
            "helmet_zsh_1-2m_v1_face_shield": { "Prototype": "ZSh-1-2M visor", "Material": "Glass", "ThicknessMm": 19,
                                  "DensityGCm3": 1.20,
                                  "Source": "two bonded polycarbonate layers over a published 3.5 dm2; Br1 against the shell's Br2" },
            "item_equipment_helmet_rys_t_shield":   { "Prototype": "Rys-T visor", "Material": "Glass", "ThicknessMm": 19.2,
                                  "DensityGCm3": 1.20,
                                  "Source": "quartz armour glass in a titanium frame, ~1.2 kg. The Altyn block of the same family is published layer by layer: PC 1.2 + triplex 6 + PC 12. Br1" },
            "item_equipment_helmet_lshz2dtm_shield": { "Prototype": "LShZ-2DTM visor, Br2", "Material": "Glass", "ThicknessMm": 20,
                                  "DensityGCm3": 1.20,
                                  "Source": "1.8 kg over a published 4.3 dm2 - 1.5 transparent plus 2.8 composite. The lighter Br1 variant is 1.0 kg over 5.0 dm2" },
            "item_equipment_helmet_tor_2_faceshield": { "Prototype": "TOR-2 visor", "Material": "Glass", "ThicknessMm": 20,
                                  "DensityGCm3": 1.20,
                                  "Source": "1.15 kg over a published 3.7-3.9 dm2, all sizes. Br1 against the shell's Br2" },
            "item_equipment_helmet_kiverm_shield": { "Prototype": "Fort Kiver-M visor", "Material": "Glass", "ThicknessMm": 21,
                                  "DensityGCm3": 1.20,
                                  "Source": "polycarbonate, 1.4 kg; area not published, so read over the 4.0-4.7 dm2 the comparable Br1 visors run" },
            "item_equipment_helmet_vulkan_shield": { "Prototype": "Vulkan-5 visor", "Material": "Glass", "ThicknessMm": 22,
                                  "DensityGCm3": 1.20,
                                  "Source": "1.8 kg over a published 4.7 dm2 - the densest of the Russian visors. Br1 against the shell's Br4" },

            // the ceramic applique of the Bastion, which the game files as a shield
            "item_equipment_helmet_diamond_age_bastion_shield": { "Prototype": "Bastion ceramic applique", "Material": "Ceramic", "ThicknessMm": 7.91, "BackingMm": 4, "BackingMaterial": "UHMWPE",
                                  "DensityGCm3": 3.15,
                                  "Source": "MEASURED - 7.72/7.70/8.10/8.13 mm, 0.354 kg, slip-cast SILICON CARBIDE on carbon fibre. Chesapeake Testing; no penetration from M855A1 at 926 m/s, 7.5 mm backface" },

            // the game calls this aluminium; no Gentex FAST hard-armour accessory is
            "helmet_ops_core_fast_side_armor": { "Prototype": "Ops-Core FAST side armour", "Material": "UHMWPE", "ThicknessMm": 7.36,
                                  "DensityGCm3": 1.05,
                                  "Source": "no sheet exists for this SKU; the sibling FAST Low Profile Ballistic Applique publishes 0.290 +/- 0.030 in of carbon and unidirectional polyethylene" },

            // --- helmets with no ballistic rating at all ---
            // Sold against blunt and edged attack. The game gives them a class anyway
            "kolpak_1s_4ml":    { "Prototype": "Kolpak-1S (K-1S)", "Material": "Aramid", "ThicknessMm": 3,
                                  "DensityGCm3": 1.20,
                                  "Source": "1.2 kg over a published 16.5 dm2, BNTI 03.06.2012. Impact-resistant composite: knife to 50 J, blunt to 100 J. There is no ballistic rating" },
            "item_equipment_helmet_k1c_shield": { "Prototype": "Kolpak-1S visor", "Material": "Glass", "ThicknessMm": 4,
                                  "DensityGCm3": 1.20,
                                  "Source": "identified from our own en.json - 'K1C' is the Kolpak-1S face shield. Impact polycarbonate, no ballistic rating" },
            "djeta_psh97":      { "Prototype": "PSh-97 Djeta", "Material": "Aramid", "ThicknessMm": 3,
                                  "DensityGCm3": 1.20,
                                  "Source": "1.3 kg over 14 dm2. Impact plastic with a polycarbonate visor: 30 J edged, 80 J blunt. No ballistic rating" },
            "firefighter_shpm": { "Prototype": "ShPM fire helmet", "Material": "Aramid", "ThicknessMm": 3.5,
                                  "DensityGCm3": 1.20,
                                  "Source": "1.3 kg, injection-moulded Bayer POLYCARBONATE - not aramid. GOST R 53269-2009 certifies 200 C for 3 min and 400 V, and nothing ballistic at all" },

            // --- soft packages with published construction ---
            // The IOTV is the only soft-armour system in the game whose thickness AND
            // areal density are both published, and by the US Army rather than a shop.
            // Its 630 kg/m3 is where the sewn-package density in SoftArmor comes from
            "iotv_gen4_a":      { "Prototype": "IOTV Gen4 base panel", "Material": "Aramid", "ThicknessMm": 7.6,
                                  "DensityGCm3": 0.63,
                                  "Source": "PUBLISHED - 0.30 in at 0.98 lb/ft2, purchase description FQ/PD 07-05G tables V and VI. Front 22.8 dm2, back 23.9. NIJ IIIA-equivalent, no rifle protection. The fibre is deliberately unspecified: a contractor 'shoot pack' of X plies tested only on performance" },
            "iotv_gen4_f":      { "Prototype": "IOTV Gen4 base panel", "Material": "Aramid", "ThicknessMm": 7.6,
                                  "DensityGCm3": 0.63, "Source": "the same panel; the cut adds groin and deltoids, not thickness" },
            "iotv_gen4_m":      { "Prototype": "IOTV Gen4 base panel", "Material": "Aramid", "ThicknessMm": 7.6,
                                  "DensityGCm3": 0.63, "Source": "the same panel; the mid cut drops the deltoids" },
            "trooper":          { "Prototype": "HighCom Trooper SA3920", "Material": "Aramid", "ThicknessMm": 4.82,
                                  "DensityGCm3": 0.87,
                                  "Source": "PUBLISHED - 0.19 in at 0.86 psf. A unidirectional Dyneema and Twaron laminate, so denser than a sewn pack and lighter than a pressed shell. NIJ IIIA on its own" },
            "6b23-1":           { "Prototype": "6B23 fabric package", "Material": "Aramid", "ThicknessMm": 7,
                                  "DensityGCm3": 0.63,
                                  "Source": "30 layers of TSVM-2, class II alone - TT and PMM at 5 m, fragment 1 g at 600 m/s. Chest 8 dm2 and back 8. Thickness not published; read at the IOTV's density" },
            "6b23-2":           { "Prototype": "6B23 fabric package", "Material": "Aramid", "ThicknessMm": 7,
                                  "DensityGCm3": 0.63, "Source": "the same 30-layer TSVM-2 package" },
            "interceptor":      { "Prototype": "Interceptor OTV", "Material": "Aramid", "ThicknessMm": 7.6,
                                  "DensityGCm3": 0.63,
                                  "Source": "the vest IS the package - Kevlar KM2 or Twaron, 7.7 lb per the PEO Soldier fact sheet (ciehub says 8.4, the two official sources disagree). Stops 9 mm 124 gr at 426 m/s, V50 465" }
          },

          // ===== Reference plate per material and class =====
          // Most of the armour in the game is invented for it, so there is no product to
          // look up. What an invented plate stands in for is a REAL one of the same
          // material and class, and wherever this book documents such a product the rung
          // simply names it ("SameAs"): the thickness is not a number the model solved
          // out of its own class table, it is a plate somebody built and certified. Read
          // through ResolveByClass, never directly.
          //
          // Rungs with no product to borrow from — nobody sells a Br5 steel monolith or
          // a Br3 alumina plate — carry their own figures as a LAST RESORT: solved from
          // the class's own test cartridges under the strict zero-of-five criterion
          // (CertificationCriteria) at the constants of the coherence recalibration, and
          // checked for areal density so an absurd answer cannot hide behind arithmetic
          // (20 mm of steel would weigh what a whole vest does). Each says so.
          "ArmorByClass": {
            "ArmoredSteel/2": { "Prototype": "steel insert, Br1",      "ThicknessMm": 1.9,
                                "Source": "computed, last resort: solved from Br1's own 9x18 Pst under zero-of-five (1.82, rounded up); 1.5 g/cm2. Was 1.3 while the hardness clamp stood at 4.5 - both of these rungs are solved AT the clamp, which is why they could never be evidence for it" },
            "ArmoredSteel/3": { "Prototype": "steel insert, Br2",      "ThicknessMm": 2.5,
                                "Source": "computed, last resort: solved from Br2's 9x21 P - a lead core, the softest bullet in the standard - under zero-of-five (2.48, rounded up); 2.0 g/cm2. Was 1.7 at the old clamp" },
            "ArmoredSteel/4": { "SameAs": "kora_kulon",
                                "Source": "the Kora-Kulon panel: 4.3 mm of steel over a fabric package, certified Br3" },
            "ArmoredSteel/5": { "SameAs": "korund",
                                "Source": "the Korund-VM panel: 6.3 mm of 44S, certified Br4. The 0.25-in AR500 Level III plate lands within half a millimetre of it" },
            "ArmoredSteel/6": { "Prototype": "armour steel, Br5",      "ThicknessMm": 8.8,
                                "Source": "computed, last resort: no maker sells a Br5 steel monolith. Solved from Br5's 7N13 and B-32 under zero-of-five; 6.9 g/cm2, the weight of the heaviest real steel panels" },

            "Ceramic/4":      { "Prototype": "alumina, Br3",           "ThicknessMm": 2.9,
                                "Source": "computed, last resort: nobody makes a Br3 ceramic plate. Solved from Br3's 9x19 7N21 under zero-of-five; 1.1 g/cm2" },
            "Ceramic/5":      { "SameAs": "granitBr4",
                                "Source": "the Granit Br4: 6 mm of alumina on a 12 mm package, certified Br4" },
            "Ceramic/6":      { "SameAs": "granitBr5",
                                "Source": "the Granit Br5: 6.8 mm of alumina on a 14 mm package, certified Br5" },

            // no combined-construction product is documented in this book at any class,
            // so the whole column is computed. Note Br2 over Br3: a 7.93 g lead 9x21
            // is genuinely harder for a ceramic face than the 5.2 g steel-cored 9x19,
            // and since the hardness term left the brittle mode (3.2) the model cannot
            // say otherwise; /4 is set a hair over /3 to keep the rating ladder
            // monotone rather than pretend the inversion is not there
            "Combined/3":     { "Prototype": "ceramic face, Br2",      "ThicknessMm": 3.0,
                                "Source": "computed, last resort; 1.0 g/cm2" },
            "Combined/4":     { "Prototype": "ceramic face, Br3",      "ThicknessMm": 3.1,
                                "Source": "computed, last resort; solved 2.9, held at 3.1 for ladder monotonicity - see the column note" },
            "Combined/5":     { "Prototype": "ceramic face, Br4",      "ThicknessMm": 6.9,
                                "Source": "computed, last resort; 2.2 g/cm2" },
            "Combined/6":     { "Prototype": "ceramic face, Br5",      "ThicknessMm": 8.8,
                                "Source": "computed, last resort; 2.8 g/cm2" },

            "Titan/4":        { "SameAs": "6b3TM",
                                "Source": "the 6B3TM-01 front: 6.5 mm of VT-23 titanium over a 30-layer package - the game itself rates it class 4; ARMOR-TABLE holds its layout, not a Br class, so this is a construction borrowed, not a certificate claimed" },
            "Titan/5":        { "Prototype": "titanium, Br4",          "ThicknessMm": 11.5,
                                "Source": "computed, last resort: the one titanium-faced rifle plate in the book (Adept Mantis) is a thin face on a thick PE backer, not a titanium plate. Solved under zero-of-five with the rung's recorded shortfall; 5.1 g/cm2. Re-solved from 11.2 when the 7.62x39 PS became the hardened core it has been since 1989: that cartridge now binds the rung instead of the 7N10, and demands 11.44 mm where the 5.45 asks 11.17. Nothing in the game resolves to this rung - the one class-5 titanium item is the Adept Mantis and it has its own entry - so this number moves no armour; it exists so that an item nobody has yet added is not silently given a thickness solved against a cartridge that no longer exists" },
            "Titan/6":        { "Prototype": "titanium, Br5",          "ThicknessMm": 14.4,
                                "Source": "computed, last resort: nobody makes one. Solved under zero-of-five; 6.3 g/cm2" },

            // polyethylene stops by encapsulating the round, so it needs far more of
            // itself than a hard face does
            "UHMWPE/3":       { "Prototype": "UHMWPE hard insert, IIIA / Br2", "ThicknessMm": 6.5,
                                "Source": "read at the real IIIA-class polyethylene inserts, 5.5-6.5 mm. NOT the solve: the model would settle for 3.4 mm here, lighter than a class-2 helmet shell, which no real product is - the fibre mode has no ladder behind it (3.1) and its thin-plate solves are not to be trusted over a real product's thickness. The old 20 mm was a monolith guess with no cartridge behind it" },
            "UHMWPE/4":       { "SameAs": "granit4_zhukBr3_3class_front",
                                "Source": "the Zhuk-3 panel: 23 mm of pressed polyethylene, certified Br3" },
            "UHMWPE/5":       { "Prototype": "UHMWPE monolith, NIJ III / Br4", "ThicknessMm": 33.0,
                                "Source": "standalone Level III polyethylene plate, 1.3 in - a real product's thickness even though no book entry carries it; reads 8% under the strict Br4 bar, recorded in CertShortfalls under this rung's key" },
            "UHMWPE/6":       { "Prototype": "UHMWPE monolith, Br5",   "ThicknessMm": 42.2,
                                "Source": "computed, last resort: solved under zero-of-five; 4.1 g/cm2" },

            "Aluminium/4":    { "Prototype": "aluminium armour",      "ThicknessMm": 11.3,
                                "Source": "computed, last resort: solved from Br3's 9x19 7N21 under zero-of-five; 3.1 g/cm2. The old 20 mm was a vehicle-armour figure, a class too strong" }
          },

          // ===== Searched, nothing published =====
          // A headstone per product, so the same dead end is not walked twice. Nothing
          // here changes a figure — the item still takes its reference construction —
          // but the report says which entries have been looked into and which are still
          // waiting, and that is the whole difference between a to-do list and a wall.
          //
          // Two reasons an entry lands here. Either the thing does not exist outside the
          // game, in which case there is nothing to find and never will be; or it exists
          // and the maker publishes a rating and a price and no construction at all.
          // Only the second is worth revisiting.
          "NoRealSpecs": {
            // real, and the maker genuinely publishes no construction
            "item_equipment_helmet_galvion_fixed_arm_visor": "Galvion certify it to ANSI Z87.1 and MIL-PRF-32432 - EYE protection standards, not a ballistic rating - and publish no mass",
            "helmet_ops_core_fast_visor": "tested to Gentex PRS-1011, V50 221 m/s against a 17-grain FSP; no mass, and the thickness varies across the lens",
            "granit4_4class_diy_back": "a cut-down panel, not a product",
            "granit4_6b33_4class_front": "real - ZAO Kirasa, used in the Zhuk, Gzhel, 6B13 and 6B23-2 - but only a vest mass and a class are published, never the plate",
            "shlemofon_tsh_4ml":      "a tanker's helmet with NO ballistic element at all - the real armour is the separate 6B15-2 aramid overlay, <0.8 kg over >8 dm2, 1 g fragment at 460 m/s",

            // eyewear and masks
            "item_equipment_glasses_npp": "a polycarbonate shooting lens; no thickness published",
            "Item_equipment_glasses_oakley": "Oakley certify to MIL-PRF-31013 and publish no lens thickness",
            "tactical_glass":         "a polycarbonate shooting lens; no thickness published",
            "item_equipment_facecover_ballistic_mask": "invented for the game",
            "item_equipment_facecover_ballistic_mask_arena_bp_01": "a reskin of the ballistic mask",
            "item_equipment_facecover_ballistic_mask_arena_bp_02": "a reskin of the ballistic mask",
            "item_equipment_facecover_ballistic_mask_arena_bp_03": "a reskin of the ballistic mask",
            "item_equipment_facecover_ballistic_mask_arena_bp_04": "a reskin of the ballistic mask",
            "item_equipment_facecover_ballistic_mask_arena_bp_05": "a reskin of the ballistic mask",
            "item_equipment_facecover_ballistic_mask_arena_bp_06": "a reskin of the ballistic mask",
            "item_equipment_facecover_ballistic_mask_arena_bp_07": "a reskin of the ballistic mask",
            "item_equipment_facecover_strikeball_mask": "an airsoft mask; no ballistic rating to work from",
            "item_equipment_facecover_strikeball_mask_leshiy": "an airsoft mask; no ballistic rating to work from",

            // invented for the game: no prototype exists to look up. Checked, not
            // assumed - the EFT wiki names the real basis where one exists and is silent
            // where none does, so its silence is itself the evidence
            "helmet_all_exeptNeck":   "a development item, not a product",
            "item_equipment_helmet_tk_heavy_trooper": "a costume after the GALAC-TAC Heavy Gunner, itself airsoft",
            "drd":                    "a Dr. Disrespect collaboration; soft-only, no plate slots at all",
            "tac_kek_fast_mt":        "an airsoft replica of the FAST, with no ballistic shell",
            "balaclava":              "a balaclava is fabric; the game rates it armour",
            "test_balaclava":         "a development item, not a product",
            "balaclava_development":  "a development item, not a product",
            "jack_o_lantern":         "a costume",
            "item_equipment_facecover_glorious":   "invented for the game",
            "item_equipment_facecover_shatteredmask": "invented for the game",
            "item_equipment_facecover_mask_boss_blackknight": "invented for the game",
            "item_equipment_facecover_welding_gorilla": "a welding mask; no ballistic rating to work from",
            "item_equipment_facecover_welding_kill":   "a welding mask; no ballistic rating to work from",
            "item_equipment_facecover_welding_minotaur": "a welding mask; no ballistic rating to work from",
            "item_equipment_head_bomber": "a hat; the game rates it armour",
            "SAPI_Cult_Termite":      "invented; its sibling Cult Locust has a wiki trivia note naming the Adept Mantis and this one has none",
            "SAPI_GlobalArmors_Steel": "invented; no wiki trivia note where comparable plates in the same table have one",
            "SAPI_KITECO_SCIVSA":     "invented; no body-armour maker called Kiteco exists",
            "SAPI_KibaArms_Steel":    "invented twice over - Kiba Arms is a fictional gun store INSIDE Tarkov, in the Ultra Mall on Interchange",
            "SAPI_KibaArms_Titan":    "the same fictional store",
            "tac_kek_fast_mt":        "the item's own description calls it 'a lower protection class replica' - an airsoft FAST shell with no ballistic core"
          },

          // ===== Soft armour =====
          // The package sewn into a carrier: layers of fabric held together by the
          // stitching and nothing else. Separate from the plate table because a pack of
          // a given rating is nothing like a monolithic plate of it, and separate from
          // the shell table because pressed laminate is not fabric.
          //
          // A woven package has a ceiling no rating can lift, so anything the game rates
          // above 2 is read as 2 — the fabric is the same fabric. Reaching Br3 with
          // aramid alone would take about 200 mm of it, which is why carriers are sold
          // as Br1 or Br2 and the rifle protection lives in the plates.
          //
          // DENSITY. A sewn pack is loose plies with air between them, and reading it at
          // the fibre's 1.44 makes it weigh twice what it does. The US Army's IOTV
          // purchase description publishes both numbers for the same panel — 7.6 mm at
          // 4.79 kg/m2 — which is 630 kg/m3. Two independent anchors agree: a Russian
          // 35-layer package patent at 120-160 g/m2 a layer comes to 4.9 kg/m2, and
          // owner measurements of 14-24 layer class-1 packs give 3.12-3.57.
          "SoftArmor": {
            "Aramid/1":       { "Prototype": "18-layer aramid package", "ThicknessMm": 5.5,
                                "DensityGCm3": 0.63,
                                "Source": "3.5 kg/m2 - the middle of the measured 14-24 layer band" },
            "Aramid/2":       { "Prototype": "IOTV-weight aramid package", "ThicknessMm": 7.6,
                                "DensityGCm3": 0.63,
                                "Source": "IOTV base panel, 7.6 mm at 4.79 kg/m2, purchase description FQ/PD 07-05G" },

            // polyethylene fibre is lighter than aramid in the same weave, in the ratio
            // of the two fibre densities
            "UHMWPE/1":       { "Prototype": "light UHMWPE package",   "ThicknessMm": 5.0,
                                "DensityGCm3": 0.45 },
            "UHMWPE/2":       { "Prototype": "UHMWPE package",         "ThicknessMm": 7.0,
                                "DensityGCm3": 0.45 }
          },

          // ===== Helmet shells, visors and rigid masks =====
          // Aramid in a helmet is not the aramid of a vest package. The fabric is
          // prepreg — impregnated with 16-18% PVB-phenolic resin — and pressed under
          // heat into one rigid laminate, so it fails as a solid rather than as a stack
          // of layers and it is thicker than anything sewn.
          //
          // The ladder is anchored to real helmets. Each rung carries the laminate's own
          // density, because a pressed shell is denser than a sewn pack and lighter than
          // solid fibre: aramid prepreg comes out at 1350 kg/m3 (the ACH patent publishes
          // 8.13 mm at 10.94 kg/m2, and the PASGT agrees), polyethylene at 1050 (Galvion
          // publish 6.00 mm at 6.35 kg/m2 for the Caiman TL). Density times thickness
          // then reproduces the published areal density, which is what the penetration
          // model actually spends.
          //
          // Fibre still has a ceiling. Above Br2 a pressed shell stops getting thicker
          // and starts getting a metal or ceramic element instead, so aramid and
          // polyethylene are read at 3 at most — the thickest fielded shell of each is
          // the last rung. Anything the game rates above that is a shell plus something
          // else, and the something else belongs in ArmorPlates by name.
          "HelmetShells": {
            "Aramid/1":       { "Prototype": "light aramid shell",     "ThicknessMm": 5.6,
                                "DensityGCm3": 1.35,
                                "Source": "the 6B47 at 7.5 kg/m2 - a Br1 shell, the lightest fielded" },
            "Aramid/2":       { "Prototype": "aramid shell",           "ThicknessMm": 8.3,
                                "DensityGCm3": 1.35,
                                "Source": "the PASGT at 11.2 kg/m2; the ACH is 8.13 mm published" },
            "Aramid/3":       { "Prototype": "heavy aramid shell",     "ThicknessMm": 8.6,
                                "DensityGCm3": 1.35,
                                "Source": "the MSA Gallet TC 801 at 11.23 kg/m2 - pure aramid tops out here" },

            "UHMWPE/1":       { "Prototype": "light UHMWPE shell",     "ThicknessMm": 4.6,
                                "DensityGCm3": 1.05,
                                "Source": "the MTEK FLUX, 0.5 kg over its published 164 in2" },
            "UHMWPE/2":       { "Prototype": "UHMWPE shell",           "ThicknessMm": 6.0,
                                "DensityGCm3": 1.05,
                                "Source": "the Galvion Caiman TL, 6.00 mm at 6.35 kg/m2, both published" },
            "UHMWPE/3":       { "Prototype": "heavy UHMWPE shell",     "ThicknessMm": 7.3,
                                "DensityGCm3": 1.05,
                                "Source": "the Team Wendy EXFIL, 0.79 kg over 10.25 dm2" },

            // a visor is polycarbonate, and stops where polycarbonate stops
            "Glass/1":        { "Prototype": "shooting glasses",       "ThicknessMm": 5.0,
                                "DensityGCm3": 1.20,
                                "Source": "the 6B34 is 6 mm; a fragmentation visor is 6.4" },
            "Glass/2":        { "Prototype": "ballistic visor",        "ThicknessMm": 19.0,
                                "DensityGCm3": 1.20,
                                "Source": "patent RU209135U1, a certified Br1 visor: three PC panels of 5.5-6.1 mm bonded with 0.9-1.0 mm polyurethane" },

            // metal and ceramic shells are not capped: one really is thicker on a
            // heavier helmet. But they do not run away either — every rung above the
            // anchor is heavier than any helmet anyone has fielded
            "ArmoredSteel/2": { "Prototype": "steel helmet shell",     "ThicknessMm": 3.0,
                                "Source": "the Maska-1Sch, 4.3 kg over 13 dm2" },
            "ArmoredSteel/3": { "Prototype": "steel helmet shell",     "ThicknessMm": 3.5 },
            "ArmoredSteel/4": { "Prototype": "heavy steel shell",      "ThicknessMm": 4.0 },
            "ArmoredSteel/5": { "Prototype": "heavy steel shell",      "ThicknessMm": 4.5 },
            "ArmoredSteel/6": { "Prototype": "thickest steel shell",   "ThicknessMm": 5.0 },

            "Titan/2":        { "Prototype": "titanium shell",         "ThicknessMm": 3.0,
                                "Source": "the Altyn, 3 mm; the Rys-T works out at 3.6" },
            "Titan/3":        { "Prototype": "titanium shell",         "ThicknessMm": 3.6 },
            "Titan/4":        { "Prototype": "heavy titanium shell",   "ThicknessMm": 4.0,
                                "Source": "the Altyn-R2 went to 4 mm and nothing has gone past it" },
            "Titan/5":        { "Prototype": "heavy titanium shell",   "ThicknessMm": 4.0 },
            "Titan/6":        { "Prototype": "thickest titanium shell", "ThicknessMm": 4.0 },

            "Combined/3":     { "Prototype": "composite shell",        "ThicknessMm": 4.0 },
            "Combined/4":     { "Prototype": "composite shell",        "ThicknessMm": 5.0 },
            "Combined/5":     { "Prototype": "heavy composite shell",  "ThicknessMm": 6.0,
                                "Source": "the Vulkan-5, 4.5 kg over 13 dm2 - the heaviest worn" },
            "Combined/6":     { "Prototype": "thickest composite shell", "ThicknessMm": 6.5 },

            "Ceramic/4":      { "Prototype": "ceramic shell",          "ThicknessMm": 5.0 },
            "Ceramic/5":      { "Prototype": "ceramic shell",          "ThicknessMm": 6.0 },
            "Ceramic/6":      { "Prototype": "thickest ceramic shell", "ThicknessMm": 7.0 },

            // aluminium armour in a helmet does exist, but only just: the ZSh-1-2M is an
            // aluminium-alloy shell with an aramid backer, and it works out at 3.5-4 mm
            "Aluminium/3":    { "Prototype": "aluminium shell",        "ThicknessMm": 3.8,
                                "Source": "the ZSh-1-2M, 2.2 kg over 13.6 dm2 less its aramid backer" },
            "Aluminium/4":    { "Prototype": "heavy aluminium shell",  "ThicknessMm": 4.5 }
          },

          // Blast anchor: Strength_i = Strength_anchor * (TntG_i / TntG_anchor)^(1/3)
          "BlastAnchor": { "Name": "RGD-5", "Strength": 100, "TntG": 110 },

          // Raise this when a figure above is CORRECTED. Adding entries needs no bump —
          // they merge in on their own — but a correction has to be able to overwrite,
          // and on a bump this file is rewritten with the old one kept as a .bak
          //
          // 2: armour read at the density of its form rather than of its fibre, and over
          //    rated areas rather than outer rectangles. Moved most of the armour
          // 3: the class-reference ladder became a bracket. With a ballistic limit,
          //    "class C stops C and the rung below does not" closes on a thickness from
          //    both ends, and most rungs were outside theirs. Materials gained a hardness
          // 4: the ceramic brackets were fitted to a ladder invented for the purpose, and
          //    then said every real ceramic plate in the book was a third too thin for
          //    its own class. Refitted onto the plates instead
          // 5: a bullet with no penetrator drives the limit with its whole mass at its
          //    own calibre, not with the fraction of it that survives the plate. The
          //    ceramic brackets moved with it
          // 6: the SLAAP entries gained "Plate", which lifts the shell ceiling off them.
          //    A new field on an existing entry is a correction — the per-entry merge
          //    would never reach it, and the applique would be re-rated 5 -> 3
          // 7: the two computed steel pistol rungs re-solved at the re-derived hardness
          //    clamp (1.3 -> 1.8 and 1.7 -> 2.4 mm). They are solved AT the clamp, so a
          //    clamp that moves moves them
          // 8: the 5.45 PS is the modernised cartridge, not the 1974 original. One index
          //    covers a mild core and a hardened one, and the round in service is the
          //    hardened one: 410 HV over the whole bullet becomes 697 HV on the core
          // 9: the MAI AP is a sabot round carrying a tungsten carbide penetrator, per
          //    the only description of it that exists. It was read as lead, and as the
          //    whole projectile at a velocity only the penetrator reaches
          // 10: the 7.62x39 PS is the modernised cartridge too, on the same evidence as
          //    the 5.45 in version 8. The MAI penetrator gained a width, and with it a
          //    mass its own volume allows rather than one read off the calibre's energy
          // 11: Russian steel panels carry their own alloy (44S at 2050 MPa yield)
          //    instead of the AR500 datasheet one game material had to serve for
          // 12: the Br4 titanium rung re-solved from 11.2 to 11.5 mm — the corrected
          //    7.62x39 PS binds it now, where the 7N10 used to
          // 13: the Soviet vests' package is 7.6 mm, borrowed from a certified IIIA
          //    package that publishes a thickness, instead of an unsourced flat 8
          "Version": 13
        }
        """;
}
