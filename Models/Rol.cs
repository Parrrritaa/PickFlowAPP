using System.ComponentModel.DataAnnotations;

namespace PickingApp.Models
{
    // Catálogo de roles: Usuario.RolId apunta aquí, así un rol tiene una única forma de escribirse.
    // Los Id fijos están en Roles (IdAdministrador, ...) y se siembran en ApplicationDbContext.
    public class Rol
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(30)]
        public string Nombre { get; set; } = string.Empty;
    }
}
