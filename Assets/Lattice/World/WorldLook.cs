using UnityEngine;

namespace Lattice.World
{
    /// <summary>
    /// How the world looks: the terrain's layers and material, the models
    /// cover is drawn with, and the ground's scatter. An asset made by
    /// Lattice > Setup (LatticeSetup.World), referenced by the scene.
    /// </summary>
    [CreateAssetMenu(menuName = "Lattice/World Look")]
    public sealed class WorldLook : ScriptableObject
    {
        [Header("Terrain")]
        /// <summary>Grass, meadow, soil, rock (the splat's channels, in order).</summary>
        public TerrainLayer[] Layers = new TerrainLayer[4];
        public Material TerrainMaterial;
        /// <summary>Alphamap resolution per 2,048 m tile (2 m per texel at 1024).</summary>
        public int SplatResolution = 1024;
        /// <summary>How much the far terrain simplifies: 1 keeps every 4 m sample
        /// within reach (what you see is what you stand on).</summary>
        public float PixelError = 1f;
        public float BasemapDistance = 1200f;

        [Header("Ground scatter")]
        /// <summary>Grass (GrassField): clumps scattered around the camera by a compute
        /// shader where the ground is grass or meadow, drawn in one indirect draw.</summary>
        public Material GrassMaterial;
        public ComputeShader GrassCompute;
        /// <summary>How far grass grows from the camera (m); it thins out from
        /// GrassThinFrom, and shrinks away over the last 30%.</summary>
        public float GrassDistance = 60f, GrassThinFrom = 14f;
        /// <summary>The scatter grid's cell (m), and the chance of a clump in a cell on
        /// full grass.</summary>
        public float GrassCell = 0.3f, GrassDensity = 0.75f;
        /// <summary>The share of clumps left at the far edge (those left are widened).</summary>
        public float GrassFarKeep = 0.3f;
        /// <summary>A clump's height (m; on meadow up to a third taller), and its blades'
        /// width (x the mesh's).</summary>
        public Vector2 GrassHeight = new Vector2(0.18f, 0.34f), GrassWidth = new Vector2(0.8f, 1.25f);
        /// <summary>Small rock clusters on the open ground (RockScatter), each drawn
        /// while at least this fraction of the screen high: a 1.2 m rock to ~300 m,
        /// as far as the Bevy client draws them.</summary>
        public Mesh RockMesh;
        public Material RockMaterial;
        public float RockCullHeight = 0.004f;

        [Header("Cover")]
        /// <summary>A model per cover kind (LtCode.Cover*), each fitted to its box:
        /// normalized to a unit box, x along its long side, y up, front at +z.</summary>
        public CoverModel[] Cover = new CoverModel[7];
        /// <summary>Screen heights (fraction of the screen) below which a piece of
        /// cover is drawn as a plain box, and then not at all.</summary>
        public float CoverDetailHeight = 0.004f, CoverFarHeight = 0.0003f;
        /// <summary>For kinds without a model.</summary>
        public Material PlainCover;

        [Header("Kiln")]
        /// <summary>Kiln's ground, a terrain layer per <c>LT_GROUND_*</c> (sand, glass, salt,
        /// ochre, basalt, pale stone, road, pad, scree): what the plugin says each
        /// height sample is made of (K0: the project's textures, recolored).</summary>
        public TerrainLayer[] KilnLayers = new TerrainLayer[9];
        /// <summary>Kiln's blockout boxes by kind (<c>LT_COVER_BLOCK</c>, <c>ROCK</c>,
        /// <c>CRYSTAL</c>, <c>BONE</c>): plain boxes until they have art.</summary>
        public Material[] Blockout = new Material[4];
        /// <summary>The lattice's veins along the lanes (and dimmer along the ways
        /// inside sites), and the control points' rings.</summary>
        public Material VeinMaterial, PointMaterial;
        /// <summary>How wide a vein is drawn (m), and how far above the ground.</summary>
        public float VeinWidth = 1.1f, VeinLift = 0.12f;

        [Header("Collision kit (K1)")]
        /// <summary>Shields, a material per faction (amber, jade, magenta): see-through,
        /// glowing in its colors.</summary>
        public Material[] Shields = new Material[3];
        /// <summary>Force volumes' columns of light: lifts (rising), drop shafts and
        /// springs (<see cref="Connectors"/>).</summary>
        public Material LiftMaterial, DropMaterial;
    }

    [System.Serializable]
    public struct CoverModel
    {
        public Mesh Mesh;
        public Material[] Materials;
        /// <summary>The box drawn at a distance: the model's own color, flat.</summary>
        public Material Far;
        /// <summary>The model tiles along the box's long side, each tile this many
        /// times as long as the box is tall (0: stretched once, as for buildings).</summary>
        public float TileAspect;
        /// <summary>The model's front and back are its ends (a container's doors):
        /// its length runs along z, not x.</summary>
        public bool FrontOnEnd;
    }
}
