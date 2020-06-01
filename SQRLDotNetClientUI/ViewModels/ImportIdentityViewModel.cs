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
    public class ImportIdentityViewModel: ViewModelBase
    {       
        private string _textualIdentity = "";
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
        /// Gets or sets the current frame from the webcam.
        /// </summary>
        Bitmap CameraFrame
        {
            get { return _cameraFrame; }
            set { this.RaiseAndSetIfChanged(ref _cameraFrame, value); }
        }

        public string TextualIdentity 
        { 
            get => _textualIdentity; 
            set { this.RaiseAndSetIfChanged(ref _textualIdentity, value); } 
        }

        private string _identityFile="";
        public string IdentityFile 
        {
            get => _identityFile; 
            set { this.RaiseAndSetIfChanged(ref _identityFile, value); } 
        }

        private bool _canImport = false;
        public bool CanImport
        {
            get => _canImport;
            set { this.RaiseAndSetIfChanged(ref _canImport, value); }
        }

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
        }

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

        public async void ImportQrCode()
        {
            Log.Information("QR-code scan initiated");

            await Task.Run(() =>
            {
                VideoCapture capture = new VideoCapture();
                capture.Open(0, VideoCaptureAPIs.ANY);

                if (!capture.IsOpened())
                {
                    // Could not find a suitable camera/capture device
                    // Create an "video frame" containing an error message

                    System.Drawing.Bitmap errorBmp = new System.Drawing.Bitmap(640, 480);
                    using (var graphics = System.Drawing.Graphics.FromImage(errorBmp))
                    {
                        graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
                        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        graphics.FillRectangle(System.Drawing.Brushes.Black, new System.Drawing.Rectangle(0, 0, errorBmp.Width, errorBmp.Height));
                        System.Drawing.StringFormat sf = new System.Drawing.StringFormat()
                        {
                            Alignment = System.Drawing.StringAlignment.Center,
                            LineAlignment = System.Drawing.StringAlignment.Center
                        };

                        graphics.DrawString(
                            @"This is a test let's look how it's being drawn to the image asdfasd sfwefewf wefawef a f!",
                            new System.Drawing.Font("Arial", 18), 
                            System.Drawing.Brushes.White,
                            new System.Drawing.RectangleF(20, 20, errorBmp.Width - 20, errorBmp.Height - 20), 
                            sf);

                        // Display the error frame in the UI
                        using (var stream = new MemoryStream())
                        {
                            errorBmp.Save(stream, System.Drawing.Imaging.ImageFormat.Bmp);
                            stream.Seek(0, SeekOrigin.Begin);
                            Bitmap frame = new Bitmap(stream);

                            Dispatcher.UIThread.Post(() =>
                                this.CameraFrame = frame
                            );
                        }
                    }

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
                        Dispatcher.UIThread.Post(() =>
                        {
                            _identityDataFromQrCode = qrCodes[0];
                            ImportVerify();
                        });
                        
                        break;
                    }
                }

                capture.Dispose();
            }, _token);
            
        }

        public void Cancel()
        {
            if (!_cts.IsCancellationRequested)
            {
                _cts.Cancel();
            }

            ((MainWindowViewModel)_mainWindow.DataContext).Content = 
                ((MainWindowViewModel)_mainWindow.DataContext).PriorContent;
        }

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
