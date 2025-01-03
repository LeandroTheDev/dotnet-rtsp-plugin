﻿using System.Diagnostics;

namespace RTSPPlugin
{
    /// <summary>
    /// Stores in directory a single stream
    /// </summary>
    public class VideoStream
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
                        Debug.WriteLine($"Video Stream has been killed with id: {processId}");
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
        private Timer? timeoutTimer;

        public VideoStream(
            string cameraAddress,
            string outputPath,
            byte compression = 5,
            byte quality = 23,
            byte framerate = 30,
            string bitrate = "1M",
            string? ffmpegPath = null,
            string codec = "libx265",
            string fileType = "matroska",
            int cutTimerInSeconds = 0,
            int timeout = 10000,
            bool enableLogs = false)
        {
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

            Debug.WriteLine($"[VideoStream] Starting...");

            CameraAddress = cameraAddress;
            if (cutTimerInSeconds > 0)
                Arguments = $"-i \"{CameraAddress}\" -c:v {codec} -preset {preset} -r {framerate} -crf {quality} -b:v {bitrate} -f {fileType} -t {cutTimerInSeconds} \"{OutputPath}\"";
            else
                Arguments = $"-i \"{CameraAddress}\" -c:v {codec} -preset {preset} -r {framerate} -crf {quality} -b:v {bitrate} -f {fileType} \"{OutputPath}\"";

            if (enableLogs)
                Debug.WriteLine($"[VideoStream] Address: {cameraAddress}\nArguments: {Arguments}");

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
                    Debug.WriteLine($"[FFmpeg Output] {e.Data}");
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
                        Debug.WriteLine($"[Ffmpeg Error] {e.Data}");
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

            ActivesStreams.Add(FfmpegProcess.Id);
            if (enableLogs)
                Debug.WriteLine($"[VideoStream] Starting Ffmpeg Process");
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
