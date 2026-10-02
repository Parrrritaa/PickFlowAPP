using PickingApp.Models;

namespace PickingApp.Tests
{
    // Reglas para asignar un pedido a un empleado (PickingEngine.ValidarAsignacionesAsync). Salvo que se indique,
    // el reloj marca las 10:00 y el empleado trabaja de 08:00 a 17:00.
    public class AsignacionTests
    {
        private static Pedido PedidoPequeno(Escenario e, string estado = PedidoEstados.Pendiente) =>
            // 10 × 0,3 + 1 min de recorrido = 4 min
            e.Pedido(estado, (e.Ubicacion(Escenario.Pequeno), 10));

        [Theory]
        [InlineData(PedidoEstados.Cancelado)]
        [InlineData(PedidoEstados.Completado)]
        [InlineData(PedidoEstados.Dividido)]
        public async Task UnPedidoCerradoODivididoNoSeAsigna(string estado)
        {
            using var e = new Escenario();
            var pedido = PedidoPequeno(e, estado);
            var empleado = e.Empleado();

            var r = await e.Motor().ValidarAsignacionAsync(pedido.Id, empleado.Id);

            Assert.False(r.PuedeAsignar);
            Assert.NotNull(r.MensajeError);
        }

        [Fact]
        public async Task UnEmpleadoInactivoNoRecibePedidos()
        {
            using var e = new Escenario();
            var pedido = PedidoPequeno(e);
            var empleado = e.Empleado(activo: false);

            var r = await e.Motor().ValidarAsignacionAsync(pedido.Id, empleado.Id);

            Assert.False(r.PuedeAsignar);
            Assert.Equal("Inactivo", r.MotivoCorto);
        }

        [Fact]
        public async Task UnEmpleadoConOtroPedidoEnCursoNoRecibeOtro()
        {
            using var e = new Escenario();
            var empleado = e.Empleado();
            var enCurso = PedidoPequeno(e, PedidoEstados.Asignado);
            enCurso.Empleado = empleado;
            e.Db.SaveChanges();
            var nuevo = PedidoPequeno(e);

            var motor = e.Motor();
            var paraOtroPedido = await motor.ValidarAsignacionAsync(nuevo.Id, empleado.Id);
            // Al reasignar, el pedido que ya tiene no cuenta como "otro pedido en curso"
            var paraSuMismoPedido = await motor.ValidarAsignacionAsync(enCurso.Id, empleado.Id);

            Assert.False(paraOtroPedido.PuedeAsignar);
            Assert.Equal("Con pedido en curso", paraOtroPedido.MotivoCorto);
            Assert.True(paraSuMismoPedido.PuedeAsignar);
        }

        [Theory]
        [InlineData(7, 8, 17, false)]   // Antes de entrar
        [InlineData(12, 8, 17, true)]
        [InlineData(23, 22, 6, true)]   // Turno nocturno, antes de medianoche
        [InlineData(3, 22, 6, true)]    // Turno nocturno, después de medianoche
        [InlineData(12, 22, 6, false)]  // Turno nocturno, a mediodía
        public async Task SoloSeAsignaDentroDeLaJornada(int hora, int entrada, int salida, bool puedeAsignar)
        {
            using var e = new Escenario();
            var pedido = PedidoPequeno(e);
            var empleado = e.Empleado(TimeSpan.FromHours(entrada), TimeSpan.FromHours(salida));

            var r = await e.Motor(TimeSpan.FromHours(hora)).ValidarAsignacionAsync(pedido.Id, empleado.Id);

            Assert.Equal(puedeAsignar, r.PuedeAsignar);
            if (!puedeAsignar) Assert.Equal("Fuera de jornada", r.MotivoCorto);
        }

        [Fact]
        public async Task NoSeAsignaSiElPedidoNoAlcanzaATerminarseEnLaJornada()
        {
            using var e = new Escenario();
            var pedido = PedidoPequeno(e);  // 4 min
            var empleado = e.Empleado();

            var aTiempo = await e.Motor(new TimeSpan(16, 55, 0)).ValidarAsignacionAsync(pedido.Id, empleado.Id);
            var tarde = await e.Motor(new TimeSpan(16, 58, 0)).ValidarAsignacionAsync(pedido.Id, empleado.Id);

            Assert.True(aTiempo.PuedeAsignar);
            Assert.False(tarde.PuedeAsignar);
            Assert.StartsWith("Jornada insuficiente", tarde.MotivoCorto);
        }

        [Fact]
        public async Task ElTiempoEstimadoAplicaElRendimientoDelEmpleado()
        {
            using var e = new Escenario();
            var pedido = PedidoPequeno(e);  // 4 min
            var empleado = e.Empleado();
            for (var i = 0; i < 3; i++) e.Completado(empleado, real: 15, estandar: 10);

            var r = await e.Motor().ValidarAsignacionAsync(pedido.Id, empleado.Id);

            Assert.Equal(4.0, r.TiempoEstandarMinutos);
            Assert.Equal(6.0, r.TiempoEstimadoMinutos);  // 4 × 1,5
        }

        [Theory]
        [InlineData(2, 11, true)]    // Poca experiencia y más de 10 ubicaciones
        [InlineData(2, 10, false)]   // Exactamente 10 ubicaciones no es "más de 10"
        [InlineData(12, 11, false)]  // Con experiencia suficiente no hay advertencia
        public async Task AdvierteCuandoUnEmpleadoNuevoRecibeUnPedidoConMuchasUbicaciones(int meses, int ubicaciones, bool advertencia)
        {
            using var e = new Escenario();
            var lineas = Enumerable.Range(0, ubicaciones).Select(_ => (e.Ubicacion(Escenario.Pequeno), 1)).ToArray();
            var pedido = e.Pedido(lineas: lineas);
            var empleado = e.Empleado(mesesExperiencia: meses);

            var r = await e.Motor().ValidarAsignacionAsync(pedido.Id, empleado.Id);

            // La advertencia no impide asignar: queda registrada para el Administrador
            Assert.True(r.PuedeAsignar);
            Assert.Equal(advertencia, r.GeneraAdvertencia);
        }
    }
}
