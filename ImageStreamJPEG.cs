using System.Diagnostics;

namespace RTSPPlugin
{
    /// <summary>
    /// Creates any instance that updates the LastFrameBytes with the last image received from the streaming
    /// </summary>
    public class ImageStreamJPEG
    {
        /// <summary>
        /// Stores all process running by ImageStreamJPEG
        /// </summary>
        public static readonly List<int> ActivesStreams = [];

        /// <summary>
        /// Kill all process running in ActivesStreams
        /// </summary>
        public static void KillActivesStreams()
        {
            ActivesStreams.ForEach((processId) =>
            {
                try
                {
                    var process = Process.GetProcessById(processId);
                    if (process.ProcessName == "ffmpeg")
                    {
                        Debug.WriteLine($"Image Stream has been killed with id: {processId}");
                        process.Kill();
                    };
                }
                catch (Exception) { }
            });
        }

        private readonly string Arguments = string.Empty;
        private readonly string FfmpegPath = string.Empty;
        private readonly string CameraAddress = string.Empty;
        private Process? FfmpegProcess = null;

        /// <summary>
        /// If any errors occurs will be stored in this variable
        /// </summary>
        public string? ErrorMessage { get; private set; }

        /// <summary>
        /// Last image bytes from stream
        /// </summary>
        public List<byte> LastFrameBytes = [];

        /// <summary>
        /// Called every time we receive a new image from the stream, returns the image bytes
        /// </summary>
        public Action<List<byte>>? OnImageUpdate { get; set; }

        /// <summary>
        /// Returns the error message
        /// </summary>
        public Action<string>? OnStreamFail { get; set; }

        private int untilTimeout = 0;
        private Timer? timeoutTimer;

        public ImageStreamJPEG(
            string cameraAddress,
            string bitrate = "1M",
            string? ffmpegPath = null,
            int timeout = 10000,
            bool enableLogs = false)
        {
            CameraAddress = cameraAddress;

            if (ffmpegPath == null)
                FfmpegPath =
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ffmpeg", "bin", "ffmpeg.exe");
            else
                FfmpegPath = ffmpegPath;

            Arguments = $"-i \"{CameraAddress}\" -b:v {bitrate} -f image2pipe -vcodec mjpeg -";

            if (enableLogs)
                Debug.WriteLine($"[ImageStream] Address: {cameraAddress}\nArguments: {Arguments}");

            var startInfo = new ProcessStartInfo
            {
                FileName = FfmpegPath,
                Arguments = Arguments,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            FfmpegProcess = new()
            {
                StartInfo = startInfo
            };

            FfmpegProcess.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    if (e.Data.StartsWith("Error opening input files: Server returned"))
                    {
                        int startIndex = "Error opening input files: ".Length;
                        ErrorMessage = e.Data[startIndex..];
                        OnStreamFail?.Invoke(ErrorMessage);
                    }
                    if (enableLogs)
                        Debug.WriteLine($"[ImageStream Error]: {e.Data}");
                }
            };

            FfmpegProcess.Start();
            FfmpegProcess.BeginErrorReadLine();

            Task.Run(() =>
            {
                try
                {
                    using var stream = FfmpegProcess.StandardOutput.BaseStream;
                    int currentByte;
                    Queue<byte> headerBuffer = new();
                    List<byte> receivingImage = [];

                    byte[] jpegHeader = [0xFF, 0xD8];
                    byte[] jpegFooter = [0xFF, 0xD9];

                    while ((currentByte = stream.ReadByte()) != -1)
                    {
                        byte currentByteValue = (byte)currentByte;
                        headerBuffer.Enqueue(currentByteValue);

                        if (headerBuffer.Count > jpegHeader.Length)
                            headerBuffer.Dequeue();

                        if (headerBuffer.SequenceEqual(jpegHeader))
                        {
                            untilTimeout = 0;
                            if (enableLogs)
                                Debug.WriteLine("[ImageStream] JPEG Header Detected!");
                            receivingImage = new List<byte>(jpegHeader);
                        }
                        else if (receivingImage.Count > 0)
                        {
                            untilTimeout = 0;
                            receivingImage.Add(currentByteValue);

                            if (receivingImage.Count >= jpegFooter.Length &&
                                receivingImage.Skip(receivingImage.Count - jpegFooter.Length).Take(jpegFooter.Length).SequenceEqual(jpegFooter))
                            {
                                if (enableLogs)
                                    Debug.WriteLine($"[ImageStream] JPEG Frame Complete! Size: {receivingImage.Count} bytes");

                                LastFrameBytes = receivingImage;
                                OnImageUpdate?.Invoke(LastFrameBytes);

                                receivingImage.Clear();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ImageStream] error while streaming: {ex.Message}");
                }
            });

            int increaser = 100;
            timeoutTimer = new Timer((_) =>
            {
                try
                {
                    untilTimeout += increaser;
                    if (untilTimeout >= timeout)
                    {
                        timeoutTimer?.Dispose();
                        timeoutTimer = null;
                        ErrorMessage = "Stream Timeout";
                        OnStreamFail?.Invoke(ErrorMessage);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }
            }, null, 0, 100);

            ActivesStreams.Add(FfmpegProcess.Id);
            if (enableLogs)
                Debug.WriteLine($"[ImageStream]: Starting Ffmpeg Process");
        }

        /// <summary>
        /// Stops the stream process and clean the memory
        /// </summary>
        /// <returns></returns>
        public Task Dispose()
        {
            try
            {
                timeoutTimer?.Dispose();
                timeoutTimer = null;
            }
            catch (Exception) { }

            int? processId = FfmpegProcess?.Id;
            try
            {
                // Safe close
                FfmpegProcess?.StandardInput.WriteLine("q");
            }
            catch (Exception) { }

            try
            {
                // Memory cleanup
                ActivesStreams.Remove((int)processId!);
                FfmpegProcess?.Close();
                FfmpegProcess?.Dispose();
            }
            catch (Exception) { }
            FfmpegProcess = null;

            // Wait until process is finished
            return Task.Run(async () =>
            {
                if (processId == null) return;

                int maxTries = 50;
                int actualTries = 0;
                while (true)
                {
                    try
                    {
                        var processExistance = Process.GetProcessById((int)processId);
                        if (processExistance.ProcessName != "ffmpeg") return;

                        if (actualTries >= maxTries)
                        {
                            ActivesStreams.Remove((int)processId!);
                            processExistance.Kill();
                            return;
                        };

                        await Task.Delay(50);
                        actualTries++;
                    }
                    catch (Exception)
                    {
                        return;
                    }
                }
            });
        }
    }
}