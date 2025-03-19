namespace XiaozhiAI.Services
{
    public static class Constants
    {
        // 音频参数
        public const int DEFAULT_SAMPLE_RATE = 24000;
        public const int DEFAULT_CHANNELS = 1;
        public const int DEFAULT_FRAME_DURATION = 60;
        
        // WebSocket消息类型
        public const string MSG_TYPE_HELLO = "hello";
        public const string MSG_TYPE_GOODBYE = "goodbye";
        public const string MSG_TYPE_TTS = "tts";
        public const string MSG_TYPE_LISTEN = "listen";
        public const string MSG_TYPE_IOT = "iot";
        public const string MSG_TYPE_ABORT = "abort";
        
        // TTS状态
        public const string TTS_STATE_IDLE = "idle";
        public const string TTS_STATE_START = "start";
        public const string TTS_STATE_SENTENCE_START = "sentence_start";
        public const string TTS_STATE_STOP = "stop";
        
        // 监听状态
        public const string LISTEN_STATE_START = "start";
        public const string LISTEN_STATE_STOP = "stop";
    }
}