using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PickingApp.Data;
using PickingApp.Models;
using PickingApp.Services;

namespace PickingApp.Tests
{
    // Base de datos en memoria con lo mínimo para cada prueba: una bodega, ubicaciones con su producto,
    // pedidos y empleados. Trae las categorías iniciales del sistema (HasData en ApplicationDbContext):
    // Pequeño 0,3 min/u, Mediano 1,5 (la predeterminada), Grande 4 y Voluminoso 10.
    // Los parámetros sin fila en ConfiguracionesSistema usan su valor por defecto (1 min de recorrido por ubicación,
    // 3 pedidos para medir rendimiento, advertencia con menos de 6 meses y más de 10 ubicaciones).
    public sealed class Escenario : IDisposable
    {
        public const int Pequeno = 1;
        public const int Mediano = 2;
        public const int Grande = 3;

        public ApplicationDbContext Db { get; }
        private readonly Bodega _bodega;
        private int _siguienteUbicacion = 1;

        public Escenario()
        {
            var opciones = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                // La base en memoria no tiene transacciones: las acciones que abren una siguen como si no la hubiera
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            Db = new ApplicationDbContext(opciones);
            Db.Database.EnsureCreated();

            _bodega = new Bodega { Codigo = "BOD-01", Nombre = "Bodega de prueba" };
            Db.Bodegas.Add(_bodega);
            Db.SaveChanges();
        }

        // Motor con el reloj detenido en la hora indicada
        public PickingEngine Motor(TimeSpan? hora = null) => new(Db, new RelojFijo(hora ?? new TimeSpan(10, 0, 0)));

        public void Parametro(string clave, string valor)
        {
            Db.ConfiguracionesSistema.Add(new ConfiguracionSistema { Clave = clave, Valor = valor });
            Db.SaveChanges();
        }

        // Ubicación nueva con un producto propio; categoriaId null = producto sin clasificar
        public Ubicacion Ubicacion(int? categoriaId, int unidadesPorCaja = 1)
        {
            var n = _siguienteUbicacion++;
            var ubicacion = new Ubicacion
            {
                Bodega = _bodega,
                Pasillo = "1", Estante = "1", Nivel = n.ToString(),
                CodigoUbicacion = PickingApp.Models.Ubicacion.GenerarCodigo("1", "1", n.ToString()),
                Producto = new Producto
                {
                    Codigo = $"PROD-{n:000}", Nombre = $"Producto {n}",
                    CategoriaProductoId = categoriaId, UnidadesPorCaja = unidadesPorCaja
                }
            };
            Db.Ubicaciones.Add(ubicacion);
            Db.SaveChanges();
            return ubicacion;
        }

        // Pedido con una línea por (ubicación, cantidad)
        public Pedido Pedido(string estado = PedidoEstados.Pendiente, params (Ubicacion Ubicacion, int Cantidad)[] lineas)
        {
            var pedido = new Pedido
            {
                CodigoPedido = $"PED-{Guid.NewGuid():N}"[..12],
                Bodega = _bodega,
                Estado = estado
            };
            foreach (var (ubicacion, cantidad) in lineas)
            {
                pedido.Detalles.Add(new DetallePedido
                {
                    Ubicacion = ubicacion,
                    Cantidad = cantidad,
                    CodigoProducto = ubicacion.Producto!.Codigo,
                    NombreProducto = ubicacion.Producto.Nombre
                });
            }
            Db.Pedidos.Add(pedido);
            Db.SaveChanges();
            return pedido;
        }

        public Empleado Empleado(TimeSpan? entrada = null, TimeSpan? salida = null, int mesesExperiencia = 24, bool activo = true)
        {
            var empleado = new Empleado
            {
                Identificacion = Guid.NewGuid().ToString("N")[..10],
                NombreCompleto = "Empleado de prueba",
                HorarioEntrada = entrada ?? new TimeSpan(8, 0, 0),
                HorarioSalida = salida ?? new TimeSpan(17, 0, 0),
                FechaIngreso = DateTime.Today.AddMonths(-mesesExperiencia),
                Activo = activo
            };
            Db.Empleados.Add(empleado);
            Db.SaveChanges();
            return empleado;
        }

        // Pedido ya completado por el empleado, con su tiempo real y su tiempo estándar guardados
        public void Completado(Empleado empleado, double real, double estandar, DateTime? finalizacion = null)
        {
            Db.Pedidos.Add(new Pedido
            {
                CodigoPedido = $"PED-{Guid.NewGuid():N}"[..12],
                Bodega = _bodega,
                Estado = PedidoEstados.Completado,
                Empleado = empleado,
                FechaFinalizacion = finalizacion ?? DateTime.Today,
                TiempoProcesamientoMinutos = real,
                TiempoEstandarMinutos = estandar
            });
            Db.SaveChanges();
        }

        public void Dispose() => Db.Dispose();
    }

    // Reloj que siempre marca la misma hora del 2 de octubre de 2026 (en UTC, que aquí es también la hora local)
    public sealed class RelojFijo : TimeProvider
    {
        private readonly DateTimeOffset _ahora;

        public RelojFijo(TimeSpan hora) => _ahora = new DateTimeOffset(new DateTime(2026, 10, 2).Add(hora), TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _ahora;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
