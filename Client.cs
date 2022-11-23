﻿using MQTTnet;
using MQTTnet.Client;
using System.Text;

using Newtonsoft.Json;

namespace EHMRS.signallingMqttClient;

public class Client
{
    private readonly IMqttClient _mqClient;
    private readonly MqttClientOptions _options;

    private bool _connected = false;
    private bool _connecting = false;

    private Dictionary<string, BaseMessageHandler> _messageHandlers = new Dictionary<string, BaseMessageHandler>();

    private readonly string _application;
    private readonly string _mqttPrefix;

    private class MessageTemplate
    {
        public string username;
        public string source;
        public Object payload;
    }

    public Client(string application, string hostname, int port, string username, string password, string mqttPrefix = "", bool useTls = true, bool allowUntrustedCertificate = false)
    {
        _mqClient = new MqttFactory().CreateMqttClient();

        MqttClientOptionsBuilderTlsParameters tlsOptions = new MqttClientOptionsBuilderTlsParameters
            {
                AllowUntrustedCertificates = allowUntrustedCertificate,
                UseTls = useTls,
                CertificateValidationHandler = (certContext) => true
            };

        _options = new MqttClientOptionsBuilder().WithTcpServer(hostname, port).WithCredentials(username, password).WithTls(tlsOptions).Build();

        _application = application;
        _mqttPrefix = mqttPrefix;
    }

    public void RegisterHandler(string topic, BaseMessageHandler handler)
    {
        _messageHandlers.Add(topic, handler);
    }

    public async Task<bool> SubscribeToTopic(string topic)
    {
        string newtopic = _mqttPrefix;
        if (newtopic.Substring(newtopic.Length - 1) != "/")
        {
            newtopic += "/";
        }
        newtopic += topic;
        if (!_connected)
        {
            return false;
        }
        await _mqClient.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic(newtopic).Build());
        return true;
    }

    public async void SendMessage(string topic, string payload, string user, bool retain = false)
    {
        Object encapsulatedPayload = JsonConvert.DeserializeObject(payload);
        MessageTemplate message = new MessageTemplate();
        message.payload = encapsulatedPayload;
        message.username = user;
        message.source = _application;

        MqttApplicationMessage applicationMessage = new MqttApplicationMessageBuilder().WithTopic(topic).WithPayload(JsonConvert.SerializeObject(message)).WithRetainFlag(retain).Build();
        await _mqClient.PublishAsync(applicationMessage);
    }

    public async Task Connect()
    {
        if (_connecting || _connected)
        {
            return;
        }

        while(!_connected)
        {
            try
            {
                await _mqClient.ConnectAsync(_options);
                _connected = true;
            } catch (MQTTnet.Exceptions.MqttCommunicationException e)
            {
                Thread.Sleep(1000);
            }
        }
        _mqClient.ApplicationMessageReceivedAsync += OnMessage;
        _mqClient.DisconnectedAsync += OnDisconnect;
        _connecting = false;
    }


    private async Task OnDisconnect(MqttClientDisconnectedEventArgs e)
    {
        _connected = false;
        await Connect();
    }

    private async Task OnMessage(MqttApplicationMessageReceivedEventArgs e)
    {
        string payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);

        string topic = e.ApplicationMessage.Topic;
        topic = topic.Substring(_mqttPrefix.Length);

        if (_messageHandlers.ContainsKey(topic))
        {
            BaseMessageHandler handler = _messageHandlers[topic];
            handler.Prepare(e, _mqttPrefix);
            handler.Handle();
            return;
        }

        foreach (KeyValuePair<string, BaseMessageHandler> entry in _messageHandlers)
        {
            if (topic.StartsWith(entry.Key))
            {
                BaseMessageHandler handler = entry.Value;

                handler.Prepare(e, _mqttPrefix, entry.Key);
                handler.Handle();
            }
        }
    }
}