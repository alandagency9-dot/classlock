using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;

namespace ClassLock
{
    /// <summary>
    /// The student app talks to the SYSTEM service over a local named pipe.
    /// One request line of JSON, one answer line of JSON.
    /// </summary>
    public static class Pipe
    {
        public const string Name = "ClassLockAgent";
        public const int MaxLine = 8192;

        /// <summary>Reads one line, at most MaxLine characters. Null at end of stream.</summary>
        public static string ReadLine(Stream stream)
        {
            var bytes = new List<byte>();
            while (true)
            {
                int b = stream.ReadByte();
                if (b < 0) return bytes.Count == 0 ? null : Encoding.UTF8.GetString(bytes.ToArray());
                if (b == '\n') return Encoding.UTF8.GetString(bytes.ToArray());
                if (bytes.Count >= MaxLine) throw new IOException("Message too long.");
                bytes.Add((byte)b);
            }
        }

        public static void WriteLine(Stream stream, string line)
        {
            byte[] data = Encoding.UTF8.GetBytes(line.Replace("\n", " ") + "\n");
            stream.Write(data, 0, data.Length);
            stream.Flush();
        }

        /// <summary>Client side, used by the student app.</summary>
        public static Dictionary<string, object> Call(Dictionary<string, object> request, int timeoutMs)
        {
            using (var client = new NamedPipeClientStream(".", Name, PipeDirection.InOut))
            {
                client.Connect(2000);
                WriteLine(client, Json.Write(request));
                var readTask = System.Threading.Tasks.Task.Factory.StartNew(() => ReadLine(client));
                if (!readTask.Wait(timeoutMs)) throw new TimeoutException("The ClassLock service did not answer.");
                string line = readTask.Result;
                if (line == null) throw new IOException("The ClassLock service closed the connection.");
                return Json.ReadObject(line);
            }
        }
    }
}
