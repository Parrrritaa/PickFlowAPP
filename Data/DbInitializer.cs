using BCrypt.Net;
using Microsoft.EntityFrameworkCore;
using PickingApp.Models;
using PickingApp.Services;

namespace PickingApp.Data
{
    // Se ejecuta una vez al arrancar la aplicación (Program.cs). Cada bloque solo inserta datos
    // si la tabla está vacía, así que reiniciar la aplicación no duplica nada.
    public static class DbInitializer
    {
        public static async Task SeedAsync(ApplicationDbContext context)
        {
            // Crea la base de datos si no existe y aplica las migraciones pendientes de la carpeta Migrations
            await context.Database.MigrateAsync();

            // 1. Configuraciones del Sistema (RNF-06, RNF-07)
            if (!await context.ConfiguracionesSistema.AnyAsync())
            {
                var configuraciones = ParametrosSistema.Numericos
                    .Select(p => new ConfiguracionSistema { Clave = p.Key, Valor = Formato.ParaInput(p.Value.Defecto) })
                    .Append(new ConfiguracionSistema { Clave = ParametrosSistema.LogHabilitado, Valor = "true" })
                    .ToList();
                await context.ConfiguracionesSistema.AddRangeAsync(configuraciones);
                await context.SaveChangesAsync();
            }

            // 2. Usuarios Iniciales por Rol (CU-01)
            if (!await context.Usuarios.AnyAsync())
            {
                var usuarios = new List<Usuario>
                {
                    new Usuario
                    {
                        NombreCompleto = "Jonathan Romero (Admin)",
                        Correo = "admin@pickingapp.com",
                        Identificacion = "1000000001",
                        // La contraseña se guarda como hash BCrypt, nunca en texto plano
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin123*"),
                        RolId = Roles.IdAdministrador,
                        Estado = EstadosUsuario.Activo,
                        IntentosFallidos = 0,
                        FechaRegistro = DateTime.Now
                    },
                    new Usuario
                    {
                        NombreCompleto = "Santiago Parra (Supervisor)",
                        Correo = "supervisor@pickingapp.com",
                        Identificacion = "1000000002",
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword("Super123*"),
                        RolId = Roles.IdSupervisor,
                        Estado = EstadosUsuario.Activo,
                        IntentosFallidos = 0,
                        FechaRegistro = DateTime.Now
                    },
                    new Usuario
                    {
                        NombreCompleto = "Juan David Salcedo (Auxiliar)",
                        Correo = "auxiliar@pickingapp.com",
                        Identificacion = "1000000003",
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword("Aux123*"),
                        RolId = Roles.IdAuxiliar,
                        Estado = EstadosUsuario.Activo,
                        IntentosFallidos = 0,
                        FechaRegistro = DateTime.Now
                    }
                };
                await context.Usuarios.AddRangeAsync(usuarios);
                await context.SaveChangesAsync();
            }

            // 3. Bodegas Iniciales (CU-18)
            if (!await context.Bodegas.AnyAsync())
            {
                var bodegaPrincipal = new Bodega
                {
                    Codigo = "BOD-01",
                    Nombre = "Bodega Central Facatativá",
                    Descripcion = "Bodega principal de almacenamiento y distribución de picking",
                    Activa = true,
                    FechaCreacion = DateTime.Now
                };
                var bodegaSecundaria = new Bodega
                {
                    Codigo = "BOD-02",
                    Nombre = "Bodega Norte Distribución",
                    Descripcion = "Bodega de alta rotación y productos prioritarios",
                    Activa = true,
                    FechaCreacion = DateTime.Now
                };

                // Se guarda antes de crear las ubicaciones: MySQL asigna el Id de cada bodega en el INSERT
                await context.Bodegas.AddRangeAsync(bodegaPrincipal, bodegaSecundaria);
                await context.SaveChangesAsync();

                // 4. Ubicaciones Iniciales (CU-19). Mismas reglas que el formulario: pasillo, estante y nivel
                // solo con números, y el código con el formato que genera BodegasController (PASXX-ESTXX-NIVXX)
                var ubicaciones = new List<Ubicacion>
                {
                    new Ubicacion { BodegaId = bodegaPrincipal.Id, CodigoUbicacion = "PAS01-EST01-NIV01", Pasillo = "1", Estante = "1", Nivel = "1", Activa = true },
                    new Ubicacion { BodegaId = bodegaPrincipal.Id, CodigoUbicacion = "PAS01-EST01-NIV02", Pasillo = "1", Estante = "1", Nivel = "2", Activa = true },
                    new Ubicacion { BodegaId = bodegaPrincipal.Id, CodigoUbicacion = "PAS01-EST02-NIV01", Pasillo = "1", Estante = "2", Nivel = "1", Activa = true },
                    new Ubicacion { BodegaId = bodegaPrincipal.Id, CodigoUbicacion = "PAS02-EST01-NIV01", Pasillo = "2", Estante = "1", Nivel = "1", Activa = true },
                    new Ubicacion { BodegaId = bodegaPrincipal.Id, CodigoUbicacion = "PAS02-EST02-NIV01", Pasillo = "2", Estante = "2", Nivel = "1", Activa = true },
                    new Ubicacion { BodegaId = bodegaSecundaria.Id, CodigoUbicacion = "PAS01-EST01-NIV01", Pasillo = "1", Estante = "1", Nivel = "1", Activa = true }
                };
                await context.Ubicaciones.AddRangeAsync(ubicaciones);
                await context.SaveChangesAsync();
            }

            // 5. Empleados de Prueba con distinta experiencia y turnos (CU-06)
            if (!await context.Empleados.AnyAsync())
            {
                var empleados = new List<Empleado>
                {
                    new Empleado
                    {
                        Identificacion = "1001",
                        NombreCompleto = "Carlos Andrés Mendoza",
                        Correo = "carlos.mendoza@empresa.com",
                        HorarioEntrada = new TimeSpan(8, 0, 0),
                        HorarioSalida = new TimeSpan(17, 0, 0),
                        FechaIngreso = DateTime.Today.AddYears(-2), // 24 meses experiencia (> 6 meses)
                        Activo = true
                    },
                    new Empleado
                    {
                        Identificacion = "1002",
                        NombreCompleto = "Laura Vanessa Gómez",
                        Correo = "laura.gomez@empresa.com",
                        HorarioEntrada = new TimeSpan(8, 0, 0),
                        HorarioSalida = new TimeSpan(17, 0, 0),
                        FechaIngreso = DateTime.Today.AddMonths(-3), // 3 meses experiencia (< 6 meses -> generará advertencia si pedido > 10 ubicaciones)
                        Activo = true
                    },
                    new Empleado
                    {
                        Identificacion = "1003",
                        NombreCompleto = "Mateo Silva Castro",
                        Correo = "mateo.silva@empresa.com",
                        HorarioEntrada = new TimeSpan(14, 0, 0),
                        HorarioSalida = new TimeSpan(22, 0, 0),
                        FechaIngreso = DateTime.Today.AddMonths(-10), // 10 meses experiencia
                        Activo = true
                    }
                };
                await context.Empleados.AddRangeAsync(empleados);
                await context.SaveChangesAsync();
            }
        }
    }
}
