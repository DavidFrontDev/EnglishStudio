using System.Windows;
using System.Windows.Controls;
using EnglishStudio.App.ViewModels.Listening;
using EnglishStudio.Modules.Ielts.Core.Entities;

namespace EnglishStudio.App.Views.Listening;

/// <summary>
/// Routes a <see cref="ListeningCardViewModel"/> to its card template by layout.
/// Adding a future card type = a new <see cref="QuestionGroupLayout"/> value, a new template,
/// and a new case here.
/// </summary>
public sealed class CardLayoutTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Anketa { get; set; }         // StructuredNotes (bulleted notes)
    public DataTemplate? SummaryFlow { get; set; }    // SummaryFlow (wrapping prose, text gaps)
    public DataTemplate? Table { get; set; }          // Table
    public DataTemplate? Comparison { get; set; }     // MapLabeling
    public DataTemplate? Cascade { get; set; }        // Cascade (flow chart)
    public DataTemplate? CascadeImage { get; set; }   // CascadeImage (flow chart + letter box)
    public DataTemplate? Selector { get; set; }       // Selector (TFNG / matching dropdowns + opt. image)
    public DataTemplate? AnketaImage { get; set; }    // AnketaImage (flowing text + letter dropdowns + image)
    public DataTemplate? Choice { get; set; }         // FlatList (Radio / Doble)

    public override DataTemplate? SelectTemplate(object? item, DependencyObject container)
    {
        if (item is not ListeningCardViewModel card) return base.SelectTemplate(item, container);
        return card.Layout switch
        {
            QuestionGroupLayout.StructuredNotes => Anketa ?? Choice,
            QuestionGroupLayout.SummaryFlow => SummaryFlow ?? Anketa ?? Choice,
            QuestionGroupLayout.Table => Table ?? Choice,
            QuestionGroupLayout.MapLabeling => Comparison ?? Choice,
            QuestionGroupLayout.Cascade => Cascade ?? Choice,
            QuestionGroupLayout.CascadeImage => CascadeImage ?? Cascade ?? Choice,
            QuestionGroupLayout.Selector => Selector ?? Choice,
            QuestionGroupLayout.AnketaImage => AnketaImage ?? CascadeImage ?? Choice,
            _ => Choice
        };
    }
}
