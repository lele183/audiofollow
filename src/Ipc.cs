using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace BarracudaSwitch
{
    public sealed class Ipc : IDisposable
    {
        static readonly string Identity = WindowsIdentity.GetCurrent().User.Value;
        public static readonly string Name = "BarracudaSwitch." + Identity;
        public static readonly string MutexName = @"Local\" + Name;
        readonly Func<string, string> handler;
        readonly Thread thread;
        volatile bool stopping;
        NamedPipeServerStream server;
        readonly object gate = new object();

        public Ipc(Func<string, string> handler)
        {
            this.handler = handler;
            thread = new Thread(Serve) { IsBackground = true, Name = "Audio switcher control" };
            thread.Start();
        }

        void Serve()
        {
            var security = new PipeSecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(Identity), PipeAccessRights.FullControl, AccessControlType.Allow));
            while (!stopping)
            {
                try
                {
                    using (var pipe = new NamedPipeServerStream(Name, PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security))
                    {
                        lock (gate) { if (stopping) return; server = pipe; }
                        pipe.WaitForConnection();
                        using (var reader = new StreamReader(pipe))
                        using (var writer = new StreamWriter(pipe) { AutoFlush = true })
                        {
                            string command = reader.ReadLine();
                            if (command != null && command.Length < 128) writer.WriteLine(handler(command));
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (!stopping) { try { AppFiles.Log("IPC " + ex.Message); } catch { } Thread.Sleep(200); }
                }
                finally { lock (gate) { server = null; } }
            }
        }

        public static bool Send(string command, out string reply)
        {
            reply = null;
            try
            {
                using (var pipe = new NamedPipeClientStream(".", Name, PipeDirection.InOut, PipeOptions.Asynchronous))
                {
                    pipe.Connect(750);
                    using (var reader = new StreamReader(pipe))
                    using (var writer = new StreamWriter(pipe) { AutoFlush = true })
                    {
                        writer.WriteLine(command);
                        Task<string> read = reader.ReadLineAsync();
                        if (!read.Wait(2500)) return false;
                        reply = read.Result;
                        return reply != null;
                    }
                }
            }
            catch (Exception) { return false; }
        }

        public void Dispose()
        {
            stopping = true;
            lock (gate) { if (server != null) server.Dispose(); }
            thread.Join(1000);
        }
    }
}
