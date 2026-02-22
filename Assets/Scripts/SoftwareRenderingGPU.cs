using System.Collections.Generic;
using UnityEngine;

public class SoftwareRenderingGPU : MonoBehaviour
{
    public struct TriangleScreen
    {
        public Vector2 v0;
        public Vector2 v1;
        public Vector2 v2;

        public Vector2 uv0;
        public Vector2 uv1;
        public Vector2 uv2;

        public Vector2 rwuv0;
        public Vector2 rwuv1;
        public Vector2 rwuv2;

        public float rwz0;
        public float rwz1;
        public float rwz2;

        public float rw0;
        public float rw1;
        public float rw2;

        public float a0;
        public float b0;
        public float c0;

        public float a1;
        public float b1;
        public float c1;

        public float a2;
        public float b2;
        public float c2;

        public float area;
        public float rarea;
    };

    struct TriangleNDC
    {
        public Vector3 v0;
        public Vector3 v1;
        public Vector3 v2;

        public Vector2 uv0;
        public Vector2 uv1;
        public Vector2 uv2;

        public Vector2 rwuv0;
        public Vector2 rwuv1;
        public Vector2 rwuv2;

        public float rwz0;
        public float rwz1;
        public float rwz2;

        public float rw0;
        public float rw1;
        public float rw2;
    }

    public struct clipLocal
    {
        public Vector4 v;
        public Vector2 uv;
        public int b;
    };

    public struct clipNDC
    {
        public Vector3 v;
        public Vector2 uv;
        public Vector2 rwuv;
        public float rwz;
        public float rw;
        public int b;
    };

    public ComputeShader transformCS;
    public ComputeShader rasterCS;
    public GameObject triangles;
    public Camera cam;
    public Texture2D mainTexture;

    const int tileWidth = 8;
    const int tileHeight = 8;
    const int MAX_TRIS = 512;

    Mesh mesh;

    List<Vector3> vertices = new List<Vector3>();
    List<Vector2> textures = new List<Vector2>();
    List<int> indices = new List<int>();

    ComputeBuffer vertexBuffer;
    ComputeBuffer uvBuffer;
    ComputeBuffer indexBuffer;

    ComputeBuffer triangleBuffer;
    ComputeBuffer triangleNDCBuffer;
    ComputeBuffer tileOffsetsBuffer;
    ComputeBuffer tileTriIndicesBuffer;
    ComputeBuffer triangleNDCCounterBuffer;
    ComputeBuffer triangleCounterBuffer;

    ComputeBuffer processBuffer;
    ComputeBuffer temporaryBuffer;

    ComputeBuffer processNDCBuffer;
    ComputeBuffer temporaryNDCBuffer;

    int[] resolution;

    int LocalTransform;
    int NDCTransform;
    int RasterizeTiles;
    int TriangleBinning;
    int ClearRendering;
    int tilesX;
    int tilesY;
    int triangleCount;
    int indexCount;

    int clipStride;
    int clipNDCStride;
    int vertexStride;
    int textureStride;
    int intStride;
    int uintStride;
    int triStride;
    int triNDCStride;

    RenderTexture frontColor, frontDepth;
    RenderTexture backColor, backDepth;

    public RenderTexture rtColor => frontColor;
    public RenderTexture rtDepth => frontDepth;

    void Start()
    {
        mesh = triangles.GetComponent<MeshFilter>().mesh;

        LocalTransform = transformCS.FindKernel("LocalTransform");
        NDCTransform = transformCS.FindKernel("NDCTransform");
        RasterizeTiles = rasterCS.FindKernel("RasterizeTiles");
        TriangleBinning = rasterCS.FindKernel("TriangleBinning");
        ClearRendering = rasterCS.FindKernel("ClearRendering");

        SetMeshBuffers();
        SetResolution(Screen.width, Screen.height);
        SetTileBuffers();

        transformCS.SetBuffer(LocalTransform, "vertices", vertexBuffer);
        transformCS.SetBuffer(LocalTransform, "uvs", uvBuffer);
        transformCS.SetBuffer(LocalTransform, "indices", indexBuffer);

        transformCS.SetBuffer(LocalTransform, "processLocal", processBuffer);
        transformCS.SetBuffer(LocalTransform, "temporaryLocal", temporaryBuffer);

        transformCS.SetBuffer(LocalTransform, "triangleNDCCounter", triangleNDCCounterBuffer);
        transformCS.SetBuffer(LocalTransform, "trianglesNDC", triangleNDCBuffer);

        transformCS.SetBuffer(NDCTransform, "processNDC", processNDCBuffer);
        transformCS.SetBuffer(NDCTransform, "temporaryNDC", temporaryNDCBuffer);

        transformCS.SetBuffer(NDCTransform, "trianglesNDC", triangleNDCBuffer);
        transformCS.SetBuffer(NDCTransform, "triangleNDCCounter", triangleNDCCounterBuffer);

        transformCS.SetBuffer(NDCTransform, "trianglesScreenWrite", triangleBuffer);
        transformCS.SetBuffer(NDCTransform, "triangleScreenCounter", triangleCounterBuffer);

        rasterCS.SetBuffer(ClearRendering, "triangleScreenCounter", triangleCounterBuffer);
        rasterCS.SetBuffer(ClearRendering, "triangleNDCCounter", triangleNDCCounterBuffer);

        rasterCS.SetBuffer(TriangleBinning, "triangleScreenCounter", triangleCounterBuffer);
        rasterCS.SetBuffer(TriangleBinning, "trianglesScreenRead", triangleBuffer);

        rasterCS.SetBuffer(RasterizeTiles, "trianglesScreenRead", triangleBuffer);
    }

    void SetMeshBuffers()
    {
        mesh.GetVertices(vertices);
        mesh.GetUVs(0, textures);
        mesh.GetTriangles(indices, 0);

        indexCount = indices.Count;
        triangleCount = indexCount / 3;

        clipStride = System.Runtime.InteropServices.Marshal.SizeOf(typeof(clipLocal));
        clipNDCStride = System.Runtime.InteropServices.Marshal.SizeOf(typeof(clipNDC));
        vertexStride = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Vector3));
        textureStride = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Vector2));
        intStride = System.Runtime.InteropServices.Marshal.SizeOf(typeof(int));
        uintStride = System.Runtime.InteropServices.Marshal.SizeOf(typeof(uint));
        triStride = System.Runtime.InteropServices.Marshal.SizeOf(typeof(TriangleScreen));
        triNDCStride = System.Runtime.InteropServices.Marshal.SizeOf(typeof(TriangleNDC));

        vertexBuffer = new ComputeBuffer(vertices.Count, vertexStride);
        uvBuffer = new ComputeBuffer(textures.Count, textureStride);
        indexBuffer = new ComputeBuffer(indices.Count, intStride);

        triangleCounterBuffer = new ComputeBuffer(1, uintStride);
        triangleNDCCounterBuffer = new ComputeBuffer(1, uintStride);

        triangleBuffer = new ComputeBuffer(triangleCount * 4, triStride);
        triangleNDCBuffer = new ComputeBuffer(triangleCount * 4, triNDCStride);

        processBuffer = new ComputeBuffer(triangleCount * 256, clipStride);
        temporaryBuffer = new ComputeBuffer(triangleCount * 256, clipStride);

        processNDCBuffer = new ComputeBuffer((triangleCount * 4) * 256, clipNDCStride);
        temporaryNDCBuffer = new ComputeBuffer((triangleCount * 4) * 256, clipNDCStride);

        vertexBuffer.SetData(vertices);
        uvBuffer.SetData(textures);
        indexBuffer.SetData(indices);
    }

    void SetResolution(int width, int height)
    {
        frontColor?.Release();
        frontDepth?.Release();
        backColor?.Release();
        backDepth?.Release();

        frontColor = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBFloat);
        frontColor.enableRandomWrite = true;
        frontColor.Create();

        frontDepth = new RenderTexture(width, height, 0, RenderTextureFormat.RFloat);
        frontDepth.enableRandomWrite = true;
        frontDepth.Create();

        backColor = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBFloat);
        backColor.enableRandomWrite = true;
        backColor.Create();

        backDepth = new RenderTexture(width, height, 0, RenderTextureFormat.RFloat);
        backDepth.enableRandomWrite = true;
        backDepth.Create();

        tilesX = Mathf.CeilToInt((float)width / tileWidth);
        tilesY = Mathf.CeilToInt((float)height / tileHeight);

        resolution = new int[] { width, height };
    }

    void SetTileBuffers()
    {
        int tileCount = tilesX * tilesY;
        int maxTris = MAX_TRIS;

        tileOffsetsBuffer?.Dispose();
        tileTriIndicesBuffer?.Dispose();

        tileOffsetsBuffer = new ComputeBuffer(tileCount, uintStride);
        tileTriIndicesBuffer = new ComputeBuffer(tileCount * maxTris, uintStride);

        rasterCS.SetBuffer(ClearRendering, "tileOffsets", tileOffsetsBuffer);

        rasterCS.SetBuffer(TriangleBinning, "tileOffsets", tileOffsetsBuffer);
        rasterCS.SetBuffer(TriangleBinning, "tileTriIndices", tileTriIndicesBuffer);

        rasterCS.SetBuffer(RasterizeTiles, "tileOffsets", tileOffsetsBuffer);
        rasterCS.SetBuffer(RasterizeTiles, "tileTriIndices", tileTriIndicesBuffer);
    }

    void Update()
    {
        if (frontColor == null ||
            frontColor.width != Screen.width || frontColor.height != Screen.height)
        {
            SetResolution(Screen.width, Screen.height);
            SetTileBuffers();
        }

        RenderDispatch();
    }

    void RenderDispatch()
    {
        Matrix4x4 view = cam.worldToCameraMatrix;
        Matrix4x4 proj = GL.GetGPUProjectionMatrix(cam.projectionMatrix, false);
        Matrix4x4 localToWorld = triangles.transform.localToWorldMatrix;

        transformCS.SetMatrix("view", view);
        transformCS.SetMatrix("proj", proj);
        transformCS.SetMatrix("localToWorld", localToWorld);
        transformCS.SetInts("resolution", resolution);
        transformCS.SetInt("triangleCount", triangleCount);

        rasterCS.SetInt("tilesX", tilesX);
        rasterCS.SetInt("tilesY", tilesY);
        rasterCS.SetInts("resolution", resolution);

        rasterCS.SetTexture(ClearRendering, "colorBuffer", backColor);
        rasterCS.SetTexture(ClearRendering, "depthBuffer", backDepth);

        rasterCS.Dispatch(ClearRendering, tilesX, tilesY, 1);

        transformCS.Dispatch(LocalTransform, triangleCount, 1, 1);

        transformCS.Dispatch(NDCTransform, triangleCount * 4, 1, 1);

        rasterCS.Dispatch(TriangleBinning, tilesX, tilesY, 1);

        rasterCS.SetTexture(RasterizeTiles, "colorBuffer", backColor);
        rasterCS.SetTexture(RasterizeTiles, "depthBuffer", backDepth);
        rasterCS.SetTexture(RasterizeTiles, "_MainTex", mainTexture);

        rasterCS.Dispatch(RasterizeTiles, tilesX, tilesY, 1);

        SwapBuffers();
    }

    void SwapBuffers()
    {
        RenderTexture tc = frontColor;
        frontColor = backColor;
        backColor = tc;

        RenderTexture td = frontDepth;
        frontDepth = backDepth;
        backDepth = td;
    }

    void OnDestroy()
    {
        vertexBuffer?.Dispose();
        uvBuffer?.Dispose();
        indexBuffer?.Dispose();
        processBuffer?.Dispose();
        temporaryBuffer?.Dispose();
        processNDCBuffer?.Dispose();
        temporaryNDCBuffer?.Dispose();
        triangleCounterBuffer?.Dispose();
        triangleNDCCounterBuffer?.Dispose();
        triangleBuffer?.Dispose();
        triangleNDCBuffer?.Dispose();
        tileOffsetsBuffer?.Dispose();
        tileTriIndicesBuffer?.Dispose();
    }
}