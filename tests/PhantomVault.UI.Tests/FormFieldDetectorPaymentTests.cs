using PhantomVault.Core.Services.Autofill;
using Xunit;

namespace PhantomVault.UI.Tests
{
    /// <summary>
    /// Detection of payment, identity and PIN fields.
    ///
    /// The cases that matter are the collisions. A CVC input is routinely
    /// <c>type="password"</c> and labelled "security code", so the generic password and
    /// two-factor rules would both claim it; "pin" is a substring of ordinary words like
    /// "shipping". Getting either wrong means typing the wrong secret into a form.
    /// </summary>
    public sealed class FormFieldDetectorPaymentTests
    {
        private readonly FormFieldDetector _detector = new();

        private static FormFieldInfo Field(
            string id = "", string name = "", string type = "text",
            string placeholder = "", string label = "", string autoComplete = "")
            => new()
            {
                Id = id,
                Name = name,
                Type = type,
                Placeholder = placeholder,
                Label = label,
                AutoComplete = autoComplete
            };

        [Theory]
        [InlineData("cc-number")]
        [InlineData("cardnumber")]
        [InlineData("card-number")]
        public void Card_number_fields_are_recognised(string token)
        {
            Assert.Equal(FormFieldType.CardNumber, _detector.DetectFieldType(Field(autoComplete: token)));
        }

        [Fact]
        public void A_cvc_field_typed_as_password_is_not_treated_as_a_password()
        {
            // The trap: type="password" plus the word "code". Without payment rules running first
            // this is classified as a password or a one-time code, and the account password gets
            // typed into a card form.
            var cvc = Field(id: "cvc", type: "password", label: "Security code");

            Assert.Equal(FormFieldType.CardSecurityCode, _detector.DetectFieldType(cvc));
        }

        [Theory]
        [InlineData("cvv")]
        [InlineData("csc")]
        [InlineData("cc-csc")]
        public void Security_code_variants_are_recognised(string token)
        {
            Assert.Equal(FormFieldType.CardSecurityCode, _detector.DetectFieldType(Field(name: token)));
        }

        [Fact]
        public void Expiry_month_and_year_are_distinguished_from_a_combined_expiry()
        {
            Assert.Equal(FormFieldType.CardExpiryMonth, _detector.DetectFieldType(Field(autoComplete: "cc-exp-month")));
            Assert.Equal(FormFieldType.CardExpiryYear, _detector.DetectFieldType(Field(autoComplete: "cc-exp-year")));
            Assert.Equal(FormFieldType.CardExpiry, _detector.DetectFieldType(Field(autoComplete: "cc-exp")));
        }

        [Fact]
        public void A_cardholder_name_is_not_mistaken_for_a_username()
        {
            Assert.Equal(FormFieldType.CardHolderName, _detector.DetectFieldType(Field(autoComplete: "cc-name")));
            Assert.Equal(FormFieldType.CardHolderName, _detector.DetectFieldType(Field(id: "nameOnCard")));
        }

        [Fact]
        public void Pin_fields_are_recognised_without_claiming_ordinary_words()
        {
            Assert.Equal(FormFieldType.Pin, _detector.DetectFieldType(Field(id: "pin", label: "Enter your PIN")));
            Assert.Equal(FormFieldType.Pin, _detector.DetectFieldType(Field(name: "pincode")));

            // "shipping" contains "pin". A shipping address field must not become a PIN prompt.
            Assert.NotEqual(FormFieldType.Pin, _detector.DetectFieldType(Field(id: "shipping_address")));
            Assert.NotEqual(FormFieldType.Pin, _detector.DetectFieldType(Field(name: "spinner")));
        }

        [Theory]
        [InlineData("passport")]
        [InlineData("idnumber")]
        [InlineData("nationalid")]
        public void Identity_documents_are_recognised(string token)
        {
            Assert.Equal(FormFieldType.IdentityNumber, _detector.DetectFieldType(Field(name: token)));
        }

        [Fact]
        public void A_checkout_page_with_a_login_box_is_still_a_payment_form()
        {
            // Checkout pages commonly carry both. Classifying this as a login form would offer the
            // account password for a form whose sensitive field is a card number.
            var result = _detector.DetectLoginForm(new[]
            {
                Field(id: "email", type: "email"),
                Field(id: "password", type: "password"),
                Field(autoComplete: "cc-number"),
                Field(autoComplete: "cc-csc")
            });

            Assert.Equal(FormType.Payment, result.FormType);
            Assert.True(result.HasPaymentFields);
        }

        [Fact]
        public void An_ordinary_login_form_is_unaffected_by_the_new_rules()
        {
            var result = _detector.DetectLoginForm(new[]
            {
                Field(id: "username"),
                Field(id: "password", type: "password")
            });

            Assert.Equal(FormType.Login, result.FormType);
            Assert.True(result.HasLoginFields);
            Assert.False(result.HasPaymentFields);
        }
    }
}
