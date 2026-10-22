using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using BarracudaSwitch;

class UiPreview
{
    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using (var host = new Form { ClientSize = new Size(600, 600), Controls = { } })
        {
            // Prove the test renderer itself can show a WinForms control tree.
            host.Controls.Add(new Label { Text = "render check", AutoSize = false, Dock = DockStyle.Fill });
            host.Show();
            Application.DoEvents();
            using (var check = new Bitmap(host.Width, host.Height))
            {
                host.DrawToBitmap(check, new Rectangle(0, 0, check.Width, check.Height));
                Console.WriteLine("HOST_RENDER_NONWHITE=" + HasInk(check));
            }
            host.Hide();
        }
        using (var audio = new AudioCore(null))
        {
            string[] original = audio.Read().Defaults;
            try
            {
                Settings settings = Settings.Load();
                using (var engine = new SwitchEngine(settings))
                using (var hotkeys = new HotkeyWindow(engine, delegate { }))
                using (var form = new SettingsForm(engine, settings, hotkeys, delegate { }))
                {
                    form.Show();
                    var clock = Stopwatch.StartNew();
                    while (clock.ElapsedMilliseconds < 1800) { Application.DoEvents(); Thread.Sleep(30); }
                    typeof(SettingsForm).GetMethod("RefreshStatus", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null);
                    form.PerformLayout();
                    Rectangle bounds = form.Bounds;
                    using (var image = new Bitmap(bounds.Width, bounds.Height))
                    {
                        form.DrawToBitmap(image, new Rectangle(0, 0, image.Width, image.Height));
                        image.Save(args[0], ImageFormat.Png);
                        Console.WriteLine("FORM_RENDER_NONWHITE=" + HasInk(image));
                    }
                    form.Hide();
                    Console.WriteLine(Json.Encode(new { Image = args[0], Width = form.Width, Height = form.Height, Status = engine.Status }));
                }
            }
            finally { audio.RestoreDefaults(original); }
        }
        return 0;
    }
    static bool HasInk(Bitmap image)
    {
        for (int y = 0; y < image.Height; y += 5)
            for (int x = 0; x < image.Width; x += 5)
            {
                Color color = image.GetPixel(x, y);
                if (color.R < 240 || color.G < 240 || color.B < 240) return true;
            }
        return false;
    }
}
