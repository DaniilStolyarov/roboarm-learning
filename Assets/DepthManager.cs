using UnityEngine.UI;
using UnityEngine;

public class DepthSetup : MonoBehaviour
{
    [Header("Links")]
    public Camera depthCamera;
    public RawImage depthPreview;

    [Header("Resolution")]
    public int width = 640;
    public int height = 480;

    RenderTexture depthRT;

    void Awake()
    {
        // ➊ Создаём RT в формате RFloat (1 канал, 32-бит float)
        depthRT = new RenderTexture(width, height, 16, RenderTextureFormat.RFloat)
        {
            enableRandomWrite = false
        };
        depthRT.Create();

        // ➋ Камера пишет в него…
        depthCamera.targetTexture = depthRT;

        // ➌ …а RawImage показывает тот же RT
        depthPreview.texture = depthRT;

        // ➍ (опционально) если в билде будет 2-й монитор, активируем
        if (Display.displays.Length > 1)
            Display.displays[1].Activate();
    }
}
