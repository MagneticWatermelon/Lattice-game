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

        [Header("Cover")]
        /// <summary>A model per cover kind (LtCode.Cover*), each fitted to its box:
        /// normalized to a unit box, x along its long side, y up, front at +z.</summary>
        public CoverModel[] Cover = new CoverModel[7];
        /// <summary>Plain boxes past this (m), and for kinds without a model.</summary>
        public float CoverModelDistance = 700f;
        public Material PlainCover;
    }

    [System.Serializable]
    public struct CoverModel
    {
        public Mesh Mesh;
        public Material[] Materials;
        /// <summary>The model tiles along the box's long side every this many
        /// meters (0: stretched once, as for buildings).</summary>
        public float TileLength;
    }
}
