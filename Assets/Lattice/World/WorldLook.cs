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
        public Material GrassMaterial;
        public Mesh GrassMesh;
        public float GrassDistance = 140f;
        public float GrassDensity = 1f;
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
