using System;
using System.Collections;
using System.Globalization;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
// Для rosbridge подписки (см. пункт 2)
// using RosSharp.RosBridgeClient;

[Serializable]
public class MoveResponse
{
    public bool success;
    public string error;
}

[Serializable]
public class MovePayload
{
    public float x, y, z, qx, qy, qz, qw;
    public void CopyFrom(MovePayload other)
    {
        x = other.x; y = other.y; z = other.z;
        qx = other.qx; qy = other.qy; qz = other.qz; qw = other.qw;
    }
    public void Add(MovePayload other)
    {
        x += other.x; y += other.y; z += other.z;
        qx += other.qx; qy += other.qy; qz += other.qz; qw += other.qw;
    }
}
    public class JointAPI : MonoBehaviour
{
    [Header("REST endpoint ROS")]
    public string url = "http://localhost:5000/move";

    private float px = -0.5f, py = 0, pz = 1.5f;
    private float rqx = 0, rqy = 0, rqz = 0, rqw = 1;

    private string sx, sy, sz, sqx, sqy, sqz, sqw;
    public MovePayload MovePayload = new();

    void Awake()
    {
        // Инициализируем строки из float-значений
        sx = px.ToString("F2", CultureInfo.InvariantCulture);
        sy = py.ToString("F2", CultureInfo.InvariantCulture);
        sz = pz.ToString("F2", CultureInfo.InvariantCulture);
        sqx = rqx.ToString("F2", CultureInfo.InvariantCulture);
        sqy = rqy.ToString("F2", CultureInfo.InvariantCulture);
        sqz = rqz.ToString("F2", CultureInfo.InvariantCulture);
        sqw = rqw.ToString("F2", CultureInfo.InvariantCulture);
    }
    private void Start()
    {
        MovePayload movePayload = new MovePayload
        {
            x = px,
            y = py,
            z = pz,
            qx = rqx,
            qy = rqy,
            qz = rqz,
            qw = rqw
        };
        StartCoroutine(SendMoveRequest(
            ));
    }
    /*void OnGUI()
    {
        GUI.skin.label.fontSize = 18;
        GUI.skin.textField.fontSize = 16;
        GUI.skin.button.fontSize = 18;
        GUI.skin.window.fontSize = 20;

        GUILayout.BeginArea(new Rect(20, 20, 400, 600), "KUKA Control", GUI.skin.window);
        GUILayout.Space(10);

        // Отрисуем спиннер для каждого поля
        sx = SpinnerField("Target X:", sx, ref px);
        sy = SpinnerField("Target Y:", sy, ref py);
        sz = SpinnerField("Target Z:", sz, ref pz);

        GUILayout.Space(10);

        sqx = SpinnerField("Quat X:", sqx, ref rqx);
        sqy = SpinnerField("Quat Y:", sqy, ref rqy);
        sqz = SpinnerField("Quat Z:", sqz, ref rqz);
        sqw = SpinnerField("Quat W:", sqw, ref rqw);

        GUILayout.Space(15);
        if (GUILayout.Button("GO", GUILayout.Height(40)))
        {
            // Используем уже обновлённые float-значения
            MovePayload.x = px;
            MovePayload.y = py;
            MovePayload.z = pz;
            MovePayload.qx = rqx;
            MovePayload.qy = rqy;
            MovePayload.qz = rqz;
            MovePayload.qw = rqw;
            // Отправляем запрос на сервер
            StartCoroutine(SendMoveRequest(
            ));
        }

        GUILayout.EndArea();
    }*/

    private string SpinnerField(string label, string text, ref float value)
    {
        const float step = 0.01f;
        GUILayout.BeginHorizontal();

        GUILayout.Label(label, GUILayout.Width(100));

        // 1) Обработка кнопки «–»
        if (GUILayout.Button("–", GUILayout.Width(30), GUILayout.Height(30)))
        {
            value -= step;
            text = value.ToString("F2", CultureInfo.InvariantCulture);
        }

        // 2) Поле ввода — заменяем ','→'.' и парсим, если пользователь правит вручную
        string newText = GUILayout.TextField(text.Replace(',', '.'), GUILayout.Width(100), GUILayout.Height(30));
        if (newText != text)
        {
            if (float.TryParse(newText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                value = parsed;
                text = newText;
            }
        }

        // 3) Обработка кнопки «+»
        if (GUILayout.Button("+", GUILayout.Width(30), GUILayout.Height(30)))
        {
            value += step;
            text = value.ToString("F2", CultureInfo.InvariantCulture);
        }

        GUILayout.EndHorizontal();
        return text;
    }

    public IEnumerator SendMoveRequest(TaskCompletionSource<long> tcs = null)
    {
        var json = JsonUtility.ToJson(MovePayload);

        using var uwr = new UnityWebRequest(url, "POST")
        {
            uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json)),
            downloadHandler = new DownloadHandlerBuffer()
        };
        uwr.SetRequestHeader("Content-Type", "application/json");
        yield return uwr.SendWebRequest();

        if (uwr.result != UnityWebRequest.Result.Success)
        {
            Debug.Log($"HTTP Error {uwr.responseCode}: {uwr.error}");
            tcs?.SetResult(uwr.responseCode);
            yield break;
        }

        var resp = JsonUtility.FromJson<MoveResponse>(uwr.downloadHandler.text);
        if (!resp.success)
        {
            Debug.LogError($"Move failed: {resp.error}");
        }
        // Больше ничего не делаем — суставы будут обновляться через rosbridge
        tcs?.SetResult(uwr.responseCode);
    }
}
