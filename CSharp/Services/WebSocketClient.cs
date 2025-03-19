using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static System.Net.Mime.MediaTypeNames;

namespace XiaozhiAI.Services
{
    public class WebSocketClient
    {
        private ClientWebSocket webSocket;
        private Uri serverUri;
        private Dictionary<string, string> headers;
        private Action<string> textMessageHandler;
        private Action<byte[]> binaryMessageHandler;
        private CancellationTokenSource cancellationTokenSource;
        private bool isConnected;

        public bool IsConnected => isConnected;

        public WebSocketClient(string url, Dictionary<string, string> headers, 
                              Action<string> textMessageHandler, 
                              Action<byte[]> binaryMessageHandler)
        {
            serverUri = new Uri(url);
            this.headers = headers;
            this.textMessageHandler = textMessageHandler;
            this.binaryMessageHandler = binaryMessageHandler;
            isConnected = false;
        }

        public async Task ConnectAsync()
        {
            try
            {
                webSocket = new ClientWebSocket();
                
                // 添加头信息
                foreach (var header in headers)
                {
                    webSocket.Options.SetRequestHeader(header.Key, header.Value);
                }

                cancellationTokenSource = new CancellationTokenSource();
                await webSocket.ConnectAsync(serverUri, cancellationTokenSource.Token);
                isConnected = true;
                
                // 发送初始化握手消息
                var helloMsg = new
                {
                    type = "hello",
                    version = 1,
                    transport = "websocket",
                    audio_params = new
                    {
                        format = "opus",
                        sample_rate = 16000,
                        channels = 1,
                        frame_duration = 60
                    }
                };
                
                await SendMessageAsync(helloMsg);
                
                // 启动接收消息的任务
                _ = ReceiveMessagesAsync();

                Console.WriteLine("WebSocket连接已建立");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WebSocket连接失败: {ex.Message}");
                isConnected = false;
            }
        }

        public async Task DisconnectAsync()
        {
            if (webSocket != null && webSocket.State == WebSocketState.Open)
            {
                try
                {
                    cancellationTokenSource?.Cancel();
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, 
                                             "Client initiated close", 
                                             CancellationToken.None);
                    isConnected = false;
                    Console.WriteLine("WebSocket连接已关闭");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"关闭WebSocket连接时出错: {ex.Message}");
                }
            }
        }

        public async Task SendMessageAsync(object message)
        {
            if (!IsConnected)
            {
                Console.WriteLine("WebSocket未连接，无法发送消息");
                return;
            }

            try
            {
                string messageText;

                if (message is string messageStr)
                {
                    messageText = messageStr;
                }
                else if (message is JObject jObject)
                {
                    messageText = jObject.ToString(Formatting.None);
                }
                else
                {
                    messageText = JsonConvert.SerializeObject(message);
                }

                await webSocket.SendAsync(
                    new ArraySegment<byte>(Encoding.UTF8.GetBytes(messageText)),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None
                );

                Console.WriteLine($"已发送JSON消息: {messageText}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发送消息失败: {ex.Message}");
            }
        }
        public async Task SendMessageDectAsync(string message)
        {
            if (!IsConnected)
            {
                Console.WriteLine("WebSocket未连接，无法发送消息");
                return;
            }
            string messages = @"{
                    ""type"": ""listen"",
                    ""state"": ""detect"",
                    ""text"": ""<唤醒词>""
                }";
            messages = messages.Replace("<唤醒词>", message);
            messages = messages.Replace("\n", "").Replace("\r", "").Replace("\r\n", "").Replace(" ", "");
            try
            {
                await webSocket.SendAsync(
                    new ArraySegment<byte>(Encoding.UTF8.GetBytes(messages)),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None
                );

                Console.WriteLine($"已发送JSON消息: {messages}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发送消息失败: {ex.Message}");
            }
        }


        public async Task SendBinaryMessageAsync(byte[] data)
        {
            if (webSocket == null || webSocket.State != WebSocketState.Open)
            {
                Console.WriteLine("WebSocket未连接，无法发送二进制消息");
                return;
            }

            try
            {
                await webSocket.SendAsync(new ArraySegment<byte>(data), 
                                        WebSocketMessageType.Binary, 
                                        true, 
                                        cancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发送二进制消息失败: {ex.Message}");
            }
        }

        private async Task ReceiveMessagesAsync()
        {
            byte[] buffer = new byte[8192];
            
            try
            {
                while (webSocket.State == WebSocketState.Open && !cancellationTokenSource.Token.IsCancellationRequested)
                {
                    WebSocketReceiveResult result = await webSocket.ReceiveAsync(
                        new ArraySegment<byte>(buffer), cancellationTokenSource.Token);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, 
                                                 "Server closed connection", 
                                                 CancellationToken.None);
                        isConnected = false;
                        Console.WriteLine("服务器关闭了连接");
                        break;
                    }

                    // 处理接收到的消息
                    byte[] messageBytes = new byte[result.Count];
                    Array.Copy(buffer, messageBytes, result.Count);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string message = Encoding.UTF8.GetString(messageBytes);
                        textMessageHandler?.Invoke(message);
                    }
                    else if (result.MessageType == WebSocketMessageType.Binary)
                    {
                        binaryMessageHandler?.Invoke(messageBytes);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"接收消息时出错: {ex.Message}");
                isConnected = false;
            }
        }
    }
}