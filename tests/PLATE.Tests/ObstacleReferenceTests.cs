using System;
using System.Collections.Generic;
using System.Linq;
using EFT.Ballistics;
using PLATE.Client.Ballistics;
using Xunit;

namespace PLATE.Tests
{
    /// <summary>
    /// The shipped obstacle book. A stray comma in it would not throw anywhere a player
    /// would notice — it would quietly hand every wall back to vanilla — so the text
    /// gets parsed here, and every material the game can put on a collider has to be
    /// accounted for one way or the other.
    /// </summary>
    public class ObstacleReferenceTests
    {
        private static ObstacleReference.Book Book()
        {
            var book = ObstacleReference.Parse(ObstacleReference.DefaultJsonc);
            Assert.NotNull(book);
            return book;
        }

        [Fact]
        public void Shipped_book_parses()
        {
            var book = Book();
            Assert.NotNull(book.Globals);
            Assert.NotNull(book.Steel);
            Assert.NotNull(book.Ricochet);
            Assert.NotNull(book.Materials);
            Assert.True(book.Version >= 1);
            Assert.Equal(book.Version, ObstacleReference.ShippedVersion);
        }

        /// <summary>
        /// Every MaterialType the game has must be in the book, even if only to say it
        /// is left alone. An omission is indistinguishable at runtime from a deliberate
        /// vanilla pass, and the two need to be told apart when a new material appears
        /// in a game update.
        /// </summary>
        [Fact]
        public void Every_game_material_is_accounted_for()
        {
            var book = Book();
            var missing = Enum.GetNames(typeof(MaterialType))
                .Where(n => !book.Materials.ContainsKey(n))
                .ToList();

            Assert.True(missing.Count == 0,
                "MaterialType values with no entry in the obstacle book: " +
                string.Join(", ", missing));
        }

        [Fact]
        public void Every_mechanism_is_one_the_model_knows()
        {
            var known = new HashSet<string>
            {
                ObstacleModel.MechSteel, ObstacleModel.MechPoncelet,
                ObstacleModel.MechAlways, ObstacleModel.MechNever, ObstacleModel.MechVanilla,
            };

            var bad = Book().Materials
                .Where(kv => !known.Contains(kv.Value.Mechanism))
                .Select(kv => $"{kv.Key}={kv.Value.Mechanism}")
                .ToList();

            Assert.True(bad.Count == 0, "Unknown mechanisms: " + string.Join(", ", bad));
        }

        /// <summary>
        /// A mechanism that needs a thickness and has no anchors resolves to nothing and
        /// the material silently falls back to vanilla — the failure this whole book
        /// exists to avoid.
        /// </summary>
        [Fact]
        public void Every_thickness_mechanism_has_anchors()
        {
            var bad = Book().Materials
                .Where(kv => (kv.Value.Mechanism == ObstacleModel.MechSteel ||
                              kv.Value.Mechanism == ObstacleModel.MechPoncelet) &&
                             (kv.Value.Anchors == null || kv.Value.Anchors.Count == 0))
                .Select(kv => kv.Key)
                .ToList();

            Assert.True(bad.Count == 0, "No thickness anchors: " + string.Join(", ", bad));
        }

        /// <summary>A bulk medium with no strength or no density has no law to obey.</summary>
        [Fact]
        public void Every_poncelet_material_has_a_strength_and_a_density()
        {
            var bad = Book().Materials
                .Where(kv => kv.Value.Mechanism == ObstacleModel.MechPoncelet &&
                             (kv.Value.StrengthMPa <= 0 || kv.Value.DensityGCm3 <= 0))
                .Select(kv => kv.Key)
                .ToList();

            Assert.True(bad.Count == 0, "Poncelet materials with no material: " +
                                        string.Join(", ", bad));
        }

        [Fact]
        public void Every_named_ricochet_class_exists()
        {
            var book = Book();
            var bad = book.Materials
                .Where(kv => !string.IsNullOrEmpty(kv.Value.Ricochet) &&
                             kv.Value.Ricochet != ObstacleReference.RicochetNone &&
                             kv.Value.Ricochet != ObstacleReference.RicochetVanilla &&
                             !book.Ricochet.ContainsKey(kv.Value.Ricochet))
                .Select(kv => $"{kv.Key}={kv.Value.Ricochet}")
                .ToList();

            Assert.True(bad.Count == 0, "Undefined ricochet classes: " + string.Join(", ", bad));
        }

        /// <summary>
        /// A name override pointing at a material the book does not define would mean
        /// "vanilla" at runtime — a typo silently switching a material off.
        /// </summary>
        [Fact]
        public void Every_name_override_points_at_a_material_that_exists()
        {
            var book = Book();
            var bad = book.Materials
                .Where(kv => kv.Value.NameOverrides != null)
                .SelectMany(kv => kv.Value.NameOverrides
                    .Where(o => !book.Materials.ContainsKey(o.Value))
                    .Select(o => $"{kv.Key}[{o.Key}]={o.Value}"))
                .ToList();

            Assert.True(bad.Count == 0, "Undefined override targets: " + string.Join(", ", bad));
        }

        /// <summary>
        /// The game has one preset for concrete and brick, and the only thing that tells
        /// them apart is what the level author called the object. Weak evidence, so it
        /// only ever adds a material — anything unrecognised keeps the preset.
        /// </summary>
        [Theory]
        // the real objects, off a raid journal
        [InlineData("Concrete", "Area_01_inside_wall_C_bricks_01_BALLISTIC_concrete", "Brick")]
        [InlineData("Concrete", "Area_03_inside_wall_D_bricks_02_BALLISTIC_concrete", "Brick")]
        // a raid put an eyeball on Factory's interior walls: brick under the plaster,
        // whether the name says _bricks_ or only inside_wall
        [InlineData("Concrete", "Area_01_inside_wall_A_BALLISTIC_Concrete", "Brick")]
        [InlineData("Concrete", "Pillar_concrete_rough_01_BALLISTIC_Concrete", "Concrete")]
        // case is the author's business, not ours
        [InlineData("Concrete", "Wall_BRICK_02", "Brick")]
        // no name at all, and a material that has no overrides to apply
        [InlineData("Concrete", null, "Concrete")]
        [InlineData("MetalThin", "Brick_wall_flashing", "MetalThin")]
        // the campaign's reference, one representative per rule family — real names
        // from the survey and the census
        [InlineData("MetalThin", "metal_fence_02_BALLISTIC_chainfence", "Chainfence")]
        [InlineData("MetalThin", "container_6m_close_BALLISTIC_metalthin", "ContainerSteel")]
        [InlineData("MetalThick", "Pump_engine_BALLISTIC_metalthick", "Machinery")]
        [InlineData("MetalThick", "Railway_part_01_BALLISTIC_Metalthick", "Machinery")]
        [InlineData("MetalThick", "atm_BALLISTIC_MetalThick_HiPen", "Machinery")]
        // the words the census burned must NOT fire: a handrail is not a rail, the
        // substation shed is not its transformer
        [InlineData("MetalThick", "Platform_railing_handrail_BALLISTIC_metalthick", "MetalThick")]
        [InlineData("MetalThick", "Transformer_substation_03_L_Door_01", "MetalThick")]
        // the player's ruling: a closed ammo crate is its contents, not its boards
        [InlineData("WoodThick", "Military_AmmoBox122mm_Closed_BALLISTIC_Metalthick", "WoodThick")]
        [InlineData("WoodThick", "pallet_weapon_box_1_BALLISTIC_woodthick", "WoodThin")]
        [InlineData("WoodThin", "Poleno_1_LOD0", "WoodThick")]
        [InlineData("Fabric", "military_Sandbag_01_BALLISTIC_fabric", "Sand")]
        [InlineData("Soil", "sandbags_new_BALLISTIC_Soil", "Sand")]
        [InlineData("Stone", "curb_city_1m_BALLISTIC_stone", "Concrete")]
        [InlineData("Glass", "Window_glass_block_01_A", "GlassBlock")]
        [InlineData("Tyre", "koyoto_dirty_closed_hull_BALLISTIC_Tyre", "MetalThin")]
        // BSG's typos land on None and are read as the suffix the designer meant —
        // most of them by the suffix layer now, which is why their rules are gone
        // from the book; the default collider matches nothing anywhere and stays
        // impenetrable
        [InlineData("None", "streetlight_01_BALLISTIC_metalthin_top", "MetalThin")]
        [InlineData("None", "Burzhuyka_BALLISTIC_metlalthick", "MetalThick")]
        [InlineData("None", "RemBox_Wall_Outside_BALLISTIC_chainfance", "Chainfence")]
        [InlineData("None", "Projection_Screen_BALLISTIC_cloth", "Fabric")]
        [InlineData("None", "DefaultBallisticCollider", "None")]
        public void The_object_name_can_name_a_material_the_preset_cannot(string material,
            string objectName, string expected)
        {
            Assert.Equal(expected,
                ObstacleReference.EffectiveMaterial(Book(), material, objectName));
        }

        /// <summary>
        /// First match wins, so the order rules are written in is load-bearing:
        /// 'gunsafe' contains no 'container' but a gun safe's NAME contains both words,
        /// and a broken glass block must be claimed by the debris identity rule before
        /// the block rule sees it. These pins are what keeps a book edit from silently
        /// reshuffling the dictionary.
        /// </summary>
        [Theory]
        [InlineData("MetalThin", "scontainer_gunsafe_tall_BALLISTIC_metalthin", "GunSafe")]
        [InlineData("MetalThin", "scontainer_gunsafe_tall_DoorUp_BALLISTIC_metalthin", "GunSafe")]
        [InlineData("Glass", "Window_glass_block_01_A_chunk_02", "Glass")]
        [InlineData("Glass", "glass_block_broken_01", "Glass")]
        // the mesh tread of a staircase keeps its free pass; 'metal_stairs' only
        // hardens the plate parts, so chainfence must be claimed first
        [InlineData("MetalThin", "Metal_stairs_07_BALLISTIC_chainfence", "Chainfence")]
        // the typo'd counterweight resolves to cast iron, not to the generic typo
        // rescue one hop earlier (overrides never chain)
        [InlineData("None", "Loader_small_01_BALLISTIC_metalthic", "Machinery")]
        // a sandbag flagged HiPen is still sand, not generic padding
        [InlineData("Fabric", "military_Sandbag1_HiPen", "Sand")]
        [InlineData("Fabric", "massage_chair_BALLISTIC_Fabric_MedPen", "Upholstery")]
        [InlineData("Fabric", "Fabric_HiPen", "Upholstery")]
        // the raid-check corrections, one row per rule family
        [InlineData("MetalThick", "garbage_container_old_BALLISTIC_MetalThick_LowPen", "Sand")]
        [InlineData("Concrete", "garbage_container_old_BALLISTIC_concrete", "Sand")]
        [InlineData("MetalThin", "Metal_stairs_02_BALLISTIC_metalthin", "StructuralSteel")]
        [InlineData("MetalThin", "kabel_pallet2_BALLISTIC_metalthin", "Cable")]
        [InlineData("Plastic", "Cable_drum_03_BALLISTIC_plastic", "Cable")]
        [InlineData("Plastic", "Polythene_box_BALLISTIC_plastic", "BoxCargo")]
        [InlineData("Cardboard", "box_carton_BALLISTIC_Cardboard_MedPen", "BoxCargo")]
        [InlineData("WoodThin", "box_carton_BALLISTIC_woodthin", "BoxCargo")]
        [InlineData("MetalThick", "Loader_01_BALLISTIC_metalthick", "Machinery")]
        [InlineData("MetalThin", "Loader_01_BALLISTIC_metalthin", "StructuralSteel")]
        // autoloader is a truck, and bare 'loader' was never the rule — so it escapes
        // the counterweight rules and is then claimed by the taxonomy as the truck it
        // is. It used to come out untouched, which was only ever the absence of an
        // answer: a truck's plate is chassis rail, not a locker door
        [InlineData("MetalThick", "autoloader_BALLISTIC_metalthick", "StructuralSteel")]
        // the Hyundai's story in three acts: Machinery made the whole forklift
        // immune (one collider spans the machine) and a raid withdrew it; the
        // heavy-plant ruling (JCB, paver, roller) later settled the class on
        // structural plate — cab pays with them, nothing is immune
        [InlineData("MetalThick", "Loader_Hyundai_70DF-7_BALLISTIC_metalthick", "StructuralSteel")]
        // the Reserve smoke raid of the layers (book v16): the closed UAZ van's
        // merged collider is suffixed bare '_metal' — no alias on purpose — and
        // the taxonomy read a minivan as truck plate until its name spoke
        [InlineData("MetalThick", "UAZ_buhanka_BALLISTIC_metal", "VehicleChassis")]
        // a kerbstone spells 'brick' inside 'porebrick' and priced as one; the
        // shield outranks the family word, the brick pallet keeps its bricks
        [InlineData("Concrete", "porebrick05_LOD1", "Concrete")]
        [InlineData("Concrete", "bricks_pallet_01_brics_BALLISTIC_concrete", "Brick")]
        // the mall pallets drop the 'd' ('CarboardMall') and resolved apart from
        // their correctly-spelled twins
        [InlineData("Cardboard", "pallet_CarboardMall_6_BALLISTIC_Cardboard_MedPen", "BoxCargo")]
        // rail track carrying the terrain material: a stop either way, but steel
        // bounces hard — and bare 'rail' stays banned (handrails, trailers)
        [InlineData("Soil", "railway_rail_final_lod1", "Machinery")]
        [InlineData("Soil", "railway_stop_platform_handrail", "Soil")]
        // a roller shutter is a curtain of 1 mm slats, not 10 mm of plate — and it is
        // a material rule, not a leaf, so the shutter stays one layer
        [InlineData("MetalThick", "Rollete_Gate_low_Closed_BALLISTIC_metalthick", "MetalThin")]
        [InlineData("MetalThick", "Rollete_Gate_Opened_BALLISTIC_metalthick", "MetalThin")]
        // the bunkers' blast doors and gates are machines, not carpentry; the
        // bunkers' interior shells wear the same material and are NOT doors
        [InlineData("MetalThick", "Bunker_Door_B_01_R_220-110_Door_BALLISTIC_metalthick", "Machinery")]
        [InlineData("MetalThick", "reserve_AA_pos_bunker_Door_L_BALLISTIC_metalthick", "Machinery")]
        [InlineData("MetalThick", "Reserve_BunkerBig_Gate_L_BALLISTIC_metalthick", "Machinery")]
        [InlineData("MetalThick", "Reserve_Bunkers_Academy_01_BALLISTIC_metalthick", "MetalThick")]
        // the Kamaz split: every variant's doors are cab skin, the rest is
        // structural chassis, and the armoured one is armour. The doors are a
        // vehicle's skin rather than bare sheet — a truck cab door is built like a
        // car's, two panels with the window mechanism between them
        [InlineData("MetalThick", "Kamaz_4310_cargo_01_door_L_BALLISTIC_metalthick", "VehicleChassis")]
        [InlineData("MetalThick", "Kamaz_4310_mixer_door_R_BALLISTIC_metalthick", "VehicleChassis")]
        [InlineData("MetalThick", "Kamaz_4310_garbage_door_L_BALLISTIC_metalthick", "VehicleChassis")]
        [InlineData("MetalThick", "Kamaz_4310_tent_closed_door_L_BALLISTIC_metalthick", "VehicleChassis")]
        [InlineData("MetalThick", "Kamaz_4310_door_R_BALLISTIC_metalthick", "VehicleChassis")]
        [InlineData("MetalThick", "Kamaz_4310_mixer_BALLISTIC_metalthick", "StructuralSteel")]
        [InlineData("MetalThick", "kamaz_5490_closed_BALLISTIC_metalthick", "StructuralSteel")]
        [InlineData("MetalThick", "Kamaz_Armored_BALLISTIC_metalthick", "Machinery")]
        // Lighthouse: the armoured and the machine
        [InlineData("MetalThick", "Locomotive_wheels_1_BALLISTIC_metalthick", "Machinery")]
        [InlineData("MetalThick", "Armor_Locomotive_BALLISTIC_metalthick", "Machinery")]
        [InlineData("MetalThin", "BRDM_2_Base_BALLISTIC_metalthin", "Machinery")]
        [InlineData("MetalThick", "BRDM_2_crash_BALLISTIC_metalthick", "Machinery")]
        [InlineData("MetalThick", "airfilter_system_BALLISTIC_metalthick", "MetalThin")]
        [InlineData("MetalThin", "Water_Cleaner_Flotator_01_BALLISTIC_metalthin", "Machinery")]
        [InlineData("MetalThin", "Water_Filter_Facility_Indoor_BALLISTIC_metalthin", "StructuralSteel")]
        [InlineData("MetalThin", "Klimova_Mall_corridor_01_constraction_01_BALLISTIC_Metalthin", "StructuralSteel")]
        [InlineData("WoodThick", "Chalet_inside_BALLISTIC_woodthick", "TimberWall")]
        [InlineData("Cardboard", "pallet_cardboard_terragroup_BALLISTIC_Cardboard_MedPen", "BoxCargo")]
        [InlineData("WoodThin", "pallet_cardboard_terragroup_BALLISTIC_woodthin", "BoxCargo")]
        [InlineData("MetalThick", "military_T90_crash_BALLISTIC_metalthick", "Machinery")]
        // the audit's catches: the armoured fleet, drums, plant, planters, tarps
        [InlineData("MetalThick", "Tiger_BALLISTIC_metalthick", "Machinery")]
        [InlineData("MetalThick", "typhoon_cargo_closed_BALLISTIC_metalthick", "Machinery")]
        [InlineData("MetalThick", "Military_Stryker_crash_BALLISTIC_metalthick", "Machinery")]
        [InlineData("MetalThick", "Caterpillar_330dl_Body_BALLISTIC_metalthick", "Machinery")]
        [InlineData("MetalThick", "Metal_barrel_04_Closed_BALLISTIC_metalthick", "MetalThin")]
        [InlineData("MetalThick", "Beer_barrel_set_2_BALLISTIC_metalthick", "MetalThin")]
        [InlineData("MetalThin", "K702MA_BALLISTIC_metalthick", "StructuralSteel")]
        [InlineData("Plastic", "Lab_recreation_flowerbed_07_right_BALLISTIC_plastic_PL100", "GenericHard")]
        [InlineData("Fabric", "Pallet_cardboard_terragroup_cloth_01_BALLISTIC_fabric", "BoxCargo")]
        [InlineData("Fabric", "pallet_weapon_box_2_cloth1_BALLISTIC_fabric", "BoxCargo")]
        // the Streets audit: the metalthin suffix rescue (metal doors were 10 mm
        // plate), scaffolding's honest chainfence suffix, the Terrakot mall's
        // steel-clad faces; a door whose suffix says THICK keeps the plate
        [InlineData("MetalThick", "Inside_Door_Metal_09_R_210-100_door_R_BALLISTIC_MetalThin", "MetalThin")]
        [InlineData("MetalThick", "Outside_Door_Metal_02_R_210-110_BALLISTIC_metalthin", "MetalThin")]
        [InlineData("MetalThick", "Stall_01_shutters_BALLISTIC_metalthin", "MetalThin")]
        [InlineData("MetalThick", "Inside_Door_Metal_15_R_220-140_BALLISTIC_metalthick", "MetalThick")]
        [InlineData("Plastic", "scaffolding_01_BALLISTIC_chainfence", "Chainfence")]
        [InlineData("MetalThick", "Terrakot_Outdoor_Part_4_BALLISTIC_metalthick", "Concrete")]
        // the judged half of the Streets audit: heavy plant to structural plate
        // (the roller's thin-tagged cab stays sheet, like a Kamaz door; the
        // Paver_handle prop is a separate tool and keeps its material), cast-iron
        // radiators to the GunSafe shell (thin-tagged panel radiators stay sheet)
        [InlineData("MetalThick", "Paver_BALLISTIC_metalthick", "StructuralSteel")]
        [InlineData("MetalThin", "Paver_BALLISTIC_metalthin", "StructuralSteel")]
        [InlineData("MetalThick", "Paver_handle_01_BALLISTIC_metalthick", "MetalThick")]
        [InlineData("MetalThick", "Road_roller_01_BALLISTIC_metalthick", "StructuralSteel")]
        [InlineData("MetalThin", "Road_roller_01_BALLISTIC_metalthin", "MetalThin")]
        [InlineData("MetalThick", "reserve_radiator_01_BALLISTIC_metalthick", "GunSafe")]
        [InlineData("MetalThick", "Radiator_set2_City_BALLISTIC_metalthick", "GunSafe")]
        [InlineData("MetalThin", "Radiator_Chalet_01_BALLISTIC_metalthin", "MetalThin")]
        public void Override_order_resolves_overlapping_keywords(string material,
            string objectName, string expected)
        {
            Assert.Equal(expected,
                ObstacleReference.EffectiveMaterial(Book(), material, objectName));
        }

        /// <summary>
        /// Half the scene names its colliders "Metal" and hangs the prop's identity a
        /// transform or two above — a BTR is three anonymous boxes under
        /// `balistic/BTR_82`, a fridge door is `Fridge (1)/Door_D/Ballistic 1/Metal 1`.
        /// Ancestors are tried nearest-first, and only when the collider's own name
        /// fired nothing: a named part keeps its own reading whatever it is parented
        /// to, identity shields included.
        /// </summary>
        [Fact]
        public void Ancestor_names_speak_when_the_collider_is_anonymous()
        {
            var book = Book();

            // the raid cases, verbatim
            Assert.Equal("Machinery", ObstacleReference.EffectiveMaterial(
                book, "MetalThick", "MetalThick", "balistic", "BTR_82_(1)"));

            // the audit's underscore catch: the drivable T-90 spells itself T_90A on
            // an ancestor, which 't90' alone never matched
            Assert.Equal("Machinery", ObstacleReference.EffectiveMaterial(
                book, "MetalThick", "MetalThick_100PL", "Body", "T_90A"));

            // the BMP-2 raid catch: turret armour hangs its name on the
            // great-grandparent — the third climb, exactly the reach
            Assert.Equal("Machinery", ObstacleReference.EffectiveMaterial(
                book, "MetalThick", "Metal 1_PL100", "Ballistic 1", "Turret", "vechicle_BMP2"));
            Assert.Equal("Machinery", ObstacleReference.EffectiveMaterial(
                book, "MetalThick", "Metal_PL100", "Ballistic", "vechicle_BMP2 (1)"));

            // the JCB backhoe: anonymous 'metal' colliders, identity on the
            // grandparent
            Assert.Equal("StructuralSteel", ObstacleReference.EffectiveMaterial(
                book, "MetalThin", "metal 1", "balistic", "jcb3cx (1)"));

            // the kerb from the Reserve raid: an anonymous concrete collider whose
            // grandparent spells 'brick' inside 'Porebrick' — the shield reads the
            // same ancestor the family word would have
            Assert.Equal("Concrete", ObstacleReference.EffectiveMaterial(
                book, "Concrete", "Ballistic_Concrete", "Ballistic", "Porebrick_city_1m_set_03_(48)"));

            // the tunnel blast door: an anonymous 'ballistic' box whose parents spell
            // door_bunker — a machine, reached through the ancestors
            Assert.Equal("Machinery", ObstacleReference.EffectiveMaterial(
                book, "MetalThick", "ballistic", "door", "door_bunker_(2)"));

            // the radiator trap the census caught: Heating_Radiator_Set hangs over
            // a family of PIPES, and a bare 'radiator' keyword would have turned
            // their flanges into gun safes through the ancestor climb — the exact
            // rules must leave the pipe on its preset
            Assert.Equal("MetalThick", ObstacleReference.EffectiveMaterial(
                book, "MetalThick", "Pipe_flange_01_A_BALLISTIC_metalthick",
                "Pipe_flange_01_A", "Heating_Radiator_Set"));

            // the Lighthouse reel-to-reel deck: anonymous colliders, named parent
            Assert.Equal("MetalThin", ObstacleReference.EffectiveMaterial(
                book, "MetalThick", "Metal", "Ballistic", "Recorder_(1)"));
            Assert.Equal("Machinery", ObstacleReference.EffectiveMaterial(
                book, "MetalThick", "model_LOD1 1_MetalThick", "turret", "BTR_82_(1)"));
            Assert.Equal("MetalThin", ObstacleReference.EffectiveMaterial(
                book, "MetalThick", "Metal 1", "Ballistic 1", "Door_D", "Fridge (1)"));
            Assert.Equal("Cable", ObstacleReference.EffectiveMaterial(
                book, "Plastic", "plastic", "balistic_col", "kabel_pallet2 (1)"));

            // the collider's own name wins over any ancestor
            Assert.Equal("Machinery", ObstacleReference.EffectiveMaterial(
                book, "MetalThick", "Pump_engine_BALLISTIC_metalthick", "kamaz_group"));

            // an identity rule on the own name shields it from the ancestors too
            Assert.Equal("Glass", ObstacleReference.EffectiveMaterial(
                book, "Glass", "pane_chunk_02", "Window_glass_block_01"));

            // and no name anywhere means the preset, not an accident
            Assert.Equal("MetalThick", ObstacleReference.EffectiveMaterial(
                book, "MetalThick", "Metal", "Ballistic", "PROPS"));
        }

        // --- The suffix layer ---

        /// <summary>
        /// The second layer: the word the level designer wrote after `_BALLISTIC_`.
        ///
        /// It is the same evidence the book already trusted one material at a time
        /// (`woodthin` on the thick wood, `metalthin` on the thick metal) generalised to
        /// every material, because the census found the disagreement everywhere: 1 099
        /// `WoodThin` colliders saying metal, an entire shower block of `MetalThin`
        /// saying concrete, the Labs cells saying sheet while their material gives them
        /// away for free. It fires only when the word is one the book knows, names
        /// something other than the preset, and the preset is not a substance the word
        /// cannot outrank.
        /// </summary>
        [Theory]
        // the classes no hand-written rule had reached: metal door frames and sling
        // loops tagged as boards, a shower block tagged as tin, the Labs cells tagged
        // as mesh, and a material with no rules of its own at all
        [InlineData("WoodThin", "Metal_sling_loop_01_BALLISTIC_metalthin", "MetalThin")]
        [InlineData("MetalThin", "Case_shower_01_BALLISTIC_concrete", "Concrete")]
        [InlineData("Chainfence", "lab_basement_cell_02_BALLISTIC_metalthin", "MetalThin")]
        [InlineData("MetalNoDecal", "Ladder_frame_BALLISTIC_metalthin", "MetalThin")]
        // BSG's misspellings are in the alias table on purpose: they are in shipped
        // scenes, and the engine's own suffix parser is what fails on them
        [InlineData("WoodThin", "Curtain_02_BALLISTIC_fabrick", "Fabric")]
        [InlineData("MetalThin", "Wall_panel_BALLISTIC_concete", "Concrete")]
        [InlineData("None", "Sign_BALLISTIC_galss", "Glass")]
        // numbering after the word is numbering, not material
        [InlineData("MetalThick", "Vent_01_BALLISTIC_metalthin_top", "MetalThin")]
        [InlineData("MetalThick", "Vent_02_BALLISTIC_metalthin_02", "MetalThin")]
        [InlineData("MetalThick", "Panel_BALLISTIC_metalthin_PL100", "MetalThin")]
        [InlineData("MetalThick", "Panel_BALLISTIC_metalthin_HiPen", "MetalThin")]
        // the whole word is tried before any of it is cut away, so an underscored
        // spelling never decays into the ambiguous half of itself
        [InlineData("MetalThin", "Shelf_BALLISTIC_wood_thin", "WoodThin")]
        // junk words are not in the table, and the layer says nothing
        [InlineData("MetalThin", "Cover_BALLISTIC_simple", "MetalThin")]
        [InlineData("MetalThin", "Box_BALLISTIC_new", "MetalThin")]
        [InlineData("MetalThin", "Panel_BALLISTIC_collider", "MetalThin")]
        [InlineData("MetalThin", "Frame_BALLISTIC_(1)", "MetalThin")]
        // and neither does an ambiguous one: the census puts bare 'metal' and bare
        // 'wood' on thin and thick carriers alike
        [InlineData("MetalThick", "Beam_BALLISTIC_metal", "MetalThick")]
        [InlineData("WoodThick", "Beam_BALLISTIC_wood", "WoodThick")]
        // no marker, no word: a collider CALLED Metal is not a designer saying metal
        [InlineData("MetalThick", "Metal_PL100", "MetalThick")]
        [InlineData("MetalThick", "Metal 1", "MetalThick")]
        // the substance list: the word describes the skin and the material is what has
        // to be crossed, so a tiled wall stays concrete and a shattered pane stays the
        // cheaper of the two glasses
        [InlineData("Concrete", "Floor_01_BALLISTIC_tile", "Concrete")]
        [InlineData("GlassShattered", "Window_debris_BALLISTIC_glass", "GlassShattered")]
        [InlineData("Soil", "Ground_patch_BALLISTIC_metalthin", "Soil")]
        // identity is above the layer and defends what it claimed: the Kirovets says
        // metalthick in its own name and is structural plate, the loader counterweight
        // says metalthic and is cast iron
        [InlineData("MetalThin", "K702MA_BALLISTIC_metalthick", "StructuralSteel")]
        [InlineData("None", "Loader_small_02_BALLISTIC_metalthic", "Machinery")]
        public void The_designers_own_word_is_read_when_no_rule_claims_the_object(
            string material, string objectName, string expected)
        {
            Assert.Equal(expected,
                ObstacleReference.EffectiveMaterial(Book(), material, objectName));
        }

        /// <summary>
        /// The suffix is the collider's OWN word. An ancestor's name may say what the
        /// prop IS (that is what identity and taxonomy read), but not what this
        /// particular box is made of — the parent of a metal-framed wooden door says
        /// "door", and reading its material word onto every child would price the panel
        /// and the frame the same.
        /// </summary>
        [Fact]
        public void The_suffix_is_read_off_the_collider_and_not_off_its_parents()
        {
            var book = Book();

            Assert.Equal("MetalThin", ObstacleReference.EffectiveMaterial(
                book, "WoodThin", "Frame_BALLISTIC_metalthin", "Door_wood_01"));

            // the same word one level up says nothing
            Assert.Equal("WoodThin", ObstacleReference.EffectiveMaterial(
                book, "WoodThin", "Panel", "Door_wood_01_BALLISTIC_metalthin"));
        }

        // --- The taxonomy layer ---

        /// <summary>
        /// The third layer: what the scene graph says this collider is part of. A
        /// vehicle's skin is not a road sign's skin, and the only place that fact lives
        /// is the grouping node BSG park the prop under — or, when the map does not use
        /// one, the model name in the prop's own line.
        /// </summary>
        [Fact]
        public void The_scene_taxonomy_says_what_a_sheet_is_part_of()
        {
            var book = Book();

            // under the node, both metals map
            Assert.Equal("VehicleChassis", ObstacleReference.EffectiveMaterial(
                book, "MetalThin", "Metal", "Ballistic", "VEHICLES"));
            Assert.Equal("StructuralSteel", ObstacleReference.EffectiveMaterial(
                book, "MetalThick", "Metal", "Ballistic", "Vehicle"));

            // the node is matched WHOLE: 'vechicle_BMP2' is a prop that happens to
            // contain the word, and a substring test would have swallowed it and
            // everything else parked near a named car park
            Assert.Equal("MetalThin", ObstacleReference.EffectiveMaterial(
                book, "MetalThin", "Metal", "Ballistic", "vechicle_BMP2"));

            // and it is a PARENT's job: a collider called "vehicle" is a badly named
            // prop, not a grouping of anything
            Assert.Equal("MetalThin", ObstacleReference.EffectiveMaterial(
                book, "MetalThin", "vehicle", "Ballistic"));

            // the same car lives under the node on one map and under OFF on another,
            // which is why the model words exist at all
            Assert.Equal("VehicleChassis", ObstacleReference.EffectiveMaterial(
                book, "MetalThin", "Metal", "Ballistic", "Cruze_Off"));
            Assert.Equal("VehicleChassis", ObstacleReference.EffectiveMaterial(
                book, "MetalThin", "Tramvay_01_BALLISTIC_metalthin", "OFF"));

            // the layers chain on purpose: the designer's word makes a wood-tagged
            // collider sheet, and the scene makes that sheet a car's flank
            Assert.Equal("VehicleChassis", ObstacleReference.EffectiveMaterial(
                book, "WoodThin", "Metal_frame_BALLISTIC_metalthin", "balistic", "VEHICLES"));

            // a material the map says nothing about is left where it was
            Assert.Equal("Glass", ObstacleReference.EffectiveMaterial(
                book, "Glass", "Glass_01", "Ballistic", "VEHICLES"));

            // identity is final, and the fleet it protects is the whole reason:
            // armour parked under VEHICLES is still armour, and a shipping container
            // on a truck bed is still a container
            Assert.Equal("Machinery", ObstacleReference.EffectiveMaterial(
                book, "MetalThick", "Metal", "balistic", "BTR_82_(1)", "VEHICLES"));
            Assert.Equal("ContainerSteel", ObstacleReference.EffectiveMaterial(
                book, "MetalThin", "container_6m_close_BALLISTIC_metalthin", "Ballistic",
                "kamaz_4310_cargo_01"));
        }

        /// <summary>
        /// A door leaf is what its MATERIAL says a leaf is. Only sheet that cannot
        /// carry itself laminates — thin steel and plastic pay two skins under a
        /// DOORS node. Everything else has a fixed leaf thickness, never the
        /// collider's chord (that is the door assembly's depth, 100-200 mm): 50 mm
        /// for wood, 5 mm for thick steel — an armoured door, not the vault plate
        /// the plain-69 anchor hands a hull (and the bunkers' blast doors are
        /// Machinery by identity outright, so they never read a leaf at all).
        /// </summary>
        [Fact]
        public void A_door_leaf_is_what_its_material_says_a_leaf_is()
        {
            var book = Book();

            // hollow leaves: two skins
            Assert.Equal(2, ObstacleReference.WallsCrossed(
                book, "MetalThin", "Metal", "Ballistic", "DOORS"));
            Assert.Equal(2, ObstacleReference.WallsCrossed(
                book, "Plastic", "Panel", "Doors", "Hangar"));

            // a plate carries itself: one wall, never two. Its leaf is a fixed 5 mm —
            // an armoured door, not the 10 mm vault plate the plain-69 anchor was
            // handing every entrance, interior and garage door in the game
            Assert.Equal(1, ObstacleReference.WallsCrossed(
                book, "MetalThick", "Metal", "Ballistic", "DOORS"));
            Assert.Equal(5, ObstacleReference.DoorLeafThicknessMm(
                book, "MetalThick", "Metal", "Ballistic", "DOORS"), 6);
            // the same plate off a door is the anchor again: a hull is not a leaf
            Assert.Equal(0, ObstacleReference.DoorLeafThicknessMm(
                book, "MetalThick", "Tank", "Ballistic", "PROPS"), 6);
            Assert.True(ObstacleReference.TryBarrier(book, "MetalThick", 69, out var hull));
            Assert.Equal(10, hull.ThicknessMm, 6);
            Assert.True(ObstacleReference.TryBarrier(
                book, "MetalThick", 69, out var steelLeaf, 1, 5));
            Assert.Equal(5, steelLeaf.ThicknessMm, 6);
            Assert.Equal(1, steelLeaf.Walls, 6);

            // a wooden door is a FIXED 50 mm of wood — not the collider's chord
            // (leaf colliders run 100-200 mm deep: read as timber, every wooden
            // door was a safe), and not one 20 mm board either
            Assert.Equal(1, ObstacleReference.WallsCrossed(
                book, "WoodThin", "Door", "Ballistic", "DOORS"));
            Assert.Equal(50, ObstacleReference.DoorLeafThicknessMm(
                book, "WoodThin", "Door", "Ballistic", "DOORS"), 6);
            // the same board off a door is still one board of a shell
            Assert.Equal(0, ObstacleReference.DoorLeafThicknessMm(
                book, "WoodThin", "Plank", "Ballistic", "PROPS"), 6);
            // and a hollow leaf has no fixed thickness — its price is its skins
            Assert.Equal(0, ObstacleReference.DoorLeafThicknessMm(
                book, "MetalThin", "Metal", "Ballistic", "DOORS"), 6);

            // A gate is a leaf whatever it is parented to. Factory's entrance gate has
            // no DOORS node anywhere in its chain, and a raid found the leaf and its
            // wicket door each paying the 10 mm plain-69 anchor — the wicket twice
            // over, being a child of the leaf with overlapping colliders. Named as
            // leaves, both are 5 mm, so the doubled crossing costs one plate.
            Assert.Equal(5, ObstacleReference.DoorLeafThicknessMm(
                book, "MetalThick", "Enterance_Gate_01_R_BALLISTIC_Metalthick",
                "Enterance_Gate_01_R", "Enterance_Gate_01"), 6);
            Assert.Equal(5, ObstacleReference.DoorLeafThicknessMm(
                book, "MetalThick", "Enterance_Gate_01_Door_BALLISTIC_Metalthick",
                "Enterance_Gate_01_Door", "Enterance_Gate_01_R", "Enterance_Gate_01"), 6);
            // and it reaches through an anonymous collider, like every other name rule
            Assert.Equal(5, ObstacleReference.DoorLeafThicknessMm(
                book, "MetalThick", "metal", "balistic", "Enterance_Gate_01_L"), 6);
            // the rest of the family, one row per census word
            Assert.Equal(5, ObstacleReference.DoorLeafThicknessMm(
                book, "MetalThick", "gate", "gate_metall2 (3)", "Gates_group"), 6);
            Assert.Equal(5, ObstacleReference.DoorLeafThicknessMm(
                book, "MetalThick", "lod0_gate_L", "gate_L", "gate_metall1"), 6);
            Assert.Equal(5, ObstacleReference.DoorLeafThicknessMm(
                book, "MetalThick", "Gates_PTOR_BALLISTIC_metalthick", "Gates_PTOR"), 6);
            Assert.Equal(5, ObstacleReference.DoorLeafThicknessMm(
                book, "MetalThick", "Transfer_gateways_01_Door_R_BALLISTIC_metalthick"), 6);
            Assert.Equal(5, ObstacleReference.DoorLeafThicknessMm(
                book, "MetalThick", "ballistic", "garage_gate_red_close (4)"), 6);
            // a gate's thin parts are a leaf of two skins, not one sheet
            Assert.Equal(2, ObstacleReference.WallsCrossed(
                book, "MetalThin", "Gates_PTOR_Door_R_BALLISTIC_metalthin", "Gates_PTOR_Door_R"), 6);

            // a plate that is nobody's leaf keeps the anchor
            Assert.Equal(0, ObstacleReference.DoorLeafThicknessMm(
                book, "MetalThick", "Cistern_01_BALLISTIC_metalthick", "Cistern_01", "PROPS"), 6);

            // whole-name match here too: a fridge's Door_D is not the scene's node,
            // and the collider's own name is not a grouping either
            Assert.Equal(1, ObstacleReference.WallsCrossed(
                book, "MetalThin", "Metal 1", "Ballistic 1", "Door_D", "Fridge (1)"));
            Assert.Equal(1, ObstacleReference.WallsCrossed(book, "MetalThin", "DOORS", "Ballistic"));
            Assert.Equal(1, ObstacleReference.WallsCrossed(
                book, "MetalThin", "Metal", "Ballistic", "PROPS"));

            // what it does to the barrier: one wall of book thickness becomes two
            Assert.True(ObstacleReference.TryBarrier(book, "MetalThin", 4, out var one));
            Assert.True(ObstacleReference.TryBarrier(book, "MetalThin", 4, out var leaf, 2));
            Assert.Equal(one.ThicknessMm, leaf.ThicknessMm, 6);
            Assert.Equal(2 * ObstacleModel.WallMm(one), ObstacleModel.WallMm(leaf), 6);

            // a fixed leaf replaces the anchor thickness and stays a shell: 50 mm
            // of wood at the entry, the collider's depth never enters
            Assert.True(ObstacleReference.TryBarrier(
                book, "WoodThin", 3, out var wooden, 1, 50));
            Assert.False(wooden.Solid);
            Assert.Equal(50, wooden.ThicknessMm, 6);
            Assert.Equal(1, wooden.Walls, 6);

            // and nothing to an already-solid material: its thickness is the measured
            // chord, which already contains everything the projectile has to cross
            Assert.True(ObstacleReference.TryBarrier(book, "WoodThick", 25, out var log, 2));
            Assert.Equal(ObstacleModel.WallMm(log), log.ThicknessMm, 6);
        }

        // --- The layer tables themselves ---

        /// <summary>
        /// The same rule the name overrides live under: a layer that names a material
        /// the book does not define would silently mean "leave it alone", so a typo in
        /// the table would be a rule that does nothing.
        /// </summary>
        [Fact]
        public void Every_layer_target_is_a_material_that_exists()
        {
            var book = Book();
            var bad = new List<string>();

            foreach (var pair in book.SuffixAliases)
            {
                if (!book.Materials.ContainsKey(pair.Value))
                {
                    bad.Add($"SuffixAliases[{pair.Key}]={pair.Value}");
                }
            }

            foreach (var name in book.SuffixFinal)
            {
                if (!book.Materials.ContainsKey(name))
                {
                    bad.Add($"SuffixFinal[{name}]");
                }
            }

            foreach (var pair in book.Taxonomy.VehicleMap)
            {
                if (!book.Materials.ContainsKey(pair.Key))
                {
                    bad.Add($"VehicleMap key {pair.Key}");
                }

                if (!book.Materials.ContainsKey(pair.Value))
                {
                    bad.Add($"VehicleMap[{pair.Key}]={pair.Value}");
                }
            }

            Assert.True(bad.Count == 0, "Layer targets with no material: " +
                                        string.Join(", ", bad));
        }

        /// <summary>
        /// The words the census refused. `metal` and `wood` sit on thin and thick
        /// carriers alike, so an alias for either would be a coin flip dressed as
        /// evidence; a door leaf that pays one wall is the bug the DOORS node exists
        /// to fix, so the count must be more than one.
        /// </summary>
        [Fact]
        public void The_layer_tables_keep_the_decisions_that_were_argued_over()
        {
            var book = Book();

            Assert.False(book.SuffixAliases.ContainsKey("metal"));
            Assert.False(book.SuffixAliases.ContainsKey("wood"));

            Assert.Contains("vehicle", book.Taxonomy.VehicleNodes);
            Assert.Contains("door", book.Taxonomy.DoorNodes);
            Assert.True(book.Taxonomy.DoorWalls > 1);

            // bare 'man_' catches 'woman_', bare 'paz_' catches props that are not the
            // bus: both were rejected in favour of the full names
            Assert.DoesNotContain("man_", book.Taxonomy.VehicleFamilies);
            Assert.DoesNotContain("paz_", book.Taxonomy.VehicleFamilies);
        }

        /// <summary>
        /// Brick is ours, not the game's — nothing reaches it except through a name — so
        /// it has to be a complete entry in its own right.
        /// </summary>
        [Fact]
        public void The_virtual_brick_is_a_material_like_any_other()
        {
            var book = Book();
            Assert.DoesNotContain("Brick", Enum.GetNames(typeof(MaterialType)));

            Assert.True(ObstacleReference.TryBarrier(book, "Brick", 100, out var brick));
            Assert.Equal(ObstacleModel.MechPoncelet, brick.Mechanism);
            Assert.True(brick.Solid);
            Assert.True(brick.ThicknessMm > 0);
            Assert.True(brick.SpallFactor > 1);

            // weaker and lighter than the concrete it is carved out of, which is the
            // entire reason for its existence
            Assert.True(ObstacleReference.TryBarrier(book, "Concrete", 100, out var concrete));
            Assert.True(brick.StrengthMPa < concrete.StrengthMPa);
            Assert.True(brick.DensityGCm3 < concrete.DensityGCm3);
            Assert.True(brick.HardnessHv < concrete.HardnessHv);
        }

        // --- Thickness interpolation ---

        [Theory]
        [InlineData(4, 1.0)]   // the one anchor
        [InlineData(1, 1.0)]   // below it: flat, not extrapolated to nothing
        [InlineData(90, 1.0)]  // above it: flat
        public void A_single_anchor_is_flat_everywhere(float level, double expected)
        {
            var m = Book().Materials["MetalThin"];
            Assert.Equal(expected, ObstacleReference.Thickness(m, level), 4);
        }

        [Theory]
        [InlineData(7, 2)]
        [InlineData(18, 4)]
        [InlineData(32, 6)]
        [InlineData(69, 10)]
        [InlineData(12.5f, 3.0)]   // halfway between 7 and 18
        [InlineData(3, 2)]         // clamped below the ladder
        [InlineData(200, 10)]      // clamped above it
        public void Anchors_interpolate_piecewise_and_clamp(float level, double expected)
        {
            var m = Book().Materials["MetalThick"];
            Assert.Equal(expected, ObstacleReference.Thickness(m, level), 4);
        }

        [Fact]
        public void Thickness_never_falls_as_the_level_rises()
        {
            var m = Book().Materials["MetalThick"];
            var last = -1.0;
            for (var level = 0; level <= 100; level++)
            {
                var t = ObstacleReference.Thickness(m, level);
                Assert.True(t >= last, $"thickness fell at level {level}");
                last = t;
            }
        }

        // --- Resolution ---

        /// <summary>
        /// PenetrationLevel 100 used to be read as the designer saying "this is a wall"
        /// and beat the material outright. The raid census says otherwise — it sits on
        /// an IBC tote's plastic cage and a boiler housing as readily as on a concrete
        /// wall — so it is a thickness selector and nothing else, and a sheet of tin
        /// carrying it is still a sheet of tin.
        /// </summary>
        [Fact]
        public void The_top_level_does_not_turn_a_material_into_a_wall()
        {
            var book = Book();
            Assert.True(ObstacleReference.TryBarrier(book, "MetalThin", 4, out var thin));
            Assert.Equal(ObstacleModel.MechSteel, thin.Mechanism);

            Assert.True(ObstacleReference.TryBarrier(book, "MetalThin", 100, out var top));
            Assert.Equal(ObstacleModel.MechSteel, top.Mechanism);

            // the case that made it worth removing: a plastic tote is not a wall
            Assert.True(ObstacleReference.TryBarrier(book, "Plastic", 100, out var tote));
            Assert.Equal(ObstacleModel.MechPoncelet, tote.Mechanism);
        }

        [Fact]
        public void An_unknown_material_is_left_to_the_game()
        {
            var book = Book();
            Assert.False(ObstacleReference.TryBarrier(book, "SomeModdedFoam", 5, out _));
            Assert.False(ObstacleReference.TryRicochet(book, "SomeModdedFoam", out _, out _));
        }

        [Fact]
        public void A_vanilla_material_is_left_to_the_game()
        {
            var book = Book();
            Assert.False(ObstacleReference.TryBarrier(book, "Grate", 0, out _));
            Assert.False(ObstacleReference.TryRicochet(book, "Grate", out _, out _));

            // the default collider must stay impenetrable, which is vanilla's job
            Assert.False(ObstacleReference.TryBarrier(book, "None", 0, out _));
        }

        /// <summary>
        /// Water keeps vanilla's penetration (a medium of unknown depth) and takes our
        /// bounce: the seven-degree critical angle is one of the best-measured numbers
        /// in the ricochet literature.
        /// </summary>
        [Fact]
        public void Water_splits_penetration_from_ricochet()
        {
            var book = Book();
            Assert.False(ObstacleReference.TryBarrier(book, "Water", 0, out _));
            Assert.True(ObstacleReference.TryRicochet(book, "Water", out var alpha, out var k));
            Assert.Equal(7, alpha, 3);
            Assert.True(k > 0);
        }

        [Fact]
        public void A_surface_that_never_bounces_resolves_to_zero_and_not_to_vanilla()
        {
            var book = Book();
            Assert.True(ObstacleReference.TryRicochet(book, "Glass", out var alpha, out var k));
            Assert.Equal(0, alpha, 6);
            Assert.Equal(0, k, 6);
        }

        /// <summary>
        /// Shell materials must NOT be solid. This is the flag that decides whether a
        /// measured collider is read as the path or as the object's outline, and getting
        /// it wrong on a sheet is what made barrels, canisters and container sides
        /// bulletproof: a barrel's collider is 600 mm of air with a millimetre of steel
        /// at each end, and believing that number stops a 7.62.
        /// </summary>
        [Theory]
        [InlineData("MetalThin")]
        [InlineData("Plastic")]
        [InlineData("Fabric")]
        [InlineData("Cardboard")]
        [InlineData("GarbagePaper")]
        [InlineData("Glass")]
        // wood too: "thin" names a board, and what is built out of boards — cabinets,
        // crates, pallets, doors — is hollow. A raid confirmed it object by object.
        [InlineData("WoodThin")]
        // and rubber, which on every map so far has been a loader's wheel: its collider
        // spans the whole tyre, air included, while the rubber is the tread at each end
        [InlineData("Rubber")]
        // MetalThick flipped with the campaign: its census carriers are barrels,
        // cisterns, pipes, gates and trucks — outlines around air. What is genuinely
        // dense is carved out by name into Machinery, which IS solid.
        [InlineData("MetalThick")]
        [InlineData("ContainerSteel")]
        // a gun safe is a steel box: 4 mm at entry and 4 more at exit, not one sheet
        // and not a solid block
        [InlineData("GunSafe")]
        // structural plate: the members are plate, the space between them is air
        [InlineData("StructuralSteel")]
        // a log building's shell: one wall per face, not the whole outline as timber
        [InlineData("TimberWall")]
        // a glass block is itself hollow — ~10 mm of glass at each face
        [InlineData("GlassBlock")]
        public void A_shell_material_is_never_solid(string material)
        {
            Assert.False(Book().Materials[material].Solid,
                $"{material} is a shell: its collider is an outline, not a path");
        }

        /// <summary>
        /// And the bulk ones must be, or the measurement never gets used and the whole
        /// point of reading the scene is lost — an electric motor goes back to being a
        /// locker door.
        /// </summary>
        [Theory]
        // WoodThick stays solid deliberately: the one object carrying it on a raided map
        // was a crate at the highest wood level vanilla has, which reads as "this box is
        // full of something you are not shooting through" rather than as a bare plank.
        [InlineData("WoodThick")]
        // concrete and brick: a wall is what the collider says it is, and the whole
        // point of making it penetrable is that a 150 mm partition is not a 400 mm one
        [InlineData("Concrete")]
        [InlineData("Brick")]
        [InlineData("GenericHard")]
        [InlineData("Pebbles")]
        // the carve-outs that must keep the measured chord: a 5.45 does not cross an
        // electric motor, a sandbag is as thick as the bag, a couch is as deep as the
        // couch, and a bank screen is its full laminate
        [InlineData("Machinery")]
        [InlineData("Sand")]
        [InlineData("Upholstery")]
        [InlineData("ArmoredGlass")]
        // wound copper: a drum is as deep as the drum
        [InlineData("Cable")]
        public void A_bulk_material_is_solid(string material)
        {
            Assert.True(Book().Materials[material].Solid,
                $"{material} is solid through: the collider is the path");
        }

        [Fact]
        public void Steel_is_structural_and_flows_rather_than_plugs()
        {
            var book = Book();
            Assert.True(ObstacleReference.TryBarrier(book, "MetalThin", 4, out var b));

            // mild steel has strain-hardening reserve left, so it cannot localise shear
            Assert.Equal(PLATE.Server.Services.BallisticLimit.HoleExpansion, b.FailureMode);
            Assert.True(b.YieldMPa > 0);

            // a steel material does not repeat the steel's own density and hardness: it
            // takes them from the one Steel block, and everything that reads them (the
            // core's fate, the deflection's areal density) breaks silently at zero
            Assert.Equal(7.85, b.DensityGCm3, 3);
            Assert.True(b.HardnessHv > 0);
        }

        /// <summary>
        /// Density and thickness are read by the deflection and by the core's fate, not
        /// only by the depth law. An "always" material that costs a projectile something
        /// is a real object it went through, so it has to carry both — otherwise
        /// crossing a pane of glass can silently neither turn a bullet nor deform it.
        /// A material that costs nothing (wire mesh, grass) is not an object in that
        /// sense and carries neither, which the model reads as "no deflection of ours,
        /// leave the game's".
        /// </summary>
        [Fact]
        public void An_always_material_with_a_price_is_a_real_object()
        {
            var bad = Book().Materials
                .Where(kv => kv.Value.Mechanism == ObstacleModel.MechAlways &&
                             kv.Value.CostJ > 0 &&
                             (kv.Value.DensityGCm3 <= 0 ||
                              kv.Value.Anchors == null || kv.Value.Anchors.Count == 0))
                .Select(kv => kv.Key)
                .ToList();

            Assert.True(bad.Count == 0,
                "Priced 'always' materials with no body to them: " + string.Join(", ", bad));
        }
    }
}
