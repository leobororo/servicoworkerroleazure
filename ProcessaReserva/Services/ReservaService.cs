using DomainClasses;
using ProcessaReserva.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcessaReserva.Services
{
    // Serviço para a reserva da quadra
    class ReservaService
    {
        private static readonly IQuadraRepository _quadraRepository = new QuadraRepository();

        public List<string> fazerReserva(PedidoReservaQuadra pedido)
        {
            Quadra quadraComPedidoReserva = pedido.quadra;
            Quadra quadra = _quadraRepository.Get(pedido.quadra._id.ToString());

            bool reservaPossivel = true;
            List<string> horariosReservados = new List<string>();

            // Verifica que as requisições de reserva podem ser atendidas de acordo com o estado da quadra que está persistido no MongoDB
            for (int i = 0; i < quadraComPedidoReserva.reservas.Count; i++)
            {
                if (quadraComPedidoReserva.reservas.ElementAt(i).select && !quadra.reservas.ElementAt(i).select)
                {
                    quadra.reservas.ElementAt(i).select = true;
                    horariosReservados.Add(quadra.reservas.ElementAt(i).hour);
                }
                else if (quadraComPedidoReserva.reservas.ElementAt(i).select && quadra.reservas.ElementAt(i).select)
                {
                    reservaPossivel = false;
                }

            }

            // Salva as modificações caso seja possível fazer a reserva
            if (reservaPossivel)
            {
                return _quadraRepository.Save(pedido.quadra._id.ToString(), quadra.reservas) ? horariosReservados : null;
            } else
            {
                return null;
            }
        }
    }
}
