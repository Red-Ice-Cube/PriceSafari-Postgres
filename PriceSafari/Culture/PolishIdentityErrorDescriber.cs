namespace PriceSafari.Culture
{// Wklej ten kod do nowego pliku PolishIdentityErrorDescriber.cs
    using Microsoft.AspNetCore.Identity;

    public class PolishIdentityErrorDescriber : IdentityErrorDescriber
    {
        public override IdentityError DefaultError()
            => new IdentityError { Code = nameof(DefaultError), Description = "Wystąpił nieznany błąd." };

        public override IdentityError ConcurrencyFailure()
            => new IdentityError { Code = nameof(ConcurrencyFailure), Description = "Błąd współbieżności, obiekt został zmodyfikowany." };

        public override IdentityError PasswordMismatch()
            => new IdentityError { Code = nameof(PasswordMismatch), Description = "Nieprawidłowe hasło." };

        public override IdentityError InvalidToken()
            => new IdentityError { Code = nameof(InvalidToken), Description = "Nieprawidłowy token." };

        public override IdentityError LoginAlreadyAssociated()
            => new IdentityError { Code = nameof(LoginAlreadyAssociated), Description = "Użytkownik z tym loginem już istnieje." };

        public override IdentityError InvalidUserName(string userName)
            => new IdentityError { Code = nameof(InvalidUserName), Description = $"Nazwa użytkownika '{userName}' jest nieprawidłowa. Może zawierać tylko litery i cyfry." };

        public override IdentityError InvalidEmail(string email)
            => new IdentityError { Code = nameof(InvalidEmail), Description = $"Adres email '{email}' jest nieprawidłowy." };

        public override IdentityError DuplicateUserName(string userName)
            => new IdentityError { Code = nameof(DuplicateUserName), Description = $"Użytkownik o nazwie '{userName}' już istnieje." };

        public override IdentityError DuplicateEmail(string email)
            => new IdentityError { Code = nameof(DuplicateEmail), Description = $"Użytkownik z adresem email '{email}' już istnieje." };

        public override IdentityError InvalidRoleName(string role)
            => new IdentityError { Code = nameof(InvalidRoleName), Description = $"Nazwa roli '{role}' jest nieprawidłowa." };

        public override IdentityError DuplicateRoleName(string role)
            => new IdentityError { Code = nameof(DuplicateRoleName), Description = $"Rola o nazwie '{role}' już istnieje." };

        public override IdentityError UserAlreadyHasPassword()
            => new IdentityError { Code = nameof(UserAlreadyHasPassword), Description = "Użytkownik ma już ustawione hasło." };

        public override IdentityError UserLockoutNotEnabled()
            => new IdentityError { Code = nameof(UserLockoutNotEnabled), Description = "Blokada nie jest włączona dla tego użytkownika." };

        public override IdentityError UserAlreadyInRole(string role)
            => new IdentityError { Code = nameof(UserAlreadyInRole), Description = $"Użytkownik już posiada rolę '{role}'." };

        public override IdentityError UserNotInRole(string role)
            => new IdentityError { Code = nameof(UserNotInRole), Description = $"Użytkownik nie posiada roli '{role}'." };

        public override IdentityError PasswordTooShort(int length)
            => new IdentityError { Code = nameof(PasswordTooShort), Description = $"Hasło musi mieć co najmniej {length} znaków." };

        public override IdentityError PasswordRequiresNonAlphanumeric()
            => new IdentityError { Code = nameof(PasswordRequiresNonAlphanumeric), Description = "Hasło musi zawierać co najmniej jeden znak specjalny." };

        public override IdentityError PasswordRequiresDigit()
            => new IdentityError { Code = nameof(PasswordRequiresDigit), Description = "Hasło musi zawierać co najmniej jedną cyfrę ('0'-'9')." };

        public override IdentityError PasswordRequiresLower()
            => new IdentityError { Code = nameof(PasswordRequiresLower), Description = "Hasło musi zawierać co najmniej jedną małą literę ('a'-'z')." };

        public override IdentityError PasswordRequiresUpper()
            => new IdentityError { Code = nameof(PasswordRequiresUpper), Description = "Hasło musi zawierać co najmniej jedną wielką literę ('A'-'Z')." };
    }
}
