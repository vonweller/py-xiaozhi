using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XiaozhiAI.Services;

namespace XiaozhiAI
{
    class Program
    {
        private static VoiceAssistant voiceAssistant;
        private static bool isRunning = true;
        private static bool spaceKeyPressed = false;

        static async Task Main(string[] args)
        {
            Console.Title = "小智AI客户端";
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            
            Console.WriteLine("=== 小智AI客户端 ===");
            Console.WriteLine("按空格键开始/停止对话");
            Console.WriteLine("按ESC键退出程序");
            
            // 配置参数
            var config = new Dictionary<string, string>
            {
                ["access_token"] = "test-token",
                ["device_mac"] = "04:68:74:27:12:c8",
                ["device_uuid"] = "test-uuid",
                ["ws_url"] = "wss://api.tenclass.net/xiaozhi/v1/", // 默认使用小智官方服务器
                ["ota_url"] = "https://api.tenclass.net/xiaozhi/ota/",
                ["manual_mode"] = "false" // 默认自动模式
            };
            
            // 询问是否使用自定义服务器
            Console.WriteLine("\n选择服务器:");
            Console.WriteLine("1. 小智官方服务器 (wss://api.tenclass.net/xiaozhi/v1/)");
            Console.WriteLine("2. 自定义服务器 (ws://192.168.10.29:8000)");
            Console.Write("请选择 [1/2]: ");
            string serverChoice = Console.ReadLine();
            if (serverChoice == "2")
            {
                config["ws_url"] = "ws://192.168.10.29:8000";
                Console.WriteLine("已选择自定义服务器");
            }
            else
            {
                Console.WriteLine("已选择小智官方服务器");
            }
            
            // 询问是否使用手动模式
            Console.Write("\n是否使用手动模式? [y/N]: ");
            string modeChoice = Console.ReadLine();
            if (modeChoice?.ToLower() == "y")
            {
                config["manual_mode"] = "true";
                Console.WriteLine("已启用手动模式");
            }
            else
            {
                Console.WriteLine("已启用自动模式");
            }
            
            // 创建并启动语音助手
            try
            {
                voiceAssistant = new VoiceAssistant(config);
                voiceAssistant.OnLogMessage += message => Console.WriteLine(message);
                voiceAssistant.OnStatusChanged += status => Console.WriteLine($"状态: {status}");
                
                await voiceAssistant.StartAsync();
                await voiceAssistant.wsClient.SendMessageDectAsync("更新当前IOT设备");

                // 启动键盘监听线程
                var keyboardThread = new Thread(KeyboardListener)
                {
                    IsBackground = true
                };
                keyboardThread.Start();
                
                // 主线程等待，直到程序退出
                while (isRunning)
                {
                    await Task.Delay(100);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"程序出错: {ex.Message}");
            }
            finally
            {
                voiceAssistant?.Dispose();
                Console.WriteLine("程序已退出");
            }
        }
        
        static void KeyboardListener()
        {
            while (isRunning)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);

                    if (key.Key == ConsoleKey.Escape)
                    {
                        isRunning = false;
                        Console.WriteLine("正在退出程序...");
                    }
                    else if (key.Key == ConsoleKey.Enter)
                    {
                        
                       voiceAssistant.wsClient.SendMessageDectAsync(Console.ReadLine());
                    }
                    else if (key.Key == ConsoleKey.Spacebar)
                    {
                        if (!spaceKeyPressed)
                        {
                            spaceKeyPressed = true;
                            voiceAssistant?.HandleSpaceKeyDown();
                        }
                        else
                        {
                            spaceKeyPressed = false;
                            voiceAssistant?.HandleSpaceKeyUp();
                        }
                    }
                }
                
                Thread.Sleep(10);
            }
        }
    }
}