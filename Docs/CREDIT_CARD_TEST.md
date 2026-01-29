# Credit Card Add/Edit Test

## Test Steps

1. Launch the app
2. Click the **+** button in the toolbar
3. Select **"Credit Card"** from the dropdown menu
4. The edit panel should slide in from the right
5. Verify these fields are VISIBLE:
   - Title *
   - Username / Email (optional for cards)
   - Card Details section with:
     - Cardholder Name
     - Card Number
     - Card Type
     - Expiry Month / Year
     - CVV / PIN
     - Billing Address

## Expected Behavior

- `IsCreditCardEntry` should be `true`
- Credit card fields section should be visible
- Password field may or may not show (depends on `ShowPasswordField` logic)

## Debug Info

Check console output for:

```
[ADD/EDIT VM] Constructor: _existingCredential=..., EntryType=CreditCard, IsCreditCardEntry=True
```

## Code Fixed

1. **Save() method** - Now copies all type-specific fields (CardNumber, CardCVV, etc.) from `_existingCredential` to the final credential before saving
2. **EntryType preservation** - `credential.EntryType` is now set from `_existingCredential?.EntryType`

## If Still Not Working

Check:

1. Is `IsVisible="{Binding IsCreditCardEntry}"` binding correctly in VaultWindow.axaml line 3157?
2. Is `_editFormStack.DataContext = _currentEditViewModel` being called in VaultWindow.axaml.cs line 184?
3. Does the console show `IsCreditCardEntry=True` when creating a credit card?
