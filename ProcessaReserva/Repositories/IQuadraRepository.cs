using DomainClasses;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcessaReserva.Repositories
{
    interface IQuadraRepository
    {
        // método para obtenção de uma Quadra a partir de seu id
        Quadra Get(string id);

        // Método para salvar a lista de reservas de uma quadra com o id especificado
        bool Save(string id, List<Reserva> reservas);
    }
}
