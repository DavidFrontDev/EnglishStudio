namespace EnglishStudio.Modules.Dictionary.Entities;

public class Category
{
    public int Id { get; set; }

    public string Code { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string NameRu { get; set; } = string.Empty;

    public int? ParentCategoryId { get; set; }
    public Category? ParentCategory { get; set; }
    public ICollection<Category> Children { get; set; } = new List<Category>();

    public int OrderIndex { get; set; }

    public ICollection<WordCategory> WordCategories { get; set; } = new List<WordCategory>();
}

public class WordCategory
{
    public int WordId { get; set; }
    public Word Word { get; set; } = null!;

    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
}
