using System;
using System.Diagnostics;
using System.Threading;
using BarracudaSwitch;

class Probe
{
    static int Main(string[] args)
    {
        try
        {
            foreach (HidInfo info in Hid.Enumerate()) Console.WriteLine("INTERFACE " + info);
            using (var sensor = new HeadsetSensor())
            {
                sensor.Trace = Console.WriteLine;
                var timer = Stopwatch.StartNew();
                string previous = null;
                double duration = args.Length > 0 ? double.Parse(args[0], System.Globalization.CultureInfo.InvariantCulture) : 8;
                while (timer.Elapsed.TotalSeconds < duration)
                {
                    sensor.Poll(250);
                    LinkReading value = sensor.Current;
                    string text = value.State + " | " + value.Evidence + " | " + value.Raw;
                    if (previous != text) { Console.WriteLine("STATE " + text); previous = text; }
                    Thread.Sleep(10);
                }
                return sensor.Current.State == LinkState.Connected || sensor.Current.State == LinkState.Disconnected ? 0 : 2;
            }
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
