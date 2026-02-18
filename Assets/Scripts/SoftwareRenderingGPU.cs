using System.Collections.Generic;
using UnityEngine;

public class SoftwareRenderingGPU : MonoBehaviour
{
    public struct Triangle
    {
        public Vector2 v0;
        public Vector2 v1;
        public Vector2 v2;

        public float z0;
        public float z1;
        public float z2;

        public Vector2 uv0;
        public Vector2 uv1;
        public Vector2 uv2;

        public float w0;
        public float w1;
        public float w2;
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
    ComputeBuffer tileWriteOffsetsBuffer;
    ComputeBuffer tileTriIndicesBuffer;

    ComputeBuffer triangleCounterBuffer;

    ComputeBuffer processVerticesBuffer;
    ComputeBuffer processTexturesBuffer;
    ComputeBuffer processBoolBuffer;
    ComputeBuffer temporaryVerticesBuffer;
    ComputeBuffer temporaryTexturesBuffer;

    uint[] counter;
    int[] resolution;

    int VertexTransform;
    int RasterizeTiles;
    int TriangleBinning;
    int ClearRenderTargets;
    int tilesX;
    int tilesY;
    int triangleCount;
    int indexCount;

    int vec4Stride;
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
        ClearRenderTargets = rasterCS.FindKernel("ClearRendering");

        counter = new uint[] { 0 };

        SetMeshBuffers();
        SetResolution(Screen.width, Screen.height);
        SetTileBuffers();

        transformCS.SetBuffer(VertexTransform, "vertices", vertexBuffer);
        transformCS.SetBuffer(VertexTransform, "uvs", uvBuffer);
        transformCS.SetBuffer(VertexTransform, "indices", indexBuffer);

        transformCS.SetBuffer(VertexTransform, "processVertices", processVerticesBuffer);
        transformCS.SetBuffer(VertexTransform, "processTextures", processTexturesBuffer);
        transformCS.SetBuffer(VertexTransform, "processBool", processBoolBuffer);
        transformCS.SetBuffer(VertexTransform, "temporaryVertices", temporaryVerticesBuffer);
        transformCS.SetBuffer(VertexTransform, "temporaryTextures", temporaryTexturesBuffer);

        transformCS.SetBuffer(VertexTransform, "trianglesWrite", triangleBuffer);
        transformCS.SetBuffer(VertexTransform, "triangleCounter", triangleCounterBuffer);

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

        vec4Stride = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Vector4));
        vertexStride = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Vector3));
        textureStride = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Vector2));
        intStride = System.Runtime.InteropServices.Marshal.SizeOf(typeof(int));
        uintStride = System.Runtime.InteropServices.Marshal.SizeOf(typeof(uint));
        triStride = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Triangle));

        vertexBuffer = new ComputeBuffer(vertices.Count, vertexStride);
        uvBuffer = new ComputeBuffer(textures.Count, textureStride);
        indexBuffer = new ComputeBuffer(indices.Count, intStride);
        triangleCounterBuffer = new ComputeBuffer(1, uintStride, ComputeBufferType.Structured);
        triangleBuffer = new ComputeBuffer(triangleCount * 4, triStride);
        processVerticesBuffer = new ComputeBuffer(triangleCount * 256, vec4Stride);
        processTexturesBuffer = new ComputeBuffer(triangleCount * 256, textureStride);
        processBoolBuffer = new ComputeBuffer(triangleCount * 256, intStride);
        temporaryVerticesBuffer = new ComputeBuffer(triangleCount * 256, vec4Stride);
        temporaryTexturesBuffer = new ComputeBuffer(triangleCount * 256, textureStride);

        vertexBuffer.SetData(vertices);
        uvBuffer.SetData(textures);
        indexBuffer.SetData(indices);
        triangleCounterBuffer.SetData(counter);
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

        tileWriteOffsetsBuffer?.Dispose();
        tileTriIndicesBuffer?.Dispose();

        tileWriteOffsetsBuffer = new ComputeBuffer(tileCount, uintStride);
        tileTriIndicesBuffer = new ComputeBuffer(tileCount * maxTris, uintStride);

        rasterCS.SetBuffer(TriangleBinning, "tileWriteOffsets", tileWriteOffsetsBuffer);
        rasterCS.SetBuffer(TriangleBinning, "tileTriIndicesWrite", tileTriIndicesBuffer);

        rasterCS.SetBuffer(RasterizeTiles, "tileWriteOffsets", tileWriteOffsetsBuffer);
        rasterCS.SetBuffer(RasterizeTiles, "tileTriIndicesRead", tileTriIndicesBuffer);
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
        triangleCounterBuffer.SetData(counter);

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
        rasterCS.SetTexture(ClearRenderTargets, "colorBuffer", backColor);
        rasterCS.SetTexture(ClearRenderTargets, "depthBuffer", backDepth);
        rasterCS.SetBuffer(ClearRenderTargets, "tileWriteOffsets", tileWriteOffsetsBuffer);

        rasterCS.Dispatch(ClearRenderTargets, tilesX, tilesY, 1);

        transformCS.Dispatch(VertexTransform, triangleCount, 1, 1);

        rasterCS.Dispatch(TriangleBinning, triangleCount * 4, 1, 1);

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
        processVerticesBuffer?.Dispose();
        processTexturesBuffer?.Dispose();
        processBoolBuffer?.Dispose();
        temporaryVerticesBuffer?.Dispose();
        temporaryTexturesBuffer?.Dispose();
        triangleCounterBuffer?.Dispose();
        triangleBuffer?.Dispose();
        tileWriteOffsetsBuffer?.Dispose();
        tileTriIndicesBuffer?.Dispose();
    }
}