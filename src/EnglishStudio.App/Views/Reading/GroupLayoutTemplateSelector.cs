using System.Windows;
using System.Windows.Controls;
using EnglishStudio.App.ViewModels.Reading;
using EnglishStudio.Modules.Ielts.Core.Entities;

namespace EnglishStudio.App.Views.Reading;

/// <summary>
/// Routes a <see cref="ReadingQuestionGroupViewModel"/> to one of three group templates
/// (FlatList / SummaryFlow / MapLabeling) defined in QuestionTemplates.xaml.
/// </summary>
public sealed class GroupLayoutTemplateSelector : DataTemplateSelector
{
    public DataTemplate? FlatList { get; set; }
    public DataTemplate? SummaryFlow { get; set; }
    public DataTemplate? MapLabeling { get; set; }

    public override DataTemplate? SelectTemplate(object? item, DependencyObject container)
    {
        if (item is not ReadingQuestionGroupViewModel group) return base.SelectTemplate(item, container);

        return group.Layout switch
        {
            QuestionGroupLayout.SummaryFlow => SummaryFlow ?? FlatList,
            QuestionGroupLayout.MapLabeling => MapLabeling ?? FlatList,
            _ => FlatList
        };
    }
}
