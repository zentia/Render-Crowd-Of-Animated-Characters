/*
 * http://www.chenjd.me
 * 用来烘焙动作贴图。烘焙对象使用animation组件，并且在导入时设置Rig为Legacy
 */
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
public struct AnimData
{
    private int vertexCount;
    public readonly int mapWidth;
    public readonly List<AnimationState> animClips;
    public readonly string name;
    private readonly  Animation animation;
    private readonly SkinnedMeshRenderer skin;
    public AnimData(Animation anim, SkinnedMeshRenderer smr, string goName)
    {
        vertexCount = smr.sharedMesh.vertexCount;
        mapWidth = Mathf.NextPowerOfTwo(vertexCount);
        animClips = new List<AnimationState>(anim.Cast<AnimationState>());
        animation = anim;
        skin = smr;
        name = goName;
    }
    public void AnimationPlay(string animName)
    {
        animation.Play(animName);
    }
    public void SampleAnimAndBakeMesh(ref Mesh m)
    {
        SampleAnim();
        BakeMesh(ref m);
    }
    private void SampleAnim()
    {
        if (animation == null)
        {
            Debug.LogError("animation is null");
            return;
        }
        animation.Sample();
    }
    private void BakeMesh(ref Mesh m)
    {
        if (skin == null)
        {
            Debug.LogError("skin is null!!");
            return;
        }
        skin.BakeMesh(m);
    }
}
public struct BakedData
{
    public readonly string name;
    public readonly float animLen;
    public readonly byte[] rawAnimMap;
    public readonly int animMapWidth;
    public readonly int animMapHeight;
    public BakedData(string name, float animLen, Texture2D animMap)
    {
        this.name = name;
        this.animLen = animLen;
        animMapHeight = animMap.height;
        animMapWidth = animMap.width;
        rawAnimMap = animMap.GetRawTextureData();
    }
}
public class AnimMapBaker
{
    private AnimData? animData;
    private List<Vector3> vertices = new List<Vector3>();
    private Mesh bakedMesh;
    private readonly List<BakedData> bakedDataList = new List<BakedData>();
    public void SetAnimData(GameObject go)
    {
        if(go == null)
        {
            Debug.LogError("go is null");
            return;
        }
        var anim = go.GetComponent<Animation>();
        var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
        if(anim == null || smr == null)
        {
            Debug.LogError("anim or smr is null");
            return;
        }
        bakedMesh = new Mesh();
        animData = new AnimData(anim, smr, go.name);
    }
    public List<BakedData> Bake()
    {
        if(animData == null)
        {
            Debug.LogError("bake data is null!!");
            return bakedDataList;
        }
        foreach (var animClip in animData.Value.animClips)
        {
            if(!animClip.clip.legacy)
            {
                Debug.LogError($"{animClip.clip.name} is not legacy!!");
                continue;
            }
            BakePerAnimClip(animClip);
        }
        return bakedDataList;
    }
    private void BakePerAnimClip(AnimationState curAnim)
    {
        var curClipFrame = Mathf.ClosestPowerOfTwo((int)(curAnim.clip.frameRate * curAnim.length));
        var sampleTime = 0f;
        var perFrameTime = curAnim.length / curClipFrame;
        var animMap = new Texture2D(animData.Value.mapWidth, curClipFrame, TextureFormat.RGBAHalf, false)
        {
            name = string.Format($"{animData.Value.name}_{curAnim.name}.animMap")
        };
        animData.Value.AnimationPlay(curAnim.name);
        for (var i = 0; i < curClipFrame; i++)
        {
            curAnim.time = sampleTime;
            animData.Value.SampleAnimAndBakeMesh(ref bakedMesh);
            for (var j = 0; j < bakedMesh.vertexCount; j++)
            {
                var vertex = bakedMesh.vertices[j];
                animMap.SetPixel(j, i, new Color(vertex.x, vertex.y, vertex.z));
            }
            sampleTime += perFrameTime;
        }
        animMap.Apply();
        bakedDataList.Add(new BakedData(animMap.name, curAnim.clip.length, animMap));
    }
}