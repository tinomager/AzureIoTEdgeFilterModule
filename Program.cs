using System;
using AzureIoTEdgeModuleShared;

namespace AzureIoTEdgeAnomalyDetectModule
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
    using Microsoft.Azure.Devices.Client.Transport.Mqtt;
    using Microsoft.Azure.Devices.Shared;
    using Newtonsoft.Json;

    class Program
    {
        static void Main(string[] args)
        {
            // The Edge runtime gives us the connection string we need -- it is injected as an environment variable
            string connectionString = Environment.GetEnvironmentVariable("EdgeHubConnectionString");

            // Cert verification is not yet fully functional when using Windows OS for the container
            bool bypassCertVerification = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            if (!bypassCertVerification) InstallCert();
            Init(connectionString, bypassCertVerification).Wait();

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
        /// Add certificate in local cert store for use by client for secure connection to IoT Edge runtime
        /// </summary>
        static void InstallCert()
        {
            string certPath = Environment.GetEnvironmentVariable("EdgeModuleCACertificateFile");
            if (string.IsNullOrWhiteSpace(certPath))
            {
                // We cannot proceed further without a proper cert file
                Console.WriteLine($"Missing path to certificate collection file: {certPath}");
                throw new InvalidOperationException("Missing path to certificate file.");
            }
            else if (!File.Exists(certPath))
            {
                // We cannot proceed further without a proper cert file
                Console.WriteLine($"Missing path to certificate collection file: {certPath}");
                throw new InvalidOperationException("Missing certificate file.");
            }
            X509Store store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            store.Add(new X509Certificate2(X509Certificate2.CreateFromCertFile(certPath)));
            Console.WriteLine("Added Cert: " + certPath);
            store.Close();
        }


        /// <summary>
        /// Initializes the DeviceClient and sets up the callback to receive
        /// messages containing temperature information
        /// </summary>
        static async Task Init(string connectionString, bool bypassCertVerification = false)
        {
            Console.WriteLine("Connection String {0}", connectionString);

            MqttTransportSettings mqttSetting = new MqttTransportSettings(TransportType.Mqtt_Tcp_Only);
            // During dev you might want to bypass the cert verification. It is highly recommended to verify certs systematically in production
            if (bypassCertVerification)
            {
                mqttSetting.RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
            }
            ITransportSettings[] settings = { mqttSetting };

            // Open a connection to the Edge runtime
            DeviceClient ioTHubModuleClient = DeviceClient.CreateFromConnectionString(connectionString, settings);

            // Attach callback for Twin desired properties updates
            await ioTHubModuleClient.SetDesiredPropertyUpdateCallbackAsync(onDesiredPropertiesUpdate, ioTHubModuleClient);

            // Execute callback method for Twin desired properties updates
            var twin = await ioTHubModuleClient.GetTwinAsync();
            await onDesiredPropertiesUpdate(twin.Properties.Desired, ioTHubModuleClient);
            var moduleTwinCollection = twin.Properties.Desired;
            
            //check for default desired properties
            if (moduleTwinCollection["TemperatureMeanValue"] != null)
            {
                TemperatureMeanValue = moduleTwinCollection["TemperatureMeanValue"];
            }
            if (moduleTwinCollection["TemperatureStdDeviation"] != null)
            {
                TemperatureStdDeviation = moduleTwinCollection["TemperatureStdDeviation"];
            }
            if (moduleTwinCollection["HumidityMeanValue"] != null)
            {
                HumidityMeanValue = moduleTwinCollection["HumidityMeanValue"];
            }
            if (moduleTwinCollection["HumidityStdDeviation"] != null)
            {
                HumidityStdDeviation = moduleTwinCollection["HumidityStdDeviation"];
            }

            await ioTHubModuleClient.OpenAsync();

            Console.WriteLine("Anomaly Detect Edge module client initialized.");

            await ioTHubModuleClient.SetInputMessageHandlerAsync("input1", MessageReceived, ioTHubModuleClient);       

        }

        private static async Task<MessageResponse> MessageReceived(Message message, object userContext)
        {
            try{
                var deviceClient = userContext as DeviceClient;

                if (deviceClient == null)
                {
                    throw new InvalidOperationException("UserContext doesn't contain " + "expected values");
                }
             
                byte[] messageBytes = message.GetBytes();
                string messageString = Encoding.UTF8.GetString(messageBytes);
                Console.WriteLine($"Received message {DateTime.UtcNow.ToString()}: [{messageString}]");

                // Get message body
                var messageBody = JsonConvert.DeserializeObject<DHTMessageBody>(messageString);

                //do the scoring
                var prediction = AnomalyDetector.IsAnomaly(messageBody.temperature, messageBody.humidity);

                var predictionMessage = new ScoredDHTMessageBody(messageBody, prediction);

                var jsonMessage = JsonConvert.SerializeObject(predictionMessage);

                var pipeMessage = new Message(Encoding.UTF8.GetBytes(jsonMessage));

                pipeMessage.Properties.Add("content-type", "application/json");

                await deviceClient.SendEventAsync("output1", pipeMessage);

                Console.WriteLine($"Scored data sent {predictionMessage.timeCreated}: {predictionMessage.temperature} |  {predictionMessage.humidity} | Anomaly {predictionMessage.IsAnomaly}");
            }
            catch(Exception ex){
                Console.WriteLine($"Exception occured: {ex.Message}");
                Console.WriteLine(ex);
            }

            return MessageResponse.Completed;
        }

        private static double TemperatureMeanValue {get;set;}

        private static double TemperatureStdDeviation {get;set;}

        private static double HumidityMeanValue {get;set;}

        private static double HumidityStdDeviation {get;set;}

        private static AnomalyDetector AnomalyDetector { get; set; }

        private static Task onDesiredPropertiesUpdate(TwinCollection desiredProperties, object userContext)
        {
            if (desiredProperties.Count == 0)
            {
                return Task.CompletedTask;
            }

            try
            {
                Console.WriteLine("Desired property change:");
                Console.WriteLine(JsonConvert.SerializeObject(desiredProperties));

                var deviceClient = userContext as DeviceClient;

                if (deviceClient == null)
                {
                    throw new InvalidOperationException("UserContext doesn't contain " + "expected values");
                }

                var reportedProperties = new TwinCollection();

                if (desiredProperties["TemperatureMeanValue"] != null)
                {
                    TemperatureMeanValue = desiredProperties["TemperatureMeanValue"];

                    reportedProperties["TemperatureMeanValue"] = TemperatureMeanValue;
                }

                if (desiredProperties["TemperatureStdDeviation"] != null)
                {
                    TemperatureStdDeviation = desiredProperties["TemperatureStdDeviation"];

                    reportedProperties["TemperatureStdDeviation"] = TemperatureStdDeviation;
                }

                if (desiredProperties["HumidityMeanValue"] != null)
                {
                    HumidityMeanValue = desiredProperties["HumidityMeanValue"];

                    reportedProperties["HumidityMeanValue"] = HumidityMeanValue;
                }

                if (desiredProperties["HumidityStdDeviation"] != null)
                {
                    HumidityStdDeviation = desiredProperties["HumidityStdDeviation"];

                    reportedProperties["HumidityStdDeviation"] = HumidityStdDeviation;
                }

                if (reportedProperties.Count > 0)
                {
                    deviceClient.UpdateReportedPropertiesAsync(reportedProperties).ConfigureAwait(false);
                }
            }
            catch (AggregateException ex)
            {
                foreach (Exception exception in ex.InnerExceptions)
                {
                    Console.WriteLine();
                    Console.WriteLine("Error when receiving desired property: {0}", exception);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("Error when receiving desired property: {0}", ex.Message);
            }

            return Task.CompletedTask;
        }        
    }

}
