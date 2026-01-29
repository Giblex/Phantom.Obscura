namespace PhantomVault.UI.ViewModels;

/// <summary>
/// Wrapper that can represent either a credential item or a category banner header.
/// Used for displaying grouped credential lists with category separators.
/// </summary>
public class ListItemWrapper
{
    public bool IsCategoryHeader { get; }
    public string? CategoryName { get; }
    public string? CategoryColor { get; }
    public int CategoryItemCount { get; }
    public CredentialViewModel? Credential { get; }

    private ListItemWrapper(bool isCategoryHeader, string? categoryName, string? categoryColor, int categoryItemCount, CredentialViewModel? credential)
    {
        IsCategoryHeader = isCategoryHeader;
        CategoryName = categoryName;
        CategoryColor = categoryColor;
        CategoryItemCount = categoryItemCount;
        Credential = credential;
    }

    /// <summary>
    /// Creates a category header item.
    /// </summary>
    public static ListItemWrapper CreateCategoryHeader(string categoryName, string? categoryColor, int itemCount)
    {
        return new ListItemWrapper(true, categoryName, categoryColor, itemCount, null);
    }

    /// <summary>
    /// Creates a credential item.
    /// </summary>
    public static ListItemWrapper CreateCredential(CredentialViewModel credential)
    {
        return new ListItemWrapper(false, null, null, 0, credential);
    }
}
