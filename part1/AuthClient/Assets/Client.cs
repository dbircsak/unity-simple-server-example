using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections.Generic;
using System.Linq;

public class Client : MonoBehaviour
{
    int m_hostId = -1;
    int m_serverConnectionId = -1;

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

    class SphereData
    {
        public int id;
        public float lastUpdate;
        public GameObject obj;
    }
    // Remeber all spheres we spawned
    List<SphereData> sphereList = new List<SphereData>();

    void OnGUI()
    {
        GUI.Label(new Rect(10, 10, 100, 20), string.Format("{0} Spheres", sphereList.Count));
        if (m_serverConnectionId != -1) // Set when ConnectEvent received
            GUI.Label(new Rect(10, 30, 100, 20), "Connected");
        else
            GUI.Label(new Rect(10, 30, 100, 20), "Disconnected");
    }

    struct NetworkData
    {
        public int id;
        public Vector3 pos;
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
                m_serverConnectionId = connectionId;
                break;
            case NetworkEventType.DisconnectEvent:
                m_serverConnectionId = -1;
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
        NetworkReader nr = new NetworkReader(buffer);

        List<NetworkData> tmpList = new List<NetworkData>();
        NetworkData tmpData;

        // Read to the end
        while (nr.Position != buffer.Length)
        {
            // Deserialize
            tmpData.id = nr.ReadInt32();
            tmpData.pos = nr.ReadVector3();
            tmpData.r = nr.ReadSingle();
            tmpData.g = nr.ReadSingle();
            tmpData.b = nr.ReadSingle();
            tmpList.Add(tmpData);
        }

        // Clean up null game objects
        sphereList.RemoveAll(item => item.obj == null);

        SphereData sd;
        foreach (var networkData in tmpList)
        {
            sd = sphereList.FirstOrDefault(item => item.id == networkData.id);
            // Do we have this sphere already?
            if (sd != null)
            {
                // Update position
                sd.obj.transform.position = networkData.pos;
                sd.lastUpdate = Time.realtimeSinceStartup;
            }
            else
            {
                // Create new object
                GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                // No rigidbody here!
                // Set position
                sphere.transform.position = networkData.pos;
                // Set color
                sphere.GetComponent<Renderer>().material.color = new Color(networkData.r, networkData.g, networkData.b);

                sd = new SphereData();
                sd.id = networkData.id;
                sd.obj = sphere;
                sd.lastUpdate = Time.realtimeSinceStartup;
                sphereList.Add(sd);
            }
        }
    }

    void CleanupSpheres()
    {
        foreach (var item in sphereList)
        {
            // Haven't heard about sphere in a while so destroy it
            if (item.obj.gameObject != null && item.lastUpdate + 1.0f < Time.realtimeSinceStartup)
                Destroy(item.obj.gameObject); // Note this becomes null only after Update
        }
    }

    void OnApplicationQuit()
    {
        // Gracefully disconnect
        if (m_hostId != -1 && m_serverConnectionId != -1)
        {
            byte error;
            NetworkTransport.Disconnect(m_hostId, m_serverConnectionId, out error);
        }
    }
}
