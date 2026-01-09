using System;
using System.Drawing;
using System.Drawing.Imaging;

// Create 192x192 icon
var bmp = new Bitmap(192, 192);
var g = Graphics.FromImage(bmp);
g.Clear(Color.FromArgb(0, 173, 238)); // #00ADEE
g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
var font = new Font("Arial", 48, FontStyle.Bold);
var brush = new SolidBrush(Color.White);
var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
g.DrawString("GB", font, brush, new RectangleF(0, 0, 192, 192), sf);
bmp.Save("icon-color.png", ImageFormat.Png);
bmp.Dispose();
g.Dispose();

// Create 32x32 icon
var bmp2 = new Bitmap(32, 32);
var g2 = Graphics.FromImage(bmp2);
g2.Clear(Color.White);
var pen = new Pen(Color.FromArgb(0, 173, 238), 2);
g2.DrawRectangle(pen, 1, 1, 30, 30);
var font2 = new Font("Arial", 8, FontStyle.Bold);
var brush2 = new SolidBrush(Color.FromArgb(0, 173, 238));
var sf2 = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
g2.DrawString("GB", font2, brush2, new RectangleF(0, 0, 32, 32), sf2);
bmp2.Save("icon-outline.png", ImageFormat.Png);
bmp2.Dispose();
g2.Dispose();
