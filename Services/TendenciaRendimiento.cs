namespace PickingApp.Services
{
    public enum DireccionTendencia { SinDatos, MasRapido, Estable, MasLento }

    // Compara el ritmo de los pedidos más recientes de un empleado con el de los inmediatamente anteriores.
    // Ritmo de un grupo = Σ tiempo real ÷ Σ tiempo estándar (la misma medida del factor de rendimiento).
    public sealed class TendenciaRendimiento
    {
        public const int PedidosPorGrupo = 5;
        // Con menos de 3 pedidos por grupo la diferencia es ruido
        public const int MinimoPorGrupo = 3;
        // Cambios menores al 5 % se consideran el mismo ritmo (igual que "Al ritmo estándar" en RendimientoEmpleado)
        private const double CambioMinimo = 0.05;

        public DireccionTendencia Direccion { get; private init; }
        public int PedidosComparados { get; private init; }   // Por grupo
        public double RitmoReciente { get; private init; }
        public double RitmoAnterior { get; private init; }

        // Cuánto cambió el tiempo por pedido: -0,12 = 12 % más rápido que antes
        public double Cambio => RitmoAnterior > 0 ? RitmoReciente / RitmoAnterior - 1 : 0;

        // pedidos: del más antiguo al más reciente, solo los que cuentan para el rendimiento
        public static TendenciaRendimiento Calcular(IReadOnlyList<(double Real, double Estandar)> pedidos)
        {
            var validos = pedidos.Where(p => p.Estandar > 0).ToList();
            var porGrupo = Math.Min(PedidosPorGrupo, validos.Count / 2);
            if (porGrupo < MinimoPorGrupo) return new TendenciaRendimiento { Direccion = DireccionTendencia.SinDatos };

            static double Ritmo(IEnumerable<(double Real, double Estandar)> grupo) =>
                grupo.Sum(p => p.Real) / grupo.Sum(p => p.Estandar);

            var reciente = Ritmo(validos.TakeLast(porGrupo));
            var anterior = Ritmo(validos.SkipLast(porGrupo).TakeLast(porGrupo));
            var cambio = reciente / anterior - 1;

            return new TendenciaRendimiento
            {
                PedidosComparados = porGrupo,
                RitmoReciente = reciente,
                RitmoAnterior = anterior,
                Direccion = Math.Abs(cambio) < CambioMinimo ? DireccionTendencia.Estable
                    : cambio < 0 ? DireccionTendencia.MasRapido
                    : DireccionTendencia.MasLento
            };
        }

        // Frase para encabezar la ficha, p. ej. "En sus últimos 5 pedidos trabajó un 12 % más rápido que en los 5 anteriores."
        public string Resumen
        {
            get
            {
                var porcentaje = (int)Math.Round(Math.Abs(Cambio) * 100);
                var grupo = $"sus últimos {PedidosComparados} pedidos";
                return Direccion switch
                {
                    DireccionTendencia.MasRapido => $"En {grupo} trabajó un {porcentaje} % más rápido que en los {PedidosComparados} anteriores.",
                    DireccionTendencia.MasLento => $"En {grupo} trabajó un {porcentaje} % más lento que en los {PedidosComparados} anteriores.",
                    DireccionTendencia.Estable => $"En {grupo} mantuvo el mismo ritmo que en los {PedidosComparados} anteriores.",
                    _ => $"La tendencia aparece cuando complete al menos {MinimoPorGrupo * 2} pedidos."
                };
            }
        }
    }
}
