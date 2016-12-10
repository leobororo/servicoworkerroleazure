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

        // refer�ncia para a fila contida no account storage da nuvem
        static CloudQueue cloudQueue;        

        // Utilizado para fazer a conex�o com o storage account e acessar a fila
        const string connectionString = "DefaultEndpointsProtocol=https;AccountName=trabalhocomputacaonuvem;AccountKey=YNmHYnn5Wl0MzKJUVoKvn98KtkFhIcTYGPNS4TZEsOfPvMKuzl1OV65bv3FXxdRH2/hNluFNxONcUkBUlNnePw==";

        // id da fila fornecida pelo servi�o de storage account
        const string idQueue = "demoqueue";

        // refer�ncia para o servi�o de hub notification
        private static NotificationHubClient _hub;

        // Utilizado para fazer a configura��o do acesso ao hub de notifica��o
        const string defaultFullSharedAccessSignature = "Endpoint=sb://hubcomputacaonuvem.servicebus.windows.net/;SharedAccessKeyName=DefaultFullSharedAccessSignature;SharedAccessKey=rkAHLz42DgWACrTS1OAT9u5cagW1GgRHEYc7IunkTV8=";

        // nome dado ao servi�o de hub notification
        const string hubName = "hubcomputacaonuvem";

        // servi�o de notifica��o que ser� utilizado pelo hub
        const string platform = "gcm";
        //Contador para device
        static int contador = 1;

        public WorkerRole()
        {            
            // cria a refer�ncia para a fila contida no cloud storage
            CloudStorageAccount cloudStorageAccount;

            if (!CloudStorageAccount.TryParse(connectionString, out cloudStorageAccount))
            {
                Trace.TraceInformation("Erro ao conectar com o Storage account");
            }

            var cloudQueueClient = cloudStorageAccount.CreateCloudQueueClient();

            cloudQueue = cloudQueueClient.GetQueueReference(idQueue);

            cloudQueue.CreateIfNotExists();

            // cria a refer�ncia para o servi�o de hub notification
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

                //obt�m a mensagem da fila existente no Azure
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

            // trata da mensagem caso esta n�o seja nula
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

                // obt�m a mensagem que ser� enviada via push
                string mensagem = getPushNotificationMessage(pedido);
                string tag = "teste" + contador;
                contador++;

                // Temos que fazer uma forma de criar uma identifica��o �nica para o Hub, � por esse nome que ele envia a mensagem
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

        // M�todo que delega a reserva da quadra e devolve uma mensagem de acordo com o resultado da requisi��o de reserva
        private static string getPushNotificationMessage(PedidoReservaQuadra pedido)
        {
            List<string> horariosReservados = _reservaService.fazerReserva(pedido);
            
            // Monta a mensagem de notifica��o de acordo com o resultado retornado pelo servi�o
            if (horariosReservados == null)
            {
                return "Reserva n�o pode ser realizada";
            } else
            {
                string stringReservaComSucesso = "Reserva realizada com sucesso! Hor�rios reservados:";

                foreach (string horarios in horariosReservados)
                {
                    stringReservaComSucesso += " " + horarios;
                }

                return stringReservaComSucesso;
            }

            
        }

        // M�todo que delega o registro do device do usu�rio no hub de notifica��o
        private void registerDeviceOnHubService(PedidoReservaQuadra pedido, string platform, string tag)
        {
            var handle = pedido.deviceId;

            // Realizar o registro do device no servi�o de HUB da azure. ESte m�todo verifica se o device j� foi cadastrado e caso j� tenha ocorrido
            // o seu cadastro, o mesmo e eliminado e cadastrado novamente
            string registrationId = Program.CreateRegistrationIdAsync(new DeviceRegistration() {
                Handle = handle, Platform = platform,
                Tags = new List<string>() {
                    tag
                }
            }, _hub, defaultFullSharedAccessSignature, hubName).Result;
        }

        // Prepara e envia a mensagem para o hub de notifica��o que realizar� a push notification
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
