using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

public static class ArgoxPplb
{
    private const double DotsPerMm = 300.0 / 25.4;

    public static byte[] Build(string[] ids, string[][] matrices, double mediaWidth,
        double labelWidth, double labelHeight, double horizontalGap, double verticalGap,
        double leftMargin, int columns)
    {
        if (ids.Length == 0 || ids.Length != matrices.Length)
            throw new ArgumentException("Daftar label USB tidak lengkap.");

        using (var stream = new MemoryStream())
        {
            Write(stream, "\nq" + Dots(mediaWidth) + "\nQ" + Dots(labelHeight) + "," + Dots(verticalGap) + "\nZT\n");
            for (int index = 0; index < ids.Length; index++)
            {
                int column = index % columns;
                if (column == 0) Write(stream, "N\n");
                if (!Regex.IsMatch(ids[index], @"\ANG-[0-9]{6,}\z"))
                    throw new ArgumentException("ID label tidak valid.");

                string[] matrix = matrices[index];
                double qrWidth = Math.Min(labelHeight * 0.88, labelWidth * 0.45);
                int cell = matrix.Length > 0 ? (int)(Dots(qrWidth) / matrix.Length) : 0;
                if (cell < 2) throw new ArgumentException("Matriks QR terlalu besar untuk stiker.");
                int side = matrix.Length * cell;
                int rowBytes = (side + 7) / 8;
                byte[] raster = new byte[rowBytes * side];
                for (int y = 0; y < side; y++)
                {
                    string row = matrix[y / cell];
                    if (row.Length != matrix.Length) throw new ArgumentException("Matriks QR harus persegi.");
                    for (int x = 0; x < rowBytes * 8; x++)
                    {
                        if (x >= side || row[x / cell] == '0')
                            raster[y * rowBytes + x / 8] |= (byte)(128 >> (x % 8));
                    }
                }

                double labelLeft = leftMargin + column * (labelWidth + horizontalGap);
                int xQr = Dots(labelLeft) + (Dots(qrWidth) - side) / 2;
                int yQr = (Dots(labelHeight) - side) / 2;
                Write(stream, "GW" + xQr + "," + yQr + "," + rowBytes + "," + side + ",");
                stream.Write(raster, 0, raster.Length);
                int textX = Dots(labelLeft + qrWidth + 0.3);
                int textY = (Dots(labelHeight) - 16) / 2;
                Write(stream, "\nA" + textX + "," + textY + ",0,1,1,1,N,\"" + ids[index] + "\"\n");
                if (column == columns - 1 || index == ids.Length - 1) Write(stream, "P1\n");
            }
            return stream.ToArray();
        }
    }

    private static int Dots(double mm) { return (int)Math.Round(mm * DotsPerMm); }

    private static void Write(Stream stream, string text)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DocInfo
    {
        public string Name;
        public string OutputFile;
        public string Type;
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinter(string printer, out IntPtr handle, IntPtr defaults);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int StartDocPrinter(IntPtr handle, int level, ref DocInfo info);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr handle, byte[] buffer, int size, out int written);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr handle);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool AbortPrinter(IntPtr handle);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr handle);

    public static void Send(string printer, string name, byte[] data)
    {
        IntPtr handle;
        if (!OpenPrinter(printer, out handle, IntPtr.Zero)) throw new Win32Exception();
        bool started = false;
        try
        {
            var info = new DocInfo { Name = name, Type = "RAW" };
            if (StartDocPrinter(handle, 1, ref info) == 0) throw new Win32Exception();
            started = true;
            int written;
            if (!WritePrinter(handle, data, data.Length, out written)) throw new Win32Exception();
            if (written != data.Length) throw new IOException("Spooler hanya menerima sebagian job.");
            if (!EndDocPrinter(handle)) throw new Win32Exception();
            started = false;
        }
        finally
        {
            if (started) AbortPrinter(handle);
            ClosePrinter(handle);
        }
    }
}
