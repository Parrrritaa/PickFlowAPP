using PickingApp.Services;

namespace PickingApp.Tests
{
    // Tendencia de la ficha de historial: ritmo de los últimos pedidos frente a los inmediatamente anteriores
    public class TendenciaTests
    {
        private static List<(double Real, double Estandar)> Pedidos(params double[] reales) =>
            reales.Select(r => (r, 10.0)).ToList();

        [Fact]
        public void ConMenosDeSeisPedidosNoHayTendencia()
        {
            var t = TendenciaRendimiento.Calcular(Pedidos(10, 10, 10, 5, 5));

            Assert.Equal(DireccionTendencia.SinDatos, t.Direccion);
        }

        [Fact]
        public void CompruebaLosCincoMasRecientesContraLosCincoAnteriores()
        {
            // El primero queda fuera: con 11 pedidos se comparan 5 y 5
            var t = TendenciaRendimiento.Calcular(Pedidos(99, 15, 15, 15, 15, 15, 12, 12, 12, 12, 12));

            Assert.Equal(5, t.PedidosComparados);
            Assert.Equal(DireccionTendencia.MasRapido, t.Direccion);
            Assert.Equal(-0.2, t.Cambio, precision: 10);   // 12 frente a 15
            Assert.StartsWith("En sus últimos 5 pedidos trabajó un 20 % más rápido", t.Resumen);
        }

        [Theory]
        [InlineData(10.4, DireccionTendencia.Estable)]   // 4 % más lento: dentro del margen
        [InlineData(11.0, DireccionTendencia.MasLento)]  // 10 % más lento
        public void UnCambioMenorAlCincoPorCientoEsElMismoRitmo(double realReciente, DireccionTendencia esperada)
        {
            var t = TendenciaRendimiento.Calcular(Pedidos(10, 10, 10, realReciente, realReciente, realReciente));

            Assert.Equal(3, t.PedidosComparados);
            Assert.Equal(esperada, t.Direccion);
        }
    }
}
