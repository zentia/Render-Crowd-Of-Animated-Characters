using UnityEngine;

public class DynamicBoneColliderBase : MonoBehaviour
{
    public enum Direction
    {
        X, Y, Z
    }

    [Tooltip("The axis of the capsule's height.")]
    public Direction m_Direction = Direction.Y;

    [Tooltip("The center of the sphere or capsule, in the object's local space.")]
    public Vector3 m_Center = Vector3.zero;

    public enum Bound
    {
        Outside,
        Inside
    }

    [Tooltip("Constrain bones to outside bound or inside bound.")]
    public Bound m_Bound = Bound.Outside;

    public virtual void Collide(ref Vector3 particlePosition, float particleRadius)
    {
    }
}
