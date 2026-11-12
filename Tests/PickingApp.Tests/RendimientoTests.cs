namespace PickingApp.Tests
{
    // Factor de rendimiento = Σ tiempo real ÷ Σ tiempo estándar de los pedidos completados (desde el último reinicio)
    public class RendimientoTests
    {
        [Fact]
        public async Task SinElMinimoDePedidosSeUsaElEstandar()
        {
            using var e = new Escenario();
            var empleado = e.Empleado();
            e.Completado(empleado, real: 20, estandar: 10);
            e.Completado(empleado, real: 20, estandar: 10);

            var r = (await e.Motor().CalcularRendimientosAsync(new[] { empleado.Id }))[empleado.Id];

            // Con 2 de 3 pedidos se mide (×2) pero no se aplica
            Assert.False(r.TieneHistorialSuficiente);
            Assert.Equal(2.0, r.FactorMedido);
            Assert.Equal(1.0, r.Factor);
        }

        [Fact]
        public async Task LosPedidosGrandesPesanMasQueLosPequenos()
        {
            using var e = new Escenario();
            var empleado = e.Empleado();
            e.Completado(empleado, real: 10, estandar: 10);
            e.Completado(empleado, real: 10, estandar: 10);
            e.Completado(empleado, real: 40, estandar: 20);

            var r = (await e.Motor().CalcularRendimientosAsync(new[] { empleado.Id }))[empleado.Id];

            // 60 / 40 = 1,5 (el promedio simple de las razones daría 1,33)
            Assert.True(r.TieneHistorialSuficiente);
            Assert.Equal(1.5, r.Factor);
        }

        [Theory]
        [InlineData(30, 2.0)]  // Tres veces el estándar: se limita al doble
        [InlineData(2, 0.5)]   // Una quinta parte del estándar: se limita a la mitad
        public async Task ElFactorSeLimitaEntreLaMitadYElDoble(double realPorPedido, double factorEsperado)
        {
            using var e = new Escenario();
            var empleado = e.Empleado();
            for (var i = 0; i < 3; i++) e.Completado(empleado, real: realPorPedido, estandar: 10);

            var r = (await e.Motor().CalcularRendimientosAsync(new[] { empleado.Id }))[empleado.Id];

            Assert.Equal(realPorPedido / 10, r.FactorMedido);
            Assert.Equal(factorEsperado, r.Factor);
        }

        [Fact]
        public async Task LosPedidosAnterioresAlReinicioNoCuentan()
        {
            using var e = new Escenario();
            var empleado = e.Empleado();
            var reinicio = new DateTime(2026, 9, 1);
            empleado.FechaReinicioPromedio = reinicio;
            for (var i = 0; i < 3; i++) e.Completado(empleado, real: 30, estandar: 10, finalizacion: reinicio.AddDays(-1));
            for (var i = 0; i < 3; i++) e.Completado(empleado, real: 12, estandar: 10, finalizacion: reinicio.AddDays(1));

            var r = (await e.Motor().CalcularRendimientosAsync(new[] { empleado.Id }))[empleado.Id];

            Assert.Equal(3, r.PedidosConsiderados);
            Assert.Equal(1.2, r.Factor);
        }
    }
}
