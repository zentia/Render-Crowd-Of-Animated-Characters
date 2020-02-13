using UnityEngine;
using System.Collections.Generic;
using System.Linq;

[AddComponentMenu("Dynamic Bone/Dynamic Bone")]
public class DynamicBone : MonoBehaviour
{
	[Tooltip("动态骨骼跟节点")]
    public Transform m_Root;
    [Tooltip("动态骨骼帧率")]
    public float m_UpdateRate = 60.0f;
    public enum UpdateMode
    {
        Normal,
        AnimatePhysics,
        UnscaledTime
    }
    public UpdateMode m_UpdateMode = UpdateMode.Normal;
    [Tooltip("骨头慢了多少")]
    [Range(0, 1)]
    public float m_Damping = 0.1f; // 阻尼
    public AnimationCurve m_DampingDistribute;
    [Tooltip("使每块骨头恢复到原来的方向所施加的力有多大")]
    [Range(0, 1)]
    public float m_Elasticity = 0.1f; // 弹力
    public AnimationCurve m_ElasticityDistribute;
    [Tooltip("多少骨头的原始方位被保留了下来")]
    [Range(0, 1)]
    public float m_Stiffness = 0.1f; // 坚硬
    public AnimationCurve m_StiffnessDistribute;
    [Tooltip("在物理模拟中，角色的位置变化被忽略了多少")]
    [Range(0, 1)]
    public float m_Inert; // 惯性
    public AnimationCurve m_InertDistribute;
    [Tooltip("每块骨头可以是一个球体，用来与对撞机碰撞。半径描述球体的大小")]
    public float m_Radius;
    public AnimationCurve m_RadiusDistribute;
    [Tooltip("如果末端长度不为0，则在转换层次结构的末端生成一个额外的骨骼")]
    public float m_EndLength;
    [Tooltip("如果末端偏移量不为零，则在转换层次结构的末端生成一个额外的骨骼")]
    public Vector3 m_EndOffset = Vector3.zero;
    [Tooltip("这个力作用于骨头。施加在角色初始姿态上的部分力被抵消了")]
    public Vector3 m_Gravity = Vector3.zero;
    [Tooltip("The force apply to bones.")]
    public Vector3 m_Force = Vector3.zero;
    [Tooltip("Collider objects interact with the bones.")]
    public List<DynamicBoneColliderBase> m_ColliderList;
    [Tooltip("骨骼排除在物理模拟之外")]
    public List<Transform> m_Exclusions;

    public enum FreezeAxis
    {
        None, X, Y, Z
    }
	[Tooltip("约束骨骼在指定的平面上移动")]
    public FreezeAxis m_FreezeAxis = FreezeAxis.None;
	
	[Tooltip("如果角色远离摄像机或播放器，则自动禁用物理模拟")]
    public bool m_DistantDisable;
    public Transform m_ReferenceObject;
    public float m_DistanceToObject = 20;

    private Vector3 m_LocalGravity = Vector3.zero;
    private Vector3 m_ObjectMove = Vector3.zero;
    private Vector3 m_ObjectPrevPosition = Vector3.zero;
    private float m_BoneTotalLength;
    private float m_ObjectScale = 1.0f;
    private float m_Time;
    private float m_Weight = 1.0f;
    private bool m_DistantDisabled;

    private class Particle
    {
        public Transform m_Transform;
        public int m_ParentIndex = -1;
        public float m_Damping;
        public float m_Elasticity;
        public float m_Stiffness;
        public float m_Inert;
        public float m_Radius;
        public float m_BoneLength;

        public Vector3 m_Position = Vector3.zero;
        public Vector3 m_PrevPosition = Vector3.zero;
        public Vector3 m_EndOffset = Vector3.zero;
        public Vector3 m_InitLocalPosition = Vector3.zero;
        public Quaternion m_InitLocalRotation = Quaternion.identity;
    }

    private readonly List<Particle> m_Particles = new List<Particle>();

    private void Start()
    {
        SetupParticles();
    }
    
    private void Update()
    {
        if (m_UpdateMode == UpdateMode.AnimatePhysics)
            return;
        
        if (m_Weight > 0 && !(m_DistantDisable && m_DistantDisabled))
            InitTransforms();
    }

    private void LateUpdate()
    {
        if (m_DistantDisable)
            CheckDistance();

        if (m_Weight > 0 && !(m_DistantDisable && m_DistantDisabled))
        {
            UpdateDynamicBones(Time.deltaTime);
        }
    }

    private void CheckDistance()
    {
        var rt = m_ReferenceObject;
        if (rt == null && Camera.main != null)
            rt = Camera.main.transform;
        if (rt == null)
        {
            return;
        }
        var d = (rt.position - transform.position).sqrMagnitude;
        var disable = d > m_DistanceToObject * m_DistanceToObject;
        if (disable == m_DistantDisabled)
        {
            return;
        }
        if (!disable)
            ResetParticlesPosition();
        m_DistantDisabled = disable;
    }

    private void OnEnable()
    {
        ResetParticlesPosition();
    }

    private void OnDisable()
    {
        InitTransforms();
    }

    private void OnValidate()
    {
        m_UpdateRate = Mathf.Max(m_UpdateRate, 0);
        m_Damping = Mathf.Clamp01(m_Damping);
        m_Elasticity = Mathf.Clamp01(m_Elasticity);
        m_Stiffness = Mathf.Clamp01(m_Stiffness);
        m_Inert = Mathf.Clamp01(m_Inert);
        m_Radius = Mathf.Max(m_Radius, 0);
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            return;
        }
        InitTransforms();
        SetupParticles();
#endif
    }

    private void OnDrawGizmosSelected()
    {
        if (!enabled || m_Root == null)
            return;

        if (Application.isEditor && !Application.isPlaying && transform.hasChanged)
        {
            InitTransforms();
            SetupParticles();
        }

        Gizmos.color = Color.white;
        for (int i = 0; i < m_Particles.Count; ++i)
        {
            Particle p = m_Particles[i];
            if (p.m_ParentIndex >= 0)
            {
                Particle p0 = m_Particles[p.m_ParentIndex];
                Gizmos.DrawLine(p.m_Position, p0.m_Position);
            }
            if (p.m_Radius > 0)
                Gizmos.DrawWireSphere(p.m_Position, p.m_Radius * m_ObjectScale);
        }
    }

    public void SetWeight(float w)
    {
        if (m_Weight != w)
        {
            if (w == 0)
                InitTransforms();
            else if (m_Weight == 0)
                ResetParticlesPosition();
            m_Weight = w;
        }
    }

    private void UpdateDynamicBones(float t)
    {
        if (m_Root == null)
            return;

        m_ObjectScale = Mathf.Abs(transform.lossyScale.x);
        var position = transform.position;
        m_ObjectMove = position - m_ObjectPrevPosition;
        m_ObjectPrevPosition = position;

        var loop = 1;
        if (m_UpdateRate > 0)
        {
            var dt = 1.0f / m_UpdateRate;
            m_Time += t;
            loop = 0;

            while (m_Time >= dt)
            {
                m_Time -= dt;
                if (++loop < 3)
                {
                    continue;
                }
                m_Time = 0;
                break;
            }
        }

        if (loop > 0)
        {
            for (var i = 0; i < loop; ++i)
            {
                var force = m_Gravity;
                var fdir = m_Gravity.normalized;
                var rf = m_Root.TransformDirection(m_LocalGravity);
                var pf = fdir * Mathf.Max(Vector3.Dot(rf, fdir), 0);	// project current gravity to rest gravity
                force -= pf;	// remove projected gravity
                force = (force + m_Force) * m_ObjectScale;

                foreach (var p in m_Particles)
                {
                    if (p.m_ParentIndex >= 0)
                    {
                        // verlet integration
                        var v = p.m_Position - p.m_PrevPosition;
                        var rmove = m_ObjectMove * p.m_Inert;
                        p.m_PrevPosition = p.m_Position + rmove;
                        p.m_Position += v * (1 - p.m_Damping) + force + rmove;
                    }
                    else
                    {
                        p.m_PrevPosition = p.m_Position;
                        p.m_Position = p.m_Transform.position;
                    }
                }
                var movePlane = new Plane();

                for (var k = 1; k < m_Particles.Count; k++)
                {
                    var p = m_Particles[k];
                    var p0 = m_Particles[p.m_ParentIndex];

                    var restLen = p.m_Transform != null ? (p0.m_Transform.position - p.m_Transform.position).magnitude : p0.m_Transform.localToWorldMatrix.MultiplyVector(p.m_EndOffset).magnitude;
                    
                    // keep shape
                    var stiffness = Mathf.Lerp(1.0f, p.m_Stiffness, m_Weight);
                    if (stiffness > 0 || p.m_Elasticity > 0)
                    {
                        var m0 = p0.m_Transform.localToWorldMatrix;
                        m0.SetColumn(3, p0.m_Position);
                        Vector3 restPos;
                        if (p.m_Transform != null)
                            restPos = m0.MultiplyPoint3x4(p.m_Transform.localPosition);
                        else
                            restPos = m0.MultiplyPoint3x4(p.m_EndOffset);

                        Vector3 d = restPos - p.m_Position;
                        p.m_Position += d * p.m_Elasticity;

                        if (stiffness > 0)
                        {
                            d = restPos - p.m_Position;
                            float len = d.magnitude;
                            float maxlen = restLen * (1 - stiffness) * 2;
                            if (len > maxlen)
                                p.m_Position += d * ((len - maxlen) / len);
                        }
                    }

                    // collide
                    if (m_ColliderList != null)
                    {
                        float particleRadius = p.m_Radius * m_ObjectScale;
                        for (int j = 0; j < m_ColliderList.Count; ++j)
                        {
                            DynamicBoneColliderBase c = m_ColliderList[j];
                            if (c != null && c.enabled)
                                c.Collide(ref p.m_Position, particleRadius);
                        }
                    }

                    // freeze axis, project to plane 
                    if (m_FreezeAxis != FreezeAxis.None)
                    {
                        switch (m_FreezeAxis)
                        {
                            case FreezeAxis.X:
                                movePlane.SetNormalAndPosition(p0.m_Transform.right, p0.m_Position);
                                break;
                            case FreezeAxis.Y:
                                movePlane.SetNormalAndPosition(p0.m_Transform.up, p0.m_Position);
                                break;
                            case FreezeAxis.Z:
                                movePlane.SetNormalAndPosition(p0.m_Transform.forward, p0.m_Position);
                                break;
                        }
                        p.m_Position -= movePlane.normal * movePlane.GetDistanceToPoint(p.m_Position);
                    }

                    // keep length
                    Vector3 dd = p0.m_Position - p.m_Position;
                    float leng = dd.magnitude;
                    if (leng > 0)
                        p.m_Position += dd * ((leng - restLen) / leng);
                }
                m_ObjectMove = Vector3.zero;
            }
        }
        else
        {
            SkipUpdateParticles();
        }
        for (var i = 1; i < m_Particles.Count; ++i)
        {
            var p = m_Particles[i];
            var p0 = m_Particles[p.m_ParentIndex];

            if (p0.m_Transform.childCount <= 1)		// do not modify bone orientation if has more then one child
            {
                var v = p.m_Transform != null ? p.m_Transform.localPosition:p.m_EndOffset;
                var v2 = p.m_Position - p0.m_Position;
                var rot = Quaternion.FromToRotation(p0.m_Transform.TransformDirection(v), v2);
                p0.m_Transform.rotation = rot * p0.m_Transform.rotation;
            }
            if (p.m_Transform != null)
                p.m_Transform.position = p.m_Position;
        }
    }

    private void SetupParticles()
    {
        m_Particles.Clear();
        if (m_Root == null)
            return;

        m_LocalGravity = m_Root.InverseTransformDirection(m_Gravity);
        m_ObjectScale = Mathf.Abs(transform.lossyScale.x);
        m_ObjectPrevPosition = transform.position;
        m_ObjectMove = Vector3.zero;
        m_BoneTotalLength = 0;
        AppendParticles(m_Root, -1, 0);
        UpdateParameters();
    }

    private void AppendParticles(Transform b, int parentIndex, float boneLength)
    {
        var p = new Particle
        {
            m_Transform = b,
            m_ParentIndex = parentIndex
        };
        if (b != null)
        {
            p.m_Position = p.m_PrevPosition = b.position;
            p.m_InitLocalPosition = b.localPosition;
            p.m_InitLocalRotation = b.localRotation;
        }
        else 	// end bone
        {
            var pb = m_Particles[parentIndex].m_Transform;
            if (m_EndLength > 0)
            {
                var ppb = pb.parent;
                if (ppb != null)
                    p.m_EndOffset = pb.InverseTransformPoint((pb.position * 2 - ppb.position)) * m_EndLength;
                else
                    p.m_EndOffset = new Vector3(m_EndLength, 0, 0);
            }
            else
            {
                p.m_EndOffset = pb.InverseTransformPoint(transform.TransformDirection(m_EndOffset) + pb.position);
            }
            p.m_Position = p.m_PrevPosition = pb.TransformPoint(p.m_EndOffset);
        }

        if (parentIndex >= 0)
        {
            boneLength += (m_Particles[parentIndex].m_Transform.position - p.m_Position).magnitude;
            p.m_BoneLength = boneLength;
            m_BoneTotalLength = Mathf.Max(m_BoneTotalLength, boneLength);
        }

        var index = m_Particles.Count;
        m_Particles.Add(p);

        if (b == null)
        {
            return;
        }
        for (var i = 0; i < b.childCount; ++i)
        {
            var exclude = false;
            if (m_Exclusions != null)
            {
                exclude = m_Exclusions.Exists(e => b.GetChild(i));
            }
            if (!exclude)
                AppendParticles(b.GetChild(i), index, boneLength);
            else if (m_EndLength > 0 || m_EndOffset != Vector3.zero)
                AppendParticles(null, index, boneLength);
        }

        if (b.childCount == 0 && (m_EndLength > 0 || m_EndOffset != Vector3.zero))
            AppendParticles(null, index, boneLength);
    }

    public void UpdateParameters()
    {
        if (m_Root == null)
            return;

        m_LocalGravity = m_Root.InverseTransformDirection(m_Gravity);

        foreach (var p in m_Particles)
        {
            p.m_Damping = m_Damping;
            p.m_Elasticity = m_Elasticity;
            p.m_Stiffness = m_Stiffness;
            p.m_Inert = m_Inert;
            p.m_Radius = m_Radius;

            if (m_BoneTotalLength > 0)
            {
                var a = p.m_BoneLength / m_BoneTotalLength;
                if (m_DampingDistribute != null && m_DampingDistribute.keys.Length > 0)
                    p.m_Damping *= m_DampingDistribute.Evaluate(a);
                if (m_ElasticityDistribute != null && m_ElasticityDistribute.keys.Length > 0)
                    p.m_Elasticity *= m_ElasticityDistribute.Evaluate(a);
                if (m_StiffnessDistribute != null && m_StiffnessDistribute.keys.Length > 0)
                    p.m_Stiffness *= m_StiffnessDistribute.Evaluate(a);
                if (m_InertDistribute != null && m_InertDistribute.keys.Length > 0)
                    p.m_Inert *= m_InertDistribute.Evaluate(a);
                if (m_RadiusDistribute != null && m_RadiusDistribute.keys.Length > 0)
                    p.m_Radius *= m_RadiusDistribute.Evaluate(a);
            }

            p.m_Damping = Mathf.Clamp01(p.m_Damping);
            p.m_Elasticity = Mathf.Clamp01(p.m_Elasticity);
            p.m_Stiffness = Mathf.Clamp01(p.m_Stiffness);
            p.m_Inert = Mathf.Clamp01(p.m_Inert);
            p.m_Radius = Mathf.Max(p.m_Radius, 0);
        }
    }

    private void InitTransforms()
    {
        foreach (var p in m_Particles.Where(pa=>pa.m_Transform != null))
        {
            p.m_Transform.localPosition = p.m_InitLocalPosition;
            p.m_Transform.localRotation = p.m_InitLocalRotation;
        }
    }

    void ResetParticlesPosition()
    {
        for (int i = 0; i < m_Particles.Count; ++i)
        {
            Particle p = m_Particles[i];
            if (p.m_Transform != null)
            {
                p.m_Position = p.m_PrevPosition = p.m_Transform.position;
            }
            else	// end bone
            {
                Transform pb = m_Particles[p.m_ParentIndex].m_Transform;
                p.m_Position = p.m_PrevPosition = pb.TransformPoint(p.m_EndOffset);
            }
        }
        m_ObjectPrevPosition = transform.position;
    }

    // only update stiffness and keep bone length
    void SkipUpdateParticles()
    {
        for (int i = 0; i < m_Particles.Count; ++i)
        {
            Particle p = m_Particles[i];
            if (p.m_ParentIndex >= 0)
            {
                p.m_PrevPosition += m_ObjectMove;
                p.m_Position += m_ObjectMove;

                Particle p0 = m_Particles[p.m_ParentIndex];

                float restLen;
                if (p.m_Transform != null)
                    restLen = (p0.m_Transform.position - p.m_Transform.position).magnitude;
                else
                    restLen = p0.m_Transform.localToWorldMatrix.MultiplyVector(p.m_EndOffset).magnitude;

                // keep shape
                float stiffness = Mathf.Lerp(1.0f, p.m_Stiffness, m_Weight);
                if (stiffness > 0)
                {
                    Matrix4x4 m0 = p0.m_Transform.localToWorldMatrix;
                    m0.SetColumn(3, p0.m_Position);
                    Vector3 restPos;
                    if (p.m_Transform != null)
                        restPos = m0.MultiplyPoint3x4(p.m_Transform.localPosition);
                    else
                        restPos = m0.MultiplyPoint3x4(p.m_EndOffset);

                    Vector3 d = restPos - p.m_Position;
                    float len = d.magnitude;
                    float maxlen = restLen * (1 - stiffness) * 2;
                    if (len > maxlen)
                        p.m_Position += d * ((len - maxlen) / len);
                }

                // keep length
                Vector3 dd = p0.m_Position - p.m_Position;
                float leng = dd.magnitude;
                if (leng > 0)
                    p.m_Position += dd * ((leng - restLen) / leng);
            }
            else
            {
                p.m_PrevPosition = p.m_Position;
                p.m_Position = p.m_Transform.position;
            }
        }
    }

    private static Vector3 MirrorVector(Vector3 v, Vector3 axis)
    {
        return v - axis * (Vector3.Dot(v, axis) * 2);
    }
}
