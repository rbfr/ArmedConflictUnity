using UnityEngine;

namespace ArmedConflict.Render
{
    /// <summary>
    /// A unit quad, and GameObjects built on it — the replacement for
    /// <c>GameObject.CreatePrimitive</c> anywhere the GAME builds geometry at runtime.
    ///
    /// CreatePrimitive always attaches a Collider, and IL2CPP MANAGED STRIPPING removes collider
    /// classes from a build that never references them. This game has no physics at all, so
    /// `MeshCollider` was stripped, and the first runtime CreatePrimitive on device logged
    /// "Can't add component because class 'MeshCollider' doesn't exist!" and then threw
    /// ArgumentNullException on the Destroy of the collider that was never added — taking the
    /// whole level build down with it. It could not show up before, because every primitive used
    /// to be made in the EDITOR, where nothing is stripped.
    ///
    /// Building the mesh here fixes it at the root rather than guarding the null: a collider on
    /// a backdrop quad was never wanted in the first place.
    ///
    /// The vertex layout deliberately MATCHES Unity's built-in Quad, normal facing -Z. Every
    /// caller's 180° turn to face the camera, and the scorch mark's 90° lie-flat, stay correct.
    /// </summary>
    public static class QuadMesh
    {
        static Mesh shared;

        public static Mesh Shared
        {
            get
            {
                if (shared != null) return shared;
                shared = new Mesh
                {
                    name = "UnitQuad",
                    vertices = new[]
                    {
                        new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                        new Vector3(-0.5f,  0.5f, 0f), new Vector3(0.5f,  0.5f, 0f),
                    },
                    uv = new[]
                    {
                        new Vector2(0f, 0f), new Vector2(1f, 0f),
                        new Vector2(0f, 1f), new Vector2(1f, 1f),
                    },
                    normals = new[]
                    {
                        new Vector3(0f, 0f, -1f), new Vector3(0f, 0f, -1f),
                        new Vector3(0f, 0f, -1f), new Vector3(0f, 0f, -1f),
                    },
                    triangles = new[] { 0, 2, 1, 2, 3, 1 },
                };
                shared.RecalculateBounds();
                // Survives a level change: LevelScenery destroys what it owns, and this is shared.
                shared.hideFlags = HideFlags.HideAndDontSave;
                return shared;
            }
        }

        /// <summary>A quad GameObject with no collider, parented and ready for a material.</summary>
        public static GameObject Create(string name, Transform parent, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = Shared;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }
    }
}
