namespace PhantomVault.UI.ViewModels;

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

    public static ListItemWrapper CreateCategoryHeader(string categoryName, string? categoryColor, int itemCount)
    {
        return new ListItemWrapper(true, categoryName, categoryColor, itemCount, null);
    }

    public static ListItemWrapper CreateCredential(CredentialViewModel credential)
    {
        return new ListItemWrapper(false, null, null, 0, credential);
    }
}

