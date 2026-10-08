using PickingApp.Models;
using PickingApp.Services;

namespace PickingApp.Tests
{
    // Cambios de producto o de estado sobre una ubicación que está en pedidos (ReglasUbicacion.ValidarCambioAsync).
    public class ReglasUbicacionTests
    {
        [Theory]
        [InlineData(PedidoEstados.Pendiente)]
        [InlineData(PedidoEstados.Asignado)]
        [InlineData(PedidoEstados.Reasignado)]
        public async Task NoCambiaDeProductoConPedidosSinTerminar(string estado)
        {
            using var e = new Escenario();
            var ubicacion = e.Ubicacion(Escenario.Pequeno);
            var pedido = e.Pedido(estado, (ubicacion, 3));

            var r = await ReglasUbicacion.ValidarCambioAsync(e.Db, ubicacion, cambiaProducto: true, quedaActiva: true);

            Assert.NotNull(r);
            Assert.Contains(pedido.CodigoPedido, r);
        }

        [Fact]
        public async Task NoSeDesactivaConPedidosSinTerminar()
        {
            using var e = new Escenario();
            var ubicacion = e.Ubicacion(Escenario.Pequeno);
            e.Pedido(PedidoEstados.Pendiente, (ubicacion, 3));

            var r = await ReglasUbicacion.ValidarCambioAsync(e.Db, ubicacion, cambiaProducto: false, quedaActiva: false);

            Assert.NotNull(r);
        }

        [Theory]
        [InlineData(PedidoEstados.Completado)]
        [InlineData(PedidoEstados.Cancelado)]
        [InlineData(PedidoEstados.Dividido)]
        public async Task LosPedidosTerminadosNoImpidenElCambio(string estado)
        {
            using var e = new Escenario();
            var ubicacion = e.Ubicacion(Escenario.Pequeno);
            e.Pedido(estado, (ubicacion, 3));

            var r = await ReglasUbicacion.ValidarCambioAsync(e.Db, ubicacion, cambiaProducto: true, quedaActiva: false);

            Assert.Null(r);
        }

        [Fact]
        public async Task CambiarSoloElCodigoNoSeImpide()
        {
            using var e = new Escenario();
            var ubicacion = e.Ubicacion(Escenario.Pequeno);
            e.Pedido(PedidoEstados.Pendiente, (ubicacion, 3));

            var r = await ReglasUbicacion.ValidarCambioAsync(e.Db, ubicacion, cambiaProducto: false, quedaActiva: true);

            Assert.Null(r);
        }
    }
}
