using System.Diagnostics;

namespace RTSPPlugin
{
    public class ImageStream
    {
        private readonly string Arguments = string.Empty;
        private readonly string FfmpegPath = string.Empty;
        private readonly string VideoPath = string.Empty;
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
        private readonly System.Timers.Timer timeoutTimer;

        public ImageStream(
            string videoPath,
            byte quality = 1,
            byte framerate = 1,
            string? ffmpegPath = null,
            string codec = "libx265",
            int timeout = 5000)
        {
            VideoPath = videoPath;

            if (ffmpegPath == null)
                FfmpegPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ffmpeg", "bin", "ffmpeg.exe");
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

            Arguments = $"-i \"{VideoPath}\" -c:v {codec} -preset {preset} -r {framerate} -f image2pipe -vcodec png -";

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
                        ErrorMessage = e.Data["Error opening input files: ".Length..];
                        OnStreamFail?.Invoke(ErrorMessage);
                    }
                    Debug.WriteLine($"[ImageStream Error]: {e.Data}");
                }
            };

            FfmpegProcess.Start();
            FfmpegProcess.BeginErrorReadLine();

            Task.Run(() =>
            {
                using var stream = FfmpegProcess.StandardOutput.BaseStream;
                int currentByte;
                Queue<byte> headerBuffer = new();
                receivingImage = [];

                byte[] pngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
                byte[] pngFooter = [0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82];

                while ((currentByte = stream.ReadByte()) != -1)
                {
                    byte currentByteValue = (byte)currentByte;
                    headerBuffer.Enqueue(currentByteValue);

                    // Maintain the buffer size the same as the header size
                    if (headerBuffer.Count > pngHeader.Length)
                        headerBuffer.Dequeue();

                    // Header check
                    if (headerBuffer.SequenceEqual(pngHeader))
                    {
                        untilTimeout = 0;
                        Debug.WriteLine("[ImageStream] PNG Header Detected!");
                        receivingImage = new List<byte>(pngHeader);
                    }
                    // Inside the PNG
                    else if (receivingImage.Count > 0)
                    {
                        receivingImage.Add(currentByteValue);

                        // Footer check
                        if (receivingImage.Count >= pngFooter.Length &&
                            receivingImage.Skip(receivingImage.Count - pngFooter.Length).Take(pngFooter.Length).SequenceEqual(pngFooter))
                        {
                            Debug.WriteLine($"[ImageStream] PNG Frame Complete! Size: {receivingImage.Count} bytes");

                            LastFrameBytes = receivingImage;
                            OnImageUpdate?.Invoke(LastFrameBytes);

                            receivingImage.Clear();
                        }
                    }
                }
            });

            int increaser = 100;
            timeoutTimer = new System.Timers.Timer()
            {
                Interval = increaser,
                AutoReset = true,
            };
            timeoutTimer.Elapsed += (sender, e) =>
            {
                if (!timeoutTimer.Enabled) return;

                untilTimeout += increaser;
                if (untilTimeout >= timeout)
                {
                    timeoutTimer.Stop();
                    ErrorMessage = "Stream Timeout";
                    OnStreamFail?.Invoke(ErrorMessage);
                }
            };
            timeoutTimer.Start();

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
                timeoutTimer.Stop();
                timeoutTimer.Dispose();
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