using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcessaReserva.RegisterDevices
{
    // Essa classe eu copiei do projeto do professor
    class DeviceRegistration
    {
        public string RegistrationId { get; set; }
        public IList<string> Tags { get; set; }
        public string Platform { get; set; }
        public string Handle { get; set; }
    }
}
