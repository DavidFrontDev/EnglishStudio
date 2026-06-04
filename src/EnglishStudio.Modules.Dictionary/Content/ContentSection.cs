namespace EnglishStudio.Modules.Dictionary.Content;

/// <summary>
/// Логические секции импортируемого контент-пака. Расширяемо: новый модуль с защищённым
/// контентом добавляет своё значение (см. plans/Infra_Publish_GitHub_AgentExecution.md §9).
/// </summary>
public enum ContentSection
{
    DictionaryOxford,
    DictionaryPhave,
    Reading,
    Listening,
    Writing,
    Speaking,
    Rubrics,
}
