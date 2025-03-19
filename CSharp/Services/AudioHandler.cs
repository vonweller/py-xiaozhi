using NAudio.Wave;
using OpusSharp.Core;  // 替换 Concentus 引用为 OpusSharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
namespace XiaozhiAI.Services
{
    public class AudioHandler : IDisposable
    {
        // 音频设备
        private WaveInEvent waveIn;
        private WaveOutEvent waveOut;
        private BufferedWaveProvider bufferedWaveProvider;

        // 音频参数
        private readonly int sampleRate;
        private readonly int channels;
        private readonly int frameSize;

        // Opus 编解码器
        private OpusEncoder encoder;  // 替换为 OpusSharp 的编码器
        private OpusDecoder decoder;  // 替换为 OpusSharp 的解码器

        // 音频缓冲区
        private readonly List<byte[]> audioBuffer = new List<byte[]>();
        private readonly object bufferLock = new object();
        private readonly object outputLock = new object();
        private const int MaxBufferCount = 20;

        // 播放线程
        private Task playbackTask;
        private bool isPlaying = false;
        private CancellationTokenSource playbackCancellation;
        
        public AudioHandler(int sampleRate = Constants.DEFAULT_SAMPLE_RATE, 
            int channels = Constants.DEFAULT_CHANNELS, 
            int frameDuration = Constants.DEFAULT_FRAME_DURATION)
        {
            this.sampleRate = sampleRate;
            this.channels = channels;
            frameSize = sampleRate * frameDuration / 1000;

            // 初始化Opus编解码器
            encoder = new OpusEncoder(sampleRate, channels, OpusPredefinedValues.OPUS_APPLICATION_AUDIO);
            decoder = new OpusDecoder(sampleRate, channels);

            // 初始化音频设备
            InitializeAudioDevices();
        }

        private void InitializeAudioDevices()
        {
            try
            {
                // 初始化音频输入
                waveIn = new WaveInEvent
                {
                    WaveFormat = new WaveFormat(sampleRate, 16, channels),
                    BufferMilliseconds = 60
                };
                waveIn.DataAvailable += WaveIn_DataAvailable;

                // 初始化音频输出
                waveOut = new WaveOutEvent();
                bufferedWaveProvider = new BufferedWaveProvider(new WaveFormat(sampleRate, 16, channels));
                bufferedWaveProvider.BufferLength = 4096 * 20; // 设置更大的缓冲区
                waveOut.DesiredLatency = 200; // 设置延迟以获得更平滑的播放
                waveOut.NumberOfBuffers = 8; // 增加缓冲区数量
                waveOut.Init(bufferedWaveProvider);

                Console.WriteLine("音频设备初始化完成");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"初始化音频设备失败: {ex.Message}");
            }
        }

        public void StartRecording()
        {
            try
            {
                if (waveIn != null && waveIn.DeviceNumber != -1)
                {
                    waveIn.StartRecording();
                    Console.WriteLine("开始录音");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"开始录音失败: {ex.Message}");
            }
        }

        public void StopRecording()
        {
            try
            {
                if (waveIn != null)
                {
                    waveIn.StopRecording();
                    Console.WriteLine("停止录音");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"停止录音失败: {ex.Message}");
            }
        }

        private void WaveIn_DataAvailable(object sender, WaveInEventArgs e)
        {
            // 这里可以添加音频处理逻辑，如果需要的话
            OnAudioDataAvailable?.Invoke(e.Buffer, e.BytesRecorded);
        }

        public byte[] EncodeAudio(byte[] pcmData)
        {
            try
            {
                // 将字节数组转换为短整型数组
                short[] pcmShorts = new short[pcmData.Length / 2];
                Buffer.BlockCopy(pcmData, 0, pcmShorts, 0, pcmData.Length);

                // 编码
                byte[] opusBytes = new byte[960]; // 足够大的缓冲区
                int encodedLength = encoder.Encode(pcmData, frameSize, opusBytes, opusBytes.Length);

                // 调整数组大小以匹配实际编码长度
                byte[] result = new byte[encodedLength];
                Array.Copy(opusBytes, result, encodedLength);

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"音频编码错误: {ex.Message}");
                return null;
            }
        }

        public void InitializeAudio(int deviceNumber = -1)
        {
            try
            {
                // 初始化录音设备
                waveIn = new WaveInEvent();
                waveIn.DeviceNumber = deviceNumber;
                waveIn.WaveFormat = new WaveFormat(sampleRate, 16, channels);
                waveIn.BufferMilliseconds = 60; // 设置缓冲区大小为60毫秒
                waveIn.DataAvailable += WaveIn_DataAvailable;
                
                // 初始化播放设备
                waveOut = new WaveOutEvent();
                bufferedWaveProvider = new BufferedWaveProvider(new WaveFormat(sampleRate, 16, channels));
                bufferedWaveProvider.BufferLength = 4096 * 20; // 设置更大的缓冲区
                waveOut.DesiredLatency = 200; // 设置延迟以获得更平滑的播放
                waveOut.NumberOfBuffers = 8; // 增加缓冲区数量
                waveOut.Init(bufferedWaveProvider);

                Console.WriteLine("音频设备初始化完成");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"初始化音频设备失败: {ex.Message}");
            }
        }

        public byte[] DecodeAudio(byte[] opusData)
        {
            try
            {
                // 解码
                short[] pcmData = new short[frameSize * 10]; // 使用更大的缓冲区
                int decodedSamples = decoder.Decode(opusData, opusData.Length, pcmData, frameSize * 10, false);
                
                //Console.WriteLine($"解码音频: {opusData.Length} 字节 -> {decodedSamples} 采样点");

                // 将短整型数组转换为字节数组
                byte[] pcmBytes = new byte[decodedSamples * 2];
                Buffer.BlockCopy(pcmData, 0, pcmBytes, 0, pcmBytes.Length);

                // 添加到播放缓冲区
                lock (bufferLock)
                {
                    audioBuffer.Add(pcmBytes);
                    // 限制缓冲区大小
                    while (audioBuffer.Count > MaxBufferCount)
                    {
                        audioBuffer.RemoveAt(0);
                    }
                }

                // 如果播放线程未运行，启动它
                if (!isPlaying)
                {
                    StartPlayback();
                }

                return pcmBytes;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"音频解码错误: {ex.Message}");
                return null;
            }
        }

        private void StartPlayback()
        {
            try
            {
                isPlaying = true;
                playbackCancellation = new CancellationTokenSource();
                playbackTask = Task.Run(() => PlaybackWorker(playbackCancellation.Token));
                Console.WriteLine("开始音频播放线程");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"启动播放线程失败: {ex.Message}");
            }
        }

        private void PlaybackWorker(CancellationToken cancellationToken)
        {
            try
            {
                Console.WriteLine("音频播放线程已启动");
                
                // 确保播放设备已初始化
                lock (outputLock)
                {
                    if (waveOut == null || bufferedWaveProvider == null)
                    {
                        waveOut = new WaveOutEvent();
                        bufferedWaveProvider = new BufferedWaveProvider(new WaveFormat(sampleRate, 16, channels));
                        bufferedWaveProvider.BufferLength = 4096 * 30; // 使用更大的缓冲区
                        waveOut.DesiredLatency = 300; // 使用更长的延迟
                        waveOut.NumberOfBuffers = 10; // 增加缓冲区数量
                        waveOut.Init(bufferedWaveProvider);
                        Console.WriteLine("重新初始化音频输出设备");
                    }
                    
                    // 确保播放设备处于播放状态
                    if (waveOut.PlaybackState != PlaybackState.Playing)
                    {
                        waveOut.Play();
                    }
                }
                
                // 等待缓冲区积累更多数据再开始播放
                Thread.Sleep(300);
                
                // 创建一个平滑缓冲区
                List<byte[]> smoothBuffer = new List<byte[]>();
                
                while (isPlaying && !cancellationToken.IsCancellationRequested)
                {
                    // 从缓冲区获取数据并添加到平滑缓冲区
                    lock (bufferLock)
                    {
                        while (audioBuffer.Count > 0 && smoothBuffer.Count < 3)
                        {
                            smoothBuffer.Add(audioBuffer[0]);
                            audioBuffer.RemoveAt(0);
                        }
                    }

                    // 如果平滑缓冲区有足够的数据，则播放
                    if (smoothBuffer.Count > 0)
                    {
                        // 播放音频数据
                        lock (outputLock)
                        {
                            if (bufferedWaveProvider != null)
                            {
                                // 播放平滑缓冲区中的第一个数据包
                                byte[] pcmData = smoothBuffer[0];
                                smoothBuffer.RemoveAt(0);
                                
                                bufferedWaveProvider.AddSamples(pcmData, 0, pcmData.Length);
                                
                                // 如果缓冲区接近满，清除一些旧数据
                                if (bufferedWaveProvider.BufferedBytes > bufferedWaveProvider.BufferLength * 0.8)
                                {
                                    Console.WriteLine("缓冲区接近满，清除部分数据");
                                    bufferedWaveProvider.ClearBuffer();
                                }
                            }
                        }
                    }
                    else
                    {
                        // 没有数据时短暂休眠
                        Thread.Sleep(10);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"音频播放线程错误: {ex.Message}");
            }
            finally
            {
                isPlaying = false;
            }
        }

        public void Dispose()
        {
            // 停止播放线程
            if (playbackCancellation != null)
            {
                playbackCancellation.Cancel();
                playbackTask?.Wait(1000);
            }
            
            // 释放资源
            waveIn?.Dispose();
            waveOut?.Dispose();
            encoder?.Dispose();
            decoder?.Dispose();
        }

        // 音频数据可用事件
        public event Action<byte[], int> OnAudioDataAvailable;
    }
}