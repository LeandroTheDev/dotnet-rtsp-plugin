using System.Diagnostics;

namespace RTSPPlugin
{
    public class ImageStreamJPEG
    {
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
        private List<byte> receivingImage = [];

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
            byte quality = 1,
            byte framerate = 1,
            string? ffmpegPath = null,
            string codec = "libx265",
            int timeout = 5000)
        {
            CameraAddress = cameraAddress;

            if (ffmpegPath == null)
                FfmpegPath =
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ffmpeg", "bin", "ffmpeg.exe");
            else
                FfmpegPath = ffmpegPath;

            string preset = string.Empty;
            preset = quality switch
            {
                0 => "ultrafast",
                1 => "superfast",
                2 => "veryfast",
                3 => "faster",
                4 => "fast",
                5 => "medium",
                6 => "slow",
                7 => "slower",
                8 => "veryslow",
                _ => throw new ArgumentException("Invalid quality number, use a number between 0 and 8"),
            };

            Arguments = $"-i \"{CameraAddress}\" -c:v {codec} -preset {preset} -r {framerate} -f image2pipe -vcodec mjpeg -";

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
                        ErrorMessage = e.Data.Substring(startIndex);
                        OnStreamFail?.Invoke(ErrorMessage);
                    }
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
                    List<byte> receivingImage = new();

                    byte[] jpegHeader = { 0xFF, 0xD8 };
                    byte[] jpegFooter = { 0xFF, 0xD9 };

                    while ((currentByte = stream.ReadByte()) != -1)
                    {
                        byte currentByteValue = (byte)currentByte;
                        headerBuffer.Enqueue(currentByteValue);

                        if (headerBuffer.Count > jpegHeader.Length)
                            headerBuffer.Dequeue();

                        if (headerBuffer.SequenceEqual(jpegHeader))
                        {
                            untilTimeout = 0;
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

            Debug.WriteLine($"[ImageStream]: Starting Ffmpeg Process");
        }

        /// <summary>
        /// Stops all the stream process and clean the memory
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