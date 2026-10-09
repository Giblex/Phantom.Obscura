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

        // Payment fields. The autocomplete tokens (cc-number, cc-exp, cc-csc, cc-name) are the
        // standard and by far the most reliable signal, so they lead each list.
        private static readonly string[] CardNumberPatterns = new[]
        {
            "cc-number", "cardnumber", "card-number", "card_number", "ccnumber", "ccnum",
            "creditcard", "credit-card", "debitcard", "cardno"
        };

        private static readonly string[] CardExpiryMonthPatterns = new[]
        {
            "cc-exp-month", "expmonth", "exp-month", "exp_month", "expirymonth", "expirationmonth"
        };

        private static readonly string[] CardExpiryYearPatterns = new[]
        {
            "cc-exp-year", "expyear", "exp-year", "exp_year", "expiryyear", "expirationyear"
        };

        private static readonly string[] CardExpiryPatterns = new[]
        {
            "cc-exp", "expiry", "expiration", "exp-date", "expdate", "exp_date", "validthru", "valid-thru"
        };

        private static readonly string[] CardSecurityCodePatterns = new[]
        {
            "cc-csc", "securitycode", "security-code", "card-code", "cardcode", "cardverification"
        };

        private static readonly string[] CardHolderPatterns = new[]
        {
            "cc-name", "cardholder", "card-holder", "nameoncard", "name-on-card", "ccname"
        };

        private static readonly string[] PinPatterns = new[]
        {
            "pincode", "pin-code", "pin_code", "passcode", "paycode"
        };

        private static readonly string[] IdentityPatterns = new[]
        {
            "idnumber", "id-number", "id_number", "passport", "licence", "license",
            "nationalid", "national-id", "taxid", "tax-id", "medicare"
        };

        /// <summary>
        /// Matches a short token on word boundaries. Tokens like "pin" and "csc" are too short to
        /// test as plain substrings: "pin" appears inside "shipping" and "spinner", which would
        /// turn an address field into a PIN prompt.
        /// </summary>
        private static bool MatchesShortToken(string haystack, params string[] tokens)
        {
            foreach (var token in tokens)
            {
                var pattern = "(^|[^a-z0-9])" + Regex.Escape(token) + "([^a-z0-9]|$)";
                if (Regex.IsMatch(haystack, pattern))
                    return true;
            }
            return false;
        }

        public FormFieldType DetectFieldType(FormFieldInfo field)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));

            var combinedText = $"{field.Id} {field.Name} {field.Type} {field.Placeholder} {field.Label} {field.AutoComplete}".ToLowerInvariant();

            // Payment and identity fields are tested FIRST, and deliberately so. A CVC input is
            // frequently type="password" and its label contains "code", so leaving it to the
            // generic password and two-factor checks below would classify it as a password or an
            // OTP and fill the wrong secret into a payment form.
            if (CardNumberPatterns.Any(p => combinedText.Contains(p)))
                return FormFieldType.CardNumber;

            if (CardExpiryMonthPatterns.Any(p => combinedText.Contains(p)))
                return FormFieldType.CardExpiryMonth;

            if (CardExpiryYearPatterns.Any(p => combinedText.Contains(p)))
                return FormFieldType.CardExpiryYear;

            if (CardExpiryPatterns.Any(p => combinedText.Contains(p)))
                return FormFieldType.CardExpiry;

            if (CardSecurityCodePatterns.Any(p => combinedText.Contains(p)) ||
                MatchesShortToken(combinedText, "cvc", "cvv", "csc", "cid"))
                return FormFieldType.CardSecurityCode;

            if (CardHolderPatterns.Any(p => combinedText.Contains(p)))
                return FormFieldType.CardHolderName;

            if (PinPatterns.Any(p => combinedText.Contains(p)) || MatchesShortToken(combinedText, "pin"))
                return FormFieldType.Pin;

            if (IdentityPatterns.Any(p => combinedText.Contains(p)) || MatchesShortToken(combinedText, "ssn"))
                return FormFieldType.IdentityNumber;

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
                    case FormFieldType.CardNumber:
                        result.CardNumberFields.Add(field);
                        break;
                    case FormFieldType.CardExpiry:
                        result.CardExpiryFields.Add(field);
                        break;
                    case FormFieldType.CardExpiryMonth:
                        result.CardExpiryMonthFields.Add(field);
                        break;
                    case FormFieldType.CardExpiryYear:
                        result.CardExpiryYearFields.Add(field);
                        break;
                    case FormFieldType.CardSecurityCode:
                        result.CardSecurityCodeFields.Add(field);
                        break;
                    case FormFieldType.CardHolderName:
                        result.CardHolderNameFields.Add(field);
                        break;
                    case FormFieldType.Pin:
                        result.PinFields.Add(field);
                        break;
                    case FormFieldType.IdentityNumber:
                        result.IdentityFields.Add(field);
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

            // A card field present makes this a payment form regardless of what else is on the
            // page. Checkout pages routinely carry a login box too, and filling the account
            // password into a payment form is the wrong secret in the wrong place.
            if (result.CardNumberFields.Any() || result.CardSecurityCodeFields.Any())
                return FormType.Payment;

            if (result.IdentityFields.Any() && !hasPassword)
                return FormType.Identity;

            if (result.PinFields.Any() && !hasPassword)
                return FormType.Pin;

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
        TwoFactor,
        CardNumber,
        CardExpiry,
        CardExpiryMonth,
        CardExpiryYear,
        CardSecurityCode,
        CardHolderName,
        Pin,
        IdentityNumber
    }

    public enum FormType
    {
        Unknown,
        Login,
        Registration,
        PasswordChange,
        TwoFactor,
        Passkey,
        Payment,
        Identity,
        Pin
    }

    public sealed class LoginFormDetectionResult
    {
        public List<FormFieldInfo> EmailFields { get; } = new();
        public List<FormFieldInfo> UsernameFields { get; } = new();
        public List<FormFieldInfo> PasswordFields { get; } = new();
        public List<FormFieldInfo> PasswordConfirmFields { get; } = new();
        public List<FormFieldInfo> PasskeyFields { get; } = new();
        public List<FormFieldInfo> TwoFactorFields { get; } = new();
        public List<FormFieldInfo> CardNumberFields { get; } = new();
        public List<FormFieldInfo> CardExpiryFields { get; } = new();
        public List<FormFieldInfo> CardExpiryMonthFields { get; } = new();
        public List<FormFieldInfo> CardExpiryYearFields { get; } = new();
        public List<FormFieldInfo> CardSecurityCodeFields { get; } = new();
        public List<FormFieldInfo> CardHolderNameFields { get; } = new();
        public List<FormFieldInfo> PinFields { get; } = new();
        public List<FormFieldInfo> IdentityFields { get; } = new();
        public FormType FormType { get; set; }

        public bool HasLoginFields =>
            (EmailFields.Any() || UsernameFields.Any()) && PasswordFields.Any();

        public bool HasRegistrationFields =>
            PasswordFields.Any() && PasswordConfirmFields.Any();

        public bool Has2FAFields => TwoFactorFields.Any();

        public bool HasPaymentFields => CardNumberFields.Any() || CardSecurityCodeFields.Any();

        public bool HasPinFields => PinFields.Any();

        public bool HasIdentityFields => IdentityFields.Any();
    }
}

