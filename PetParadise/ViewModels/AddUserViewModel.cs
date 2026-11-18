using System.ComponentModel.DataAnnotations;

namespace PetParadise.ViewModels
{
    public class AddUserViewModel
    {
        [Required(ErrorMessage = "กรุณากรอก Username")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "กรุณากรอก Email")]
        [EmailAddress(ErrorMessage = "รูปแบบอีเมลไม่ถูกต้อง")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "กรุณากรอก Password")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "กรุณากรอก First Name")]
        public string FirstName { get; set; } = string.Empty;

        [Required(ErrorMessage = "กรุณากรอก Last Name")]
        public string LastName { get; set; } = string.Empty;

        public string? Phone { get; set; }

        [Required(ErrorMessage = "กรุณาเลือก Role")]
        public string Role { get; set; } = "Customer";
    }
}