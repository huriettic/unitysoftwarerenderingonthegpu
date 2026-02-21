using System.Collections.Generic;
using UnityEngine;

public class SoftwareRenderingGPU : MonoBehaviour
{
    public struct Triangle
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

        public float w0;
        public float w1;
        public float w2;

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

    public struct clipVertex
    {
        public Vector4 v;
        public Vector2 uv;
        public int b;
    };

    public ComputeShader transformCS;
    public ComputeShader rasterCS;
    public GameObject triangles;
    public Camera cam;
    public Texture2D mainTexture;

    const int tileWidth = 8;
    const int tileHeight = 8;

    Mesh mesh;

    List<Vector3> vertices = new List<Vector3>();
    List<Vector2> textures = new List<Vector2>();
    List<int> indices = new List<int>();

    ComputeBuffer vertexBuffer;
    ComputeBuffer uvBuffer;
    ComputeBuffer indexBuffer;

    ComputeBuffer triangleBuffer;
    ComputeBuffer tileOffsetsBuffer;
    ComputeBuffer tileTriIndicesBuffer;

    ComputeBuffer triangleCounterBuffer;

    ComputeBuffer processBuffer;
    ComputeBuffer temporaryBuffer;

    int[] resolution;

    int VertexTransform;
    int RasterizeTiles;
    int TriangleBinning;
    int ClearRendering;
    int tilesX;
    int tilesY;
    int triangleCount;
    int indexCount;

    int clipStride;
    int vertexStride;
    int textureStride;
    int intStride;
    int uintStride;
    int triStride;

    RenderTexture frontColor, frontDepth;
    RenderTexture backColor, backDepth;

    public RenderTexture rtColor => frontColor;
    public RenderTexture rtDepth => frontDepth;

    void Start()
    {
        mesh = triangles.GetComponent<MeshFilter>().mesh;

        VertexTransform = transformCS.FindKernel("VertexTransform");
        RasterizeTiles = rasterCS.FindKernel("RasterizeTiles");
        TriangleBinning = rasterCS.FindKernel("TriangleBinning");
        ClearRendering = rasterCS.FindKernel("ClearRendering");

        SetMeshBuffers();
        SetResolution(Screen.width, Screen.height);
        SetTileBuffers();

        transformCS.SetBuffer(VertexTransform, "vertices", vertexBuffer);
        transformCS.SetBuffer(VertexTransform, "uvs", uvBuffer);
        transformCS.SetBuffer(VertexTransform, "indices", indexBuffer);

        transformCS.SetBuffer(VertexTransform, "process", processBuffer);
        transformCS.SetBuffer(VertexTransform, "temporary", temporaryBuffer);

        transformCS.SetBuffer(VertexTransform, "trianglesWrite", triangleBuffer);
        transformCS.SetBuffer(VertexTransform, "triangleCounter", triangleCounterBuffer);

        rasterCS.SetBuffer(ClearRendering, "triangleCounter", triangleCounterBuffer);
        rasterCS.SetBuffer(TriangleBinning, "triangleCounter", triangleCounterBuffer);
        rasterCS.SetBuffer(TriangleBinning, "trianglesRead", triangleBuffer);
        rasterCS.SetBuffer(RasterizeTiles, "trianglesRead", triangleBuffer);
    }

    void SetMeshBuffers()
    {
        mesh.GetVertices(vertices);
        mesh.GetUVs(0, textures);
        mesh.GetTriangles(indices, 0);

        indexCount = indices.Count;
        triangleCount = indexCount / 3;

        clipStride = System.Runtime.InteropServices.Marshal.SizeOf(typeof(clipVertex));
        vertexStride = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Vector3));
        textureStride = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Vector2));
        intStride = System.Runtime.InteropServices.Marshal.SizeOf(typeof(int));
        uintStride = System.Runtime.InteropServices.Marshal.SizeOf(typeof(uint));
        triStride = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Triangle));

        vertexBuffer = new ComputeBuffer(vertices.Count, vertexStride);
        uvBuffer = new ComputeBuffer(textures.Count, textureStride);
        indexBuffer = new ComputeBuffer(indices.Count, intStride);
        triangleCounterBuffer = new ComputeBuffer(1, uintStride);
        triangleBuffer = new ComputeBuffer(triangleCount * 4, triStride);
        processBuffer = new ComputeBuffer(triangleCount * 256, clipStride);
        temporaryBuffer = new ComputeBuffer(triangleCount * 256, clipStride);

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
        int maxTris = 512;

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

        transformCS.Dispatch(VertexTransform, triangleCount, 1, 1);

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
        triangleCounterBuffer?.Dispose();
        triangleBuffer?.Dispose();
        tileOffsetsBuffer?.Dispose();
        tileTriIndicesBuffer?.Dispose();
    }
}