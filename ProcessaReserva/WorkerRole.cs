using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Diagnostics;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json;
using Microsoft.ServiceBus.Notifications;
using DomainClasses;
using ProcessaReserva.RegisterDevices;

namespace ProcessaReserva
{
    public class WorkerRole : RoleEntryPoint
    {
        private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private readonly ManualResetEvent runCompleteEvent = new ManualResetEvent(false);

        static CloudQueue cloudQueue;
        private static NotificationHubClient _hub;
        // Utilizado para fazer a cofiguração do device e
        string defaultFullSharedAccessSignature = "Endpoint=sb://hubcomputacaonuvem.servicebus.windows.net/;SharedAccessKeyName=DefaultFullSharedAccessSignature;SharedAccessKey=rkAHLz42DgWACrTS1OAT9u5cagW1GgRHEYc7IunkTV8=";
        string hubName = "hubcomputacaonuvem";

        public WorkerRole()
        {
            var connectionString = "DefaultEndpointsProtocol=https;AccountName=trabalhocomputacaonuvem;AccountKey=YNmHYnn5Wl0MzKJUVoKvn98KtkFhIcTYGPNS4TZEsOfPvMKuzl1OV65bv3FXxdRH2/hNluFNxONcUkBUlNnePw==";

            CloudStorageAccount cloudStorageAccount;

            if (!CloudStorageAccount.TryParse(connectionString, out cloudStorageAccount))
            {
                Trace.TraceInformation("Deu erro");
            }

            var cloudQueueClient = cloudStorageAccount.CreateCloudQueueClient();
            cloudQueue = cloudQueueClient.GetQueueReference("demoqueue");

            // Note: Usually this statement can be executed once during application startup or maybe even never in the application.
            //       A queue in Azure Storage is often considered a persistent item which exists over a long time.
            //       Every time .CreateIfNotExists() is executed a storage transaction and a bit of latency for the call occurs.
            cloudQueue.CreateIfNotExists();

            //string defaultFullSharedAccessSignature = "Endpoint=sb://hubcomputacaonuvem.servicebus.windows.net/;SharedAccessKeyName=DefaultFullSharedAccessSignature;SharedAccessKey=rkAHLz42DgWACrTS1OAT9u5cagW1GgRHEYc7IunkTV8=";
            //string hubName = "hubcomputacaonuvem";
            _hub = NotificationHubClient.CreateClientFromConnectionString(defaultFullSharedAccessSignature, hubName);
        }

        public override void Run()
        {
            Trace.TraceInformation("ProcessaReserva is running");

            try
            {
                this.RunAsync(this.cancellationTokenSource.Token).Wait();
            }
            finally
            {
                this.runCompleteEvent.Set();
            }
        }

        public override bool OnStart()
        {
            // Set the maximum number of concurrent connections
            ServicePointManager.DefaultConnectionLimit = 12;

            // For information on handling configuration changes
            // see the MSDN topic at https://go.microsoft.com/fwlink/?LinkId=166357.

            bool result = base.OnStart();

            Trace.TraceInformation("ProcessaReserva has been started");

            return result;
        }

        public override void OnStop()
        {
            Trace.TraceInformation("ProcessaReserva is stopping");

            this.cancellationTokenSource.Cancel();
            this.runCompleteEvent.WaitOne();

            base.OnStop();

            Trace.TraceInformation("ProcessaReserva has stopped");
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            // TODO: Replace the following with your own logic.
            while (!cancellationToken.IsCancellationRequested)
            {
                Trace.TraceInformation("Working");

                GetMessageFromQueue();

                await Task.Delay(1000);
            }
        }

        public async void GetMessageFromQueue()
        {
            var cloudQueueMessage = cloudQueue.GetMessage();

            if (cloudQueueMessage == null)
            {
                return;
            }

            PedidoReservaQuadra pedido;

            try
            {
                pedido = JsonConvert.DeserializeObject<PedidoReservaQuadra>(cloudQueueMessage.AsString);
                Trace.TraceInformation(cloudQueueMessage.AsString);
                cloudQueue.DeleteMessage(cloudQueueMessage);


                //Temos que configurar as mensagem aqui para o usuário
                String mensagen = pedido.quadra.name;
                
                var handle = pedido.deviceId;

                var platform = "gcm";

                // Temos que fazer uma forma de criar uma identificação única para o Hub, é por esse nome que ele envia a mensagem
                var tag = "Teste";
                
                // Realizar o registro do device no serviço de HUB da azure. ESte método verifica se o device já foi cadastrado e caso já tenha ocorrido
                // o seu cadastro, o mesmo e eliminado e cadastrado novamente
                string registrationId = Program.CreateRegistrationIdAsync(new DeviceRegistration() { Handle = handle, Platform = platform, Tags = new List<string>() { tag } },_hub, defaultFullSharedAccessSignature, hubName).Result;                
                
                // Enviar a mensagem para o devaice
                await SendNotificationAsync("gcm", mensagen, tag);
            }
            catch (Exception e)
            {
                Trace.TraceInformation("It is not a Pedido object");
            }

            
        }

        public async Task<bool> SendNotificationAsync(string platform, string message, string to_tag)
        {
            var user = "Pelada dos amigos";
            string[] userTag = new string[1];
            userTag[0] = to_tag;

            NotificationOutcome outcome = null;

            switch (platform.ToLower())
            {
                case "wns":
                    // Windows 8.1 / Windows Phone 8.1
                    var toast = @"<toast><visual><binding template=""ToastText01""><text id=""1"">" +
                                "From " + user + ": " + message + "</text></binding></visual></toast>";
                    outcome = await _hub.SendWindowsNativeNotificationAsync(toast, userTag);

                    // Windows 10 specific Action Center support
                    toast = @"<toast><visual><binding template=""ToastGeneric""><text id=""1"">" +
                                "From " + user + ": " + message + "</text></binding></visual></toast>";
                    outcome = await _hub.SendWindowsNativeNotificationAsync(toast, userTag);

                    break;
                case "apns":
                    // iOS
                    var alert = "{\"aps\":{\"alert\":\"" + "From " + user + ": " + message + "\"}}";
                    outcome = await _hub.SendAppleNativeNotificationAsync(alert, userTag);
                    break;
                case "gcm":
                    // Android
                    var notif = "{ \"data\" : {\"message\":\"" + "From " + user + ": " + message + "\"}}";
                    outcome = await _hub.SendGcmNativeNotificationAsync(notif, userTag);
                    break;
            }

            if (outcome != null)
            {
                if (!((outcome.State == NotificationOutcomeState.Abandoned) ||
                    (outcome.State == NotificationOutcomeState.Unknown)))
                {
                    return true;
                }
            }
            return false;
        }
    }


    
}
