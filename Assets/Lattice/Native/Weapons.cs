namespace Lattice.Native
{
    /// <summary>
    /// The classes and their weapons (U6), as the plugin has them
    /// (<c>lt_weapon</c>): read once, by kit (<c>LtOwn.kit</c>,
    /// <c>LtEntity.kit</c>). What the HUD, the recoil, the reload and the
    /// sounds follow; the rules themselves are the plugin's.
    /// </summary>
    public static unsafe class Weapons
    {
        public static readonly string[] Classes = { "ASSAULT", "MEDIC", "ENGINEER", "RECON", "SKIRMISHER" };
        public static readonly string[] Names = { "ASSAULT RIFLE", "CARBINE", "SHOTGUN", "MARKSMAN RIFLE", "SMG" };
        /// <summary>What each class is for (GDD §4.3), a line for the deploy screen.</summary>
        public static readonly string[] Roles =
        {
            "hold the line",
            "keep the squad alive",
            "close quarters, fortify",
            "information, precision",
            "flank, harass, the high ground",
        };

        static LtWeapon[] table;

        public static int Count => LtCode.Classes;

        /// <summary>Kit <paramref name="kit"/>'s weapon (an unknown kit: the assault rifle's).</summary>
        public static ref readonly LtWeapon Of(int kit)
        {
            if (table == null)
            {
                table = new LtWeapon[LtCode.Classes];
                if (LtLibrary.EnsureLoaded())
                    for (int k = 0; k < table.Length; k++)
                        fixed (LtWeapon* w = &table[k])
                            LtLibrary.Api.lt_weapon((byte)k, w);
            }
            return ref table[kit >= 0 && kit < table.Length ? kit : 0];
        }

        static string[] lower;

        /// <summary>Weapon <paramref name="kit"/>'s name in lower case (made once).</summary>
        public static string Lower(int kit)
        {
            if (lower == null)
            {
                lower = new string[Names.Length];
                for (int i = 0; i < Names.Length; i++)
                    lower[i] = Names[i].ToLowerInvariant();
            }
            return lower[kit >= 0 && kit < lower.Length ? kit : 0];
        }

        public static string Class(int kit) => kit >= 0 && kit < Classes.Length ? Classes[kit] : Classes[0];
        public static string Name(int kit) => kit >= 0 && kit < Names.Length ? Names[kit] : Names[0];
    }
}
