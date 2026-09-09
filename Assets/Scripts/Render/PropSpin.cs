using UnityEngine;

namespace ArmedConflict.Render
{
    /// <summary>
    /// Local-axis spin for a prop child (L13 control-tower radar).
    /// Time.deltaTime — dt varies, a bare per-tick multiply would hitch.
    /// </summary>
    public sealed class PropSpin : MonoBehaviour
    {
        public Vector3 Axis = Vector3.up;
        public float DegreesPerSecond = 48f;

        void Update()
        {
            transform.Rotate(Axis, DegreesPerSecond * Time.deltaTime, Space.Self);
        }
    }
}
