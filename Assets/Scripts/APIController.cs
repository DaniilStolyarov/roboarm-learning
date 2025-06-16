using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using WatsonWebserver;
using WatsonWebserver.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using Unity.Robotics.UrdfImporter.Control;
using System.Linq;
using Unity.Collections;
using Unity.Mathematics;
public class APIController : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private Webserver server;
    const string ApiKey = "supersecret123";
    private static readonly Queue<Action> executionQueue = new();
    void Start()
    {
        WebserverSettings settings = new WebserverSettings("127.0.0.1", 8000);
        server = new Webserver(settings, DefaultRoute);
        server.Routes.AuthenticateRequest = AuthMiddleware;
        server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/auth_check", CheckAuthentication);
        server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/arm/joints", GetJointsAngles);
        server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/arm/pose", GetPose);
        server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/arm/urdf", GetCurrentRobotURDF);
        server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/arm/default_articulation_body", GetDefaultArmArticulation);
        server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/goods/list", GetAllPickableObjects);
        server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/camera/rgb", Snapshot);
        server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/vacuum_gripper/state", GetVacuumGripperState);

        server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/scene/light_intensity", GetLightIntensity);
        server.Routes.PostAuthentication.Static.Add(HttpMethod.PUT, "/scene/light_intensity", UpdateLightIntensity);
        server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/goods/conf", GetSpawnerConfiguration);
        server.Routes.PostAuthentication.Static.Add(HttpMethod.PUT, "/goods/conf", UpdateSpawnerConfiguration);
        server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/goods/spawn", SpawnPickable);
        server.Routes.PostAuthentication.Static.Add(HttpMethod.DELETE, "/goods/remove", RemoveLastPickable);
        server.Routes.PostAuthentication.Static.Add(HttpMethod.PUT, "/arm/pose", MoveToPose);
        server.Routes.PostAuthentication.Static.Add(HttpMethod.PUT, "/arm/relative", AddToPose);

        server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/camera/depth", GetDepthData);
        server.Start();
    }

    // Update is called once per frame
    void Update()
    {
        while (executionQueue.Count > 0)
        {
            executionQueue.Dequeue()?.Invoke();
        }
    }
    public static void RunOnMainThread(Action action)
    {
        lock (executionQueue)
            executionQueue.Enqueue(action);
    }
    static async Task DefaultRoute(HttpContextBase context)
    {
        context.Response.StatusCode = 404;
        await context.Response.Send("Invalid API Request. Please check URL string");
    }

    async Task AuthMiddleware(HttpContextBase context)
    {
        string RequestApiKey = context.Request.Headers.Get("X-API-KEY");

        if (string.IsNullOrEmpty(RequestApiKey))
        {
            context.Response.StatusCode = 401;
            await context.Response.Send("API Key is missing.");
            return;
        }

        if (RequestApiKey != ApiKey)
        {
            context.Response.StatusCode = 403;
            await context.Response.Send("Invalid API Key.");
            return;
        }
    }

    async Task CheckAuthentication(HttpContextBase context)
    {
        context.Response.StatusCode = 200;
        await context.Response.Send("Authentication successful.");
    }

    async Task GetJointsAngles(HttpContextBase context)
    {
        TaskCompletionSource<JObject> tcs = new TaskCompletionSource<JObject>();
        RunOnMainThread(() =>
        {
            ArticulationBody[] joints = gameObject.GetComponent<JointListener>().joints;
            JObject jointAnglesInternal = new JObject();
            for (int i = 0; i < joints.Length; i++)
            {
                jointAnglesInternal[$"link_{i + 1}"] = joints[i].xDrive.target;
            }
            tcs.SetResult(jointAnglesInternal);
        });
        JObject jointAngles = await tcs.Task;
        context.Response.StatusCode = 200;
        context.Response.ContentType = "application/json";
        await context.Response.Send(jointAngles.ToString());
    }


    async Task GetPose(HttpContextBase context)
    {
        TaskCompletionSource<MovePayload> tcs = new TaskCompletionSource<MovePayload>();
        RunOnMainThread(() =>
        {
            MovePayload poseInternal = gameObject.GetComponent<JointAPI>().MovePayload;
            tcs.SetResult(poseInternal);
        });
        MovePayload pose = await tcs.Task;
        context.Response.StatusCode = 200;
        context.Response.ContentType = "application/json";
        await context.Response.Send(JsonConvert.SerializeObject(pose));
    }

    async Task GetCurrentRobotURDF(HttpContextBase context)
    {
        string path = Path.Combine(Application.dataPath, "Robots/KR10/kr10r1420.urdf");

        if (File.Exists(path))
        {
            string urdfContent = await File.ReadAllTextAsync(path);
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/xml"; // или "text/xml"
            await context.Response.Send(urdfContent);
        }
        else
        {
            context.Response.StatusCode = 404;
            await context.Response.Send("URDF file not found");
        }
    }

    async Task GetDefaultArmArticulation(HttpContextBase context)
    {
        TaskCompletionSource<Controller> tcs = new TaskCompletionSource<Controller>();
        RunOnMainThread(() =>
        {
            GameObject RoboArm = GameObject.Find("kuka_kr10r1420");
            Controller articulationBodyInternal = RoboArm.GetComponent<Controller>();
            tcs.SetResult(articulationBodyInternal);
        });
        Controller defaultArticulationBody = await tcs.Task;
        context.Response.StatusCode = 200;
        context.Response.ContentType = "application/json";

        var jsonArticulationBody = new { defaultArticulationBody.acceleration,
            defaultArticulationBody.control,
            defaultArticulationBody.damping,
            defaultArticulationBody.forceLimit,
            defaultArticulationBody.speed,
            defaultArticulationBody.stiffness,
            defaultArticulationBody.torque
        };
        await context.Response.Send(JsonConvert.SerializeObject(jsonArticulationBody));
    }
    public class PickableObjectDto
    {
        public Vector3Dto position { get; set; }
        public Vector3Dto scale { get; set; }
        public Vector3Dto rotation { get; set; }
        public string name { get; set; }
    }

    public class Vector3Dto
    {
        public float x { get; set; }
        public float y { get; set; }
        public float z { get; set; }
    }

    async Task GetAllPickableObjects(HttpContextBase context)
    {
        TaskCompletionSource<List<PickableObjectDto>> tcs = new TaskCompletionSource<List<PickableObjectDto>>();
        RunOnMainThread(() =>
        {
            GameObject[] pickableObjectsInternal = GameObject.FindGameObjectsWithTag("pickable");

            List<PickableObjectDto> pickableObjectsJson = pickableObjectsInternal.Select(pObj => new PickableObjectDto
            {
                position = new Vector3Dto
                {
                    x = pObj.transform.position.x,
                    y = pObj.transform.position.y,
                    z = pObj.transform.position.z
                },
                scale = new Vector3Dto
                {
                    x = pObj.transform.localScale.x,
                    y = pObj.transform.localScale.y,
                    z = pObj.transform.localScale.z
                },
                rotation = new Vector3Dto
                {
                    x = pObj.transform.rotation.eulerAngles.x,
                    y = pObj.transform.rotation.eulerAngles.y,
                    z = pObj.transform.rotation.eulerAngles.z
                },
                name = pObj.name
            }).ToList();


            tcs.SetResult(pickableObjectsJson);
        });
        List<PickableObjectDto> pickableObjects = await tcs.Task;

        context.Response.StatusCode = 200;
        context.Response.ContentType = "application/json";
        await context.Response.Send(JsonConvert.SerializeObject(pickableObjects));
    }

    async Task Snapshot(HttpContextBase ctx)
    {
        var tcs = new TaskCompletionSource<byte[]>();

        RunOnMainThread(() =>
        {
            Camera cam = Camera.main;                       // или выбери любую другую
            if (cam == null)
            {
                tcs.SetException(new Exception("No camera"));
                return;
            }

            try { tcs.SetResult(MakeSnapshot(cam)); }
            catch (Exception e) { tcs.SetException(e); }
        });

        try
        {
            byte[] img = await tcs.Task;
            ctx.Response.ContentType = "image/png";
            await ctx.Response.Send(img);
        }
        catch (Exception e)
        {
            ctx.Response.StatusCode = 500;
            await ctx.Response.Send("Snapshot error: " + e.Message);
        }
    }

    // ─────────────────────────────────────────────
    // Снимок ровно того же размера, что камера
    // ─────────────────────────────────────────────
    byte[] MakeSnapshot(Camera cam)
    {
        // 1️⃣  Определяем фактические размеры
        int w, h;

        if (cam.targetTexture != null)            // камера выводит в RenderTexture
        {
            w = cam.targetTexture.width;
            h = cam.targetTexture.height;
        }
        else                                      // обычный рендер в окно
        {
            w = cam.pixelWidth;                   // размер Game View / окна
            h = cam.pixelHeight;
            if (w == 0 || h == 0)                 // защита на случай ранних вызовов
            {
                w = Screen.width;                 // fallback
                h = Screen.height;
            }
        }

        // 2️⃣  Рендерим в временный RenderTexture
        var rt = RenderTexture.GetTemporary(w, h, 24);
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);

        try
        {
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();

            return tex.EncodeToPNG();             // <- PNG-байты
        }
        finally
        {
            cam.targetTexture = null;
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);
            Destroy(tex);
        }
    }

    async Task GetVacuumGripperState(HttpContextBase context)
    {
        int[] gripperMask = new int[] { 0, 0, 0, 0, 0, 0 };
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = 200;
        await context.Response.Send(JsonConvert.SerializeObject(gripperMask));
    }

    async Task GetLightIntensity(HttpContextBase context)
    {
        TaskCompletionSource<float> tcs = new TaskCompletionSource<float>();
        RunOnMainThread(() =>
        {
            GameObject lightObject = GameObject.Find("Directional Light");
            float intensityInternal = lightObject.GetComponent<Light>().intensity;
            tcs.SetResult(intensityInternal);
        });
        float intensity = await tcs.Task;
        context.Response.StatusCode = 200;
        context.Response.ContentType = "application/json";
        await context.Response.Send(JsonConvert.SerializeObject(new { intensity }));
    }

    async Task UpdateLightIntensity(HttpContextBase context)
    {
        if (!float.TryParse(context.Request.DataAsString, out float intensity))
        {
            context.Response.StatusCode = 400;
            await context.Response.Send("Error parsing intensity");
            return;
        }
        TaskCompletionSource<float> tcs = new TaskCompletionSource<float>();
        RunOnMainThread(() =>
        {
            GameObject lightObject = GameObject.Find("Directional Light");
            lightObject.GetComponent<Light>().intensity = intensity;
            tcs.SetResult(intensity);
        });
        await tcs.Task;
        context.Response.StatusCode = 200;
        await context.Response.Send();
    }
    public class SpawnerConfiguration
    {
        public float PositionStandard;
        public int TotalObjectsCount;
        public bool RandomizeSizes;
        public float MaxScaleFactor;
        public float MinScaleFactor;
        public bool UseRuntimeGenerator;

        public SpawnerConfiguration(float positionStandard, int totalObjectsCount, bool randomizeSizes, float maxScaleFactor, float minScaleFactor, bool useRuntimeGenerator)
        {
            PositionStandard = positionStandard;
            TotalObjectsCount = totalObjectsCount;
            RandomizeSizes = randomizeSizes;
            MaxScaleFactor = maxScaleFactor;
            MinScaleFactor = minScaleFactor;
            UseRuntimeGenerator = useRuntimeGenerator;
        }
    }

    async Task GetSpawnerConfiguration(HttpContextBase context)
    {
        TaskCompletionSource<string> tcs = new TaskCompletionSource<string>();
        RunOnMainThread(() =>
        {
            GameObject generatorObject = GameObject.Find("SamplesGenerator");
            SamplesGenerator generator = generatorObject.GetComponent<SamplesGenerator>();
            var configuration = new SpawnerConfiguration
            (
                generator.PositionStandard,
                generator.TotalObjectsCount,
                generator.RandomizeSizes,
                generator.MaxScaleFactor,
                generator.MinScaleFactor,
                generator.UseRuntimeGenerator
            );
            tcs.SetResult(JsonConvert.SerializeObject(configuration));  
        });
        context.Response.StatusCode = 200;
        context.Response.ContentType = "application/json";
        string jsonString = await tcs.Task;
        await context.Response.Send(jsonString);
    }

    async Task UpdateSpawnerConfiguration(HttpContextBase context)
    {
        SpawnerConfiguration requestConfiguration;
        try
        {
            requestConfiguration =
           JsonConvert.DeserializeObject<SpawnerConfiguration>(context.Request.DataAsString);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            context.Response.StatusCode = 400;
            await context.Response.Send("Error while parsing JSON spawner configuration");
            return;
        }
        TaskCompletionSource<string> tcs = new TaskCompletionSource<string>();
        RunOnMainThread(() =>
        {
            GameObject generatorObject = GameObject.Find("SamplesGenerator");
            SamplesGenerator generator = generatorObject.GetComponent<SamplesGenerator>();
            generator.PositionStandard = requestConfiguration.PositionStandard;
            generator.TotalObjectsCount = requestConfiguration.TotalObjectsCount;
            generator.RandomizeSizes = requestConfiguration.RandomizeSizes;
            generator.MinScaleFactor = requestConfiguration.MinScaleFactor;
            generator.UseRuntimeGenerator = requestConfiguration.UseRuntimeGenerator;
            generator.MaxScaleFactor = requestConfiguration.MaxScaleFactor; 
            tcs.SetResult("");
        });
        await tcs.Task;
        context.Response.StatusCode = 200;
        await context.Response.Send();
    }

    async Task SpawnPickable(HttpContextBase context)
    {
        TaskCompletionSource<string> tcs = new TaskCompletionSource<string>();
        RunOnMainThread(() =>
        {
            GameObject generatorObject = GameObject.Find("SamplesGenerator");
            SamplesGenerator generator = generatorObject.GetComponent<SamplesGenerator>();
            generator.SpawnObject();
            tcs.SetResult("Done.");
        });
        await tcs.Task;
        context.Response.StatusCode = 200;
        await context.Response.Send();
    }
    async Task RemoveLastPickable(HttpContextBase context)
    {
        TaskCompletionSource<string> tcs = new TaskCompletionSource<string>();
        RunOnMainThread(() =>
        {
            GameObject generatorObject = GameObject.Find("SamplesGenerator");
            SamplesGenerator generator = generatorObject.GetComponent<SamplesGenerator>();
            if (generator.SpawnedGameObjects.Count == 0)
            {
                tcs.SetException(new Exception("No pickable objects to remove."));
                return;
            }
            GameObject pickableToRemove = generator.SpawnedGameObjects.Pop();
            Destroy(pickableToRemove);
            tcs.SetResult("Done.");
        });
        try
        {
            await tcs.Task;
        }
        catch (Exception ex)
        {
            context.Response.StatusCode = 400;
            await context.Response.Send(ex.Message);
            return;
        }
        context.Response.StatusCode = 200;
        await context.Response.Send();
    }
    
    async Task MoveToPose(HttpContextBase context)
    {
        MovePayload requestPayload = new MovePayload();
        try
        {
            requestPayload = JsonConvert.DeserializeObject<MovePayload>(context.Request.DataAsString);
        }
        catch (Exception ex)
        {
            Debug.LogError(ex);
            context.Response.StatusCode = 400;
            await context.Response.Send();
            return;
        }
        TaskCompletionSource<long> tcs = new TaskCompletionSource<long>();
        RunOnMainThread(() =>
        {
            GameObject networkObject = GameObject.Find("Network");
            JointAPI jointAPI = networkObject.GetComponent<JointAPI>();
            jointAPI.MovePayload.CopyFrom(requestPayload);
            StartCoroutine(jointAPI.SendMoveRequest(tcs));
        });
        long JointAPIStatusCode = await tcs.Task;
        Debug.Log($"JointAPIStatusCode: {JointAPIStatusCode}");
        if (JointAPIStatusCode != 200)
            // скрываем статус-код сервера ROS
            context.Response.StatusCode = 400;
        else context.Response.StatusCode = 200;
        await context.Response.Send();
    }


    async Task AddToPose(HttpContextBase context)
    {
        MovePayload requestPayload = new MovePayload();
        try
        {
            requestPayload = JsonConvert.DeserializeObject<MovePayload>(context.Request.DataAsString);
        }
        catch (Exception ex)
        {
            Debug.LogError(ex);
            context.Response.StatusCode = 400;
            await context.Response.Send();
            return;
        }
        TaskCompletionSource<long> tcs = new TaskCompletionSource<long>();
        RunOnMainThread(() =>
        {
            GameObject networkObject = GameObject.Find("Network");
            JointAPI jointAPI = networkObject.GetComponent<JointAPI>();
            jointAPI.MovePayload.Add(requestPayload);
            StartCoroutine(jointAPI.SendMoveRequest(tcs));
        });
        long JointAPIStatusCode = await tcs.Task;
        Debug.Log($"JointAPIStatusCode: {JointAPIStatusCode}");
        if (JointAPIStatusCode != 200)
            // скрываем статус-код сервера ROS
            context.Response.StatusCode = 400;
        else context.Response.StatusCode = 200;
        await context.Response.Send();
    }

    async Task GetDepthData(HttpContextBase ctx)
    {
        var tcs = new TaskCompletionSource<NativeArray<float4>>();

        RunOnMainThread(() =>
        {
            GameObject DepthCamera = GameObject.FindGameObjectWithTag("depth_camera");
            DepthCameraManager depthManager = DepthCamera.GetComponent<DepthCameraManager>();
            if (depthManager.ExternalRequestSource == null)
            {
                depthManager.ExternalRequestSource = tcs;
                depthManager.RequestCaptureExternal();
            }
            else
            {
                tcs.SetException(new Exception("Depth image is already in progress. Can`t process another one!"));
            }
        });

        try
        {
            NativeArray<float4> DepthData = await tcs.Task;
            var ResponseDepth = DepthData.Select(depthElement => 2 - depthElement.x).ToArray();
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.Send(JsonConvert.SerializeObject(ResponseDepth));
        }
        catch (Exception e)
        {
            ctx.Response.StatusCode = 500;
            await ctx.Response.Send("Depth Camera Error: " + e.Message);
        }
    }
}
