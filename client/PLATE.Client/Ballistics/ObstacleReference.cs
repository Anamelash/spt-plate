using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;

namespace PLATE.Client.Ballistics
{
    /// <summary>
    /// The book that turns a collider into a barrier: what each of the game's material
    /// types is made of, how thick it is at a given PenetrationLevel, and what it does
    /// to a bullet that arrives at a shallow angle.
    ///
    /// It ships inside the assembly and is written out next to the plugin on first
    /// start, so a player can retune the whole obstacle model without touching code —
    /// which is also the only place the model's constants exist. There is no second copy
    /// in the BepInEx config: a number that lived in two files would drift, and the
    /// server's ammunition reference already taught that lesson the expensive way.
    ///
    /// The version lives ONLY in the text of the book (the "Version" line at the end of
    /// <see cref="DefaultJsonc"/>). The server's reference book carried it twice once,
    /// the two drifted, and every start rewrote the file the previous start had written
    /// — taking the player's edits with it.
    /// </summary>
    internal static class ObstacleReference
    {
        public const string FileName = "obstacle-reference.jsonc";

        // --- Schema ---

        internal class Book
        {
            public GlobalsRef Globals { get; set; }
            public SteelRef Steel { get; set; }
            public Dictionary<string, RicochetRef> Ricochet { get; set; }

            /// <summary>
            /// The word a level designer wrote after `_BALLISTIC_` → the material of this
            /// book it names. See <see cref="SuffixWord"/>: this is the second resolution
            /// layer, and it exists because the MaterialType and the designer's own word
            /// disagree on thousands of colliders.
            /// </summary>
            public Dictionary<string, string> SuffixAliases { get; set; }

            /// <summary>
            /// Presets the suffix layer may never overrule. These are substances rather
            /// than skins: on a concrete wall tagged `_tile` the tile is the facing and
            /// the concrete is what the bullet has to cross, and the same is true of
            /// stone, soil, water and a body. Anywhere else the designer's word wins,
            /// because anywhere else the preset is itself a guess about the object.
            /// </summary>
            public List<string> SuffixFinal { get; set; }

            /// <summary>Scene taxonomy — the third layer. See <see cref="TaxonomyRef"/>.</summary>
            public TaxonomyRef Taxonomy { get; set; }

            public Dictionary<string, MaterialRef> Materials { get; set; }
            public int Version { get; set; }
        }

        /// <summary>
        /// What the scene graph says about a collider, as opposed to what its material
        /// says. BSG group their props under naming nodes — `VEHICLES`, `DOORS` — and
        /// that grouping is a statement about the object no MaterialType can carry: the
        /// same 1 mm `MetalThin` sheet is a road sign in one place and a car's flank in
        /// another, and only the parent knows which.
        /// </summary>
        internal class TaxonomyRef
        {
            /// <summary>
            /// Names of the grouping nodes that mean "everything under here is a
            /// vehicle". Matched as WHOLE ancestor names, case-insensitively, never as
            /// substrings: a substring test on "vehicle" would swallow `vechicle_BMP2`
            /// and half the props parked next to a car park.
            /// </summary>
            public List<string> VehicleNodes { get; set; }

            /// <summary>
            /// Vehicle model words, matched as substrings of the collider's own name or
            /// any of its ancestors'. The node alone is not enough: the census found
            /// 5 101 colliders with vehicle names living OUTSIDE any `VEHICLES` node —
            /// the same Chevrolet Cruze sits under the node on one map and under `OFF`
            /// on another — and a taxonomy that only read nodes would price one car at
            /// 3 mm and its twin at 1.
            /// </summary>
            public List<string> VehicleFamilies { get; set; }

            /// <summary>Preset → what that preset is when it is part of a vehicle.</summary>
            public Dictionary<string, string> VehicleMap { get; set; }

            /// <summary>
            /// Names of the grouping nodes that mean "this is a door leaf". Whole-name
            /// match, like <see cref="VehicleNodes"/>. The effect is not a material:
            /// the node only says "leaf", and the MATERIAL says what a leaf of it is —
            /// <see cref="MaterialRef.DoorLeaf"/>.
            /// </summary>
            public List<string> DoorNodes { get; set; }

            /// <summary>
            /// Substrings that mean "this is a door leaf" wherever they appear in the
            /// collider's own name or its ancestors', matched like
            /// <see cref="VehicleFamilies"/>. The node above is BSG's own grouping and is
            /// the better evidence where it exists, but it does not always: Factory's
            /// entrance gate hangs off `Enterance_Gate_01` with no `DOORS` node in the
            /// chain at all, and on the maps that do have one the gate's wicket door sits
            /// four levels below it, out of the ancestor walk's reach. Both then read as
            /// plain plate.
            /// </summary>
            public List<string> DoorNames { get; set; }

            /// <summary>
            /// How many of the book's walls the ENTRY face of a HOLLOW door leaf
            /// (<see cref="MaterialRef.DoorLeaf"/> = "skins") charges. Such a leaf is
            /// two sheets over a frame, and its collider (46 mm on the metal doors the
            /// survey measured) is far under <see cref="GlobalsRef.ShellCavityMm"/>, so
            /// the shell rule alone charges it once and a bullet crosses one sheet where
            /// it should cross two. Geometry cannot tell a two-skin leaf from a single
            /// profiled sheet in a deep collider; the scene's own word "door" can —
            /// but only for a material that cannot carry itself as a slab.
            /// </summary>
            public double DoorWalls { get; set; } = 2;
        }

        internal class GlobalsRef
        {
            public double AngleMinCos { get; set; } = 0.20;
            public double ConfinementFactor { get; set; } = 5.0;
            public double DragCoefficient { get; set; } = 1.0;
            public double ExpansionDepthFactor { get; set; } = 0.4;
            public double RicochetBand { get; set; } = 0.25;
            public double RicochetVelocityRef { get; set; } = 400;
            public double RicochetVelocityExp { get; set; } = 0.35;
            public double RicochetLoss { get; set; } = 0.5;
            public double RicochetFlatten { get; set; } = 0.5;
            public double DeviationK { get; set; } = 0.2;
            public double DeviationDeformMult { get; set; } = 2.0;
            public double CoreBluntK { get; set; } = 0.6;
            public double CoreErosionK { get; set; } = 0.15;
            public double JacketStripWork { get; set; } = 0.15;
            public double ShellCavityMm { get; set; } = 150;
            public double SteelLimitScatter { get; set; } = 0.08;
            public double YawGainK { get; set; } = 1.1;
            public double YawObliquityK { get; set; } = 1.0;
        }

        internal class SteelRef
        {
            public double YieldMPa { get; set; } = 250;
            public double ShearMPa { get; set; } = 270;
            public double HardnessHv { get; set; } = 158;
            public double DensityGCm3 { get; set; } = 7.85;
            public string FailureMode { get; set; } = "HoleExpansion";
        }

        internal class RicochetRef
        {
            /// <summary>Critical grazing angle at the reference velocity, degrees.
            /// 0 = this surface never throws a projectile off.</summary>
            public double AlphaCritDeg { get; set; }

            /// <summary>Fraction of the speed kept by a grazing bounce.</summary>
            public double Retention { get; set; }
        }

        internal class MaterialRef
        {
            /// <summary>One of ObstacleModel's Mech* names.</summary>
            public string Mechanism { get; set; } = ObstacleModel.MechVanilla;

            /// <summary>Key into the Ricochet table; "Vanilla" leaves the bounce to the
            /// game, "None" means this surface never bounces anything.</summary>
            public string Ricochet { get; set; }

            /// <summary>Poncelet: crushing strength, MPa.</summary>
            public double StrengthMPa { get; set; }

            /// <summary>
            /// Bulk density, g/cm³. The depth law needs it, and so do the deflection
            /// (areal density) and the core's fate (stagnation pressure) — which is why
            /// an "always" material carries one even though nothing stops it.
            /// </summary>
            public double DensityGCm3 { get; set; }

            /// <summary>
            /// Vickers hardness, for the one question that reads it: can this barrier
            /// kill the core. Left out where the answer is obviously no.
            /// </summary>
            public double HardnessHv { get; set; }

            /// <summary>
            /// Is a thing made of this material solid through, or a shell around air.
            /// Decides whether the measured collider is read as the path or as the
            /// object's outline — see ObstacleModel.Barrier.Solid. Default false, so a
            /// material nobody has classified keeps the book's thickness rather than
            /// suddenly becoming a metre of steel.
            /// </summary>
            public bool Solid { get; set; }

            /// <summary>
            /// What a door LEAF of this material is, read only under a DOORS node.
            /// "skins": the material cannot carry itself as a slab, so a leaf is two
            /// sheets over a frame and the entry face charges
            /// <see cref="TaxonomyRef.DoorWalls"/> of them (thin steel, plastic).
            /// Absent: a leaf is one plate of the book's thickness — nobody laminates
            /// a door out of material that already carries itself (a safe door is one
            /// 10 mm slab, not two).
            /// </summary>
            public string DoorLeaf { get; set; }

            /// <summary>
            /// The fixed thickness of a door LEAF of this material, mm, read only
            /// under a DOORS node. A wooden door is ~50 mm of wood whatever its
            /// collider measures — leaf colliders run 100-200 mm deep, and reading
            /// that chord as timber made every wooden door a safe. Zero means the
            /// leaf has no fixed thickness and the other rules apply.
            /// </summary>
            public double DoorLeafMm { get; set; }

            /// <summary>Always: flat energy price of the hole, J.</summary>
            public double CostJ { get; set; }

            /// <summary>
            /// Brittle media: how much more is perforated than is penetrated, as a ratio
            /// of thicknesses. Absent — read as 1 — means the medium resists over its
            /// whole thickness. See <see cref="ObstacleModel.Barrier.SpallFactor"/>.
            /// </summary>
            public double SpallFactor { get; set; } = 1.0;

            /// <summary>PenetrationLevel → thickness in mm (steel and poncelet).</summary>
            public Dictionary<string, double> Anchors { get; set; }

            /// <summary>
            /// This material is a carrier with things packed in it, met one package at a
            /// time — see <see cref="StackRef"/> and
            /// <see cref="ObstacleModel.StackFill"/>. Absent, which is every material but
            /// palletised cargo, means the medium is homogeneous and behaves exactly as
            /// it always did.
            /// </summary>
            public StackRef Stack { get; set; }

            /// <summary>
            /// Substring of the scene object's name → the material to use instead.
            /// Case-insensitive, first match wins, applied once (a substitute's own
            /// overrides are not followed). When the collider's own name fires no
            /// rule, its ancestors are tried, nearest first, three levels up — half
            /// the scene names its colliders "Metal" and hangs the prop's identity a
            /// transform or two above (see EffectiveMaterial).
            ///
            /// FIRST MATCH means the order rules are written in the book is
            /// load-bearing: Newtonsoft fills the dictionary in document order and a
            /// Dictionary that only ever grows enumerates in insertion order. Two
            /// orderings are deliberate and pinned by tests — 'gunsafe' before
            /// 'container' (a safe's name contains both), debris identity rules
            /// before 'glass_block'.
            ///
            /// This is the game telling us something its MaterialType cannot. One preset
            /// can cover two materials — `Concrete` is on brick walls as well as concrete
            /// ones — and where the level author left the difference in the object's name
            /// (`..._bricks_01_BALLISTIC_concrete`) that name is the only evidence there
            /// is. It is weaker evidence than a material: a map or a mod that names
            /// things differently gets the fallback, which is the preset itself, so the
            /// worst case is what the book did before. The substitutes must be entries in
            /// this same book, so the numbers stay in one place.
            /// </summary>
            public Dictionary<string, string> NameOverrides { get; set; }
        }

        /// <summary>
        /// What is packed inside a carrier medium, and how often the projectile runs
        /// into it. The book's half of <see cref="ObstacleModel.StackFill"/>.
        ///
        /// <see cref="Content"/> must name a material this same book defines — the
        /// numbers stay in one place, exactly as they do for a name override — and that
        /// material must not itself be packed: cargo does not contain cargo.
        /// </summary>
        internal class StackRef
        {
            /// <summary>How much path there is per draw, mm. Zero switches the whole
            /// mechanism off and leaves a plain homogeneous medium.</summary>
            public double SpacingMm { get; set; }

            /// <summary>The material of a package.</summary>
            public string Content { get; set; }

            /// <summary>How much of a layer a package occupies when one is drawn. Tied
            /// to the spacing on purpose, so the expected cargo per metre of path does
            /// not depend on how finely the stack is sliced.</summary>
            public double ContentFraction { get; set; }

            /// <summary>Odds that a layer holds a package at all, 0..1.</summary>
            public double Chance { get; set; }
        }

        /// <summary>The ricochet class name that leaves the decision to the game.</summary>
        public const string RicochetVanilla = "Vanilla";

        /// <summary>The ricochet class name for a surface nothing bounces off.</summary>
        public const string RicochetNone = "None";

        /// <summary>
        /// The top of BSG's PenetrationLevel scale, which the module reads as a THICKNESS
        /// and nothing more.
        ///
        /// It used to be read as the designer saying "this is a wall", and that override
        /// beat whatever the material claimed. A raid census killed that reading: of the
        /// colliders carrying 100, some are concrete walls and floors, and the rest are
        /// an IBC tote's plastic cage, a polythene box, a water cistern, a boiler, a
        /// reactor housing, a run of pipes, thin metal on a pillar and a patch of gravel
        /// floor. It is a blanket "not meant to be shot through", applied by hand, and
        /// treating it as geometry made a plastic tote bulletproof. Since the thickness
        /// is now measured off the collider, the level has nothing left to say that the
        /// scene does not say better.
        ///
        /// Kept as documentation, deliberately unreferenced: a constant naming a rule
        /// the module dropped is a trap for whoever reads it next, and this comment is
        /// the only thing standing between that reader and reinstating it.
        /// </summary>
        public const float MaxPenetrationLevel = 100f;

        // --- Loading ---

        private static Book _cached;
        private static bool _failed;

        /// <summary>The version the mod ships, read out of the book's own text.</summary>
        public static int ShippedVersion => Parse(DefaultJsonc)?.Version ?? 0;

        /// <summary>
        /// The book, loaded on first ask from the file next to the plugin and written
        /// there if it is missing. Null means the file could not be parsed, which the
        /// callers read as "leave every material to the game" — a broken book must not
        /// silently become a different physics.
        /// </summary>
        public static Book Current
        {
            get
            {
                if (_cached == null && !_failed)
                {
                    Load();
                }

                return _cached;
            }
        }

        /// <summary>Test seam: hands the loader a book directly, no file involved.</summary>
        public static void UseForTests(Book book)
        {
            _cached = book;
            _failed = book == null;
        }

        private static void Load()
        {
            string path = null;
            try
            {
                var dir = Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(dir))
                {
                    dir = Path.Combine(BepInEx.Paths.PluginPath, "PLATE");
                }

                Directory.CreateDirectory(dir);
                path = Path.Combine(dir, FileName);

                if (!File.Exists(path))
                {
                    File.WriteAllText(path, DefaultJsonc);
                    Plugin.Log.LogInfo($"[PLATE] Obstacle reference written: {path}");
                }

                var book = Parse(File.ReadAllText(path));
                if (book != null && book.Version < ShippedVersion)
                {
                    book = Refresh(path, book.Version);
                }

                if (book == null)
                {
                    _failed = true;
                    Plugin.Log.LogError(
                        $"[PLATE] {FileName} could not be parsed — the obstacle model is OFF and " +
                        "every wall behaves as the game says. Fix the file or delete it to get " +
                        "the shipped one back.");
                    return;
                }

                _cached = book;
                Overlay.HitFeed.LogEvent(
                    $"[PLATE] Obstacle reference v{book.Version}: " +
                    $"{book.Materials?.Count ?? 0} materials");
            }
            catch (Exception ex)
            {
                _failed = true;
                Plugin.Log.LogError(
                    $"[PLATE] Failed to load {path ?? FileName} ({ex.Message}); the obstacle " +
                    "model is OFF and every wall behaves as the game says.");
            }
        }

        /// <summary>
        /// Replaces a book written by an older version of the mod, keeping the old one
        /// beside it. A shipped figure that turned out wrong has to reach the people who
        /// already ran the mod once, and their own edits have to survive being told so.
        /// </summary>
        private static Book Refresh(string path, int was)
        {
            try
            {
                var kept = path + $".v{was}.bak";
                File.Copy(path, kept, overwrite: true);
                File.WriteAllText(path, DefaultJsonc);
                Plugin.Log.LogInfo(
                    $"[PLATE] {FileName} was version {was}, the mod ships {ShippedVersion}. " +
                    $"Rewritten; your previous copy is at {Path.GetFileName(kept)}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[PLATE] Could not refresh {FileName}: {ex.Message}");
            }

            return Parse(DefaultJsonc);
        }

        public static Book Parse(string json)
        {
            return JsonConvert.DeserializeObject<Book>(json);
        }

        // --- Resolution ---

        public static ObstacleModel.Tuning TuningOf(Book book)
        {
            var g = book?.Globals ?? new GlobalsRef();
            return new ObstacleModel.Tuning
            {
                AngleMinCos = g.AngleMinCos,
                ConfinementFactor = g.ConfinementFactor,
                DragCoefficient = g.DragCoefficient,
                ExpansionDepthFactor = g.ExpansionDepthFactor,
                RicochetBand = g.RicochetBand,
                RicochetVelocityRef = g.RicochetVelocityRef,
                RicochetVelocityExp = g.RicochetVelocityExp,
                RicochetLoss = g.RicochetLoss,
                RicochetFlatten = g.RicochetFlatten,
                DeviationK = g.DeviationK,
                DeviationDeformMult = g.DeviationDeformMult,
                CoreBluntK = g.CoreBluntK,
                CoreErosionK = g.CoreErosionK,
                JacketStripWork = g.JacketStripWork,
                ShellCavityMm = g.ShellCavityMm,
                SteelLimitScatter = g.SteelLimitScatter,
                YawGainK = g.YawGainK,
                YawObliquityK = g.YawObliquityK,
            };
        }

        /// <summary>The entry for a material name, or null when the book says nothing
        /// about it — which is the same thing as saying "vanilla".</summary>
        public static MaterialRef Material(Book book, string materialName)
        {
            if (book?.Materials == null || materialName == null)
            {
                return null;
            }

            return book.Materials.TryGetValue(materialName, out var m) ? m : null;
        }

        /// <summary>
        /// Which material this collider really is, given the name of the object carrying
        /// it. Normally the material the game reports; a different one where the book
        /// says the object's name knows better — see
        /// <see cref="MaterialRef.NameOverrides"/>.
        ///
        /// Everything downstream is resolved from what this returns, the journal and the
        /// marker included: a line that says Concrete for a wall the model priced as
        /// brick would send the next person looking for a bug in the arithmetic.
        /// </summary>
        /// <summary>
        /// Three layers, in this order, each one only ever ADDING a material the book
        /// already defines:
        ///
        ///   identity  — the NameOverrides above. Settles the question outright: an
        ///               object claimed by name is that object, and nothing said about
        ///               its material or its parents may take it back.
        ///   suffix    — the word the level designer wrote after `_BALLISTIC_` in the
        ///               collider's OWN name, through <see cref="Book.SuffixAliases"/>.
        ///   taxonomy  — what the scene graph the collider hangs in says it is part of
        ///               (<see cref="TaxonomyRef"/>), applied to whatever the two layers
        ///               above left.
        ///
        /// The layers chain deliberately: a `WoodThin` collider whose own name says
        /// `metalthin`, parked under `VEHICLES`, is read as sheet by the suffix and then
        /// as vehicle skin by the taxonomy. What each layer cannot do is invent — a
        /// target the book does not define leaves the material where it was, so a typo
        /// in the book is a rule that does nothing rather than a material switched off.
        ///
        /// Names are tried in the order given — the collider's own first, then its
        /// ancestors — and for identity the FIRST NAME that fires any rule settles the
        /// material: a debris shard whose own name says "chunk" stays glass even if it
        /// sits under a glass-block wall. Ancestors exist because half the scene names
        /// its colliders "Metal" and hangs the identity a level or two up: a BTR is
        /// `balistic/BTR_82`, a fridge door is `Fridge (1)/Door_D/Ballistic 1/Metal 1`.
        /// The suffix layer reads the collider's own name only — the word describes the
        /// part it is written on — while the taxonomy reads all of them.
        /// </summary>
        public static string EffectiveMaterial(Book book, string materialName,
            params string[] objectNames)
        {
            if (objectNames == null || objectNames.Length == 0)
            {
                return materialName;
            }

            var identity = IdentityMaterial(book, materialName, objectNames);
            if (identity != null)
            {
                return identity;
            }

            var current = materialName;

            var suffix = SuffixMaterial(book, current, objectNames[0]);
            if (suffix != null)
            {
                current = suffix;
            }

            var vehicle = VehicleMaterial(book, current, objectNames);
            if (vehicle != null)
            {
                current = vehicle;
            }

            return current;
        }

        /// <summary>
        /// The first layer: an explicit name rule. Null means no rule fired anywhere,
        /// which is the only case the layers below get to speak in.
        /// </summary>
        private static string IdentityMaterial(Book book, string materialName,
            string[] objectNames)
        {
            var m = Material(book, materialName);
            if (m?.NameOverrides == null)
            {
                return null;
            }

            foreach (var objectName in objectNames)
            {
                if (string.IsNullOrEmpty(objectName))
                {
                    continue;
                }

                foreach (var pair in m.NameOverrides)
                {
                    if (string.IsNullOrEmpty(pair.Key) ||
                        objectName.IndexOf(pair.Key, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    // a substitute the book does not define would silently mean
                    // "vanilla", turning a typo into a disabled material. The rule still
                    // COUNTS as fired: the author of the book said this object is not
                    // its preset, and the layers below must not overrule a decision that
                    // was made and merely misspelled.
                    return Material(book, pair.Value) != null ? pair.Value : materialName;
                }
            }

            return null;
        }

        /// <summary>The marker BSG's level tooling writes the material word after.</summary>
        private const string SuffixMarker = "_BALLISTIC";

        /// <summary>
        /// The density flags the same tooling appends after the material word. They are
        /// the collider's PenetrationLevel said in words and carry no material.
        /// </summary>
        private static readonly string[] DensityFlags = { "_LowPen", "_MedPen", "_HiPen" };

        /// <summary>
        /// The material word out of a collider's own name, normalised — or null when the
        /// name carries none.
        ///
        /// Only a name containing `_BALLISTIC` has a word at all: `Metal_PL100` is a
        /// collider called Metal, not a designer saying "metal". What follows the marker
        /// is trimmed of the density flags, of a baked-in `_PL100`, and of trailing pure
        /// numbers (`_01`, ` 1`), all of which are numbering rather than material.
        /// </summary>
        public static string SuffixWord(string ownName)
        {
            if (string.IsNullOrEmpty(ownName))
            {
                return null;
            }

            var at = ownName.LastIndexOf(SuffixMarker, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
            {
                return null;
            }

            var tail = ownName.Substring(at + SuffixMarker.Length).Trim().Trim('_');

            var cutting = true;
            while (cutting && tail.Length > 0)
            {
                cutting = false;

                foreach (var flag in DensityFlags)
                {
                    if (tail.EndsWith(flag, StringComparison.OrdinalIgnoreCase))
                    {
                        tail = tail.Substring(0, tail.Length - flag.Length);
                        cutting = true;
                    }
                }

                var cut = tail.LastIndexOf('_');
                if (cut > 0 && IsNumbering(tail.Substring(cut + 1)))
                {
                    tail = tail.Substring(0, cut);
                    cutting = true;
                }
            }

            tail = tail.Trim().Trim('_');
            return tail.Length == 0 ? null : tail;
        }

        /// <summary>A trailing segment that is a number, or a `PL` and a number.</summary>
        private static bool IsNumbering(string segment)
        {
            if (segment.StartsWith("PL", StringComparison.OrdinalIgnoreCase) && segment.Length > 2)
            {
                segment = segment.Substring(2);
            }

            if (segment.Length == 0)
            {
                return false;
            }

            foreach (var c in segment)
            {
                if (c < '0' || c > '9')
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The second layer: the designer's own word for what this collider is made of.
        ///
        /// It fires only when the alias table knows the word AND names a material other
        /// than the one in hand AND the preset is not one of the substances the word
        /// cannot outrank (<see cref="Book.SuffixFinal"/>). Everything else — a junk word
        /// (`simple`, `collider`, `new`), a word the table deliberately refuses as
        /// ambiguous (`metal`, `wood`: the census has both thin and thick carriers), a
        /// name with no marker in it — leaves the material alone. Null means silent.
        /// </summary>
        private static string SuffixMaterial(Book book, string current, string ownName)
        {
            if (book?.SuffixAliases == null || IsSuffixFinal(book, current))
            {
                return null;
            }

            var target = Alias(book, SuffixWord(ownName));
            if (target == null ||
                string.Equals(target, current, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return Material(book, target) != null ? target : null;
        }

        /// <summary>
        /// The alias table, tried on the whole word first and then on progressively
        /// shorter ones: `metalthin_top` is the sheet with a position on it, and dropping
        /// the tail finds it. Two drops at most, and the whole word is always tried
        /// first, so `wood_thin` resolves as itself long before it could be cut down to
        /// the ambiguous `wood`.
        /// </summary>
        private static string Alias(Book book, string word)
        {
            if (string.IsNullOrEmpty(word))
            {
                return null;
            }

            var probe = word;
            for (var drop = 0; drop <= 2; drop++)
            {
                foreach (var pair in book.SuffixAliases)
                {
                    if (string.Equals(pair.Key, probe, StringComparison.OrdinalIgnoreCase))
                    {
                        return pair.Value;
                    }
                }

                var cut = probe.LastIndexOf('_');
                if (cut <= 0)
                {
                    return null;
                }

                probe = probe.Substring(0, cut);
            }

            return null;
        }

        private static bool IsSuffixFinal(Book book, string materialName)
        {
            if (book?.SuffixFinal == null || string.IsNullOrEmpty(materialName))
            {
                return false;
            }

            foreach (var name in book.SuffixFinal)
            {
                if (string.Equals(name, materialName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The third layer: this collider is part of a vehicle, so its sheet is a
        /// vehicle's sheet. Applied to whatever the suffix layer left, and null when the
        /// scene says nothing or the map has no entry for the material in hand.
        /// </summary>
        private static string VehicleMaterial(Book book, string current, string[] objectNames)
        {
            var map = book?.Taxonomy?.VehicleMap;
            if (map == null || !IsVehicle(book, objectNames))
            {
                return null;
            }

            foreach (var pair in map)
            {
                if (!string.Equals(pair.Key, current, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return Material(book, pair.Value) != null &&
                       !string.Equals(pair.Value, current, StringComparison.OrdinalIgnoreCase)
                    ? pair.Value
                    : null;
            }

            return null;
        }

        /// <summary>
        /// Is this collider part of a vehicle: either it hangs under a grouping node
        /// named as one, or one of the names in its line says a vehicle model. Both
        /// halves are needed — see <see cref="TaxonomyRef.VehicleFamilies"/>.
        /// </summary>
        public static bool IsVehicle(Book book, params string[] objectNames)
        {
            var tax = book?.Taxonomy;
            if (tax == null || objectNames == null)
            {
                return false;
            }

            for (var i = 0; i < objectNames.Length; i++)
            {
                var name = objectNames[i];
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                // the node is a PARENT's job: a collider called "vehicle" is a prop
                // somebody named badly, not a grouping of anything
                if (i > 0 && IsNode(tax.VehicleNodes, name))
                {
                    return true;
                }

                if (tax.VehicleFamilies == null)
                {
                    continue;
                }

                foreach (var family in tax.VehicleFamilies)
                {
                    if (!string.IsNullOrEmpty(family) &&
                        name.IndexOf(family, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>The <see cref="MaterialRef.DoorLeaf"/> value for a hollow leaf.</summary>
        public const string DoorLeafSkins = "skins";

        /// <summary>
        /// How many of the book's walls the entry face of this collider charges. Two
        /// only for a door leaf whose MATERIAL says a leaf is hollow
        /// (<see cref="MaterialRef.DoorLeaf"/> = "skins"): nobody laminates a door out
        /// of material that carries itself — a bunker door is one thick plate, not two.
        /// The names are the collider's own first, then its ancestors, and only the
        /// ancestors carry the grouping node.
        /// </summary>
        public static double WallsCrossed(Book book, string materialName,
            params string[] objectNames)
        {
            if (!IsDoorLeaf(book, objectNames))
            {
                return 1;
            }

            var m = Material(book, materialName);
            if (m?.DoorLeaf != DoorLeafSkins)
            {
                return 1;
            }

            var walls = book.Taxonomy.DoorWalls;
            return walls > 1 ? walls : 1;
        }

        /// <summary>
        /// The fixed thickness of this door leaf, mm — a wooden door is its
        /// material's <see cref="MaterialRef.DoorLeafMm"/> (~50 mm of wood) whatever
        /// its collider measures: leaf colliders run 100-200 mm deep, and a chord
        /// read as timber made every wooden door a safe. Zero: not such a leaf.
        /// </summary>
        public static double DoorLeafThicknessMm(Book book, string materialName,
            params string[] objectNames)
        {
            if (!IsDoorLeaf(book, objectNames))
            {
                return 0;
            }

            var mm = Material(book, materialName)?.DoorLeafMm ?? 0;
            return mm > 0 ? mm : 0;
        }

        /// <summary>
        /// Is this collider a door leaf. Two ways of knowing, and the scene may offer
        /// either: BSG's own `DOORS` grouping node on one of the ancestors, or a name
        /// the book recognises (<see cref="TaxonomyRef.DoorNames"/>) anywhere in the
        /// collider's own name or its ancestors' — because the node is not always there
        /// to be found, and a gate is a leaf whatever it is parented to.
        /// </summary>
        private static bool IsDoorLeaf(Book book, string[] objectNames)
        {
            var tax = book?.Taxonomy;
            if (tax == null || objectNames == null)
            {
                return false;
            }

            for (var i = 0; i < objectNames.Length; i++)
            {
                var name = objectNames[i];
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                // the grouping node is an ancestor's business — a collider of its own
                // called "DOORS" is a prop with an unfortunate name, not a group
                if (i > 0 && IsNode(tax.DoorNodes, name))
                {
                    return true;
                }

                if (tax.DoorNames == null)
                {
                    continue;
                }

                foreach (var word in tax.DoorNames)
                {
                    if (!string.IsNullOrEmpty(word) &&
                        name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Is this transform one of the named grouping nodes. Whole-name equality, not a
        /// substring test — `vechicle_BMP2` is a prop and `Door_D` is a fridge's door,
        /// and neither is the scene's `VEHICLES` or `DOORS` node. Unity's duplicate
        /// marker is not part of the name: `VEHICLES (1)` is the same node.
        /// </summary>
        private static bool IsNode(List<string> nodes, string name)
        {
            if (nodes == null)
            {
                return false;
            }

            var segment = NodeName(name);
            foreach (var node in nodes)
            {
                if (string.Equals(node, segment, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string NodeName(string name)
        {
            var s = name.Trim();
            var open = s.LastIndexOf('(');
            if (open > 0 && s.EndsWith(")", StringComparison.Ordinal) &&
                IsNumbering(s.Substring(open + 1, s.Length - open - 2)))
            {
                s = s.Substring(0, open).TrimEnd(' ', '_');
            }

            return s;
        }

        /// <summary>
        /// The barrier this collider is. False means "leave it to the game": an unknown
        /// material, or a mechanism of vanilla.
        ///
        /// <paramref name="walls"/> is how many of the book's walls the entry face
        /// charges — <see cref="WallsCrossed"/>, two for a hollow door leaf. It applies
        /// to shells only: a solid material's thickness is the measured chord, and the
        /// chord already contains everything the projectile has to cross.
        /// <paramref name="leafMm"/> — <see cref="DoorLeafThicknessMm"/> — replaces the
        /// book's anchor thickness with the leaf's fixed one: a wooden door is ~50 mm
        /// of wood whatever its collider measures.
        /// </summary>
        public static bool TryBarrier(Book book, string materialName, float penetrationLevel,
            out ObstacleModel.Barrier barrier, double walls = 1, double leafMm = 0)
        {
            barrier = default;
            var m = Material(book, materialName);
            if (m?.Mechanism == null || m.Mechanism == ObstacleModel.MechVanilla)
            {
                return false;
            }

            barrier.Mechanism = m.Mechanism;
            barrier.Solid = m.Solid;
            barrier.SpallFactor = m.SpallFactor;
            barrier.StrengthMPa = m.StrengthMPa;
            barrier.CostJ = m.CostJ;
            barrier.ThicknessMm = !m.Solid && leafMm > 0 ? leafMm : Thickness(m, penetrationLevel);
            barrier.Walls = !m.Solid && walls > 1 ? walls : 1;

            // a steel material is made of the one steel the book describes, and does not
            // repeat its density and hardness in every entry
            var steel = book?.Steel ?? new SteelRef();
            var isSteel = m.Mechanism == ObstacleModel.MechSteel;
            barrier.DensityGCm3 = isSteel ? steel.DensityGCm3 : m.DensityGCm3;
            barrier.HardnessHv = isSteel ? steel.HardnessHv : m.HardnessHv;
            barrier.YieldMPa = steel.YieldMPa;
            barrier.ShearMPa = steel.ShearMPa;
            barrier.FailureMode = steel.FailureMode;

            // a mechanism that needs a thickness and has none resolves to nothing at all;
            // saying "0 mm of steel" would make every unmapped collider free to shoot
            // through, which is the one failure mode worse than leaving it vanilla
            if (barrier.ThicknessMm <= 0 &&
                (barrier.Mechanism == ObstacleModel.MechSteel ||
                 barrier.Mechanism == ObstacleModel.MechPoncelet))
            {
                return false;
            }

            barrier.Fill = Fill(book, m, penetrationLevel);
            return true;
        }

        /// <summary>
        /// The packing of a carrier medium, or null when it has none — which is every
        /// material but palletised cargo.
        ///
        /// A packing whose content the book does not define, or names a packed material
        /// (cargo inside cargo), resolves to nothing at all: the carrier is then crossed
        /// as the plain medium it is, which is the same fallback rule every layer in this
        /// file follows — a mistake in the book costs the feature, never the physics.
        /// </summary>
        private static ObstacleModel.StackFill Fill(Book book, MaterialRef m,
            float penetrationLevel)
        {
            var s = m.Stack;
            if (s == null || s.SpacingMm <= 0 || string.IsNullOrEmpty(s.Content))
            {
                return null;
            }

            var content = Material(book, s.Content);
            if (content == null || content.Stack != null ||
                !TryBarrier(book, s.Content, penetrationLevel, out var packed))
            {
                return null;
            }

            return new ObstacleModel.StackFill
            {
                SpacingMm = s.SpacingMm,
                ContentFraction = s.ContentFraction,
                Chance = s.Chance,
                Content = packed,
            };
        }

        /// <summary>
        /// The bounce this surface does. False means the game decides — which is what an
        /// unknown material and an explicit "Vanilla" both mean. A class of "None"
        /// resolves with a critical angle of 0, i.e. never.
        /// </summary>
        public static bool TryRicochet(Book book, string materialName,
            out double alphaCritDeg, out double retention)
        {
            alphaCritDeg = 0;
            retention = 0;

            var m = Material(book, materialName);
            if (m == null)
            {
                return false;
            }

            var key = string.IsNullOrEmpty(m.Ricochet) ? DefaultRicochetClass(m.Mechanism) : m.Ricochet;
            if (key == RicochetVanilla)
            {
                return false;
            }

            if (key == RicochetNone)
            {
                return true; // decided, and the decision is "never"
            }

            if (book?.Ricochet == null || !book.Ricochet.TryGetValue(key, out var r) || r == null)
            {
                return false; // a class nobody defined is not a licence to invent one
            }

            alphaCritDeg = r.AlphaCritDeg;
            retention = r.Retention;
            return true;
        }

        /// <summary>What a material that does not name a ricochet class gets.</summary>
        private static string DefaultRicochetClass(string mechanism)
        {
            switch (mechanism)
            {
                case ObstacleModel.MechSteel:
                case ObstacleModel.MechNever:
                    return "Hard";
                case ObstacleModel.MechPoncelet:
                    return "Soft";
                case ObstacleModel.MechAlways:
                    return RicochetNone;
                default:
                    return RicochetVanilla;
            }
        }

        /// <summary>
        /// Thickness at this collider's PenetrationLevel, mm: piecewise-linear between
        /// the anchors, flat outside them.
        ///
        /// The level is read off the collider and not off the preset on purpose. A map's
        /// `_MedPen` and `_HiPen` variants are the same MaterialType with different
        /// numbers, and level designers are free to put any value they like on any
        /// object, so the only honest reading is "this number, interpolated".
        /// </summary>
        public static double Thickness(MaterialRef m, float penetrationLevel)
        {
            if (m?.Anchors == null || m.Anchors.Count == 0)
            {
                return 0;
            }

            double loLevel = 0, loValue = 0, hiLevel = 0, hiValue = 0;
            var haveLo = false;
            var haveHi = false;

            foreach (var kv in m.Anchors)
            {
                if (!double.TryParse(kv.Key, NumberStyles.Float, CultureInfo.InvariantCulture,
                        out var level))
                {
                    continue;
                }

                if (level <= penetrationLevel && (!haveLo || level > loLevel))
                {
                    loLevel = level;
                    loValue = kv.Value;
                    haveLo = true;
                }

                if (level >= penetrationLevel && (!haveHi || level < hiLevel))
                {
                    hiLevel = level;
                    hiValue = kv.Value;
                    haveHi = true;
                }
            }

            if (!haveLo)
            {
                return hiValue;
            }

            if (!haveHi)
            {
                return loValue;
            }

            if (hiLevel <= loLevel)
            {
                return loValue;
            }

            var f = (penetrationLevel - loLevel) / (hiLevel - loLevel);
            return loValue + (hiValue - loValue) * f;
        }

        // --- The book itself ---

        /// <summary>
        /// The shipped reference. Every number here is either a published material
        /// property or a thickness the object plausibly is; the two global constants the
        /// depth law needs come from theory and are checked against the pine tables in
        /// MODEL.md, "Environment barriers".
        /// </summary>
        public const string DefaultJsonc = @"// P.L.A.T.E. — environment barriers.
//
// What the game's collider materials are made of, and how thick they are. The mod
// reads a collider's MaterialType and its PenetrationLevel and turns the pair into a
// barrier; everything after that is physics on the projectile's own state (mass,
// diameter, velocity, deformable fraction). See docs/MODEL.md, ""Environment barriers"".
//
// Mechanisms:
//   ""steel""     — a steel sheet. The ballistic limit, the same law the armour model
//                 uses, against the sheet properties in ""Steel"" below. Anchors are
//                 thickness in mm.
//   ""poncelet""  — a bulk medium that resists by crushing and by inertia (wood,
//                 cardboard, gravel, snow). Needs StrengthMPa and DensityGCm3, both
//                 published properties of the real material. Anchors are thickness in mm.
//   ""always""    — through, for a flat energy price CostJ (glass, wire mesh, grass).
//
// DensityGCm3 is read by three different questions — how deep the projectile goes, how
// far off line the barrier throws it, and whether the projectile survives intact — so
// even a material nothing stops carries one. HardnessHv is read by the last of those
// alone: it is the barrier's side of the Taylor criterion that decides whether the core
// mushrooms on the way through, and a material that obviously cannot deform anything
// leaves it out.
//   ""never""     — a wall. The engine gives no thickness for concrete or soil, so the
//                 only honest answer is that bullets do not go through them.
//   ""vanilla""   — leave this material to the game entirely.
//
// Ricochet classes name an entry in ""Ricochet""; ""None"" means this surface never
// bounces anything, ""Vanilla"" leaves the bounce to the game. A material that names
// none gets Hard for steel and walls, Soft for bulk media, None for ""always"".
//
// PenetrationLevel overrides nothing. It used to be read as the designer saying ""this
// is a wall"", and a census of a raid killed that reading: at level 100 sit not only
// concrete walls but an IBC tote's plastic cage, a polythene box, a cistern, a boiler,
// a reactor housing, a run of pipes and a patch of gravel floor. It is a blanket ""not
// meant to be shot through"" applied by hand, and reading it as geometry made a plastic
// tote bulletproof. The level now only picks a thickness out of the anchors below.
//
// ""Solid"" says whether a thing made of this material is solid through. It decides
// what the measured collider means: for a solid one it IS the path (a log is as deep
// as the log), for a shell it is only the outline and the wall is the book's thickness
// (a barrel is 600 mm of collider around 1 mm of steel). Unity has no hollow flag to
// read; what the game does carry is the MaterialType the designer put on the object,
// and ""MetalThin"" on a barrel is exactly that statement. Default false — a material
// nobody has classified keeps the book's thickness rather than becoming a wall.
{
  ""Globals"": {
    // Smallest cosine an oblique hit is read at: a graze is not an infinite wall.
    ""AngleMinCos"": 0.20,

    // How much more it costs to open a hole in a medium than to crush it in a uniaxial
    // test. Cavity-expansion theory prices this at three to five times the material's
    // own strength; the armour model's ductile hole-growth constant sits on the same
    // scale.
    ""ConfinementFactor"": 5.0,

    // Poncelet's inertial coefficient: the drag term is 0.5*Cd*rho*v^2 over the
    // frontal area. One, the classic value for a blunt cavity.
    ""DragCoefficient"": 1.0,

    // A bullet that flattens digs less deeply, exactly as in tissue; the same figure
    // the wound channel uses.
    ""ExpansionDepthFactor"": 0.4,

    // Ricochet. The critical grazing angle falls with speed — a fast bullet digs its
    // own crater before the surface can turn it — as alpha0*(Vref/v)^q.
    ""RicochetBand"": 0.25,
    ""RicochetVelocityRef"": 400.0,
    ""RicochetVelocityExp"": 0.35,

    // How much of the retention is lost between a grazing bounce and one right at the
    // critical angle, and how much flatter than the mirror reflection the projectile
    // leaves.
    ""RicochetLoss"": 0.5,
    ""RicochetFlatten"": 0.5,

    // Deflection. The barrier's areal density along the path over the projectile's own
    // sectional density, which is what a lopsided impulse against the projectile's
    // momentum comes to once the velocity has cancelled out of both. Pinned so that a
    // 9x19 through a 45 mm pine door comes off line by about two degrees.
    ""DeviationK"": 0.2,

    // A projectile the barrier deformed is no longer a symmetric rigid body and veers
    // much harder. This is the ENTIRE velocity dependence of the deflection: nothing a
    // rigid projectile does depends on how fast it was going (see docs/MODEL.md).
    ""DeviationDeformMult"": 2.0,

    // What a barrier that killed the core does to it: how blunt it leaves it and how
    // much mass it shaves off in the hole, both at the point where the barrier took ALL
    // of the projectile's speed, and both scaled down by how much it actually took.
    // Pinned so that a barrier taking a third of the speed does exactly what the armour
    // model's plate constants (KDef 0.2, KFrag 0.05) do.
    ""CoreBluntK"": 0.6,
    ""CoreErosionK"": 0.15,

    // How much of the speed a barrier has to take before it has a rim solid enough to
    // shear a jacket off. Below it a hard core carries its jacket through, which is what
    // happens through thin sheet; a plate has no such threshold because a plate is never
    // thin.
    ""JacketStripWork"": 0.15,

    // How deep a shell must be before a projectile crossing it is taken to meet TWO of
    // its walls rather than one panel. A barrel is outline around air and costs its wall
    // twice; a container side is one sheet whose collider is a few centimetres thick.
    // Nothing in the scene tells them apart, so this is a judgement about how maps are
    // authored rather than physics — the weakest number in the module, and the one to
    // reach for first if sheet metal starts feeling too expensive.
    ""ShellCavityMm"": 150,

    // A ballistic limit is a distribution, not a number: certification work prices its
    // shot-to-shot scatter at CV = 0.04, and every encounter with a sheet draws its own
    // limit uniformly within the +/-2-sigma of that. Near the limit this makes a zone
    // of mixed results instead of a cliff — some rounds dribble through, some stop.
    ""SteelLimitScatter"": 0.08,

    // Yaw. A barrier does not only slow a projectile, it destabilises it: what comes out
    // is turning, and the NEXT barrier meets it part-way sideways, presenting more area
    // and paying for all of it. Delta = YawGainK * Work * (L/d - 1) * (1 + YawObliquityK
    // * tan(theta)). Work is the share of the speed this barrier took, so a sheet of tin
    // turns a bullet a little and a plate turns it a lot, out of the same figure that
    // scales the core's deformation; L/d - 1 is the lever arm, and it is why a slug of
    // buckshot comes out facing the way it went in and a flechette does not.
    //
    // YawGainK is pinned on the forensic reconstruction anchor: a 9x19 ball through a
    // 3 mm car flank (Work 0.43, L/d - 1 = 1.05) arrives at the target about half
    // sideways, which is the keyholing those cases are recognised by. YawObliquityK is a
    // judgement and not a measurement — at 45 degrees the destabilising impulse roughly
    // doubles — and it is written down here so it can be argued with.
    ""YawGainK"": 1.1,
    ""YawObliquityK"": 1.0
  },

  // Structural mild steel: what sheet metal in the world is made of. Not armour steel —
  // it has strain-hardening reserve left, so it cannot localise shear and flows aside
  // instead, which is the HoleExpansion mode. Same figures the armour fixture's
  // published mild-steel ladder is measured on.
  ""Steel"": {
    ""YieldMPa"": 250,
    ""ShearMPa"": 270,
    ""HardnessHv"": 158,
    ""DensityGCm3"": 7.85,
    ""FailureMode"": ""HoleExpansion""
  },

  // Critical grazing angle at RicochetVelocityRef, and the fraction of speed a grazing
  // bounce keeps. Forensic ricochet work (Haag; Kneubuehl for water) puts hard surfaces
  // in the mid-teens of degrees for handgun bullets, soil and sand higher and softer,
  // and water at about seven degrees with a strong velocity dependence.
  ""Ricochet"": {
    ""Hard"":  { ""AlphaCritDeg"": 17, ""Retention"": 0.80 },
    // Yielding granular ground: soil and sand roll a bullet out of a shallow trough,
    // and the forensic tables put their critical angles at 25-30 degrees.
    ""Soft"":  { ""AlphaCritDeg"": 25, ""Retention"": 0.50 },
    // Wood is not soil: the fibres cut instead of yielding, and the same tables put
    // it at 12-17 degrees for handgun rounds, less at rifle speed. One class for
    // both was why tables mirrored P90 fire — a standing shooter meets a table top
    // at 10-16 degrees of graze, under soil's threshold and over wood's.
    ""Wood"":  { ""AlphaCritDeg"": 15, ""Retention"": 0.50 },
    ""Water"": { ""AlphaCritDeg"": 7,  ""Retention"": 0.50 }
  },

  // --- Resolution layers ---
  //
  // What a collider is made of is decided in three layers: the per-material
  // ""NameOverrides"" further down (identity — settles it outright), then the word the
  // level designer wrote after ""_BALLISTIC_"" in the collider's OWN name, then the
  // scene graph the collider hangs in. Each layer can only name a material this book
  // already defines, and anything it cannot resolve leaves the material where it was —
  // so the worst case of every layer is the preset, which is what the book did before
  // they existed. See docs/MODEL.md, ""Resolution layers"".

  // The designer's word for the material, and what it means here.
  //
  // Generated from a census of 567 504 colliders: 346 080 carry a ""_BALLISTIC_<word>""
  // suffix and about 6 000 of those contradict the MaterialType on the same object —
  // 1 099 WoodThin colliders saying ""metalthin"" (metal door frames, sling loops),
  // 454 MetalThin saying ""concrete"" (an entire shower block), 20 Chainfence saying
  // ""metalthin"" (the Labs cells, until now a free pass), and BSG's own typos, which
  // fail the engine's suffix parser and land their colliders on ""None"". The
  // misspellings are therefore in the table on purpose. What is deliberately absent is
  // the ambiguous half of the vocabulary: bare ""metal"" and bare ""wood"" sit on thin
  // and thick carriers alike and name nothing.
  //
  // The word is looked up whole first, then with up to two trailing segments dropped
  // (""metalthin_top"" is the sheet with a position on it) — which is why ""wood_thin""
  // resolves as itself long before it could be cut down to the ambiguous ""wood"".
  ""SuffixAliases"": {
    ""metalthin"": ""MetalThin"",   ""metal_thin"": ""MetalThin"",  ""metaltin"": ""MetalThin"",
    ""metathin"": ""MetalThin"",    ""melalthin"": ""MetalThin"",   ""metallthin"": ""MetalThin"",
    ""metalthick"": ""MetalThick"", ""metal_thick"": ""MetalThick"", ""metalthic"": ""MetalThick"",
    ""metlathick"": ""MetalThick"", ""metlalthick"": ""MetalThick"",
    ""woodthin"": ""WoodThin"",     ""wood_thin"": ""WoodThin"",
    ""woodthick"": ""WoodThick"",   ""wood_thick"": ""WoodThick"",
    ""chainfence"": ""Chainfence"", ""chainfance"": ""Chainfence"",
    ""concrete"": ""Concrete"",     ""concete"": ""Concrete"",      ""conrete"": ""Concrete"",
    ""fabric"": ""Fabric"",         ""fabrick"": ""Fabric"",        ""cloth"": ""Fabric"",
    ""glass"": ""Glass"",           ""galss"": ""Glass"",
    ""cardboard"": ""Cardboard"",   ""carboard"": ""Cardboard"",
    ""plastic"": ""Plastic"",       ""platic"": ""Plastic"",
    ""rubber"": ""Rubber"",         ""rubbers"": ""Rubber"",
    ""stone"": ""Stone"",           ""soil"": ""Soil"",             ""tile"": ""Tile"",
    ""garbage"": ""GarbagePaper"",  ""garbagepeper"": ""GarbagePaper"",
    ""generic_soft"": ""GenericSoft"", ""genericsoft"": ""GenericSoft""
  },

  // Presets the suffix layer may not overrule. These are substances rather than skins:
  // where the material is what the projectile has to cross and the word names what is
  // spread over it. 297 Concrete colliders say ""tile"" and 55 say ""stone"" — a wall
  // faced with tile is still a wall — ground is ground whatever is scattered on it, and
  // a shattered pane saying ""glass"" is still the shattered one, which is the cheaper
  // of the two entries. Anywhere else the preset is the weaker guess and the word wins.
  ""SuffixFinal"": [ ""GlassShattered"", ""Tile"", ""Concrete"", ""Stone"", ""Soil"",
                  ""SoilForest"", ""Gravel"", ""Asphalt"", ""Pebbles"", ""Snow"", ""Sand"",
                  ""Water"", ""WaterPuddle"", ""Swamp"", ""Body"" ],

  // What the scene graph says the collider is part of. BSG group their props under
  // naming nodes, and the grouping is a statement no MaterialType can carry: the same
  // 1 mm sheet is a road sign in one place and a car's flank in another.
  //
  // Nodes are matched as WHOLE ancestor names, never as substrings — ""vehicle"" as a
  // substring swallows the ""vechicle_BMP2"" prop and everything parked in a named car
  // park, and ""door"" would catch a fridge's ""Door_D"".
  ""Taxonomy"": {
    ""VehicleNodes"": [ ""vehicle"", ""vehicles"" ],

    // Model words, matched anywhere in the collider's own name or its ancestors'. The
    // node alone is not enough: the census found 5 101 vehicle-named colliders living
    // OUTSIDE any VEHICLES node — the same Chevrolet Cruze sits under the node on one
    // map and under ""OFF"" on another — so nodes alone would price one car at 3 mm and
    // its twin at 1. The words are the census's own, and the tempting short ones were
    // refused: bare ""man_"" catches ""woman_"", bare ""paz_"" catches unrelated props,
    // so the two PAZ buses are named in full.
    ""VehicleFamilies"": [
      ""cruze"", ""ford_focus"", ""kamaz"", ""gazel"", ""gaz_3302"", ""volkswagen"",
      ""ural_280"", ""bogdan"", ""pkts-6281"", ""vaz_21"", ""bmv_m6"", ""bmw"", ""liaz"",
      ""paz_civil"", ""paz_police"", ""subaru_legacy"", ""gelik"", ""mercedes_c"",
      ""skoda_octavia"", ""solaris"", ""uaz_"", ""koyoto"", ""sand_cruiser"",
      ""autoloader"", ""prizep"", ""tram"", ""man_opened"" ],

    // A vehicle's sheet is not a road sign's sheet, and a vehicle's plate is chassis
    // rail rather than a locker door. Armour and dense machinery are claimed above this
    // by identity rules, which no layer may overrule.
    ""VehicleMap"": { ""MetalThin"": ""VehicleChassis"", ""MetalThick"": ""StructuralSteel"" },

    // A door leaf is two skins over a frame. Its collider (46 mm on the metal doors the
    // survey measured) is far below ShellCavityMm, so the shell rule charges the entry
    // face one wall and the bullet crosses one sheet where it should cross two. Nothing
    // in the geometry separates a two-skin leaf from a single profiled sheet inside a
    // deep collider; the scene's own word does. The exit face is unchanged — under the
    // cavity threshold it is free, so a leaf costs exactly two sheets in total.
    ""DoorNodes"": [ ""door"", ""doors"" ],
    // ...where the scene HAS such a node. Factory's entrance gate hangs off
    // `Enterance_Gate_01` with no DOORS node in the chain at all, and on the maps that
    // do have one the gate's wicket door sits four levels below it — out of the
    // ancestor walk's reach. Both then read as the plain-69 anchor, 10 mm of the
    // heaviest steel in the game, and a shot through the wicket pays it TWICE: the
    // wicket is a child of the leaf and their colliders share the space it occupies
    // (a raid measured 102 mm of wicket inside 30 mm of leaf). Reading them as the
    // leaves they are makes each 5 mm, so the doubled crossing lands back on the one
    // plate it should have been. Substring, own name or any ancestor, like the vehicle
    // families — a gate is a leaf whatever it is parented to.
    // The rest of the family, each checked against the census for a word that catches
    // nothing else: swing gates (`gate_metall1`/`2`), the PTOR checkpoint gates, garage
    // gates, industrial transfer gateways. Their thick colliders were all on the
    // plain-69 anchor as well, and their thin ones (a garage sectional door, the PTOR
    // door leaves) are two skins over a frame, which is exactly what such a door is.
    // Roller shutters are NOT here: a shutter is one layer of slats, not a leaf, and
    // it is handled by a material rule instead.
    ""DoorNames"": [ ""enterance_gate"", ""gate_metall"", ""gates_ptor"",
                  ""garage_gate"", ""transfer_gateway"" ],
    ""DoorWalls"": 2
  },

  ""Materials"": {
    // --- Steel ---
    // The common sheet of the environment: car and bus bodies, cabinets, ducts run
    // 0.8-1.0 mm, fence profile 0.5-0.7. One flat anchor because a census of every
    // shipped scene put 95% of MetalThin instances on level 4 — the level separates
    // nothing, so it cannot carry a ladder.
    //
    // The overrides are the campaign's yield (see .claude/docs/OBSTACLE-PROP-SURVEY.md;
    // every keyword was validated against all 18k collider names). ORDER MATTERS —
    // first match wins — and 'gunsafe' must precede 'container' because it contains it
    // as a substring (scontainer_gunsafe_tall).
    // DoorLeaf skins: 1 mm sheet cannot carry itself as a slab, so a door of it is
    // two skins over a frame and pays both under a DOORS node.
    ""MetalThin"":    { ""Mechanism"": ""steel"", ""Ricochet"": ""Hard"", ""Anchors"": { ""4"": 1.0 },
                      ""DoorLeaf"": ""skins"",
                      ""NameOverrides"": {
                        // a gun safe is a steel box with real walls, not one sheet
                        ""gunsafe"": ""GunSafe"",
                        // 40 names / 1131 instances of mesh fencing, stair grates and
                        // grille doors whose own name says chainfence while the
                        // material says sheet; the same staircase ships _metalthin
                        // rails and a _chainfence tread separately — the suffix is
                        // deliberate.
                        //
                        // The suffix layer reproduces this rule everywhere EXCEPT the
                        // one place it must not be lost: 'metal_stairs' below is an
                        // identity rule and identity is final, so deleting this one
                        // would hand the mesh treads of stairs 02/07/08 to structural
                        // plate before the layer ever ran. It stays for the ordering,
                        // not for the coverage
                        ""chainfence"": ""Chainfence"",
                        // shipping containers are corrugated Corten, not tin
                        ""container"": ""ContainerSteel"",
                        // stair stringers and treads are structural plate (a raid put
                        // an eyeball on Metal_stairs_02: ~10 mm, not tin). AFTER
                        // chainfence, so the mesh treads of stairs 02/07/08 keep
                        // their free pass
                        ""metal_stairs"": ""StructuralSteel"",
                        // the Kirovets tractor's thin-tagged collider (its own name
                        // even says metalthick — BSG's cloning drift)
                        ""k702"": ""StructuralSteel"",
                        // a loader's chassis is frame plate with the works inside,
                        // not a car door. NOT bare 'loader' — autoloader is a truck
                        ""loader_01"": ""StructuralSteel"",
                        ""loader_small"": ""StructuralSteel"",
                        // a shower block's lockers name the material back at
                        // themselves — a shield, not a change. Their word is
                        // ""concrete"" (BSG tagged the whole block that way; the census
                        // counted 454 sheet colliders saying it), and without this the
                        // suffix layer reads a changing-room locker as a concrete slab:
                        // a raid put a 5.45 through both faces of one and the round
                        // arrived at the far wall with a quarter of its speed left
                        ""case_shower"": ""MetalThin"",
                        // a cable drum is wound copper, whatever its rim is made of
                        ""cable_drum"": ""Cable"",
                        ""kabel_pallet"": ""Cable"",
                        // BRDM colliders wear the THIN material at 1 mm — an armoured
                        // car an AK was shooting straight through
                        ""brdm"": ""Machinery"",
                        // a flotation tank is an apparatus full of water
                        ""flotator"": ""Machinery"",
                        // BSG's own word (with its own spelling) for structural
                        // framing — channels, mall frames, facility catwalks
                        ""constraction"": ""StructuralSteel"",
                        ""water_filter_facility"": ""StructuralSteel"",
                        // Streets' heavy plant priced as car sheet: the JCB backhoe
                        // (anonymous 'metal' colliders, name on the grandparent
                        // jcb3cx) and the asphalt paver. Structural plate, not
                        // Machinery — the Hyundai lesson: one collider spans the
                        // whole machine, cab included. NOT bare 'paver' — the
                        // Paver_handle prop is a separate small tool
                        ""jcb"": ""StructuralSteel"",
                        ""paver_ballistic"": ""StructuralSteel"" } },
    // Scrap lying over itself — thicker than one sheet, thinner than plate.
    ""GarbageMetal"": { ""Mechanism"": ""steel"", ""Ricochet"": ""Hard"", ""Anchors"": { ""7"": 1.5 } },
    // One material, four presets: LowPen 7, MedPen 18, HiPen 32 and the plain 69, which
    // is the heaviest steel in the game and the only thing vanilla reserved for .50.
    //
    // A SHELL now, not a solid: the campaign census showed its carriers are barrels,
    // cisterns, pipes, gates, trucks and dumpsters — outlines around air that measured
    // as metres of 'steel' when the chord was believed. What is genuinely dense is the
    // ~2.6% below: machinery, rails, columns, hatches, all routed to Machinery, which
    // keeps the measured-chord behaviour (a 5.45 must not cross an electric motor).
    // Every bare word that looks tempting here was tried against the census and burned:
    // 'rail' catches handrails and trailers, 'transformer' the substation shed and its
    // 23 mm doors, 'boiler' the boiler ROOMS and a 20 mm access panel, 'engine' a fire
    // engine, 'motor' a motorbike, 'kuka' the robot's mesh cage, 'crane' lattice booms.
    // DoorLeafMm: an armoured door, not a vault door. Under a DOORS node the plain-69
    // anchor was handing 1 716 ordinary building colliders — entrance doors, interior
    // metal doors, garage shutters, the PTOR gates — 10 mm of the heaviest steel in
    // the game, the tier vanilla reserved for .50. A real steel entrance door is
    // 1.5-3 mm of sheet over a frame; 5 mm is the armoured end of that and still
    // stops what a door should stop. The bunkers' blast doors are Machinery by
    // identity and never reach this number.
    ""MetalThick"":   { ""Mechanism"": ""steel"", ""Ricochet"": ""Hard"",
                      ""Anchors"": { ""7"": 2, ""18"": 4, ""32"": 6, ""69"": 10 },
                      ""DoorLeafMm"": 5,
                      ""NameOverrides"": {
                        ""pump_engine"": ""Machinery"",  ""pump_reducer"": ""Machinery"",
                        ""vertical_pump"": ""Machinery"", ""pumpsmall"": ""Machinery"",
                        ""turbine"": ""Machinery"",       ""power_transformer"": ""Machinery"",
                        ""breaker"": ""Machinery"",       ""generator"": ""Machinery"",
                        ""diesel"": ""Machinery"",        ""kuka_hand"": ""Machinery"",
                        ""column"": ""Machinery"",        ""railway_part"": ""Machinery"",
                        ""rail_01"": ""Machinery"",       ""rails"": ""Machinery"",
                        ""rail_cart"": ""Machinery"",     ""atm_"": ""Machinery"",
                        ""hatch"": ""Machinery"",         ""boiler_apparat"": ""Machinery"",
                        ""boiler_big"": ""Machinery"",    ""boiler_small"": ""Machinery"",
                        ""boiler_industrial"": ""Machinery"", ""boiler_sync"": ""Machinery"",
                        ""boiler_top"": ""Machinery"",    ""boiler_bottom"": ""Machinery"",
                        // 435 mm of actual turret armour on an otherwise hollow hull
                        ""model_turret"": ""Machinery"",
                        // a loader's counterweight is a cast-iron block — a raid put
                        // an eyeball on it. NOT bare 'loader' (autoloader is a truck),
                        // and NOT the Hyundai: its one collider spans the whole
                        // forklift, and a raid found it wrongly immune
                        ""loader_01"": ""Machinery"",
                        ""loader_small"": ""Machinery"",
                        // armour is armour: a BTR hull carries this material at a
                        // level the ladder reads as 2 mm, which a 9x19 crossed in a
                        // raid. The names live on the PARENTS (the colliders are
                        // anonymous), which is what ancestor matching is for
                        ""btr"": ""Machinery"",
                        // BMP-2 turret and hull sit on the same plain-100 anchor and
                        // fell to an AK in a raid; the name lives on the great-
                        // grandparent (Metal 1_PL100 / Ballistic 1 / Turret /
                        // vechicle_BMP2 — three climbs, exactly the reach)
                        ""bmp"": ""Machinery"",
                        // both spellings live in the scenes: 'T90' on Interchange's
                        // crashed prop, 'T_90A' on the Body of the drivable one — the
                        // audit caught the underscore slipping through the first rule
                        ""t90"": ""Machinery"",
                        ""t_90"": ""Machinery"",
                        // the rest of the armoured fleet the audit surfaced: a GAZ
                        // Tigr, the Typhoon MRAP, a Stryker hull
                        ""tiger"": ""Machinery"",
                        ""typhoon"": ""Machinery"",
                        ""stryker"": ""Machinery"",
                        // heavy plant: an excavator and a Kirovets tractor
                        ""caterpillar"": ""Machinery"",
                        ""k702"": ""Machinery"",
                        // an ordinary steel drum is 1.2-1.5 mm of sheet, not the 10 mm
                        // the plain-69 anchor hands it (Beer_, Chemical_, Customs_ and
                        // Metal_barrel are all drums; Barrel_fire already sits on
                        // MetalThin natively)
                        ""barrel"": ""MetalThin"",
                        // a diesel locomotive is not shot through — body, bogies and
                        // wheelsets alike (Locomotive_wheels_*, _telega_*); BRDM is
                        // an armoured car whatever material its colliders wear
                        ""locomotive"": ""Machinery"",
                        ""brdm"": ""Machinery"",
                        // The Kamaz family, refined by two raids: eleven variants
                        // (cargo, tipper, mixer, garbage, police, tent, AP-2, crane,
                        // 5490), and every variant's DOORS are cab skin while the
                        // rest — chassis rails, body, drum — is structural plate.
                        // The armoured one is armour and must be claimed first;
                        // doors before the family word, the family word last.
                        // The doors are VehicleChassis rather than bare sheet: a truck
                        // cab door is built like a car's, two panels with the window
                        // mechanism between them
                        ""kamaz_armored"": ""Machinery"",
                        ""kamaz_4310_ap-2_door"": ""VehicleChassis"",
                        ""kamaz_4310_cargo_01_door"": ""VehicleChassis"",
                        ""kamaz_4310_cargo_03_door"": ""VehicleChassis"",
                        ""kamaz_4310_garbage_door"": ""VehicleChassis"",
                        ""kamaz_4310_mixer_door"": ""VehicleChassis"",
                        ""kamaz_4310_police_door"": ""VehicleChassis"",
                        ""kamaz_4310_tent_door"": ""VehicleChassis"",
                        ""kamaz_4310_tent_closed_door"": ""VehicleChassis"",
                        ""kamaz_4310_tipper_door"": ""VehicleChassis"",
                        ""kamaz_4310_door"": ""VehicleChassis"",
                        ""kamaz"": ""StructuralSteel"",
                        // appliances in thick-sheet clothing: a reel-to-reel deck
                        // (anonymous colliders under Ballistic/Recorder) and an air
                        // filter unit are housing tin, not plate
                        ""recorder"": ""MetalThin"",
                        ""airfilter"": ""MetalThin"",
                        ""fridge"": ""MetalThin"",
                        // a roller shutter is a curtain of rolled slats — 0.8-1.2 mm
                        // apiece, sheet and nothing like the 10 mm the plain-69 anchor
                        // was handing it. Deliberately NOT a name in DoorNames: a
                        // shutter is one layer, not a leaf of two skins. Its guide
                        // rails come along with the word, and they are a strip at the
                        // edge nobody takes cover behind
                        ""rollete_gate"": ""MetalThin"",
                        // blast doors and gates of the bunkers are not sheet-metal
                        // carpentry — a hermetic door is a machine, and nothing a
                        // rifle carries opens one. NOT bare 'bunker': the bunkers'
                        // interior shells (walls, ladders, halls) wear the same
                        // material and are the building-shell class, not doors
                        ""bunker_door"": ""Machinery"",
                        ""door_bunker"": ""Machinery"",
                        ""bunkerbig_gate"": ""Machinery"",
                        // the closed UAZ van's merged body collider is suffixed
                        // bare '_metal' — no alias (the word is ambiguous by
                        // design), so it fell through to the raw preset and the
                        // vehicle taxonomy read a minivan as truck chassis plate.
                        // Its own name carries the identity; the open variant's
                        // panels reach VehicleChassis through the suffix already
                        ""uaz_buhanka"": ""VehicleChassis"",
                        // the Hyundai forklift: same heavy-plant ruling as the JCB
                        // and the roller (structural plate, not Machinery — its one
                        // collider spans the whole machine, cab included)
                        ""loader_hyundai"": ""StructuralSteel"",
                        // a cast-iron heating radiator is ~4 mm walls of iron plus a
                        // water column — a pistol dies in it, a rifle crosses with
                        // loss, which is what GunSafe's 4 mm shell already prices.
                        // Three exact names, NOT bare 'radiator': the Heating_
                        // Radiator_Set parent hangs over a whole family of PIPES,
                        // and the ancestor climb would turn their flanges into
                        // safes ('radiator_01' also covers reserve_radiator_01;
                        // the thin-tagged Chalet/SummerHotel panels stay sheet)
                        ""radiator_01"": ""GunSafe"",
                        ""radiator_03"": ""GunSafe"",
                        ""radiator_set2_city"": ""GunSafe"",
                        // Streets' heavy plant, same decision as on the thin
                        // material: structural plate, not Machinery (the Hyundai
                        // lesson) and not 10 mm armour. The roller's thin-tagged
                        // cab stays sheet, like a Kamaz door
                        ""jcb"": ""StructuralSteel"",
                        ""paver_ballistic"": ""StructuralSteel"",
                        ""road_roller"": ""StructuralSteel"",
                        // ('metalthin' used to live here — ~1100 instances, nearly
                        // every metal door in the game, priced as 10 mm plate because
                        // their MaterialType says thick. The suffix layer reads that
                        // word for every material now, so the rule is gone)
                        //
                        // the Terrakot mall's exterior faces wear MetalThick — a
                        // 10 mm shell an AP rifle crosses, i.e. a shoot-through
                        // BUILDING. Masonry like its sibling faces: the measured
                        // chord (metres of building) makes it the stop it should be
                        ""terrakot_outdoor"": ""Concrete"",
                        // a dumpster full of construction debris: the fill decides,
                        // and only the corners — where the chord is short — give
                        ""garbage_container"": ""Sand"" } },

    // The wall of a shipping container: corrugated Corten, 1.6 mm (doors 2.0 — the one
    // number undershoots them slightly). A shell like the MetalThin it is carved from.
    ""ContainerSteel"": { ""Mechanism"": ""steel"", ""Ricochet"": ""Hard"", ""Anchors"": { ""4"": 1.6 } },
    // A gun safe: a hollow steel box with ~4 mm walls, paid at entry and exit. Stops
    // pistol fire inside, rifle AP goes through — which is what the real cabinet does.
    ""GunSafe"":      { ""Mechanism"": ""steel"", ""Ricochet"": ""Hard"", ""Anchors"": { ""4"": 4 } },
    // Dense machinery: engines, transformers, switchgear, cast hatches, rails, columns.
    // Solid — the measured chord is believed as steel, which is the point: nothing
    // man-portable shoots through a turbine. The anchor is only the fallback for a
    // failed probe, and it is set thick for the same reason.
    ""Machinery"":    { ""Mechanism"": ""steel"", ""Ricochet"": ""Hard"", ""Solid"": true,
                      ""Anchors"": { ""0"": 50 } },
    // Structural plate: stair stringers, checker-plate treads, machine frames — the
    // 6-10 mm class between a car body and armour. A shell: the members are plate,
    // the space between them is air.
    ""StructuralSteel"": { ""Mechanism"": ""steel"", ""Ricochet"": ""Hard"", ""Anchors"": { ""0"": 8 } },
    // A vehicle's flank: a 0.8-1.0 mm outer panel, a 0.7-1.0 mm inner panel, and the
    // window mechanism, intrusion beam or seat frame in between — an effective 3 mm of
    // steel per side. A SHELL, like the sheet it is carved out of: a car's collider is
    // the car, so crossing the whole body (a chord over ShellCavityMm) pays both sides,
    // 6 mm in all. That reproduces the anchor it was chosen for: a 9x19 ball's limit
    // against one flank comes out at 281 m/s, so over a pistol's useful range the near
    // door is roughly even odds and what does get through is nearly spent, while the far
    // side stops it outright — and rifle ball crosses the whole car with two thirds of
    // its speed. Reached by taxonomy rather than by name, so it covers every
    // car, van, bus, truck cab and tram, on maps that do not exist yet included.
    ""VehicleChassis"": { ""Mechanism"": ""steel"", ""Ricochet"": ""Hard"", ""Anchors"": { ""4"": 3 } },
    // A cable drum: tightly wound copper and insulation. Dense enough that a rifle
    // ball dies inside a full drum; copper does not shatter what hits it.
    ""Cable"":        { ""Mechanism"": ""poncelet"", ""StrengthMPa"": 8, ""DensityGCm3"": 2.50, ""HardnessHv"": 60, ""Solid"": true,
                      ""Ricochet"": ""Soft"", ""Anchors"": { ""0"": 300 } },

    // --- Wood. Pine: 6 MPa across the grain, 0.50 g/cm3 dry. ---
    // Boards, plywood, crates.
    // A shell, like MetalThin and for the same reason: ""thin"" names a board, and things
    // built out of boards are hollow. Every object carrying this on a raided map was a
    // cabinet, a nightstand, a weapon box, a pallet, a door, a window or a handrail, and
    // the collider measured 50-96 mm (once 639) where the board is twenty.
    // DoorLeafMm: a wooden door is ~50 mm of wood — a FIXED thickness, not the
    // measured chord: leaf colliders run 100-200 mm deep, and a chord read as
    // timber made every wooden door a safe. Not one 20 mm board either.
    ""WoodThin"":  { ""Mechanism"": ""poncelet"", ""StrengthMPa"": 6, ""DensityGCm3"": 0.50, ""HardnessHv"": 3,
                   ""Ricochet"": ""Wood"", ""Anchors"": { ""3"": 20 },
                   ""DoorLeafMm"": 50,
                   ""NameOverrides"": {
                     // firewood billets: a 300-400 mm round log carrying the 20 mm
                     // board material — the one WoodThin family that is solid timber
                     ""poleno"": ""WoodThick"",
                     ""firewood"": ""WoodThick"",
                     // the wooden faces of palletised carton cargo — boxes with goods
                     // in them (both of BSG's spellings: the mall pallets drop the 'd')
                     ""box_carton"": ""BoxCargo"",
                     ""pallet_cardboard"": ""BoxCargo"",
                     ""pallet_carboard"": ""BoxCargo"" } },
    // MedPen 10 is a solid door; the plain 25 is timber — a log or a stack of beams,
    // which is where a pistol bullet finally stops.
    //
    // Stays SOLID: logs, piles, stumps, statues and live trees measure honestly, and
    // the player's ruling keeps closed crates opaque — an ammo box full of shells is
    // steel, whatever the boards around it say. The overrides carve out what is
    // provably boards-around-air. Bare 'table' was tried and burned: it catches the
    // WarehouseVege*table*.
    ""WoodThick"": { ""Mechanism"": ""poncelet"", ""StrengthMPa"": 6, ""DensityGCm3"": 0.50, ""HardnessHv"": 3, ""Solid"": true,
                   ""Ricochet"": ""Wood"", ""Anchors"": { ""10"": 45, ""25"": 200 },
                   ""NameOverrides"": {
                     // the player's ruling, shielded from the suffix layer: a closed
                     // ammunition crate is a box full of shells, and the 'metalthick'
                     // its own name carries would otherwise turn the boards into a
                     // shell with air behind them. Identity is final, which is the
                     // whole reason this rule exists — it names the material it
                     // already has
                     ""ammobox"": ""WoodThick"",
                     // the chalet's building shell: solid-by-measure read its outline
                     // as 3-17 METRES of timber and made the building a bunker. A log
                     // wall is one 250 mm layer per face, which is what a shell pays
                     ""chalet_inside"": ""TimberWall"",
                     // ('woodthin' used to live here — nine props declaring the thin
                     // material in their own name while carrying the thick one. The
                     // suffix layer says it for every material now)
                     ""weapon_box"": ""WoodThin"",
                     // small crates: every sampled instance parented under woodBox_small
                     ""medpen"": ""WoodThin"",
                     ""flowerbed"": ""WoodThin"",
                     ""table_wood"": ""WoodThin"",
                     ""dry_bush"": ""GrassLow"" } },

    // A log building's wall: one 250 mm timber layer per face of the building shell.
    // A SHELL, unlike the WoodThick it is carved from — the chalet's collider is the
    // whole building, and believing that chord made it a bunker. 250 mm of pine
    // stops pistol fire and passes rifle fire slowed, which is what log walls do.
    ""TimberWall"": { ""Mechanism"": ""poncelet"", ""StrengthMPa"": 6, ""DensityGCm3"": 0.50, ""HardnessHv"": 3,
                   ""Ricochet"": ""Wood"", ""Anchors"": { ""0"": 250 } },

    // --- Soft bulk ---
    // Corrugated board: flat-crush around 0.3 MPa, 0.1 g/cm3. MedPen 11 is a stack.
    ""Cardboard"":    { ""Mechanism"": ""poncelet"", ""StrengthMPa"": 0.3, ""DensityGCm3"": 0.10,
                      ""Ricochet"": ""None"", ""Anchors"": { ""0"": 5, ""11"": 300 },
                      ""NameOverrides"": {
                        // palletised carton cargo is boxes WITH goods, not air in
                        // paper (both of BSG's spellings — the mall pallets drop
                        // the 'd'; a Reserve raid caught the two resolving apart)
                        ""box_carton"": ""BoxCargo"",
                        ""pallet_cardboard"": ""BoxCargo"",
                        ""pallet_carboard"": ""BoxCargo"" } },
    ""GarbagePaper"": { ""Mechanism"": ""poncelet"", ""StrengthMPa"": 0.2, ""DensityGCm3"": 0.15,
                      ""Ricochet"": ""None"", ""Anchors"": { ""0"": 10 } },
    // Cloth. HiPen 62 is the one preset nobody has identified on a map yet, so it is
    // read as a thick bale rather than invented into a wall — see MODEL.md.
    //
    // The overrides pull out what only WEARS cloth: sandbags (1757 instances — sand,
    // not fabric), mattresses (BSG spells it both ways, hence two entries), rubble
    // sacks, and upholstered furniture, whose padding resists like low-density bulk
    // rather than like a curtain.
    ""Fabric"":       { ""Mechanism"": ""poncelet"", ""StrengthMPa"": 0.05, ""DensityGCm3"": 0.15,
                      ""Ricochet"": ""None"", ""Anchors"": { ""0"": 3, ""3"": 30, ""62"": 400 },
                      ""NameOverrides"": {
                        ""sandbag"": ""Sand"",
                        ""mattress"": ""Upholstery"", ""matress"": ""Upholstery"",
                        ""sack"": ""Sand"",
                        ""sofa"": ""Upholstery"", ""couch"": ""Upholstery"",
                        ""armchair"": ""Upholstery"", ""coach"": ""Upholstery"",
                        // BSG's own density flags: ~650 bare 'Fabric_MedPen'/'_HiPen'
                        // colliders say 'denser than cloth' with nothing else to go
                        // on — padding is the honest reading. Listed AFTER sandbag,
                        // so 'military_Sandbag1_HiPen' stays sand
                        ""medpen"": ""Upholstery"", ""hipen"": ""Upholstery"",
                        // the tarps over palletised cargo: 3 mm of cloth in front of
                        // the same stack the wood and cardboard faces already price
                        ""pallet_cardboard"": ""BoxCargo"",
                        ""pallet_carboard"": ""BoxCargo"",
                        ""pallet_weapon_box"": ""BoxCargo"" } },
    ""GenericSoft"":  { ""Mechanism"": ""poncelet"", ""StrengthMPa"": 1, ""DensityGCm3"": 0.40, ""HardnessHv"": 1, ""Solid"": true,
                      ""Ricochet"": ""Soft"", ""Anchors"": { ""0"": 30 },
                      ""NameOverrides"": {
                        // the one awning in the game whose material is a measured
                        // solid; every other tent skin already sits on a shell
                        ""awning"": ""Fabric"" } },

    // --- Palletised cargo: a carrier and its contents, not an average of the two ---
    //
    // A pallet of boxes is two materials at once. The STACK is cardboard around air —
    // clip a corner of it and it must cost about what a cardboard box costs. What is IN
    // the boxes is packed goods, and a shot down the long axis of a loaded pallet meets
    // a lot of it. The book used to hand both jobs to GenericSoft, one homogeneous solid
    // at 0.40 g/cm3 over the whole measured chord, and that gets both ends wrong at
    // once: a corner clip stopped rifle rounds, and the long axis was no worse than the
    // short one. It was also perfectly deterministic, which stacked cargo is not.
    //
    // So the carrier is crossed continuously and the contents are met discretely. Every
    // ""SpacingMm"" of path the projectile either runs into a package or does not
    // (""Chance""), and a package is ""ContentFraction"" of that layer thick. The
    // package's thickness is tied to the spacing on purpose: the EXPECTED cargo per
    // metre of path is fraction*chance — 15% here — whatever the spacing is, so the
    // spacing is a grain size and not a strength knob. It is not perfectly neutral
    // (every layer boundary asks the yaw question again, so finer slicing costs a little
    // more); MODEL.md measures the drift and calls it a weak lever.
    //
    // BoxCargo: a stack of empty cartons, near enough to air. 0.1 MPa and 0.03 g/cm3 is
    // corrugated board at the volume fraction a stack of boxes actually has. Solid — the
    // measured chord IS the path, which is what makes a corner cheap and a long axis
    // expensive without a rule saying so. No ricochet: cardboard turns nothing away,
    // exactly as the Cardboard entry it is carved out of.
    ""BoxCargo"":     { ""Mechanism"": ""poncelet"", ""StrengthMPa"": 0.1, ""DensityGCm3"": 0.03, ""HardnessHv"": 1, ""Solid"": true,
                      ""Ricochet"": ""None"", ""Anchors"": { ""0"": 30 },
                      ""Stack"": { ""SpacingMm"": 300, ""Content"": ""BoxContent"",
                                 ""ContentFraction"": 0.3, ""Chance"": 0.5 } },
    // What is in the boxes: packed goods at 1 MPa and 0.40 g/cm3 — the density the whole
    // pallet used to be given, now carried by the fraction of it that earns it. Reached
    // only through BoxCargo's Stack, never off a collider, so its anchor is nothing but
    // the fallback for a layer whose thickness the loop could not set.
    ""BoxContent"":   { ""Mechanism"": ""poncelet"", ""StrengthMPa"": 1, ""DensityGCm3"": 0.40, ""HardnessHv"": 1, ""Solid"": true,
                      ""Ricochet"": ""None"", ""Anchors"": { ""0"": 90 } },

    // Dry sand and rubble fill: almost no cohesion, all friction and grain-shattering
    // under confinement — the effective strength is what puts a rifle ball at
    // 250-350 mm, one bag on the edge and two bags proof against everything
    // man-portable. Hardness is quartz: sand chews bullets.
    ""Sand"":         { ""Mechanism"": ""poncelet"", ""StrengthMPa"": 10, ""DensityGCm3"": 1.60, ""HardnessHv"": 700, ""Solid"": true,
                      ""Ricochet"": ""Soft"", ""Anchors"": { ""0"": 250 } },
    // Upholstered padding: foam, springs, batting, the odd frame member averaged in.
    // Dense enough that a pistol round dies somewhere inside a couch and a rifle
    // round crosses it slowed — which is what couches do.
    ""Upholstery"":   { ""Mechanism"": ""poncelet"", ""StrengthMPa"": 1, ""DensityGCm3"": 0.20, ""Solid"": true,
                      ""Ricochet"": ""None"", ""Anchors"": { ""0"": 300 } },
    ""Snow"":         { ""Mechanism"": ""poncelet"", ""StrengthMPa"": 0.3, ""DensityGCm3"": 0.35, ""Solid"": true,
                      ""Ricochet"": ""Soft"", ""Anchors"": { ""0"": 150 } },

    // --- Hard bulk ---
    // Props and machinery casings.
    ""GenericHard"": { ""Mechanism"": ""poncelet"", ""StrengthMPa"": 25, ""DensityGCm3"": 1.50, ""HardnessHv"": 100, ""Solid"": true,
                     ""Ricochet"": ""Hard"", ""Anchors"": { ""11"": 30 } },
    // Rigid sheet plastic (PVC, ABS).
    // DoorLeaf skins: a plastic door is a hollow leaf of two faces, like thin steel.
    ""Plastic"":     { ""Mechanism"": ""poncelet"", ""StrengthMPa"": 45, ""DensityGCm3"": 1.20, ""HardnessHv"": 15,
                     ""DoorLeaf"": ""skins"",
                     ""Ricochet"": ""Hard"", ""Anchors"": { ""1"": 5 },
                     ""NameOverrides"": {
                       // palletised cargo: the crate is plastic, the contents are goods
                       ""polythene_box"": ""BoxCargo"",
                       // the Labs recreation planters bake pl100 into their own name;
                       // a planter is a hard box full of soil, not a PVC panel
                       ""flowerbed"": ""GenericHard"",
                       // a cable drum's plastic rim wraps wound copper; the Factory
                       // spool's collider is an anonymous 'plastic' under
                       // kabel_pallet2, reached through ancestor matching
                       ""cable_drum"": ""Cable"",
                       // ('chainfence' used to live here for the construction
                       // scaffolding — tube-and-gap, mostly air, tagged PVC. The
                       // suffix layer reads that word on every material now)
                       ""kabel_pallet"": ""Cable"" } },
    // A tyre, and a shell for the same reason a barrel is one: every object carrying
    // this material on a raided map was a loader wheel (Loader_01_BALLISTIC_rubber,
    // Loader_small_01_BALLISTIC_rubber), and a wheel's collider is the whole wheel —
    // most of a metre of air with a wall of rubber at each end. Read as solid it is
    // unshootable; read as a shell the bullet pays for tread going in and tread coming
    // out, which is what a tyre is. The two levels are the designer saying which wheel:
    // 0 the small loader's, 2 the big one's, and the wall follows the size — a car
    // tread runs about 12 mm, an industrial tyre 25-35 with its plies.
    ""Rubber"":      { ""Mechanism"": ""poncelet"", ""StrengthMPa"": 15, ""DensityGCm3"": 1.20, ""HardnessHv"": 5,
                     ""Ricochet"": ""Soft"", ""Anchors"": { ""0"": 15, ""2"": 28 } },
    // Fired clay roof tile.
    ""Tile"":        { ""Mechanism"": ""poncelet"", ""StrengthMPa"": 40, ""DensityGCm3"": 1.90, ""HardnessHv"": 400,
                     ""Ricochet"": ""Hard"", ""Anchors"": { ""1"": 15 } },
    // Loose aggregate: almost no cohesion, so nearly all of the resistance is inertial.
    ""Pebbles"":     { ""Mechanism"": ""poncelet"", ""StrengthMPa"": 1, ""DensityGCm3"": 1.60, ""HardnessHv"": 700, ""Solid"": true,
                     ""Ricochet"": ""Soft"", ""Anchors"": { ""2"": 60, ""5"": 150 } },

    // --- Through, for a symbolic price ---
    // A pane is fractured out rather than crushed through, so what it costs hardly
    // depends on what is doing it — but it is not nothing, or a shot column would cross
    // a window free.
    // Overrides, in order: 'chunk' and 'broken' are identity rules — debris shards
    // stay plain glass and must be claimed BEFORE 'glass_block' can match a broken
    // block's name. Both spellings of the block exist in the scenes.
    ""Glass"":          { ""Mechanism"": ""always"", ""CostJ"": 15, ""DensityGCm3"": 2.50, ""HardnessHv"": 550,
                      ""Ricochet"": ""None"", ""Anchors"": { ""0"": 4 },
                      ""NameOverrides"": {
                        ""chunk"": ""Glass"", ""broken"": ""Glass"",
                        ""glass_block"": ""GlassBlock"", ""glassblock"": ""GlassBlock"",
                        ""armored_glass"": ""ArmoredGlass"" } },
    // A glass-block wall: the block is itself hollow, ~10 mm of glass at each face,
    // so it is a shell with a glass wall rather than 120 mm of solid glass. A pistol
    // round usually dies in it, a rifle round goes through with a real loss — which
    // is what the block wall does on camera.
    ""GlassBlock"":     { ""Mechanism"": ""poncelet"", ""StrengthMPa"": 40, ""DensityGCm3"": 2.50, ""HardnessHv"": 550,
                      ""Ricochet"": ""None"", ""Anchors"": { ""0"": 20 } },
    // A bank teller's screen: laminated security glass, BR4-class. Stops handguns,
    // rifle rounds defeat it — the boundary every published rating puts there.
    ""ArmoredGlass"":   { ""Mechanism"": ""poncelet"", ""StrengthMPa"": 100, ""DensityGCm3"": 2.50, ""HardnessHv"": 550, ""Solid"": true,
                      ""Ricochet"": ""Hard"", ""Anchors"": { ""0"": 40 } },
    ""GlassShattered"": { ""Mechanism"": ""always"", ""CostJ"": 8, ""DensityGCm3"": 2.50, ""HardnessHv"": 550,
                      ""Ricochet"": ""None"", ""Anchors"": { ""0"": 4 } },
    ""Chainfence"":     { ""Mechanism"": ""always"", ""CostJ"": 0,  ""Ricochet"": ""None"" },
    ""GrassLow"":       { ""Mechanism"": ""always"", ""CostJ"": 0,  ""Ricochet"": ""None"" },
    ""Mud"":            { ""Mechanism"": ""always"", ""CostJ"": 0, ""DensityGCm3"": 1.80, ""Ricochet"": ""Soft"" },

    // --- Masonry ---
    // Concrete AND brick: the game puts this one material on both, and the walls named
    // ..._bricks_01_BALLISTIC_concrete are brick. Modelled as ordinary structural
    // concrete, which reads a brick wall as a little tougher than it is — the price of
    // one preset for two materials, noted in MODEL.md.
    //
    // The strength is not a guess. Forrestal's cavity-expansion fit for concrete gives a
    // resistance R = S(f'c)·f'c with S = 82.6·f'c^-0.544, so an ordinary f'c of 30 MPa
    // gives R = 389 MPa; the entry carries R divided by the global ConfinementFactor of
    // 5, because the depth law multiplies the two back together. Checked against a
    // published test rather than left on the derivation: 120 mm of UHPFRC (f'c ~150 MPa)
    // takes 55 mm from a 7.62 ball, and the same law at that strength says 57.
    //
    // Hardness is the AGGREGATE's, not the paste's — a bullet meets stone in the first
    // millimetre, which is why lead-core ball shatters on concrete and hard cores do not.
    // The anchor is only the fallback for a collider that cannot be measured; every
    // concrete surface seen on a map so far carries level 100, so one anchor is all the
    // level can select.
    ""Concrete"":   { ""Mechanism"": ""poncelet"", ""StrengthMPa"": 78, ""DensityGCm3"": 2.35, ""HardnessHv"": 500,
                    ""Solid"": true, ""SpallFactor"": 1.3,
                    ""Ricochet"": ""Hard"", ""Anchors"": { ""100"": 200 },
                    ""NameOverrides"": {
                      // a kerbstone ('porebrick', поребрик) is cast concrete that
                      // happens to spell 'brick' inside its name — the shield must
                      // outrank the family word below (a raid caught the kerb
                      // pricing as brick)
                      ""porebrick"": ""Concrete"",
                      ""brick"": ""Brick"",
                      // Factory's interior walls are visibly brick under the plaster —
                      // a raid put an eyeball on them; the _bricks_ names above are
                      // the same family saying it out loud
                      ""inside_wall"": ""Brick"",
                      // the same debris dumpster also ships with a concrete material
                      ""garbage_container"": ""Sand"" } },

    // Brick is not a MaterialType the game has — it is the half of Concrete that the
    // level author wrote into the object's name instead
    // (Area_01_inside_wall_C_bricks_01_BALLISTIC_concrete). Nothing reaches this entry
    // unless a name says so, so a map that names its walls differently keeps the
    // concrete numbers and loses nothing it had.
    //
    // Fired clay brick is the weaker material and the lighter one: 15-25 MPa against
    // concrete's 30, and 1.9 g/cm3 against 2.35. Same route to the strength — R = S·f'c
    // with S = 82.6·f'c^-0.544 at f'c = 20 MPa gives 324 MPa, carried here as R over the
    // global confinement of 5. Applying a concrete fit to brick is an extrapolation, and
    // MODEL.md says so; what it is not is a free parameter. Hardness is fired clay's,
    // the same 400 the roof tile carries, and lower than concrete's stone aggregate —
    // which is why a bullet survives brick better than it survives concrete.
    //
    // The anchor is the common half-brick partition, and only a fallback: a brick wall
    // has two faces and gets measured like anything else.
    ""Brick"":      { ""Mechanism"": ""poncelet"", ""StrengthMPa"": 65, ""DensityGCm3"": 1.90, ""HardnessHv"": 400,
                    ""Solid"": true, ""SpallFactor"": 1.3,
                    ""Ricochet"": ""Hard"", ""Anchors"": { ""100"": 120 } },

    // --- Walls ---
    // The engine carries no thickness for these and the scene cannot supply one either:
    // ground and road surfaces have no far face to measure, so there is nothing for the
    // depth law to be compared against. The overrides pull out the PROPS stranded on
    // these ground materials — objects with two faces that measure cleanly. Broad
    // 'rock'/'stone' words were tried and rejected: they catch terrain cliffs with
    // 10-52 m norms, which would make ground locally shootable.
    ""Stone"":      { ""Mechanism"": ""never"", ""Ricochet"": ""Hard"",
                    ""NameOverrides"": {
                      // street curbs ARE concrete; 1353 instances at a clean 130 mm
                      ""curb"": ""Concrete"",
                      // stone facings: planters, garden fences, the well — masonry,
                      // and one parent literally calls itself Masonry
                      ""flowerbed"": ""Brick"", ""fence"": ""Brick"",
                      ""waterwell"": ""Brick"" } },
    ""Asphalt"":    { ""Mechanism"": ""never"", ""Ricochet"": ""Hard"" },
    ""Soil"":       { ""Mechanism"": ""never"", ""Ricochet"": ""Soft"",
                    ""NameOverrides"": {
                      // 8 names / 1687 instances of sandbag barricades carrying the
                      // terrain material; every survey sample measured 120-950 mm
                      ""sandbag"": ""Sand"",
                      // railway track carrying the terrain material: a stop either
                      // way (Soil is a never-wall), but steel bounces hard, not
                      // soft. 'railway_rail' on purpose — bare 'rail' catches
                      // handrails and trailers (the census burned it)
                      ""railway_rail"": ""Machinery"" } },
    ""SoilForest"": { ""Mechanism"": ""never"", ""Ricochet"": ""Soft"" },
    ""Gravel"":     { ""Mechanism"": ""never"", ""Ricochet"": ""Soft"",
                    ""NameOverrides"": {
                      // BSG's own spelling: a crushed-concrete rubble barricade
                      ""crushed_concreate"": ""Concrete"",
                      ""rock_pile"": ""Pebbles"" } },

    // --- Left to the game, on purpose ---
    // Water: vanilla already sends a bullet through it with a huge deflection, which is
    // a fair reading of a medium we do not know the depth of. The bounce IS ours — a
    // seven-degree critical angle is one of the best-measured numbers in the whole
    // ricochet literature.
    ""Water"":       { ""Mechanism"": ""vanilla"", ""Ricochet"": ""Water"" },
    ""WaterPuddle"": { ""Mechanism"": ""vanilla"", ""Ricochet"": ""Water"" },
    // A grating is a question about whether the bullet went between the bars. That is
    // honestly stochastic geometry and vanilla's roll is a fair model of it. The
    // campaign named its carriers: floor grates (284), boardwalk grids, stair ramps,
    // the KUKA cage in Labs.
    ""Grate"":       { ""Mechanism"": ""vanilla"", ""Ricochet"": ""Vanilla"" },
    // Tall grass: the campaign showed this is the TERRAIN's own collider (chords of
    // 24-490 metres on Shoreline). Vanilla's 0/0 settings stay untouched.
    ""GrassHigh"":   { ""Mechanism"": ""vanilla"", ""Ricochet"": ""Vanilla"" },
    // The campaign finally found its carriers, and they are not tyres: two dirty
    // pickup HULLS on Streets and a baggage cart's wheel. The name rules say what the
    // object actually is; anything else that ever shows up stays vanilla.
    ""Tyre"":        { ""Mechanism"": ""vanilla"", ""Ricochet"": ""Vanilla"",
                    ""NameOverrides"": {
                      ""hull"": ""MetalThin"",
                      ""baggage_cart"": ""Rubber"" } },
    // The default collider an object with no ballistic setup gets — and, the census
    // shows, the dumping ground for BSG's own typos: 'metaltin', 'metathin',
    // 'chainfance', 'fabrick', 'concete' all fail the engine's suffix parse and land
    // here as 'impenetrable', taking lamp posts (454 instances), a car trunk and a
    // wood stove with them. Most of that block is now the suffix layer's work — the
    // misspellings live in SuffixAliases, where every material benefits from them —
    // and what is left here is what the layer CANNOT do:
    //   - words that are not suffixes at all ('post', 'rubble', 'metall', and the
    //     'concrete' whose 8 of 9 carriers say it in the middle of the name);
    //   - shields that must beat the suffix ('loader_small' / 'loader_01' — the
    //     counterweight's own name says metalthic and it is cast iron);
    //   - dead branches kept as insurance for maps we have not seen.
    // DefaultBallisticCollider matches none of it and stays what it must be: not a
    // material, the absence of one.
    ""None"":        { ""Mechanism"": ""vanilla"", ""Ricochet"": ""Vanilla"",
                    ""NameOverrides"": {
                      // the typo'd loader counterweight must resolve to the same cast
                      // iron as its correctly-spelled twin, and identity is final, so
                      // the suffix on its own name never gets to answer
                      ""loader_small"": ""Machinery"", ""loader_01"": ""Machinery"",
                      ""metalthick"": ""MetalThick"", ""metalthic"": ""MetalThick"",
                      ""metalthin"": ""MetalThin"",
                      ""metall"": ""MetalThin"", ""post"": ""MetalThin"",
                      ""rubble"": ""MetalThin"",
                      ""concrete"": ""Concrete"",
                      ""glass"": ""Glass"" } },
    // Bodies, corpses and worn equipment are the wound and armour models' business and
    // reach this module only on props. Not ours.
    ""Body"":           { ""Mechanism"": ""vanilla"", ""Ricochet"": ""Vanilla"" },
    ""BodyArmor"":      { ""Mechanism"": ""vanilla"", ""Ricochet"": ""Vanilla"" },
    ""Helmet"":         { ""Mechanism"": ""vanilla"", ""Ricochet"": ""Vanilla"" },
    ""HelmetRicochet"": { ""Mechanism"": ""vanilla"", ""Ricochet"": ""Vanilla"" },
    ""GlassVisor"":     { ""Mechanism"": ""vanilla"", ""Ricochet"": ""Vanilla"" },
    ""MetalNoDecal"":   { ""Mechanism"": ""vanilla"", ""Ricochet"": ""Vanilla"" },
    ""Swamp"":          { ""Mechanism"": ""vanilla"", ""Ricochet"": ""Vanilla"" }
  },

  // Bump this when a shipped figure CHANGES. On a bump the file is rewritten and the
  // old one kept as a .bak beside it.
  //
  // 1 — first edition.
  // 2 — Rubber is a tyre: a shell rather than a solid, with a wall per wheel size.
  // 3 — Concrete (and the brick that shares its preset) is penetrable, by the measured
  //     collider and a Forrestal-derived strength; the PenetrationLevel 100 override is
  //     gone, so a plastic tote carrying it is no longer a wall.
  // 4 — Brick is its own material, selected off the object's name.
  // 5 — The prop-survey reference: MetalThick is a shell with Machinery carved out
  //     solid by name; MetalThin at 1.0 mm with gunsafe/chainfence/container rules;
  //     new Sand, Upholstery, ContainerSteel, GunSafe, Machinery, GlassBlock,
  //     ArmoredGlass; sandbags, curbs, masonry, firewood, glass blocks and BSG's
  //     None-typos all routed to what they are. Full evidence:
  //     .claude/docs/OBSTACLE-PROP-SURVEY.md.
  // 6 — Wood has its own ricochet class (15 deg — one Soft class shared with soil
  //     was why tables mirrored P90 fire), and bare Fabric_MedPen/_HiPen colliders
  //     read the designer's density flag as padding.
  // 7 — Raid-check corrections: Factory inside_wall is brick, the debris dumpster is
  //     its fill, palletised cargo (polythene_box, box_carton) is goods rather than
  //     empty boxes, cable drums are wound copper (new Cable), metal stairs and
  //     loader chassis are structural plate (new StructuralSteel, 8 mm), loader
  //     counterweights are cast iron.
  // 8 — Second raid-check: rules now match the collider's ANCESTORS when its own
  //     name fires nothing (a BTR is three boxes called 'MetalThick'); BTR and T90
  //     are armour, Kamaz cabs and fridges are sheet rather than the plain-69
  //     10 mm, pallet_cardboard cargo is goods, the Hyundai forklift rule is
  //     withdrawn (its one collider spans the whole machine).
  // 9 — Audit pass over six maps of survey data: the T-90 hull spells itself T_90A
  //     and slipped the 't90' rule (both spellings now held); Tigr, Typhoon and
  //     Stryker join the armoured set; steel drums are 1.2-1.5 mm sheet, not the
  //     plain-69 10 mm; Labs planters, heavy plant (Caterpillar, Kirovets) and the
  //     cloth tarps over palletised cargo priced as what they are.
  // 10 — Lighthouse raid-check: the chalet is a log building, not a bunker (new
  //      TimberWall shell); locomotive with its bogies and wheelsets, and the BRDM
  //      on both its materials, join the machinery set; the Kamaz splits into cab
  //      doors (sheet) and structural chassis; recorder and air-filter housings are
  //      tin; flotation tanks, structural framing ('constraction') and facility
  //      catwalks priced as what they are.
  // 11 — The whole Kamaz family: doors of every variant (cargo, tipper, mixer,
  //      garbage, police, tent, AP-2) are cab sheet, Kamaz_Armored is armour.
  // 12 — BMP-2 joins the armoured fleet: turret and hull sat on the plain
  //      MetalThick anchor and fell to an AK in a raid.
  // 13 — Streets audit: the metalthin suffix rescue on MetalThick (~1100 colliders,
  //      nearly every metal door in the game, priced as 10 mm plate); scaffolding's
  //      chainfence suffix rescued from Plastic; the Terrakot mall's MetalThick
  //      faces to Concrete (a 10 mm shell made it a shoot-through building).
  // 14 — Streets audit, the judged half: heavy plant (JCB backhoe, asphalt paver,
  //      road roller) to structural plate; cast-iron heating radiators to the
  //      GunSafe shell (4 mm iron + water, not 10 mm armour plate).
  // 15 — Resolution stops being a list of names. Two layers under the identity
  //      rules: the designer's own ""_BALLISTIC_<word>"" suffix through
  //      SuffixAliases (~6 000 colliders whose word contradicts their MaterialType,
  //      BSG's typos among them, and the material classes no hand-written rule had
  //      reached — Case_shower, the Labs cells, Metal_sling_loop, MetalNoDecal), and
  //      the scene's own taxonomy through Taxonomy (VEHICLES nodes plus a census-built
  //      list of model words: ~9 300 vehicle colliders). New material VehicleChassis,
  //      3 mm per side, which is what a car flank is and what the vehicle map now
  //      hands every car, van, bus, truck cab and tram — Kamaz cab doors included.
  //      DOORS nodes make a door leaf pay its two skins instead of one. Fifteen
  //      identity rules the suffix layer reproduces are gone.
  // 16 — Reserve smoke raid of the layers: the closed UAZ van's bare '_metal'
  //      collider named by identity (a minivan is not chassis plate); the Hyundai
  //      forklift joins the heavy-plant ruling; a kerbstone ('porebrick') shielded
  //      from the brick family word; the mall pallets' missing-'d' spelling; rail
  //      track on terrain material bounces hard.
  // 17 — A door leaf is what its MATERIAL says a leaf is, not blanket two skins:
  //      only sheet that cannot carry itself laminates (thin steel, plastic — the
  //      new per-material DoorLeaf: skins); a wooden door is its full depth of wood
  //      (DoorLeaf: solid, the chord is the path); a thick steel door is one plate,
  //      as before the doors rule existed.
  // 18 — Two corrections to 17: a wooden leaf's chord is the COLLIDER's depth
  //      (100-200 mm), not the wood's — reading it as timber made every wooden door
  //      a safe, so a leaf now has a FIXED thickness (DoorLeafMm 50). And the
  //      bunkers' blast doors and gates are machines, not sheet-metal carpentry.
  // 19 — A thick-steel door is 5 mm, an armoured door rather than a vault one: the
  //      plain-69 anchor was pricing 1 716 ordinary entrance, interior and garage
  //      doors as the heaviest steel in the game.
  // 20 — Barriers destabilise what goes through them: new YawGainK and YawObliquityK.
  //      A row of barrels used to cost the same at the fifth wall as at the first,
  //      because a rigid core came out of thin sheet exactly as it went in. Now a
  //      crossing hands the projectile yaw, and the next barrier meets more of it —
  //      more drag, a higher ballistic limit, more deflection.
  // 21 — Palletised cargo stops being one averaged solid. New BoxCargo (the stack of
  //      boxes, near enough to air) with a Stack block drawing BoxContent (the packed
  //      goods) every 300 mm of path, and the five pallet rules on Cardboard, WoodThin,
  //      Fabric and Plastic point at it instead of GenericSoft. A corner clip is a
  //      cardboard box again, crossing a pallet is survivable but costly, a shot down
  //      its length usually is not — and it is a lottery, so two rounds on the same
  //      line can disagree. GenericSoft itself is untouched: its 9 459 colliders are
  //      books, garbage piles and sacks, and those really are homogeneous.
  // 22 — A leaf can say so by name (DoorNames), not only by sitting under a DOORS
  //      node: a raid found Factory's entrance gate and its wicket door each paying
  //      the 10 mm plain-69 anchor, and the wicket paying it twice over because it is
  //      a child of the leaf and their colliders overlap. Read as leaves they are
  //      5 mm each, so the doubled crossing costs the one plate it always should have.
  // 23 — The rest of the gates, all of them found on the same plain-69 anchor: swing
  //      gates, the PTOR checkpoint, garage gates and transfer gateways read as the
  //      leaves they are, and roller shutters as the 1 mm curtain of slats they are.
  // 24 — The shower block's lockers are sheet again. They were the survey's own
  //      open question — 454 sheet colliders whose designer word says ""concrete"" —
  //      and a raid answered it: read as concrete slabs, a locker cost a 5.45 three
  //      quarters of its velocity across two faces of tin.
  ""Version"": 24
}
";
    }
}
