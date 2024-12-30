using System.Diagnostics;

namespace RTSPPlugin
{
    /// <summary>
    /// Saves a sequential video every cutTimerInSeconds that can be merged in future
    /// </summary>
    public class SequentialStream
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
                        Debug.WriteLine($"Sequential Stream has been killed with id: {processId}");
                        process.Kill();
                    };
                }
                catch (Exception) { }
            });
        }

        private readonly string CameraAddress = string.Empty;
        private readonly string Arguments = string.Empty;
        private readonly string FfmpegPath = string.Empty;
        private readonly string OutputPath = string.Empty;
        private readonly string TempPath = string.Empty;
        private Process? FfmpegProcess = null;
        private bool ioProcessWorking = false;

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
        private Timer? timeoutTimer;
        private Timer? tempToOutputTimer;

        public SequentialStream(
            string cameraAddress,
            string outputPath,
            byte compression = 5,
            byte quality = 23,
            byte framerate = 30,
            string bitrate = "1M",
            string? ffmpegPath = null,
            string codec = "libx265",
            int cutTimerInSeconds = 60,
            int timeout = 10000,
            bool enableLogs = false)
        {
            if (cutTimerInSeconds < 1)
                throw new ArgumentException("cutTimerInSeconds needs to be bigger than 0");

            if (ffmpegPath == null)
                FfmpegPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ffmpeg", "bin", "ffmpeg.exe");
            else
                FfmpegPath = ffmpegPath;

            string preset = string.Empty;
            preset = compression switch
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
            TempPath = Path.Combine(outputPath, "Temp");

            if (!Directory.Exists(OutputPath))
                Directory.CreateDirectory(OutputPath);

            if (!Directory.Exists(TempPath))
                Directory.CreateDirectory(TempPath);
            else
            {
                Directory.Delete(TempPath, true);
                Directory.CreateDirectory(TempPath);
            }

            if (enableLogs)
                Debug.WriteLine($"[SequentialStream] Starting...");

            string ffmpegOutputPath = Path.Combine(TempPath, "output%09d.ts");

            CameraAddress = cameraAddress;
            Arguments = $"-i \"{CameraAddress}\" -c:v {codec} -g 30 -preset {preset} -r {framerate} -crf {quality} -b:v {bitrate} -c:a aac -f segment -segment_time {cutTimerInSeconds} -reset_timestamps 1 -segment_format mpegts \"{ffmpegOutputPath}\"";

            if (enableLogs)
                Debug.WriteLine($"[SequentialStream] Address: {cameraAddress}\nArguments: {Arguments}");

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
                if (!string.IsNullOrEmpty(e.Data) && enableLogs)
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

                    if (enableLogs)
                        Debug.WriteLine($"[Ffmpeg Error]: {e.Data}");
                }
            };

            FfmpegProcess.EnableRaisingEvents = true;
            FfmpegProcess.Exited += (sender, e) =>
            {
                if (FfmpegProcess?.ExitCode == 0)
                    OnStreamEnd?.Invoke(OutputPath);
            };

            FfmpegProcess.Start();
            FfmpegProcess.BeginOutputReadLine();
            FfmpegProcess.BeginErrorReadLine();

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

            tempToOutputTimer = new Timer((_) =>
            {
                if (!ioProcessWorking && Directory.Exists(TempPath))
                {
                    string[] files = Directory.GetFiles(TempPath);
                    if (files.Length >= 2)
                    {
                        ioProcessWorking = true;

                        // Finished video
                        {
                            var orderedFiles = files.OrderBy(File.GetCreationTime).ToArray();
                            string penultimateFile = orderedFiles[files.Length - 2];
                            string destinationPath = Path.Combine(outputPath, $"{DateTime.Now:yyyyMMdd-HHmm}.ts");

                            File.Move(penultimateFile, destinationPath, true);
                            if (enableLogs)
                                Debug.WriteLine($"[SequentialStream] {penultimateFile} moved to {destinationPath}");
                        }

                        // Unused temporary files
                        {
                            string[] filesAfter = Directory.GetFiles(TempPath);
                            if (filesAfter.Length >= 2)
                            {
                                var orderedFiles = files.OrderBy(File.GetCreationTime).ToArray();

                                string lastFile = orderedFiles.Last();

                                foreach (string file in orderedFiles)
                                {
                                    if (file != lastFile)
                                    {
                                        File.Delete(file);
                                        if (enableLogs)
                                            Debug.WriteLine($"[SequentialStream] Temporary deleted because no longer will be used {file}");
                                    }
                                }
                            }
                        }

                        ioProcessWorking = false;
                    }
                }
            }, null, 0, 1000);

            ActivesStreams.Add(FfmpegProcess.Id);
            if (enableLogs)
                Debug.WriteLine($"[SequentialStream] Starting Ffmpeg Process");
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

            try
            {
                tempToOutputTimer?.Dispose();
                tempToOutputTimer = null;
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
