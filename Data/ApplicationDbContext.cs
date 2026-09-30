using Microsoft.EntityFrameworkCore;
using PickingApp.Models;

namespace PickingApp.Data
{
    // Puente entre el código y MySQL: Entity Framework traduce las consultas LINQ sobre estos DbSet a SQL
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        // Cada DbSet es una tabla de la base de datos; su nombre es el nombre de la tabla
        public DbSet<Usuario> Usuarios { get; set; } = null!;
        public DbSet<Empleado> Empleados { get; set; } = null!;
        public DbSet<Bodega> Bodegas { get; set; } = null!;
        public DbSet<Ubicacion> Ubicaciones { get; set; } = null!;
        public DbSet<Producto> Productos { get; set; } = null!;
        public DbSet<Pedido> Pedidos { get; set; } = null!;
        public DbSet<DetallePedido> DetallesPedido { get; set; } = null!;
        public DbSet<NovedadPedido> NovedadesPedido { get; set; } = null!;
        public DbSet<Solicitud> Solicitudes { get; set; } = null!;
        public DbSet<LogSistema> LogsSistema { get; set; } = null!;
        public DbSet<ConfiguracionSistema> ConfiguracionesSistema { get; set; } = null!;
        public DbSet<Notificacion> Notificaciones { get; set; } = null!;
        public DbSet<CategoriaProducto> CategoriasProducto { get; set; } = null!;

        // Reglas del modelo que no caben en los atributos de las clases: índices, relaciones y datos iniciales.
        // Cualquier cambio aquí necesita una migración nueva (dotnet ef migrations add ...).
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Índices únicos: MySQL rechaza dos filas con el mismo valor (por ejemplo, dos usuarios con el mismo correo)
            modelBuilder.Entity<Usuario>()
                .HasIndex(u => u.Correo)
                .IsUnique();

            modelBuilder.Entity<Empleado>()
                .HasIndex(e => e.Identificacion)
                .IsUnique();

            modelBuilder.Entity<Bodega>()
                .HasIndex(b => b.Codigo)
                .IsUnique();

            modelBuilder.Entity<Pedido>()
                .HasIndex(p => p.CodigoPedido)
                .IsUnique();

            modelBuilder.Entity<ConfiguracionSistema>()
                .HasIndex(c => c.Clave)
                .IsUnique();

            modelBuilder.Entity<Producto>()
                .HasIndex(p => p.Codigo)
                .IsUnique();

            modelBuilder.Entity<CategoriaProducto>()
                .HasIndex(c => c.Nombre)
                .IsUnique();

            // Al eliminar una categoría, sus productos pasan a usar la predeterminada (CategoriaProductoId = null)
            modelBuilder.Entity<Producto>()
                .HasOne(p => p.Categoria)
                .WithMany(c => c.Productos)
                .HasForeignKey(p => p.CategoriaProductoId)
                .OnDelete(DeleteBehavior.SetNull);

            // Categorías iniciales. "Mediano" (1,5 min/u) es la predeterminada: coincide con el cálculo anterior,
            // así los productos existentes conservan su tiempo hasta que se clasifiquen.
            modelBuilder.Entity<CategoriaProducto>().HasData(
                new CategoriaProducto { Id = 1, Nombre = "Pequeño", MinutosPorUnidad = 0.3, Descripcion = "Mouse, memorias USB, cables", EsPredeterminada = false },
                new CategoriaProducto { Id = 2, Nombre = "Mediano", MinutosPorUnidad = 1.5, Descripcion = "Teclados, routers, audífonos", EsPredeterminada = true },
                new CategoriaProducto { Id = 3, Nombre = "Grande", MinutosPorUnidad = 4, Descripcion = "Monitores, impresoras", EsPredeterminada = false },
                new CategoriaProducto { Id = 4, Nombre = "Voluminoso / pesado", MinutosPorUnidad = 10, Descripcion = "Torres de PC, UPS", EsPredeterminada = false });

            // Relaciones y qué pasa al borrar el registro principal:
            // Restrict impide borrarlo si tiene hijos, Cascade borra también los hijos
            // y SetNull deja la llave foránea de los hijos en null
            modelBuilder.Entity<Ubicacion>()
                .HasOne(u => u.Bodega)
                .WithMany(b => b.Ubicaciones)
                .HasForeignKey(u => u.BodegaId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Ubicacion>()
                .HasOne(u => u.Producto)
                .WithMany(p => p.Ubicaciones)
                .HasForeignKey(u => u.ProductoId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Pedido>()
                .HasOne(p => p.Bodega)
                .WithMany(b => b.Pedidos)
                .HasForeignKey(p => p.BodegaId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Pedido>()
                .HasOne(p => p.Empleado)
                .WithMany(e => e.PedidosAsignados)
                .HasForeignKey(p => p.EmpleadoId)
                .OnDelete(DeleteBehavior.SetNull);

            // Partes de un pedido dividido: no se puede borrar el pedido original mientras tenga partes
            modelBuilder.Entity<Pedido>()
                .HasOne(p => p.PedidoPadre)
                .WithMany(p => p.Partes)
                .HasForeignKey(p => p.PedidoPadreId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<DetallePedido>()
                .HasOne(d => d.Pedido)
                .WithMany(p => p.Detalles)
                .HasForeignKey(d => d.PedidoId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<DetallePedido>()
                .HasOne(d => d.Ubicacion)
                .WithMany(u => u.DetallesPedido)
                .HasForeignKey(d => d.UbicacionId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<NovedadPedido>()
                .HasOne(n => n.Pedido)
                .WithMany(p => p.Novedades)
                .HasForeignKey(n => n.PedidoId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Solicitud>()
                .HasOne(s => s.UsuarioSolicitante)
                .WithMany()
                .HasForeignKey(s => s.UsuarioSolicitanteId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Solicitud>()
                .HasOne(s => s.UsuarioResponde)
                .WithMany()
                .HasForeignKey(s => s.UsuarioRespondeId)
                .OnDelete(DeleteBehavior.Restrict);

            // Cascade: con SetNull una notificación personal quedaría sin destino ni rol y se volvería visible para todos.
            modelBuilder.Entity<Notificacion>()
                .HasOne(n => n.UsuarioDestino)
                .WithMany()
                .HasForeignKey(n => n.UsuarioDestinoId)
                .OnDelete(DeleteBehavior.Cascade);

            // SetNull: el historial de auditoría nunca se pierde; conserva Usuario/Rol en texto.
            modelBuilder.Entity<LogSistema>()
                .HasOne(l => l.UsuarioAsociado)
                .WithMany()
                .HasForeignKey(l => l.UsuarioId)
                .OnDelete(DeleteBehavior.SetNull);
        }
    }
}
