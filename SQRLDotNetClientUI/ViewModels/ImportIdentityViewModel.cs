using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using QRCodeDecoderLibrary;
using ReactiveUI;
using Serilog;
using SQRLDotNetClientUI.Views;
using SQRLUtilsLib;

namespace SQRLDotNetClientUI.ViewModels
{
    /// <summary>
    /// A view model representing the "import identity" screen.
    /// </summary>
    public class ImportIdentityViewModel: ViewModelBase
    {       
        private string _textualIdentity = "";
        private string _identityFile = "";
        private bool _canImport = false;
        private bool _canImportQrCode = true;
        private bool _showQrImport = false;
        private Bitmap _cameraFrame = null;
        private CancellationTokenSource _cts;
        private CancellationToken _token;
        private byte[] _identityDataFromQrCode = null;

        /// <summary>
        /// Gets or sets a value indicating whether or not to display the
        /// "import qr-code" UI.
        /// </summary>
        public bool ShowQrImport
        {
            get { return _showQrImport; }
            set 
            { 
                this.RaiseAndSetIfChanged(ref _showQrImport, value);
                if (value)
                {
                    _cts = new CancellationTokenSource();
                    _token = _cts.Token;
                    ImportQrCode();
                }
                else
                {
                    _cts.Cancel();
                }
            }
        }

        /// <summary>
        /// Gets or sets the current webcam preview frame.
        /// </summary>
        Bitmap CameraFrame
        {
            get { return _cameraFrame; }
            set { this.RaiseAndSetIfChanged(ref _cameraFrame, value); }
        }

        /// <summary>
        /// Gets or sets a value representing the textual identity.
        /// </summary>
        public string TextualIdentity 
        { 
            get => _textualIdentity; 
            set { this.RaiseAndSetIfChanged(ref _textualIdentity, value); } 
        }

        /// <summary>
        /// Gets or sets the path of the user-selected identity file.
        /// </summary>
        public string IdentityFile 
        {
            get => _identityFile; 
            set { this.RaiseAndSetIfChanged(ref _identityFile, value); } 
        }

        /// <summary>
        /// Gets or sets a value indicating whether the "Import" button is enabled. 
        /// </summary>        
        public bool CanImport
        {
            get => _canImport;
            set { this.RaiseAndSetIfChanged(ref _canImport, value); }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the "Import QR Code" button is enabled. 
        /// </summary>        
        public bool CanImportQrCode
        {
            get => _canImportQrCode;
            set { this.RaiseAndSetIfChanged(ref _canImportQrCode, value); }
        }

        /// <summary>
        /// Creates a new instance and initializes things.
        /// </summary>
        public ImportIdentityViewModel()
        {
            this.Title = _loc.GetLocalizationValue("ImportIdentityWindowTitle");

            this.WhenAnyValue(x => x.TextualIdentity, x => x.IdentityFile,
                  (te, id) => new Tuple<string, string>(te, id))
                .Subscribe((x) => 
                {
                    if (!string.IsNullOrEmpty(x.Item1) || !string.IsNullOrEmpty(x.Item2)) CanImport = true;
                    else CanImport = false;
                });
            
            try
            {
                SetBlackVideoFrame(_loc.GetLocalizationValue("OpeningCameraMessage"));
            }
            catch (Exception ex)
            {
                Log.Error($"Error while creating fake camera frame:\r\n{ex}");
                this.CanImportQrCode = false;

                // We need to delay the loading of the error message box until
                // the view model is fully loaded, otherwise it will mess up our
                // current/previous content model.
                _ = Task.Run(() =>
                {
                    Task.Delay(500);
                    Log.Information($"Showing libgdiplus error message");
                    Dispatcher.UIThread.Post(() => ShowLibGdiPlusErrorMsg());
                });
            }
        }

        /// <summary>
        /// Displays an error message telling the user that the libgdiplus 
        /// libraray is required for some functions to work correctly.
        /// </summary>
        private async void ShowLibGdiPlusErrorMsg()
        {
            await new MessageBoxViewModel(_loc.GetLocalizationValue("ErrorTitleGeneric"),
                _loc.GetLocalizationValue("MissingLibGdiPlusErrorMessage"),
                MessageBoxSize.Medium, MessageBoxButtons.OK, MessageBoxIcons.ERROR)
                .ShowDialog(this);
        }

        /// <summary>
        /// Displays a file picker dialog for importing an identity file.
        /// </summary>
        public async void ImportFile()
        {
            FileDialogFilter fdf = new FileDialogFilter();
            fdf.Extensions.Add("sqrl");
            fdf.Extensions.Add("sqrc");
            fdf.Name = _loc.GetLocalizationValue("FileDialogFilterName");

            OpenFileDialog ofd = new OpenFileDialog
            {
                AllowMultiple = false
            };
            ofd.Filters.Add(fdf);
            ofd.Title = _loc.GetLocalizationValue("ImportOpenFileDialogTitle");

            var files = await ofd.ShowAsync(_mainWindow);

            if(files != null && files.Length>0)
            {
                this.IdentityFile = files[0];
            }

            ImportVerify();
        }

        /// <summary>
        /// Toggles the visibility of the qr code UI.
        /// </summary>
        /// <param name="visible">Set to <c>true</c> to show the qr code, 
        /// or <c>false</c> to hide it</param>
        public void ToggleQrImport(bool visible)
        {
            this.ShowQrImport = visible;
        }

        /// <summary>
        /// Displays a video feed from the system's default camera and tries detecting 
        /// SQRL qr codes within the video frames.
        /// </summary>
        public async void ImportQrCode()
        {
            Log.Information("QR-code scan initiated");
            SetBlackVideoFrame(_loc.GetLocalizationValue("OpeningCameraMessage"));

            await Task.Run(() =>
            {
                Log.Information("Opening camera device");
                VideoCapture capture = new VideoCapture();
                capture.Open(0, VideoCaptureAPIs.ANY);

                if (!capture.IsOpened())
                {
                    // Could not find a suitable camera/capture device
                    // Create an "video frame" containing an error message
                    Log.Error("No camera or capture device found, bailing!");
                    SetBlackVideoFrame(_loc.GetLocalizationValue("NoCameraMessage"));
                    return;
                }

                while (true)
                {
                    using (var frameMat = capture.RetrieveMat())
                    {
                        // Check for cancellation
                        if (_token.IsCancellationRequested)
                        {
                            Log.Information("QR-code scan was cancelled");
                            break;
                        }

                        // If we get no video frame, just try again
                        if (frameMat.Empty()) continue;

                        // Display the video frame in the UI
                        using (MemoryStream stream = frameMat.ToMemoryStream())
                        {
                            stream.Seek(0, SeekOrigin.Begin);
                            Bitmap frame = new Bitmap(stream);

                            Dispatcher.UIThread.Post(() =>
                                this.CameraFrame = frame
                            );
                        }

                        // Try decoding a QR code within the video frame
                        QRDecoder qrDecoder = new QRDecoder();
                        byte[][] qrCodes = qrDecoder.ImageDecoder(BitmapConverter.ToBitmap(frameMat));

                        if (qrCodes == null || qrCodes.Length < 1) continue;

                        // Decoding succeeded, verify the imported data
                        Log.Information("QR code found while decoding, now verifying data");
                        Dispatcher.UIThread.Post(() =>
                        {
                            _identityDataFromQrCode = qrCodes[0];
                            ImportVerify();
                        });
                        
                        break;
                    }
                }

                Log.Information("Closing camera / capture device");
                capture.Dispose();
            }, _token);
        }

        /// <summary>
        /// Fills the image control with a fake "video frame" with black background and the given <paramref name="text"/>.
        /// </summary>
        /// <param name="text">The text to display on the black background.</param>
        private void SetBlackVideoFrame(string text)
        {
            using (System.Drawing.Bitmap errorBmp = new System.Drawing.Bitmap(640, 480))
            using (var graphics = System.Drawing.Graphics.FromImage(errorBmp))
            using (var stream = new MemoryStream())
            {
                graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.FillRectangle(System.Drawing.Brushes.Black, 
                    new System.Drawing.Rectangle(0, 0, errorBmp.Width, errorBmp.Height));
                System.Drawing.StringFormat sf = new System.Drawing.StringFormat()
                {
                    Alignment = System.Drawing.StringAlignment.Center,
                    LineAlignment = System.Drawing.StringAlignment.Center
                };

                graphics.DrawString(text, new System.Drawing.Font("Arial", 18), System.Drawing.Brushes.White,
                    new System.Drawing.RectangleF(20, 20, errorBmp.Width - 20, errorBmp.Height - 20), sf);

                // Display the error frame in the UI
                errorBmp.Save(stream, System.Drawing.Imaging.ImageFormat.Bmp);
                stream.Seek(0, SeekOrigin.Begin);
                Bitmap frame = new Bitmap(stream);

                Dispatcher.UIThread.Post(() => this.CameraFrame = frame);
            }
        }

        /// <summary>
        /// Either backs out of the QR code import UI or of the whole import screen.
        /// </summary>
        public void Cancel()
        {

            if (this.ShowQrImport)
            {
                Log.Information("Backing out of QR code import screen");
                this.ShowQrImport = false;
                return;
            }

            ((MainWindowViewModel)_mainWindow.DataContext).Content = 
                ((MainWindowViewModel)_mainWindow.DataContext).PriorContent;
        }

        /// <summary>
        /// Checks if one of the import methods has delivered import data, and if 
        /// that check succeeds, tries parsing that data and moves on to the next step.
        /// </summary>
        public  async void ImportVerify()
        {
            SQRLIdentity identity = null;
            if (!string.IsNullOrEmpty(this.TextualIdentity))
            {
                try
                {
                    byte[] identityBytes = SQRL.Base56DecodeIdentity(this.TextualIdentity);
                    bool noHeader = !SQRLIdentity.HasHeader(identityBytes);
                    identity = SQRLIdentity.FromByteArray(identityBytes, noHeader);
                }
                catch (Exception ex)
                {
                    await new MessageBoxViewModel(_loc.GetLocalizationValue("ErrorTitleGeneric"),
                        string.Format(_loc.GetLocalizationValue("TextualImportErrorMessage"), ex.Message),
                        MessageBoxSize.Medium, MessageBoxButtons.OK, MessageBoxIcons.ERROR)
                        .ShowDialog(this);
                }
            }
            else if (!string.IsNullOrEmpty(this.IdentityFile))
            {
                try
                {
                    identity = SQRLIdentity.FromFile(this.IdentityFile);
                }
                catch (Exception ex)
                {

                    await new MessageBoxViewModel(_loc.GetLocalizationValue("ErrorTitleGeneric"),
                        string.Format(_loc.GetLocalizationValue("FileImportErrorMessage"), ex.Message),
                        MessageBoxSize.Medium, MessageBoxButtons.OK, MessageBoxIcons.ERROR)
                        .ShowDialog(this);
                }
            }
            else if (_identityDataFromQrCode != null)
            {
                try
                {
                    bool noHeader = !SQRLIdentity.HasHeader(_identityDataFromQrCode);
                    identity = SQRLIdentity.FromByteArray(_identityDataFromQrCode, noHeader);
                }
                catch (Exception ex)
                {

                    await new MessageBoxViewModel(_loc.GetLocalizationValue("ErrorTitleGeneric"),
                        string.Format(_loc.GetLocalizationValue("QrCodeImportErrorMessage"), ex.Message),
                        MessageBoxSize.Medium, MessageBoxButtons.OK, MessageBoxIcons.ERROR)
                        .ShowDialog(this);

                    // Launch camera again to keep trying
                    ImportQrCode();
                }
            }

            if (identity != null)
            {
                ((MainWindowViewModel)_mainWindow.DataContext).Content = 
                    new ImportIdentitySetupViewModel(identity);
            }   
        }
    }
}
