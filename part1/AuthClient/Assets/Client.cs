using UnityEngine;
using UnityEngine.Networking;
using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;

public class Client : MonoBehaviour
{
    int m_hostId = -1;
    bool connected = false;

    void Start()
    {
        Application.runInBackground = true;
        NetworkTransport.Init();

        ConnectionConfig config = new ConnectionConfig();
        config.AddChannel(QosType.Reliable);
        HostTopology topology = new HostTopology(config, 1); // Just need 1 connection
#if UNITY_EDITOR
        m_hostId = NetworkTransport.AddHostWithSimulator(topology, 200, 400);
#else
        m_hostId = NetworkTransport.AddHost(topology);
#endif

        byte error;
        // Cannot tell we're connected until we receive the event at later time
        NetworkTransport.Connect(m_hostId, System.Net.IPAddress.Loopback.ToString(), 25000, 0, out error);
    }

    struct SphereData
    {
        public float lastUpdate;
        public GameObject obj;
    }
    // Remeber all spheres we spawned
    Dictionary<int, SphereData> sphereDictionary = new Dictionary<int, SphereData>();

    void OnGUI()
    {
        GUI.Label(new Rect(10, 10, 100, 20), string.Format("{0} Spheres", sphereDictionary.Count));
        if (connected) // Set when ConnectEvent received
            GUI.Label(new Rect(10, 30, 100, 20), "Connected");
        else
            GUI.Label(new Rect(10, 30, 100, 20), "Disconnected");
    }

    struct NetworkData
    {
        public int id;
        public float x;
        public float y;
        public float z;
        public float r;
        public float g;
        public float b;
    }

    void Update()
    {
        if (m_hostId == -1)
            return;
        int connectionId;
        int channelId;
        int receivedSize;
        byte error;
        byte[] buffer = new byte[1500];
        NetworkEventType networkEvent = NetworkTransport.ReceiveFromHost(m_hostId, out connectionId, out channelId, buffer, buffer.Length, out receivedSize, out error);
        switch (networkEvent)
        {
            case NetworkEventType.Nothing:
                break;
            case NetworkEventType.ConnectEvent:
                connected = true;
                break;
            case NetworkEventType.DisconnectEvent:
                connected = false;
                break;
            case NetworkEventType.DataEvent:
                //Debug.Log(string.Format("Got data size {0}", receivedSize));
                // Trim buffer from original size 1500
                Array.Resize(ref buffer, receivedSize);
                LoadSpheres(buffer);
                break;
        }

        CleanupSpheres();
    }

    void LoadSpheres(byte[] buffer)
    {
        MemoryStream stream = new MemoryStream(buffer);
        // Don't use BinaryFormatter as this stores meta data!!
        BinaryReader br = new BinaryReader(stream);

        List<NetworkData> tmpList = new List<NetworkData>();
        NetworkData tmpData;

        // Read to the end
        while (br.PeekChar() != -1)
        {
            // Deserialize
            tmpData.id = br.ReadInt32();
            tmpData.x = br.ReadSingle();
            tmpData.y = br.ReadSingle();
            tmpData.z = br.ReadSingle();
            tmpData.r = br.ReadSingle();
            tmpData.g = br.ReadSingle();
            tmpData.b = br.ReadSingle();
            tmpList.Add(tmpData);
        }

        SphereData sd;
        foreach (var networkData in tmpList)
        {
            // Do we have this sphere already?
            if (sphereDictionary.ContainsKey(networkData.id))
            {
                sd = sphereDictionary[networkData.id];
                // Update position
                sd.obj.transform.position = new Vector3(networkData.x, networkData.y, networkData.z);
                sd.lastUpdate = Time.realtimeSinceStartup;
                sphereDictionary[networkData.id] = sd;
            }
            else
            {
                // Create new object
                GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                // No rigidbody here!
                // Set position
                sphere.transform.position = new Vector3(networkData.x, networkData.y, networkData.z);
                // Set color
                sphere.GetComponent<Renderer>().material.color = new Color(networkData.r, networkData.g, networkData.b);

                sd.obj = sphere;
                sd.lastUpdate = Time.realtimeSinceStartup;
                sphereDictionary.Add(networkData.id, sd);
            }
        }
    }

    void CleanupSpheres()
    {
        // Clean up null game objects
        sphereDictionary = sphereDictionary.Where(pair => pair.Value.obj.gameObject != null).ToDictionary(pair => pair.Key, pair => pair.Value); // Ahhh Ling!

        foreach (var pair in sphereDictionary)
        {
            // Haven't heard about sphere in a while so destroy it
            if (pair.Value.obj.gameObject != null && pair.Value.lastUpdate + 1.0f < Time.realtimeSinceStartup)
                Destroy(pair.Value.obj.gameObject); // Note this becomes null only after Update
        }
    }
}
