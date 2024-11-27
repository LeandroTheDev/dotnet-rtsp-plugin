using System.Diagnostics;

namespace RTSPPlugin
{
    public class VideoStream
    {
        private readonly string CameraAddress = string.Empty;
        private readonly string Arguments = string.Empty;
        private readonly string FfmpegPath = string.Empty;
        private readonly string OutputPath = string.Empty;
        private readonly string OutputDirectory = string.Empty;
        private Process? FfmpegProcess = null;

        /// <summary>
        /// If any errors occurs will be stored in this variable
        /// </summary>
        public string? ErrorMessage { get; private set; }

        /// <summary>
        /// Returns the error message
        /// </summary>
        public Action<string>? OnStreamFail { get; set; }

        /// <summary>
        /// Returns the output path from the stream, only invoked when the stream ends with the code 0
        /// </summary>
        public Action<string>? OnStreamEnd { get; set; }

        private int untilTimeout = 0;
        private readonly System.Timers.Timer timeoutTimer;

        public VideoStream(
            string cameraAddress,
            string outputPath,
            byte quality = 5,
            byte framerate = 1,
            string? ffmpegPath = null,
            string fileType = "matroska",
            string codec = "libx265",
            int cutTimerInSeconds = 0,
            int timeout = 5000)
        {
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

            OutputPath = outputPath;
            OutputDirectory = Path.GetDirectoryName(OutputPath) ??
                throw new ArgumentException($"Cannot get the output directory from: {OutputPath}");

            if (!Directory.Exists(OutputDirectory))
                Directory.CreateDirectory(OutputDirectory);

            if (cutTimerInSeconds > 0)
            {
                var fileName = Path.GetFileNameWithoutExtension(OutputPath);
                var fileExtension = Path.GetExtension(OutputPath);
                OutputPath = Path.Combine(OutputDirectory, fileName + $"-{DateTime.Now:yyyyMMdd-HHmmss}" + fileExtension);
            }

            if (File.Exists(OutputPath))
                File.Delete(OutputPath);

            Debug.WriteLine($"[Stream]: Starting...");

            CameraAddress = cameraAddress;
            if (cutTimerInSeconds > 0)
                Arguments = $"-i \"{CameraAddress}\" -c:v {codec} -preset {preset} -r {framerate} -f {fileType} -t {cutTimerInSeconds} \"{OutputPath}\"";
            else
                Arguments = $"-i \"{CameraAddress}\" -c:v {codec} -preset {preset} -r {framerate} -f {fileType} \"{OutputPath}\"";

            Debug.WriteLine($"[Stream]: Address: {cameraAddress} Arguments: {Arguments}");

            var startInfo = new ProcessStartInfo
            {
                FileName = FfmpegPath,
                Arguments = Arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            FfmpegProcess = new()
            {
                StartInfo = startInfo
            };

            FfmpegProcess.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    Debug.WriteLine($"[FFmpeg Output]: {e.Data}");
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
                    untilTimeout = 0;
                    Debug.WriteLine($"[Ffmpeg Error]: {e.Data}");
                }
            };

            FfmpegProcess.EnableRaisingEvents = true;
            FfmpegProcess.Exited += (sender, e) =>
            {
                if (FfmpegProcess?.ExitCode == 0)
                    OnStreamEnd?.Invoke(OutputPath);
            };

            Debug.WriteLine($"[Stream]: Starting Ffmpeg Process");
            FfmpegProcess.Start();
            FfmpegProcess.BeginOutputReadLine();
            FfmpegProcess.BeginErrorReadLine();

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
