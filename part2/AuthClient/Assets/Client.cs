using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Networking.Types;
using System;
using System.IO;
using System.Net;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class Client : MonoBehaviour
{
    byte reliableChannel;
    int m_hostId = -1;
    int m_serverConnectionId = -1;

    class ClientData
    {
        public int objectId;
        public string name;
        public GameObject obj;
    }
    List<ClientData> clientList = new List<ClientData>();

    void Start()
    {
        Application.runInBackground = true;
        NetworkTransport.Init();

        ConnectionConfig config = new ConnectionConfig();
        reliableChannel = config.AddChannel(QosType.Reliable);
        HostTopology topology = new HostTopology(config, 1);
#if UNITY_EDITOR
        m_hostId = NetworkTransport.AddHostWithSimulator(topology, 200, 400);
#else
        m_hostId = NetworkTransport.AddHost(topology);
#endif

        byte error;
        // Cannot tell we're connected until we receive the event at later time
        NetworkTransport.Connect(m_hostId, IPAddress.Loopback.ToString(), 25000, 0, out error);

        // Send input data every so often
        StartCoroutine(SendInputCoroutine());
    }

    void OnGUI()
    {
        if (m_serverConnectionId != -1) // Set when ConnectEvent received
            GUI.Label(new Rect(10, 10, 100, 20), "Connected");
        else
            GUI.Label(new Rect(10, 10, 100, 20), "Disconnected");
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
                if (connectionId != m_serverConnectionId)
                {
                    Debug.Log("Data not from server!?");
                }
                else
                {
                    Array.Resize(ref buffer, receivedSize);
                    ProcessServerData(buffer);
                }
                break;
        }
    }

    enum PacketTypeEnum
    {
        Unknown = (1 << 0),
        Position = (1 << 1),
        Information = (1 << 2)
    }

    class PositionData
    {
        public int objectId;
        public Vector3 pos;
    }

    class InformationData
    {
        public int objectId;
        public string name;
        public Vector3 pos;
        public float r;
        public float g;
        public float b;
    }

    // Server sent us a packet about all connected clients or an update for all cube positions
    void ProcessServerData(byte[] buffer)
    {
        NetworkReader nr = new NetworkReader(buffer);

        PacketTypeEnum packetType = (PacketTypeEnum)nr.ReadByte();
        switch (packetType)
        {
            case PacketTypeEnum.Position:
                List<PositionData> posList = new List<PositionData>();
                PositionData p;
                while (nr.Position != buffer.Length)
                {
                    p = new PositionData();
                    p.objectId = nr.ReadInt32();
                    p.pos = nr.ReadVector3();
                    posList.Add(p);
                }

                // Update game objects
                foreach (var item in clientList)
                {
                    if (item.obj == null)
                        continue;
                    p = posList.FirstOrDefault(x => x.objectId == item.objectId);
                    if (p == null)
                        Debug.Log("Cannot find game object");
                    else
                        item.obj.transform.position = p.pos;
                }
                break;
            case PacketTypeEnum.Information:
                List<InformationData> infoList = new List<InformationData>();
                InformationData info;
                while (nr.Position != buffer.Length)
                {
                    info = new InformationData();
                    info.objectId = nr.ReadInt32();
                    info.name = nr.ReadString();
                    info.pos = nr.ReadVector3();
                    info.r = nr.ReadSingle();
                    info.g = nr.ReadSingle();
                    info.b = nr.ReadSingle();
                    infoList.Add(info);
                }

                // Remove clients that aren't listed
                foreach (var item in clientList)
                {
                    if (item.obj == null)
                        continue;
                    info = infoList.FirstOrDefault(x => x.objectId == item.objectId);
                    if (info == null)
                        Destroy(item.obj);
                }
                clientList.RemoveAll(x => x.obj == null); // Note items are set to null only after Update!

                foreach (var item in infoList)
                {
                    ClientData cd = clientList.FirstOrDefault(x => x.objectId == item.objectId);
                    // Is this new client info?
                    if (cd == null)
                    {
                        // Create new object
                        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        // No CharacterController here!
                        // Set position
                        obj.transform.position = item.pos;
                        // Set color
                        obj.GetComponent<Renderer>().material.color = new Color(item.r, item.g, item.b);

                        cd = new ClientData();
                        cd.objectId = item.objectId;
                        cd.name = item.name;
                        cd.obj = obj;
                        clientList.Add(cd);
                        Debug.Log(string.Format("New client info for {0}", cd.name));
                    }
                }
                break;
            default:
                Debug.Log("Unknown packet type");
                break;
        }
    }

    enum InputTypeEnum
    {
        KeyNone = (1 << 0),
        KeyUp = (1 << 1),
        KeyDown = (1 << 2),
        KeyLeft = (1 << 3),
        KeyRight = (1 << 4),
        KeyJump = (1 << 5)
    }

    // Send input data every so often
    IEnumerator SendInputCoroutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(0.1f);

            // Anything to do?
            if (m_hostId == -1 || m_serverConnectionId == -1)
                continue;

            InputTypeEnum input = InputTypeEnum.KeyNone;
            float f;
            f = Input.GetAxis("Horizontal");
            if (f > 0.0f)
                input |= InputTypeEnum.KeyUp;
            if (f < 0.0f)
                input |= InputTypeEnum.KeyDown;
            f = Input.GetAxis("Vertical");
            if (f > 0.0f)
                input |= InputTypeEnum.KeyRight;
            if (f < 0.0f)
                input |= InputTypeEnum.KeyLeft;
            if (Input.GetKey(KeyCode.Space))
                input |= InputTypeEnum.KeyJump;

            if (input == InputTypeEnum.KeyNone)
                continue;

            // Send data out
            byte[] buffer = new byte[1];
            buffer[0] = (byte)input;
            byte error;
            NetworkTransport.Send(m_hostId, m_serverConnectionId, reliableChannel, buffer, buffer.Length, out error);
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
