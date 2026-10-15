namespace Lattice.Native
{
    /// <summary>
    /// The classes and the weapons (U6), as the plugin has them: each weapon's
    /// numbers by id (<c>lt_weapon</c>; <c>LtOwn.weapon</c>, <c>LtEntity.weapon</c>,
    /// <c>LtFx.weapon</c>, a recap's cause) and the weapons each class may carry
    /// (<c>lt_class_weapons</c>, its own first). What the HUD, the recoil, the
    /// reload and the sounds follow; the rules themselves are the plugin's.
    /// </summary>
    public static unsafe class Weapons
    {
        public static readonly string[] Classes = { "ASSAULT", "MEDIC", "ENGINEER", "RECON", "SKIRMISHER", "EXO" };
        /// <summary>The weapons' names, by id (<c>LT_WEAPON_*</c>).</summary>
        public static readonly string[] Names = { "ASSAULT RIFLE", "CARBINE", "SHOTGUN", "MARKSMAN RIFLE", "SMG", "LMG", "SNIPER RIFLE", "TWIN GUNS", "LAUNCHER" };
        /// <summary>What each class is for (GDD §4.3), a line for the deploy screen.</summary>
        public static readonly string[] Roles =
        {
            "hold the line",
            "keep the squad alive",
            "close quarters, fortify",
            "information, precision",
            "flank, harass, the high ground",
            "heavy armor, one every 3 minutes",
        };

        static LtWeapon[] table;
        static byte[][] lists;
        static string[] lower;

        public static int Count => LtCode.Weapons;
        public static int ClassCount => LtCode.Classes;

        /// <summary>Weapon <paramref name="id"/>'s numbers (an unknown one: the assault rifle's).</summary>
        public static ref readonly LtWeapon Of(int id)
        {
            if (table == null)
            {
                table = new LtWeapon[LtCode.Weapons];
                if (LtLibrary.EnsureLoaded())
                    for (int k = 0; k < table.Length; k++)
                        fixed (LtWeapon* w = &table[k])
                            LtLibrary.Api.lt_weapon((byte)k, w);
            }
            return ref table[id >= 0 && id < table.Length ? id : 0];
        }

        /// <summary>The weapons class <paramref name="kit"/> may carry, its own first.</summary>
        public static byte[] OfClass(int kit)
        {
            if (lists == null)
            {
                lists = new byte[LtCode.Classes][];
                for (int k = 0; k < lists.Length; k++)
                {
                    var buf = new byte[8];
                    uint n = 0;
                    if (LtLibrary.EnsureLoaded())
                        fixed (byte* b = buf)
                            n = LtLibrary.Api.lt_class_weapons((byte)k, b, (uint)buf.Length);
                    lists[k] = new byte[System.Math.Min((int)n, buf.Length)];
                    System.Array.Copy(buf, lists[k], lists[k].Length);
                    if (lists[k].Length == 0)
                        lists[k] = new byte[] { 0 };
                }
            }
            return lists[kit >= 0 && kit < lists.Length ? kit : 0];
        }

        /// <summary>Class <paramref name="kit"/>'s own weapon.</summary>
        public static int Own(int kit) => OfClass(kit)[0];

        public static string Class(int kit) => kit >= 0 && kit < Classes.Length ? Classes[kit] : Classes[0];
        public static string Name(int id) => id >= 0 && id < Names.Length ? Names[id] : Names[0];

        /// <summary>The gadgets' names, by kind (<c>LT_GADGET_*</c>; U6), for the HUD.</summary>
        public static readonly string[] GadgetNames = { "", "FRAG", "SMOKE", "C4", "MINE", "AMMO PACK", "RECON DART", "TURRET", "SHELL" };
        static string[] gadgetLower;

        public static string Gadget(int kind) => kind > 0 && kind < GadgetNames.Length ? GadgetNames[kind] : "";

        /// <summary>A recap's cause in lower case (made once): a weapon (<c>LT_WEAPON_*</c>),
        /// or a gadget (<c>LT_CAUSE_GADGET</c> plus its kind: "frag grenade", "turret"...).</summary>
        public static string Lower(int id)
        {
            if (id >= LtCode.CauseGadget)
            {
                if (gadgetLower == null)
                    gadgetLower = new[] { "a blast", "frag grenade", "smoke", "C4", "mine", "ammo pack", "recon dart", "turret", "launcher" };
                int k = id - LtCode.CauseGadget;
                return gadgetLower[k > 0 && k < gadgetLower.Length ? k : 0];
            }
            if (lower == null)
            {
                lower = new string[Names.Length];
                for (int i = 0; i < Names.Length; i++)
                    lower[i] = Names[i].ToLowerInvariant();
            }
            return lower[id >= 0 && id < lower.Length ? id : 0];
        }

        /// <summary>A weapon with a scope (the marksman and sniper rifles), and how far it zooms.</summary>
        public static bool Scoped(int id) => id == LtCode.WeaponMarksman || id == LtCode.WeaponSniper;
        public static float ScopeZoom(int id) => id == LtCode.WeaponSniper ? 6f : 3f;

        /// <summary>The Exo's arm weapons (no rifle in its hands).</summary>
        public static bool Arms(int id) => id == LtCode.WeaponExoGuns || id == LtCode.WeaponExoLauncher;
    }
}
