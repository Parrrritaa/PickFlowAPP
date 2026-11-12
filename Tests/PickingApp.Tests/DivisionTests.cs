using PickingApp.Models;

namespace PickingApp.Tests
{
    // Partes de un pedido dividido (Pedido.NuevaParte, DetallePedido.Copia), compartidas por la división y la demostración
    public class DivisionTests
    {
        [Fact]
        public void UnaParteHeredaLaCabeceraYQuedaPendiente()
        {
            var original = new Pedido
            {
                CodigoPedido = "PED-7", BodegaId = 3, Prioridad = Prioridades.Alta, Estado = PedidoEstados.Dividido,
                FechaCarga = new DateTime(2026, 10, 1, 9, 0, 0)
            };

            var parte = original.NuevaParte(2);

            Assert.Equal("PED-7-P2", parte.CodigoPedido);
            Assert.Equal((3, Prioridades.Alta, PedidoEstados.Pendiente, 2), (parte.BodegaId, parte.Prioridad, parte.Estado, parte.NumeroParte));
            Assert.Equal(original.FechaCarga, parte.FechaCarga);
            Assert.Same(original, parte.PedidoPadre);
            Assert.Empty(parte.Detalles);
        }

        [Fact]
        public void LaCopiaDeUnaLineaCambiaSoloLaCantidad()
        {
            var linea = new DetallePedido { Id = 9, CodigoProducto = "PROD-1", NombreProducto = "Caja", Cantidad = 10, UbicacionId = 4 };

            var copia = linea.Copia(3);

            Assert.Equal(("PROD-1", "Caja", 3, 4, 0), (copia.CodigoProducto, copia.NombreProducto, copia.Cantidad, copia.UbicacionId, copia.Id));
        }
    }
}
