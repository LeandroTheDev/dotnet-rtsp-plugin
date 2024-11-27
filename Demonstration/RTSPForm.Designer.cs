using System.Diagnostics;

namespace RTSPTemplate
{
    partial class RTSPForm
    {
        private RTSPPlugin.ImageStream imageStreamInstance = null;
        private RTSPPlugin.VideoStream videoStreamInstance = null;
        string OutputPath = string.Empty;

        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            _ = DisposeStreams();
            base.Dispose(disposing);
        }

        private async void StartImageStream(object sender, EventArgs e)
        {
            await DisposeStreams();

            imageStreamInstance = new(
                streamAddressTextBox.Text,
                quality: byte.Parse(qualityBox.Text),
                framerate: byte.Parse(framerateBox.Text),
                ffmpegPath: Path.Combine(Directory.GetCurrentDirectory(), "Libs", "ffmpeg.exe"));
            imageStreamInstance.OnImageUpdate += ImageUpdated;
            imageStreamInstance.OnStreamFail += StreamFailed;

            startStream.BackColor = Color.Green;
        }

        private async void StartVideoStream(object sender, EventArgs e)
        {
            await DisposeStreams();

            OutputPath = Path.Combine(Directory.GetCurrentDirectory(), "Output", "stream.mp4");
            videoStreamInstance = new(
                streamAddressTextBox.Text,
                OutputPath,
                quality: byte.Parse(qualityBox.Text),
                framerate: byte.Parse(framerateBox.Text),
                ffmpegPath: Path.Combine(Directory.GetCurrentDirectory(), "Libs", "ffmpeg.exe"),
                cutTimerInSeconds: int.Parse(cuttimerBox.Text),
                fileType: "mp4");

            videoStreamInstance.OnStreamFail += StreamFailed;
            videoStreamInstance.OnStreamEnd += StreamEnded;

            startDownload.BackColor = Color.Green;
            if (!(sender is string && (string)sender == "nodialog"))
                _ = Task.Run(() =>
                    MessageBox.Show($"Download Path: {OutputPath}", "Stream Download", MessageBoxButtons.OK, MessageBoxIcon.Information));
        }

        private void StreamEnded(string obj) => StartVideoStream("nodialog", null);


        private void StreamFailed(string obj)
        {
            DisposeStreams().ContinueWith((_) =>
                MessageBox.Show($"Stream Failed: {obj}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error));
        }


        private void ImageUpdated(List<byte> bytes)
        {
            if (bytes == null || bytes.Count == 0 || imageStreamInstance == null)
                return;

            try
            {
                byte[] imageBytes = bytes.ToArray();

                using (var ms = new MemoryStream(imageBytes))
                {
                    var image = Image.FromStream(ms);

                    streamView.Invoke((Action)(() =>
                    {
                        streamView.Image = image;
                    }));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating image: {ex.Message}");
            }
        }

        private void DisposeStream(object sender, EventArgs e) => _ = DisposeStreams();

        private async Task DisposeStreams()
        {
            if (videoStreamInstance != null)
                await videoStreamInstance.Dispose();
            if (imageStreamInstance != null)
                await imageStreamInstance.Dispose();
            imageStreamInstance = null;
            videoStreamInstance = null;
            streamView.Image = null;
            startDownload.BackColor = Color.White;
            startStream.BackColor = Color.White;
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            streamAddressTextBox = new TextBox();
            titleLabel = new Label();
            startStream = new Button();
            close = new Button();
            streamView = new PictureBox();
            startDownload = new Button();
            qualityBox = new TextBox();
            framerateBox = new TextBox();
            qualityLabel = new Label();
            framerateLabel = new Label();
            cuttimerBox = new TextBox();
            cuttimerLabel = new Label();
            ((System.ComponentModel.ISupportInitialize)streamView).BeginInit();
            SuspendLayout();
            // 
            // streamAddressTextBox
            // 
            streamAddressTextBox.Location = new Point(64, 29);
            streamAddressTextBox.Name = "streamAddressTextBox";
            streamAddressTextBox.Size = new Size(229, 23);
            streamAddressTextBox.TabIndex = 1;
            streamAddressTextBox.Text = "rtsp://admin:password@127.0.0.1";
            // 
            // titleLabel
            // 
            titleLabel.AutoSize = true;
            titleLabel.BackColor = Color.Transparent;
            titleLabel.ForeColor = SystemColors.ControlLightLight;
            titleLabel.Location = new Point(125, 9);
            titleLabel.Name = "titleLabel";
            titleLabel.Size = new Size(93, 15);
            titleLabel.TabIndex = 2;
            titleLabel.Text = "Camera Address";
            // 
            // startStream
            // 
            startStream.Location = new Point(489, 30);
            startStream.Name = "startStream";
            startStream.Size = new Size(87, 23);
            startStream.TabIndex = 3;
            startStream.Text = "Connect";
            startStream.UseVisualStyleBackColor = true;
            startStream.Click += StartImageStream;
            // 
            // close
            // 
            close.Location = new Point(663, 30);
            close.Name = "close";
            close.Size = new Size(75, 23);
            close.TabIndex = 4;
            close.Text = "Close";
            close.UseVisualStyleBackColor = true;
            close.Click += DisposeStream;
            // 
            // streamView
            // 
            streamView.Location = new Point(64, 93);
            streamView.Name = "streamView";
            streamView.Size = new Size(674, 301);
            streamView.SizeMode = PictureBoxSizeMode.AutoSize;
            streamView.TabIndex = 5;
            streamView.TabStop = false;
            // 
            // startDownload
            // 
            startDownload.Location = new Point(582, 29);
            startDownload.Name = "startDownload";
            startDownload.Size = new Size(75, 23);
            startDownload.TabIndex = 6;
            startDownload.Text = "Download";
            startDownload.UseVisualStyleBackColor = true;
            startDownload.Click += StartVideoStream;
            // 
            // qualityBox
            // 
            qualityBox.ImeMode = ImeMode.Disable;
            qualityBox.Location = new Point(336, 29);
            qualityBox.MaxLength = 1;
            qualityBox.Name = "qualityBox";
            qualityBox.Size = new Size(23, 23);
            qualityBox.TabIndex = 7;
            qualityBox.Text = "0";
            // 
            // framerateBox
            // 
            framerateBox.Location = new Point(430, 30);
            framerateBox.Name = "framerateBox";
            framerateBox.Size = new Size(23, 23);
            framerateBox.TabIndex = 8;
            framerateBox.Text = "1";
            // 
            // qualityLabel
            // 
            qualityLabel.AutoSize = true;
            qualityLabel.BackColor = Color.Transparent;
            qualityLabel.ForeColor = SystemColors.ControlLightLight;
            qualityLabel.Location = new Point(308, 9);
            qualityLabel.Name = "qualityLabel";
            qualityLabel.Size = new Size(71, 15);
            qualityLabel.TabIndex = 9;
            qualityLabel.Text = "Quality [0,8]";
            // 
            // framerateLabel
            // 
            framerateLabel.AutoSize = true;
            framerateLabel.BackColor = Color.Transparent;
            framerateLabel.ForeColor = SystemColors.ControlLightLight;
            framerateLabel.Location = new Point(411, 9);
            framerateLabel.Name = "framerateLabel";
            framerateLabel.Size = new Size(60, 15);
            framerateLabel.TabIndex = 10;
            framerateLabel.Text = "Framerate";
            // 
            // cuttimerBox
            // 
            cuttimerBox.Location = new Point(582, 58);
            cuttimerBox.Name = "cuttimerBox";
            cuttimerBox.Size = new Size(33, 23);
            cuttimerBox.TabIndex = 11;
            cuttimerBox.Text = "30";
            // 
            // cuttimerLabel
            // 
            cuttimerLabel.AutoSize = true;
            cuttimerLabel.BackColor = Color.Transparent;
            cuttimerLabel.ForeColor = SystemColors.ControlLightLight;
            cuttimerLabel.Location = new Point(621, 61);
            cuttimerLabel.Name = "cuttimerLabel";
            cuttimerLabel.Size = new Size(111, 15);
            cuttimerLabel.TabIndex = 12;
            cuttimerLabel.Text = "Seconds Per Stream";
            // 
            // RTSPForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = SystemColors.Desktop;
            ClientSize = new Size(800, 611);
            Controls.Add(cuttimerLabel);
            Controls.Add(cuttimerBox);
            Controls.Add(framerateLabel);
            Controls.Add(qualityLabel);
            Controls.Add(framerateBox);
            Controls.Add(qualityBox);
            Controls.Add(startDownload);
            Controls.Add(streamView);
            Controls.Add(close);
            Controls.Add(startStream);
            Controls.Add(titleLabel);
            Controls.Add(streamAddressTextBox);
            Name = "RTSPForm";
            Text = "RTSP Template";
            ((System.ComponentModel.ISupportInitialize)streamView).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private TextBox streamAddressTextBox;
        private Label titleLabel;
        private Button startStream;
        private Button close;
        private PictureBox streamView;
        private Button startDownload;
        private TextBox qualityBox;
        private TextBox framerateBox;
        private Label qualityLabel;
        private Label framerateLabel;
        private TextBox cuttimerBox;
        private Label cuttimerLabel;
    }
}
