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
using ProcessaReserva.RegisterDevices;
using ProcessaReserva.Repositories;
using DomainClasses;
using ProcessaReserva.Services;

namespace ProcessaReserva
{
    public class WorkerRole : RoleEntryPoint
    {
        private static ReservaService _reservaService = new ReservaService();

        private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private readonly ManualResetEvent runCompleteEvent = new ManualResetEvent(false);

        // referência para a fila contida no account storage da nuvem
        static CloudQueue cloudQueue;        

        // Utilizado para fazer a conexão com o storage account e acessar a fila
        const string connectionString = "DefaultEndpointsProtocol=https;AccountName=trabalhocomputacaonuvem;AccountKey=YNmHYnn5Wl0MzKJUVoKvn98KtkFhIcTYGPNS4TZEsOfPvMKuzl1OV65bv3FXxdRH2/hNluFNxONcUkBUlNnePw==";

        // id da fila fornecida pelo serviço de storage account
        const string idQueue = "demoqueue";

        // referência para o serviço de hub notification
        private static NotificationHubClient _hub;

        // Utilizado para fazer a configuração do acesso ao hub de notificação
        const string defaultFullSharedAccessSignature = "Endpoint=sb://hubcomputacaonuvem.servicebus.windows.net/;SharedAccessKeyName=DefaultFullSharedAccessSignature;SharedAccessKey=rkAHLz42DgWACrTS1OAT9u5cagW1GgRHEYc7IunkTV8=";

        // nome dado ao serviço de hub notification
        const string hubName = "hubcomputacaonuvem";

        // serviço de notificação que será utilizado pelo hub
        const string platform = "gcm";
        //Contador para device
        static int contador = 1;

        public WorkerRole()
        {            
            // cria a referência para a fila contida no cloud storage
            CloudStorageAccount cloudStorageAccount;

            if (!CloudStorageAccount.TryParse(connectionString, out cloudStorageAccount))
            {
                Trace.TraceInformation("Erro ao conectar com o Storage account");
            }

            var cloudQueueClient = cloudStorageAccount.CreateCloudQueueClient();

            cloudQueue = cloudQueueClient.GetQueueReference(idQueue);

            cloudQueue.CreateIfNotExists();

            // cria a referência para o serviço de hub notification
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

                //obtém a mensagem da fila existente no Azure
                GetMessageFromQueue();

                await Task.Delay(60000);
            }
        }

        public async void GetMessageFromQueue()
        {
            var cloudQueueMessage = cloudQueue.GetMessage();

            if (cloudQueueMessage == null)
            {
                return;
            }

            // trata da mensagem caso esta não seja nula
            await handleMessage(cloudQueueMessage);

        }


        private async Task handleMessage(CloudQueueMessage cloudQueueMessage)
        {
            PedidoReservaQuadra pedido;

          

            try
            {
                // tenta converter o objeto que foi serializado no formato JSON
                pedido = JsonConvert.DeserializeObject<PedidoReservaQuadra>(cloudQueueMessage.AsString);

                Trace.TraceInformation(cloudQueueMessage.AsString);                

                // obtém a mensagem que será enviada via push
                string mensagem = getPushNotificationMessage(pedido);
                string tag = "teste" + contador;
                contador++;

                // Temos que fazer uma forma de criar uma identificação única para o Hub, é por esse nome que ele envia a mensagem
                registerDeviceOnHubService(pedido, platform, tag);

                // Enviar a mensagem para o devaice
                await SendNotificationAsync(platform, mensagem, tag);

                //exclui a mensagem da fila
                cloudQueue.DeleteMessage(cloudQueueMessage);
            }
            catch (Exception e)
            {
                Trace.TraceInformation("It is not a Pedido object");
            }
        }

        // Método que delega a reserva da quadra e devolve uma mensagem de acordo com o resultado da requisição de reserva
        private static string getPushNotificationMessage(PedidoReservaQuadra pedido)
        {
            List<string> horariosReservados = _reservaService.fazerReserva(pedido);
            
            // Monta a mensagem de notificação de acordo com o resultado retornado pelo serviço
            if (horariosReservados == null)
            {
                return "Reserva não pode ser realizada";
            } else
            {
                string stringReservaComSucesso = "Reserva realizada com sucesso! Horários reservados:";

                foreach (string horarios in horariosReservados)
                {
                    stringReservaComSucesso += " " + horarios;
                }

                return stringReservaComSucesso;
            }

            
        }

        // Método que delega o registro do device do usuário no hub de notificação
        private void registerDeviceOnHubService(PedidoReservaQuadra pedido, string platform, string tag)
        {
            var handle = pedido.deviceId;

            // Realizar o registro do device no serviço de HUB da azure. ESte método verifica se o device já foi cadastrado e caso já tenha ocorrido
            // o seu cadastro, o mesmo e eliminado e cadastrado novamente
            string registrationId = Program.CreateRegistrationIdAsync(new DeviceRegistration() {
                Handle = handle, Platform = platform,
                Tags = new List<string>() {
                    tag
                }
            }, _hub, defaultFullSharedAccessSignature, hubName).Result;
        }

        // Prepara e envia a mensagem para o hub de notificação que realizará a push notification
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
