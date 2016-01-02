using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Networking.Types;
using System;
using System.IO;
using System.Net;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class Server : MonoBehaviour
{
    byte reliableChannel;
    int m_hostId = -1;

    class ClientData
    {
        public int connectionId;
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
        HostTopology topology = new HostTopology(config, 5);
#if UNITY_EDITOR
        // Listen on port 25000
        m_hostId = NetworkTransport.AddHostWithSimulator(topology, 200, 400, 25000);
#else
        m_hostId = NetworkTransport.AddHost(topology, 25000);
#endif

        // Send client position data every so often to those connected
        StartCoroutine(SendPositionCoroutine());
    }

    void Update()
    {
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
                ClientConnected(connectionId);
                break;
            case NetworkEventType.DisconnectEvent:
                ClientData cd = clientList.FirstOrDefault(item => item.connectionId == connectionId);
                if (cd != null)
                {
                    Destroy(cd.obj);
                    clientList.Remove(cd);
                    Debug.Log("Client disconnected");
                    // Send all clients new info
                    SendClientInformation();
                }
                else
                {
                    Debug.Log("Client disconnected that we didn't know about!?");
                }
                break;
            case NetworkEventType.DataEvent:
                //Debug.Log(string.Format("Got data size {0}", receivedSize));
                Array.Resize(ref buffer, receivedSize);
                ProcessClientInput(connectionId, buffer);
                break;
        }
    }

    // Client connected so create cube for them
    void ClientConnected(int connectionId)
    {
        string address;
        int port;
        NetworkID network;
        NodeID dstNode;
        byte error;
        NetworkTransport.GetConnectionInfo(m_hostId, connectionId, out address, out port, out network, out dstNode, out error);

        // Create new object
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.AddComponent<CharacterController>();
        // Set position
        obj.transform.position = new Vector3(UnityEngine.Random.Range(-5.0f, 5.0f), 0, UnityEngine.Random.Range(-5.0f, 5.0f));
        // Set color
        obj.GetComponent<Renderer>().material.color = new Color(UnityEngine.Random.value, UnityEngine.Random.value, UnityEngine.Random.value);

        ClientData cd = new ClientData();
        cd.connectionId = connectionId;
        cd.name = new IPEndPoint(IPAddress.Parse(address), port).ToString();
        cd.obj = obj;
        // Remember client
        clientList.Add(cd);
        Debug.Log(string.Format("Client {0} connected", cd.name));

        // Send all clients new info
        SendClientInformation();
    }

    enum PacketTypeEnum
    {
        Unknown = (1 << 0),
        Position = (1 << 1),
        Information = (1 << 2)
    }

    // Send client data to all clients whenever someone connects or disconnects
    void SendClientInformation()
    {
        MemoryStream stream = new MemoryStream();
        BinaryWriter bw = new BinaryWriter(stream);
        Renderer rend;

        bw.Write((byte)PacketTypeEnum.Information);
        foreach (var item in clientList)
        {
            bw.Write(item.obj.GetInstanceID());
            bw.Write(item.name);
            bw.Write(item.obj.transform.position.x);
            bw.Write(item.obj.transform.position.y);
            bw.Write(item.obj.transform.position.z);
            rend = item.obj.GetComponent<Renderer>();
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
        foreach (var item in clientList)
        {
            NetworkTransport.Send(m_hostId, item.connectionId, reliableChannel, buffer, buffer.Length, out error);
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

    void ProcessClientInput(int connectionId, byte[] buffer)
    {
        ClientData cd = clientList.FirstOrDefault(item => item.connectionId == connectionId);
        if (cd == null)
        {
            Debug.Log("Client that we didn't know about!?");
            return;
        }

        InputTypeEnum input = (InputTypeEnum)buffer[0];
        float deltaX = 0.0f;
        float deltaZ = 0.0f;
        if ((input & InputTypeEnum.KeyUp) == InputTypeEnum.KeyUp)
            deltaX = 1.0f;
        if ((input & InputTypeEnum.KeyDown) == InputTypeEnum.KeyDown)
            deltaX = -1.0f;
        if ((input & InputTypeEnum.KeyRight) == InputTypeEnum.KeyRight)
            deltaZ = 1.0f;
        if ((input & InputTypeEnum.KeyLeft) == InputTypeEnum.KeyLeft)
            deltaZ = -1.0f;
        Vector3 movement = new Vector3(deltaX, 0, deltaZ);
        movement = transform.TransformDirection(movement);
        movement *= 10.0f;
        cd.obj.GetComponent<CharacterController>().Move(movement * Time.deltaTime);
    }

    // Send client position data every so often to those connected
    IEnumerator SendPositionCoroutine()
    {
        MemoryStream stream = new MemoryStream();
        BinaryWriter bw = new BinaryWriter(stream);

        while (true)
        {
            yield return new WaitForSeconds(0.1f);

            // Anything to do?
            if (clientList.Count == 0)
                continue;

            // Reset stream
            stream.SetLength(0);

            bw.Write((byte)PacketTypeEnum.Position);
            foreach (var item in clientList)
            {
                bw.Write(item.obj.GetInstanceID());
                bw.Write(item.obj.transform.position.x);
                bw.Write(item.obj.transform.position.y);
                bw.Write(item.obj.transform.position.z);

                // Don't pack too much
                // Fix me! Send more than one packet instead
                if (stream.Position > 1300)
                    break;
            }

            // Send data out
            byte[] buffer = stream.ToArray();
            byte error;
            //Debug.Log(string.Format("Sending data size {0}", buffer.Length));
            foreach (var item in clientList)
            {
                NetworkTransport.Send(m_hostId, item.connectionId, reliableChannel, buffer, buffer.Length, out error);
            }
        }
    }

    void OnApplicationQuite()
    {
        // Gracefully disconnect
        if (m_hostId != -1 && clientList.Count > 0)
        {
            byte error;

            foreach (var item in clientList)
            {
                NetworkTransport.Disconnect(m_hostId, item.connectionId, out error);
            }
        }
    }
}
