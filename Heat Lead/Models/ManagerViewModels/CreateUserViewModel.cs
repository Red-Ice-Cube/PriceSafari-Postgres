using System.ComponentModel.DataAnnotations;

namespace Heat_Lead.Models.ManagerViewModels
{
    public class CreateUserViewModel
    {
        [Required(ErrorMessage = "Pole Email jest wymagane.")]
        [EmailAddress(ErrorMessage = "Nieprawidłowy format adresu email.")]
        [Display(Name = "Email")]
        public string Email { get; set; }

        [Required(ErrorMessage = "Pole Hasło jest wymagane.")]
        [StringLength(100, ErrorMessage = "Hasło musi mieć przynajmniej {2} i maksymalnie {1} znaków długości.", MinimumLength = 8)]
        [DataType(DataType.Password)]
        [Display(Name = "Hasło")]
        public string Password { get; set; }

        [Required(ErrorMessage = "Powtórzenie hasła jest wymagane.")]
        [DataType(DataType.Password)]
        [Display(Name = "Potwierdź hasło")]
        [Compare("Password", ErrorMessage = "Hasło i hasło potwierdzające nie są identyczne.")]
        public string ConfirmPassword { get; set; }

        [Required(ErrorMessage = "Pole Imię jest wymagane.")]
        public string Imie { get; set; }

        [Required(ErrorMessage = "Pole Nazwisko jest wymagane.")]
        public string Nazwisko { get; set; }

        public List<string> Roles { get; set; } = new List<string>();
        public string SelectedRole { get; set; }

        public bool SendConfirmationEmail { get; set; } = true;
    }
}