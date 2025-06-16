using UnityEngine;
using UnityEngine.UI;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Perception.GroundTruth;
using UnityEngine.Perception.GroundTruth.Sensors.Channels;
using System.Threading.Tasks;

public class DepthCameraManager : MonoBehaviour
{
    [Header("References")]
    public PerceptionCamera pc;
    public RawImage previewUI;

    [Header("Visualisation")]
    [Tooltip("Ширина цветового окна, м. 0 = авто (min..max)")]
    public float windowSize = 2f;
    [Tooltip("Гамма-коррекция контраста. 1 = линейно")]
    public float gamma = 0.7f;

    DepthChannel depthCh;
    /// <summary>
    /// null -> camera is ready to capture
    /// not null -> some image is in process, reject any incoming requests
    /// </summary>
    public TaskCompletionSource<NativeArray<float4>> ExternalRequestSource = null;

    void Start()
    {
        pc.captureTriggerMode = UnityEngine.Perception.GroundTruth.DataModel.CaptureTriggerMode.Manual;
        depthCh = pc.EnableChannel<DepthChannel>();
        depthCh.outputTextureReadback += OnDepth;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        { 
            pc.RequestCapture(); 
        }
    }
    public void RequestCaptureExternal()
    {
        pc.RequestCapture(); 
    }
    void OnDepth(int frame, NativeArray<float4> data)
    {
        if (ExternalRequestSource != null)
        {
            ExternalRequestSource.SetResult(data);
            ExternalRequestSource = null;
        }

        int w = depthCh.outputTexture.width;
        int h = depthCh.outputTexture.height;

        // --- ищем фактический min/max ---
        float zMinAct = float.MaxValue, zMaxAct = -1f;
        foreach (var p in data)
        {
            float z = p.x;
            if (z <= 0f) continue;
            if (z < zMinAct) zMinAct = z;
            if (z > zMaxAct) zMaxAct = z;
        }
        if (zMaxAct < 0f) { Debug.LogWarning("Все пиксели = фон"); return; }

        // --- задаём цветовое окно ---
        float zMin = zMinAct;
        float zMax = (windowSize > 0f) ? zMinAct + windowSize : zMaxAct;
        if (math.abs(zMax - zMin) < 0.01f) zMax = zMin + 0.01f; // защита

        // --- раскрашиваем ---
        var colors = new Color32[w * h];
        for (int i = 0; i < data.Length; ++i)
        {
            float z = data[i].x;

            // фон или за окном → чёрный
            if (z <= 0f || z > zMax)
            {
                colors[i] = new Color32(0, 0, 0, 255);
                continue;
            }

            float t = math.saturate((z - zMin) / (zMax - zMin));  // 0..1
            if (gamma != 1f) t = math.pow(t, gamma);              // контраст
            Color c = Color.HSVToRGB(0.66f - 0.66f * t, 1f, 1f);  // синий→красный
            colors[i] = c;
        }

        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.SetPixels32(colors);
        tex.Apply();
        previewUI.texture = tex;

        Debug.Log($"Frame {frame}   window [{zMin:F2} .. {zMax:F2}]  γ={gamma}");
    }
}
