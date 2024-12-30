# RTSP Plugin for .NET
Download or visualize streams using rtsp protocol with [ffmpeg](https://www.ffmpeg.org/) software

### Using
Stream Address Example: ``rtsp://admin:password@127.0.0.1``

You need the [ffmpeg binary](https://www.ffmpeg.org/download.html), you can also choose the specific location to load the ffmpeg in the stream constructor, more examples below

Creating the Video Stream
```cs
private RTSPPlugin.VideoStream videoStreamInstance = null;

private async void StartVideoStream()
{
    if(videoStreamInstance != null)
        await videoStreamInstance.Dispose();

    OutputPath = Path.Combine(Directory.GetCurrentDirectory(), "Output", "stream.mkv");
    videoStreamInstance = new(
        "rtsp://admin:password@127.0.0.1", // Connection to the stream
        OutputPath, // Where the file will be stored
        quality: 5, // Compression Type
        framerate: 10, // Frames per second for the video
        ffmpegPath: Path.Combine(Directory.GetCurrentDirectory(), "Libs", "ffmpeg.exe"), // Where the ffmpeg is stored
        cutTimerInSeconds: 30, // The video download will stop at this time
        fileType: "matroska"); // Video fileType (you must change also the OutputPath extension)

     videoStreamInstance.OnStreamFail += StreamFailed;
     videoStreamInstance.OnStreamEnd += StreamEnded;

     Console.WriteLine("Stream Started");
}

private void StreamFailed(string obj) 
{
    Console.WriteLine($"Stream Failed Reason: {obj}");
    if(videoStreamInstance != null)
        videoStreamInstance.Dispose();
}

// Continuously getting the stream after the cutTimerInSeconds finish the stream
private void StreamEnded(string obj) => StartVideoStream();
```

Creating the Image Stream
```cs
private RTSPPlugin.ImageStream imageStreamInstance = null;

private async void StartImageStream()
{
    if(imageStreamInstance != null)
        await imageStreamInstance.Dispose();

    imageStreamInstance = new(
        "rtsp://admin:password@127.0.0.1", // Connection to the stream
        quality: 5, // Compression Type
        framerate: 10, // Frames per second for the video
        ffmpegPath: Path.Combine(Directory.GetCurrentDirectory(), "Libs", "ffmpeg.exe")); // Where the ffmpeg is stored
    imageStreamInstance.OnImageUpdate += ImageUpdated;
    imageStreamInstance.OnStreamFail += StreamFailed;
}

private void StreamFailed(string obj) 
{
    Console.WriteLine($"Stream Failed Reason: {obj}");
    if(imageStreamInstance != null)
        imageStreamInstance.Dispose();
}

// Every time the system receives a new image from stream will be invoked by this function with the image bytes
private void ImageUpdated(List<byte> bytes) => ...
```

### Technical Information
When using the VideoStream and you set up the ``cutTimerInSeconds`` the stream will automatically modify the file name to a name with DateTime.Now on the moment of stream started

Difference between Video Stream and Image Stream, the Video Stream will download the stream into a file, the Image Stream will get the stream frames and save it in the memory to be used

Working on Linux, you can make it work on linux easily, just change the ffmpeg path to the currently path in the linux system