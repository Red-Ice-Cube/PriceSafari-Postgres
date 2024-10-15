using System;
using System.ComponentModel.DataAnnotations;

namespace PriceSafari.Models.ViewModels
{
    public class ContactFormViewModel
    {
        [Required(ErrorMessage = "Adres email jest wymagany.")]
        [EmailAddress(ErrorMessage = "Nieprawidłowy format adresu email.")]
        public string Email { get; set; }

        [Required(ErrorMessage = "Nazwa firmy jest wymagana.")]
        public string CompanyName { get; set; }

        [Required(ErrorMessage = "Imię jest wymagane.")]
        public string FirstName { get; set; }

        [Required(ErrorMessage = "Nazwisko jest wymagane.")]
        public string LastName { get; set; }

        [Required(ErrorMessage = "Musisz wyrazić zgodę na przetwarzanie danych.")]
        public bool ConsentToDataProcessing { get; set; }

        [Required(ErrorMessage = "Numer telefonu jest wymagany.")]
        [Phone(ErrorMessage = "Nieprawidłowy format numeru telefonu.")]
        public string PhoneNumber { get; set; }
    }
}
