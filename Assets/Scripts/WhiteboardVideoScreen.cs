using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Plays a looping animated lesson atlas on the classroom whiteboard.</summary>
public sealed class WhiteboardVideoScreen : MonoBehaviour
{
    public Texture2D animationAtlas;
    public int columns = 10;
    public int rows = 12;
    public int frameCount = 120;
    public float framesPerSecond = 15f;

    private Material screenMaterial;
    private Mesh screenMesh;
    private int lastFrame = -1;
    private const int FrameWidth = 480;
    private const int FrameHeight = 270;

    private void Awake()
    {
        if (animationAtlas == null)
        {
            Debug.LogWarning("Whiteboard lesson atlas is not assigned.", this);
            enabled = false;
            return;
        }

        var boardMesh = GetComponent<MeshFilter>();
        if (boardMesh == null || boardMesh.sharedMesh == null)
        {
            Debug.LogWarning("Whiteboard needs a MeshFilter to size its video surface.", this);
            enabled = false;
            return;
        }

        var bounds = boardMesh.sharedMesh.bounds;
        float width = Mathf.Min(bounds.size.x * 0.94f, bounds.size.y * 0.88f * (16f / 9f));
        float height = width * (9f / 16f);
        var surface = new GameObject("Whiteboard lesson playback");
        surface.layer = gameObject.layer;
        surface.transform.SetParent(transform, false);
        surface.transform.localPosition = new Vector3(bounds.center.x, bounds.center.y, bounds.max.z + 0.003f);
        surface.transform.localRotation = Quaternion.identity;

        screenMesh = new Mesh { name = "Whiteboard video surface" };
        float halfWidth = width * 0.5f;
        float halfHeight = height * 0.5f;
        screenMesh.vertices = new[]
        {
            new Vector3(-halfWidth, -halfHeight, 0f),
            new Vector3( halfWidth, -halfHeight, 0f),
            new Vector3( halfWidth,  halfHeight, 0f),
            new Vector3(-halfWidth,  halfHeight, 0f)
        };
        // The board's room-facing side is viewed from behind the FBX's local +X
        // direction, so flip U to keep the text readable from the classroom.
        screenMesh.uv = new[]
        {
            new Vector2(1f, 0f),
            new Vector2(0f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f)
        };
        screenMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        screenMesh.RecalculateNormals();
        screenMesh.RecalculateBounds();
        surface.AddComponent<MeshFilter>().sharedMesh = screenMesh;

        var shader = Shader.Find("Unlit/Texture");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Standard");
        screenMaterial = new Material(shader) { name = "Whiteboard lesson screen" };
        if (screenMaterial.HasProperty("_MainTex")) screenMaterial.SetTexture("_MainTex", animationAtlas);
        if (screenMaterial.HasProperty("_BaseMap")) screenMaterial.SetTexture("_BaseMap", animationAtlas);
        var renderer = surface.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = screenMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

        animationAtlas.filterMode = FilterMode.Bilinear;
        UpdateFrame(true);
    }

    private void Update()
    {
        UpdateFrame(false);
    }

    private void UpdateFrame(bool force)
    {
        if (screenMaterial == null || animationAtlas == null) return;
        int frame = Mathf.FloorToInt(Time.time * framesPerSecond) % frameCount;
        if (!force && frame == lastFrame) return;
        lastFrame = frame;

        int column = frame % columns;
        int row = frame / columns;
        float insetX = 0.5f / animationAtlas.width;
        float insetY = 0.5f / animationAtlas.height;
        var scale = new Vector2(1f / columns - 2f * insetX, 1f / rows - 2f * insetY);
        // The atlas is arranged from its bottom row upward for Unity UV coordinates.
        var offset = new Vector2(column / (float)columns + insetX, row / (float)rows + insetY);
        screenMaterial.mainTexture = animationAtlas;
        screenMaterial.mainTextureScale = scale;
        screenMaterial.mainTextureOffset = offset;
    }

    private void OnDestroy()
    {
        if (screenMaterial != null) Destroy(screenMaterial);
        if (screenMesh != null) Destroy(screenMesh);
    }
}
