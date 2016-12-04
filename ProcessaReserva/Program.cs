using Microsoft.ServiceBus.Notifications;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProcessaReserva.RegisterDevices
{
    // Classe para registro do device no serviço do hub de notificação
    class Program
    {

        public static async Task<string> CreateRegistrationIdAsync(DeviceRegistration deviceUpdate, NotificationHubClient _hub, string defaultFullSharedAccessSignature, string hubName)
        {
            string newRegistrationId = null;
            var handle = deviceUpdate.Handle;
            _hub = NotificationHubClient.CreateClientFromConnectionString(defaultFullSharedAccessSignature, hubName);
            if (handle != null)
            {
                var registrations = await _hub.GetRegistrationsByChannelAsync(handle, 100);

                foreach (RegistrationDescription registration in registrations)
                {
                    await _hub.DeleteRegistrationAsync(registration);
                }
            }

            newRegistrationId = await CreateOrUpdateRegistrationAsync(deviceUpdate, _hub, defaultFullSharedAccessSignature, hubName);

            return newRegistrationId;
        }

        private static async Task<string> CreateOrUpdateRegistrationAsync(DeviceRegistration deviceUpdate, NotificationHubClient _hub, string defaultFullSharedAccessSignature, string hubName)
        {

            var newRegistrationId = await _hub.CreateRegistrationIdAsync();

            RegistrationDescription registration = null;
            switch (deviceUpdate.Platform)
            {
                case "mpns":
                    registration = new MpnsRegistrationDescription(deviceUpdate.Handle);
                    break;
                case "wns":
                    registration = new WindowsRegistrationDescription(deviceUpdate.Handle);
                    break;
                case "apns":
                    registration = new AppleRegistrationDescription(deviceUpdate.Handle);
                    break;
                case "gcm":
                    registration = new GcmRegistrationDescription(deviceUpdate.Handle);
                    break;
            }

            registration.RegistrationId = newRegistrationId;

            // add check if user is allowed to add these tags
            registration.Tags = new HashSet<string>(deviceUpdate.Tags);

            await _hub.CreateOrUpdateRegistrationAsync(registration);

            return registration.RegistrationId;
        }
    }
}

