using PickingApp.Services;

namespace PickingApp.Tests
{
    // Tiempo estándar = Σ unidades × minutos de la categoría ÷ unidades por caja + ubicaciones distintas × minutos de recorrido
    public class TiempoEstandarTests
    {
        [Fact]
        public async Task SumaLasUnidadesPorCategoriaYUnRecorridoPorUbicacionDistinta()
        {
            using var e = new Escenario();
            var cables = e.Ubicacion(Escenario.Pequeno);
            var monitores = e.Ubicacion(Escenario.Grande);
            // Dos líneas en la misma ubicación de cables: el recorrido hasta ella se cuenta una sola vez
            var pedido = e.Pedido(lineas: new[] { (cables, 6), (cables, 4), (monitores, 2) });

            var tiempo = await e.Motor().CalcularTiempoEstandarAsync(pedido.Id);

            // 10 × 0,3 + 2 × 4 + 2 ubicaciones × 1 min
            Assert.Equal(13.0, tiempo);
        }

        [Fact]
        public async Task UnaCajaCompletaCuestaLoMismoQueUnaUnidad()
        {
            using var e = new Escenario();
            var cajasDe12 = e.Ubicacion(Escenario.Grande, unidadesPorCaja: 12);
            var pedido = e.Pedido(lineas: new[] { (cajasDe12, 24) });

            var tiempo = await e.Motor().CalcularTiempoEstandarAsync(pedido.Id);

            // 24 unidades = 2 cajas = 2 tomas × 4 min, más 1 min de recorrido
            Assert.Equal(9.0, tiempo);
        }

        [Fact]
        public async Task UnProductoSinCategoriaUsaLaPredeterminada()
        {
            using var e = new Escenario();
            var sinClasificar = e.Ubicacion(categoriaId: null);
            var pedido = e.Pedido(lineas: new[] { (sinClasificar, 2) });

            var tiempo = await e.Motor().CalcularTiempoEstandarAsync(pedido.Id);

            // 2 × 1,5 (Mediano, la predeterminada) + 1 min de recorrido
            Assert.Equal(4.0, tiempo);
        }

        [Fact]
        public async Task NuncaBajaDeUnMinuto()
        {
            using var e = new Escenario();
            e.Parametro(ParametrosSistema.MinutosPorUbicacion, "0");
            var pedido = e.Pedido(lineas: new[] { (e.Ubicacion(Escenario.Pequeno), 1) });

            var tiempo = await e.Motor().CalcularTiempoEstandarAsync(pedido.Id);

            // 1 × 0,3 sin recorrido daría 0,3 min
            Assert.Equal(1.0, tiempo);
        }

        [Fact]
        public void ElTiempoRealNuncaEsMenorQueUnMinuto()
        {
            using var e = new Escenario();
            var inicio = new DateTime(2026, 10, 2, 10, 0, 0);

            Assert.Equal(1.0, e.Motor().CalcularTiempoProcesamiento(inicio, inicio.AddMinutes(-5)));
            Assert.Equal(12.5, e.Motor().CalcularTiempoProcesamiento(inicio, inicio.AddMinutes(12.5)));
        }
    }
}
