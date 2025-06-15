using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using System.Net.WebSockets;

[Serializable]
public class JointStateMsg
{
    public string[] name;
    public float[] position;
}

[Serializable]
public class JointStateWrapper
{
    public string op;
    public string topic;
    public JointStateMsg msg;
}
public class JointListener : MonoBehaviour
{
    public string ConnectionURI;
    
    private ClientWebSocket webSocket;
    private CancellationTokenSource cancellation;

    public ArticulationBody[] joints; // 
    private async void Start()
    {
        webSocket = new ClientWebSocket();
        cancellation = new CancellationTokenSource();

        try
        {
            Uri serverUri = new Uri(ConnectionURI);
            await webSocket.ConnectAsync(serverUri, cancellation.Token);
            Debug.Log("WebSocket connected!");

            // Подписываемся на топик (если требуется явно отправлять сообщение)
            await SubscribeToTopic("/joint_states");

            // Начинаем слушать входящие сообщения
            ReceiveLoop();
        }
        catch (Exception e)
        {
            Debug.LogError($"WebSocket error: {e.Message}");
        }
    }

    private async Task SubscribeToTopic(string topic)
    {
        var subscribeJson = $"{{\"op\": \"subscribe\", \"topic\": \"{topic}\"}}";
        ArraySegment<byte> bytesToSend = new ArraySegment<byte>(Encoding.UTF8.GetBytes(subscribeJson));
        await webSocket.SendAsync(bytesToSend, WebSocketMessageType.Text, true, cancellation.Token);
        Debug.Log($"Sent subscription to topic: {topic}");
    }


    private async void ReceiveLoop()
    {
        var buffer = new byte[1024];

        try
        {
            while (webSocket.State == WebSocketState.Open)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellation.Token);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    // Debug.Log($"Received: {message}");

                    try
                    {
                        JointStateWrapper jointState = JsonUtility.FromJson<JointStateWrapper>(message);

                        // Теперь jointState.msg.position доступен
                        for (int i = 0; i < Mathf.Min(joints.Length, jointState.msg.position.Length); i++)
                        {
                            ArticulationDrive drive = joints[i].xDrive;
                            drive.target = jointState.msg.position[i] * Mathf.Rad2Deg;
                            joints[i].xDrive = drive;
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Failed to parse message: {e.Message}");
                    }
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    Debug.Log("WebSocket closed.");
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", cancellation.Token);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"WebSocket receive error: {e.Message}");
        }
    }

    private void OnDestroy()
    {
        if (webSocket != null)
        {
            cancellation.Cancel();
            webSocket.Dispose();
        }
    }
}
