using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickingApp.Models
{
    // Operario de bodega que recoge los pedidos. No inicia sesión: no confundir con Usuario.
    public class Empleado
    {
        [Key]
        public int Id { get; set; }

        [Required(ErrorMessage = "La identificación es obligatoria")]
        [StringLength(30)]
        [RegularExpression(@"^\d+$", ErrorMessage = "La identificación solo debe contener números")]
        public string Identificacion { get; set; } = string.Empty;

        [Required(ErrorMessage = "El nombre completo es obligatorio")]
        [StringLength(100)]
        public string NombreCompleto { get; set; } = string.Empty;

        [EmailAddress]
        [StringLength(100)]
        public string? Correo { get; set; }


        [Required]
        public TimeSpan HorarioEntrada { get; set; } = new TimeSpan(8, 0, 0);

        [Required]
        public TimeSpan HorarioSalida { get; set; } = new TimeSpan(17, 0, 0);

        [Required]
        public DateTime FechaIngreso { get; set; } = DateTime.Today;

        public bool Activo { get; set; } = true;

        public virtual ICollection<TrasladoPersonal> Traslados { get; set; } = new List<TrasladoPersonal>();

        // shortcut: admite null mientras el administrador asigna bodega a los empleados existentes; pasa a obligatorio después.
        public int? BodegaId { get; set; }

        [ForeignKey("BodegaId")]
        public virtual Bodega? Bodega { get; set; }

        // La ocupación no se guarda: un empleado está ocupado mientras tenga un pedido en curso.
        public static string DescribirEstado(bool activo, bool conPedidoEnCurso)
        {
            if (!activo) return "Inactivo";
            return conPedidoEnCurso ? "Ocupado" : "Libre";
        }

        // Pedidos completados antes de esta fecha se ignoran al calcular el tiempo promedio del empleado.
        // Permite "reiniciar" su historial de tiempos sin borrar los pedidos.
        public DateTime? FechaReinicioPromedio { get; set; }

        // Derivada del horario: no se guarda para que no pueda quedar distinta de él
        [NotMapped]
        public string JornadaLaboral => $"{HorarioEntrada:hh\\:mm} - {HorarioSalida:hh\\:mm}";

        // Dentro de su horario a esa hora; un turno cuya salida no es posterior a la entrada cruza la medianoche
        public bool TrabajaA(TimeSpan hora) => HorarioEntrada < HorarioSalida
            ? hora >= HorarioEntrada && hora <= HorarioSalida
            : hora >= HorarioEntrada || hora <= HorarioSalida;

        // [NotMapped]: se calcula en C# y no existe como columna en MySQL
        [NotMapped]
        public int MesesExperiencia
        {
            get
            {
                var hoy = DateTime.Today;
                var meses = ((hoy.Year - FechaIngreso.Year) * 12) + hoy.Month - FechaIngreso.Month;
                return meses < 0 ? 0 : meses;
            }
        }

        public virtual ICollection<Pedido> PedidosAsignados { get; set; } = new List<Pedido>();
    }
}
