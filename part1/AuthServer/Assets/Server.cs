using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Networking.Types;
using System.IO;
using System.Net;
using System.Collections;
using System.Collections.Generic;

public class Server : MonoBehaviour
{
    byte reliableChannel;
    int m_hostId = -1;
    Dictionary<int, IPEndPoint> connectDictionary = new Dictionary<int, IPEndPoint>();

    // Remeber all spheres we spawned
    List<GameObject> sphereList = new List<GameObject>();

    void Start()
    {
        Application.runInBackground = true;
        NetworkTransport.Init();

        ConnectionConfig config = new ConnectionConfig();
        reliableChannel = config.AddChannel(QosType.Reliable);
        HostTopology topology = new HostTopology(config, 5);
#if UNITY_EDITOR
        m_hostId = NetworkTransport.AddHostWithSimulator(topology, 200, 400, 25000);
#else
        m_hostId = NetworkTransport.AddHost(topology, 25000);
#endif

        Physics.gravity = new Vector3(); // No gravity

        // Spawn a sphere every so often
        StartCoroutine(SpawnRandomCoroutine());
        // Send sphere data every so often to those connected
        StartCoroutine(SendCoroutine());
    }

    // Send sphere data every so often to those connected
    IEnumerator SendCoroutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(0.1f);

            // Anything to do?
            if (connectDictionary.Count == 0 || sphereList.Count == 0)
                continue;

            // Serialize list
            MemoryStream stream = new MemoryStream();
            // Don't use BinaryFormatter as this stores meta data!!
            BinaryWriter bw = new BinaryWriter(stream);
            Renderer rend;
            foreach (var item in sphereList)
            {
                bw.Write(item.GetInstanceID());
                bw.Write(item.transform.position.x);
                bw.Write(item.transform.position.y);
                bw.Write(item.transform.position.z);
                rend = item.GetComponent<Renderer>();
                bw.Write(rend.material.color.r);
                bw.Write(rend.material.color.g);
                bw.Write(rend.material.color.b);

                // Don't pack too much
                // Fix me! Send more than one packet instead
                if (stream.Position > 1300)
                    break;
            }

            // Send data out
            byte[] buffer = stream.ToArray();
            byte error;
            //Debug.Log(string.Format("Sending data size {0}", buffer.Length));
            foreach (var pair in connectDictionary)
            {
                NetworkTransport.Send(m_hostId, pair.Key, reliableChannel, buffer, buffer.Length, out error);
            }
        }
    }

    void OnGUI()
    {
        GUI.Label(new Rect(10, 10, 100, 20), string.Format("{0} Spheres", sphereList.Count));
    }

    void Update()
    {
        // Let user spawn a sphere if they want
        if (Input.GetKey(KeyCode.Space))
        {
            StartCoroutine(SpawnCoroutine());
        }

        // Remember who's connecting and disconnecting to us
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
                string address;
                int port;
                NetworkID network;
                NodeID dstNode;
                NetworkTransport.GetConnectionInfo(m_hostId, connectionId, out address, out port, out network, out dstNode, out error);
                connectDictionary.Add(connectionId, new IPEndPoint(IPAddress.Parse(address), port));
                Debug.Log(string.Format("Client connected", connectionId));
                break;
            case NetworkEventType.DisconnectEvent:
                connectDictionary.Remove(connectionId);
                Debug.Log(string.Format("Client disconnected", connectionId));
                break;
            case NetworkEventType.DataEvent:
                Debug.Log(string.Format("Got data size {0}", receivedSize));
                break;
        }
    }

    IEnumerator SpawnCoroutine()
    {
        if (sphereList.Count > 50)
        {
            Debug.Log(string.Format("Spawning too many spheres to send over the network"));
            yield break;
        }

        // Create new object
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.AddComponent<Rigidbody>();
        //sphere.GetComponent<Rigidbody>().velocity = new Vector3(UnityEngine.Random.Range(-1.0f, 1.0f), UnityEngine.Random.Range(-1.0f, 1.0f), UnityEngine.Random.Range(-1.0f, 1.0f));
        // Set position
        sphere.transform.position = new Vector3(UnityEngine.Random.Range(-2.0f, 2.0f), UnityEngine.Random.Range(-2.0f, 2.0f), UnityEngine.Random.Range(-2.0f, 2.0f));
        // Set color
        sphere.GetComponent<Renderer>().material.color = new Color(UnityEngine.Random.value, UnityEngine.Random.value, UnityEngine.Random.value);

        // Save data for when we send across the net
        sphereList.Add(sphere);
        yield return new WaitForSeconds(5.0f);

        sphereList.Remove(sphere);
        Destroy(sphere);
    }

    // Spawn a sphere every so often
    IEnumerator SpawnRandomCoroutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(UnityEngine.Random.value);
            StartCoroutine(SpawnCoroutine());
        }
    }
}
