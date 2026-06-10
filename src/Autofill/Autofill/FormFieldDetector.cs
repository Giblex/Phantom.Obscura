using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PhantomVault.Core.Services.Autofill
{

    public sealed class FormFieldDetector
    {
        private static readonly string[] EmailPatterns = new[]
        {
            "email", "e-mail", "mail", "user-mail", "user_mail", "usermail"
        };

        private static readonly string[] UsernamePatterns = new[]
        {
            "username", "user-name", "user_name", "user", "login", "userid", "user-id", "account"
        };

        private static readonly string[] PasswordPatterns = new[]
        {
            "password", "passwd", "pass", "pwd", "secret"
        };

        private static readonly string[] ConfirmPasswordPatterns = new[]
        {
            "confirm", "confirmation", "verify", "repeat", "retype", "again", "re-enter", "reenter"
        };

        private static readonly string[] PasskeyPatterns = new[]
        {
            "passkey", "webauthn", "fido", "security-key", "authenticator"
        };

        private static readonly string[] TwoFactorPatterns = new[]
        {
            "2fa", "mfa", "otp", "token", "code", "verification-code", "auth-code", "totp"
        };

        public FormFieldType DetectFieldType(FormFieldInfo field)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));

            var combinedText = $"{field.Id} {field.Name} {field.Type} {field.Placeholder} {field.Label} {field.AutoComplete}".ToLowerInvariant();

            if (field.Type == "email" || EmailPatterns.Any(p => combinedText.Contains(p)))
            {
                return FormFieldType.Email;
            }

            if (PasskeyPatterns.Any(p => combinedText.Contains(p)))
            {
                return FormFieldType.Passkey;
            }

            if (TwoFactorPatterns.Any(p => combinedText.Contains(p)))
            {
                return FormFieldType.TwoFactor;
            }

            if (field.Type == "password" || PasswordPatterns.Any(p => combinedText.Contains(p)))
            {

                if (ConfirmPasswordPatterns.Any(p => combinedText.Contains(p)))
                {
                    return FormFieldType.PasswordConfirm;
                }
                return FormFieldType.Password;
            }

            if (UsernamePatterns.Any(p => combinedText.Contains(p)))
            {
                return FormFieldType.Username;
            }

            return FormFieldType.Unknown;
        }

        public LoginFormDetectionResult DetectLoginForm(IEnumerable<FormFieldInfo> fields)
        {
            var result = new LoginFormDetectionResult();
            var fieldList = fields.ToList();

            foreach (var field in fieldList)
            {
                var fieldType = DetectFieldType(field);

                switch (fieldType)
                {
                    case FormFieldType.Email:
                        result.EmailFields.Add(field);
                        break;
                    case FormFieldType.Username:
                        result.UsernameFields.Add(field);
                        break;
                    case FormFieldType.Password:
                        result.PasswordFields.Add(field);
                        break;
                    case FormFieldType.PasswordConfirm:
                        result.PasswordConfirmFields.Add(field);
                        break;
                    case FormFieldType.Passkey:
                        result.PasskeyFields.Add(field);
                        break;
                    case FormFieldType.TwoFactor:
                        result.TwoFactorFields.Add(field);
                        break;
                }
            }

            result.FormType = DetermineFormType(result);

            return result;
        }

        private FormType DetermineFormType(LoginFormDetectionResult result)
        {
            var hasPassword = result.PasswordFields.Any();
            var hasConfirm = result.PasswordConfirmFields.Any();
            var hasUsername = result.UsernameFields.Any() || result.EmailFields.Any();
            var has2FA = result.TwoFactorFields.Any();
            var hasPasskey = result.PasskeyFields.Any();

            if (hasPassword && hasConfirm)
            {
                return FormType.Registration;
            }

            if (has2FA)
            {
                return FormType.TwoFactor;
            }

            if (hasPasskey)
            {
                return FormType.Passkey;
            }

            if (hasPassword && hasUsername)
            {
                return FormType.Login;
            }

            if (hasPassword)
            {
                return FormType.PasswordChange;
            }

            return FormType.Unknown;
        }
    }

    public sealed class FormFieldInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Placeholder { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string AutoComplete { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public int TabIndex { get; set; }
        public BoundingBox BoundingBox { get; set; } = new();
    }

    public sealed class BoundingBox
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    public enum FormFieldType
    {
        Unknown,
        Email,
        Username,
        Password,
        PasswordConfirm,
        Passkey,
        TwoFactor
    }

    public enum FormType
    {
        Unknown,
        Login,
        Registration,
        PasswordChange,
        TwoFactor,
        Passkey
    }

    public sealed class LoginFormDetectionResult
    {
        public List<FormFieldInfo> EmailFields { get; } = new();
        public List<FormFieldInfo> UsernameFields { get; } = new();
        public List<FormFieldInfo> PasswordFields { get; } = new();
        public List<FormFieldInfo> PasswordConfirmFields { get; } = new();
        public List<FormFieldInfo> PasskeyFields { get; } = new();
        public List<FormFieldInfo> TwoFactorFields { get; } = new();
        public FormType FormType { get; set; }

        public bool HasLoginFields =>
            (EmailFields.Any() || UsernameFields.Any()) && PasswordFields.Any();

        public bool HasRegistrationFields =>
            PasswordFields.Any() && PasswordConfirmFields.Any();

        public bool Has2FAFields => TwoFactorFields.Any();
    }
}

