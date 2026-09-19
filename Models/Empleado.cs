using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickingApp.Models
{
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

        // Se calcula automáticamente a partir de HorarioEntrada/HorarioSalida (ver EmpleadosController).
        [StringLength(50)]
        public string JornadaLaboral { get; set; } = "Diurna";

        [Required]
        public TimeSpan HorarioEntrada { get; set; } = new TimeSpan(8, 0, 0);

        [Required]
        public TimeSpan HorarioSalida { get; set; } = new TimeSpan(17, 0, 0);

        [Required]
        public DateTime FechaIngreso { get; set; } = DateTime.Today;

        [Required]
        [StringLength(30)]
        public string EstadoDisponibilidad { get; set; } = "Disponible"; // "Disponible", "EnPicking", "Inactivo", "FueraDeJornada"

        public bool Activo { get; set; } = true;

        // Pedidos completados antes de esta fecha se ignoran al calcular el tiempo promedio del empleado.
        // Permite "reiniciar" su historial de tiempos sin borrar los pedidos.
        public DateTime? FechaReinicioPromedio { get; set; }

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

        [NotMapped]
        public bool TieneMenosDeSeisMeses => MesesExperiencia < 6;

        public virtual ICollection<Pedido> PedidosAsignados { get; set; } = new List<Pedido>();
    }
}
