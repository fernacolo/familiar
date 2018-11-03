using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using wcmd.Native;
using Image = System.Windows.Controls.Image;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace wcmd.UI
{
    /// <summary>
    /// Interaction logic for AttachSelector.xaml
    /// </summary>
    public partial class AttachSelector : Window
    {
        public AttachSelector()
        {
            InitializeComponent();
        }

        private void Window_Loaded( object sender, RoutedEventArgs e )
        {
            var processlist = Process.GetProcesses();

            foreach ( var process in processlist )
            {
                if ( process.MainWindowHandle == IntPtr.Zero )
                    continue;
                if ( User32.IsIconic( process.MainWindowHandle ) )
                    continue;

                var bitmap = PrintWindow( process.MainWindowHandle );
                var image = new Image();
                image.Source = bitmap;
                SpMain.Children.Add( image );
            }
        }

        public static BitmapImage PrintWindow( IntPtr hwnd )
        {
            User32.GetWindowRect( hwnd, out var rc );

            var bmp = new Bitmap( rc.Width, rc.Height, PixelFormat.Format32bppArgb );
            using ( var bmpGfx = Graphics.FromImage( bmp ) )
            {
                var hdcBitmap = bmpGfx.GetHdc();
                User32.PrintWindow( hwnd, hdcBitmap, 0 );
                bmpGfx.ReleaseHdc( hdcBitmap );
                bmpGfx.Dispose();
            }

            using ( var memory = new MemoryStream() )
            {
                bmp.Save( memory, ImageFormat.Png );
                memory.Position = 0;
                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memory;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();

                return bitmapImage;
            }
        }
    }
}