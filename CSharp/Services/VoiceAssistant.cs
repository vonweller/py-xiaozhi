using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using XiaozhiAI.Models.IoT;
using static System.Net.Mime.MediaTypeNames;

namespace XiaozhiAI.Services
{
    public class VoiceAssistant : IDisposable
    {
        // 配置
        private readonly Dictionary<string, string> config;
        private bool isManualMode;
        private string sessionId;
        private string ttsState = "idle";
        private string listenState = "stop";
        private bool spaceKeyPressed = false;

        // 组件
        private readonly AudioHandler audioHandler;
        public  WebSocketClient wsClient;
        private readonly ThingManager thingManager;
        private CancellationTokenSource audioSendCts;
        private Task audioSendTask;

        // 事件
        public event Action<string> OnLogMessage;
        public event Action<string> OnStatusChanged;

        public VoiceAssistant(Dictionary<string, string> config)
        {
            this.config = config;
            this.isManualMode = config.ContainsKey("manual_mode") && bool.Parse(config["manual_mode"]);

            // 初始化音频处理器
            audioHandler = new AudioHandler();

            // 初始化WebSocket客户端
            var headers = new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {config["access_token"]}",
                ["Protocol-Version"] = "1",
                ["Device-Id"] = config["device_mac"],
                ["Client-Id"] = config["device_uuid"]
            };

            wsClient = new WebSocketClient(
                config["ws_url"],
                headers,
                HandleTextMessage,
                HandleBinaryMessage
            );

            // 初始化物联网设备管理器
            thingManager = ThingManager.GetInstance();
            LogMessage("语音助手已初始化");
        }

        public async Task StartAsync()
        {
            // 检查OTA版本
            await CheckOtaVersionAsync();

            // 连接WebSocket服务器
            await wsClient.ConnectAsync();

            // 启动音频发送任务
            StartAudioSendTask();

            LogMessage("语音助手已启动");
        }

        private async Task CheckOtaVersionAsync()
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("Device-Id", config["device_mac"]);

                var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
                var response = await client.PostAsync(config["ota_url"], content);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    var data = JObject.Parse(responseJson);
                    LogMessage($"当前固件版本: {data["firmware"]["version"]}");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"OTA检查失败: {ex.Message}", true);
            }
        }

        private void StartAudioSendTask()
        {
            audioSendCts = new CancellationTokenSource();
            audioSendTask = Task.Run(AudioSendLoop, audioSendCts.Token);
        }

        private async Task AudioSendLoop()
        {
            byte[] buffer = new byte[1920]; // 16位采样, 单声道, 960采样点 = 1920字节
            
            while (!audioSendCts.Token.IsCancellationRequested)
            {
                if (listenState != "start" || !wsClient.IsConnected)
                {
                    await Task.Delay(100);
                    continue;
                }

                try
                {
                    // 使用事件模式获取音频数据
                    audioHandler.OnAudioDataAvailable += (data, length) =>
                    {
                        if (listenState == "start" && wsClient.IsConnected)
                        {
                            var opusData = audioHandler.EncodeAudio(data);
                            if (opusData != null)
                            {
                                wsClient.SendBinaryMessageAsync(opusData).Wait();
                            }
                        }
                    };
                    
                    // 启动录音
                    audioHandler.StartRecording();
                    
                    // 等待取消信号
                    await Task.Delay(Timeout.Infinite, audioSendCts.Token);
                }
                catch (TaskCanceledException)
                {
                    // 正常取消
                }
                catch (Exception ex)
                {
                    LogMessage($"音频发送失败: {ex.Message}", true);
                    await Task.Delay(500);
                }
            }
        }

        private void HandleTextMessage(string message)
        {
            try
            {
                var msg = JObject.Parse(message);
                LogMessage($"收到服务器消息: {message}");

                string msgType = msg["type"]?.ToString();
                
                switch (msgType)
                {
                    case "hello":
                        sessionId = msg["session_id"].ToString();
                        StartListening();
                        // 接收到hello后初始化物联网设备
                        InitializeIotDevices();
                        break;
                        
                    case "tts":
                        ttsState = msg["state"].ToString();
                        if (ttsState == "stop")
                        {
                            // 确保音频播放完成后再开始监听
                            StartListening();
                        }
                        else if (ttsState == "sentence_start")
                        {
                            // 新的语音开始，清除之前可能的音频缓冲
                            audioHandler.StopRecording();
                        }
                        break;
                        
                    case "goodbye":
                        sessionId = null;
                        listenState = "stop";
                        // 停止所有音频播放
                        audioHandler.StopRecording();
                        break;
                        
                    case "iot":
                        HandleIotMessage(msg);
                        break;
                }
            }
            catch (JsonException)
            {
                LogMessage("收到非JSON格式消息", true);
            }
            catch (Exception ex)
            {
                LogMessage($"处理消息时发生错误: {ex.Message}", true);
            }
        }

        private void HandleBinaryMessage(byte[] data)
        {
            try
            {
                //LogMessage($"收到音频数据: {data.Length} 字节");
                // 解码音频数据并播放
                var pcmData = audioHandler.DecodeAudio(data);
                if (pcmData != null)
                {
                   // LogMessage($"解码成功: {pcmData.Length} 字节");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"处理音频数据失败: {ex.Message}", true);
            }
        }

        private void HandleIotMessage(JObject data)
        {
            try
            {
                // 检查消息类型
                var type = data["type"]?.ToString();
                if (type != "iot")
                {
                    LogMessage($"非物联网消息类型: {type}", true);
                    return;
                }

                // 获取命令数组
                var commands = data["commands"] as JArray;
                if (commands == null || commands.Count == 0)
                {
                    LogMessage("物联网命令为空或格式不正确", true);
                    return;
                }

                foreach (JObject command in commands)
                {
                    try
                    {
                        // 记录接收到的命令
                        LogMessage($"收到物联网命令: {command.ToString(Newtonsoft.Json.Formatting.None)}");

                        // 执行命令
                        var result = thingManager.Invoke(command);
                        LogMessage($"执行物联网命令结果: {result}");

                        // 命令执行后更新设备状态
                        UpdateIotStates();
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"执行物联网命令失败: {ex.Message}", true);
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"处理物联网消息失败: {ex.Message}", true);
            }
        }

        private void UpdateIotStates()
        {
            try
            {
                // 获取当前设备状态
                string statesJson = thingManager.GetStatesJson();
                
                // 发送状态更新
                wsClient.SendMessageAsync(statesJson);
                LogMessage("物联网设备状态已更新");
            }
            catch (Exception ex)
            {
                LogMessage($"更新物联网状态失败: {ex.Message}", true);
            }
        }

        public void  StartListening()
        {
            if (!isManualMode && wsClient.IsConnected && !string.IsNullOrEmpty(sessionId))
            {
                listenState = "start";
                wsClient.SendMessageAsync(new
                {
                    session_id = sessionId,
                    type = "listen",
                    state = "start",
                    mode = "auto"
                });
                
                UpdateStatus("正在监听...");
            }
        }

        public void  StopListening()
        {
            if (wsClient.IsConnected && !string.IsNullOrEmpty(sessionId))
            {
                listenState = "stop";
                wsClient.SendMessageAsync(new
                {
                    session_id = sessionId,
                    type = "listen",
                    state = "stop"
                });
                
                UpdateStatus("已停止监听");
            }
        }

        public void HandleSpaceKeyDown()
        {
            spaceKeyPressed = true;

            // 需要重新连接的情况
            if (!wsClient.IsConnected)
            {
                wsClient.ConnectAsync().Wait();
                return;
            }

            // 打断当前语音播放
            if (ttsState == "start" || ttsState == "sentence_start")
            {
                wsClient.SendMessageAsync(new { type = "abort" });
                LogMessage("中断当前语音播放");
            }

            // 手动模式开始录音
            if (isManualMode)
            {
                listenState = "start";
                wsClient.SendMessageAsync(new
                {
                    session_id = sessionId,
                    type = "listen",
                    state = "start",
                    mode = "manual"
                });
                
                UpdateStatus("手动录音中...");
            }
        }

        public void HandleSpaceKeyUp()
        {
            spaceKeyPressed = false;
            
            if (isManualMode)
            {
                StopListening();
            }
        }

        private async Task SendIotDescriptors()
        {
            try
            {
                var thingManager = ThingManager.GetInstance();
                var descriptorsJson = thingManager.GetDescriptorsJson();
                
                // 解析为对象
                var descriptorsObj = JObject.Parse(descriptorsJson);
                
                // 添加 session_id
                descriptorsObj["session_id"] = sessionId;
                
                // 直接发送对象，而不是字符串
               await wsClient.SendMessageAsync(descriptorsObj);
                
                LogMessage($"已发送IoT设备描述\n{descriptorsObj}");
            }
            catch (Exception ex)
            {
                LogMessage($"发送IoT设备描述失败: {ex.Message}", true);
            }
        }

        private void InitializeIotDevices()
        {
            // 发送设备描述
            if (!string.IsNullOrEmpty(sessionId))
            {
                // 添加设备
                thingManager.ID = sessionId;
                thingManager.AddThing(new Models.IoT.Things.Lamp());
                thingManager.AddThing(new Models.IoT.Things.Speaker());
                thingManager.AddThing(new Models.IoT.Things.Camera());
                Console.WriteLine("发送iot设备描述");
                SendIotDescriptors();
            }
        }

        private void LogMessage(string message, bool isError = false)
        {
            string logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {(isError ? "ERROR" : "INFO")} - {message}";
            OnLogMessage?.Invoke(logMessage);
        }

        private void UpdateStatus(string status)
        {
            OnStatusChanged?.Invoke(status);
        }

        public void Dispose()
        {
            // 停止音频发送任务
            audioSendCts?.Cancel();
            try
            {
                audioSendTask?.Wait(1000);
            }
            catch { }

            // 关闭WebSocket连接
            wsClient.DisconnectAsync().Wait();

            // 释放音频资源
            audioHandler.Dispose();

            LogMessage("语音助手已关闭");
        }
    }
}