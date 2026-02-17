using UnityEngine;

[RequireComponent(typeof(Camera))]
public class SoftwareRenderingBlit : MonoBehaviour
{
    public SoftwareRenderingGPU softwareRendering;

    void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        if (softwareRendering != null && softwareRendering.rtColor != null)
        {
            Graphics.Blit(softwareRendering.rtColor, dest);
        }
        else
        {
            Graphics.Blit(src, dest);
        }
    }
}
