namespace rpicputemp
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Runtime.Loader;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Devices.Client;
    using Microsoft.Extensions.Configuration;

    class Program
    {
        static int counter;

        static void Main(string[] args)
        {
            Init().Wait();

            // Wait until the app unloads or is cancelled
            var cts = new CancellationTokenSource();
            AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
            Console.CancelKeyPress += (sender, cpe) => cts.Cancel();
            WhenCancelled(cts.Token).Wait();
        }

        /// <summary>
        /// Handles cleanup operations when app is cancelled or unloads
        /// </summary>
        public static Task WhenCancelled(CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).SetResult(true), tcs);
            return tcs.Task;
        }

        /// <summary>
        /// Initializes the ModuleClient and sets up the callback to receive
        /// messages containing temperature information
        /// </summary>
        static async Task Init()
        {

            IConfiguration configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("config/appsettings.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            int delay = configuration.GetValue("Delay", 5000);

            AmqpTransportSettings amqpSetting = new AmqpTransportSettings(TransportType.Amqp_Tcp_Only);
            ITransportSettings[] settings = { amqpSetting };

            // Open a connection to the Edge runtime
            ModuleClient ioTHubModuleClient = await ModuleClient.CreateFromEnvironmentAsync(settings);
            await ioTHubModuleClient.OpenAsync();
            Console.WriteLine("IoT Hub module client initialized.");

            // Register callback to be called when a message is received by the module
            await ioTHubModuleClient.SetInputMessageHandlerAsync("input1", PipeMessage, ioTHubModuleClient);

            // Start sending messages
            await SendTempMessages(ioTHubModuleClient, delay);
        }

        /// <summary>
        /// This method is called whenever the module is sent a message from the EdgeHub. 
        /// It just pipe the messages without any change.
        /// It prints all the incoming messages.
        /// </summary>
        static async Task<MessageResponse> PipeMessage(Message message, object userContext)
        {
            int counterValue = Interlocked.Increment(ref counter);

            var moduleClient = userContext as ModuleClient;
            if (moduleClient == null)
            {
                throw new InvalidOperationException("UserContext doesn't contain " + "expected values");
            }

            byte[] messageBytes = message.GetBytes();
            string messageString = Encoding.UTF8.GetString(messageBytes);
            Console.WriteLine($"Received message: {counterValue}, Body: [{messageString}]");

            if (!string.IsNullOrEmpty(messageString))
            {
                var pipeMessage = new Message(messageBytes);
                foreach (var prop in message.Properties)
                {
                    pipeMessage.Properties.Add(prop.Key, prop.Value);
                }
                await moduleClient.SendEventAsync("output1", pipeMessage);
                Console.WriteLine("Received message sent");
            }
            return MessageResponse.Completed;
        }

        static async Task SendTempMessages(ModuleClient moduleClient, int delay)
        {
            Console.WriteLine($"Start sending messages with a delay of {delay}.");
            int i = 0;
            while (true)
            {
                try
                {
                    MessageBody msg = new MessageBody()
                    {
                        MessageNumber = i,
                        ThermalZone0Temp = GetThermalZone0Temp()

                    };
                    string dataBuffer = Newtonsoft.Json.JsonConvert.SerializeObject(msg);
                    var eventMessage = new Message(Encoding.UTF8.GetBytes(dataBuffer));

                    Console.WriteLine($"{DateTime.Now.ToLocalTime()}> Sending message: {i}, Body: [{dataBuffer}]");

                    await moduleClient.SendEventAsync("output1", eventMessage);
                    i++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);

                }
                await Task.Delay(delay);
            }
        }

        private static double GetThermalZone0Temp()
        {
            string tv;
            using (System.IO.TextReader tr =
                new System.IO.StreamReader("/sys/class/thermal/thermal_zone0/temp"))
            {
                tv = tr.ReadLine();
            }
            return double.Parse(tv) / 1000;
        }
    }

    public class MessageBody
    {
        public int MessageNumber { get; set; }
        public double ThermalZone0Temp { get; set; }
    }
}
