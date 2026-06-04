using EnglishStudio.Modules.Dictionary.Entities;

namespace EnglishStudio.Modules.Dictionary.Seed;

internal static class PartOfSpeechSeedMap
{
    public static readonly (string Code, string NameEn, string NameRu)[] All =
    {
        ("n",         "Noun",                "Существительное"),
        ("v",         "Verb",                "Глагол"),
        ("adj",       "Adjective",           "Прилагательное"),
        ("adv",       "Adverb",              "Наречие"),
        ("pron",      "Pronoun",             "Местоимение"),
        ("prep",      "Preposition",         "Предлог"),
        ("det",       "Determiner",          "Определитель"),
        ("num",       "Number",              "Числительное"),
        ("conj",      "Conjunction",         "Союз"),
        ("exclam",    "Exclamation",         "Восклицание"),
        ("modal",     "Modal verb",          "Модальный глагол"),
        ("ordnum",    "Ordinal number",      "Порядковое числительное"),
        ("aux",       "Auxiliary verb",      "Вспомогательный глагол"),
        ("art_indef", "Indefinite article",  "Неопределённый артикль"),
        ("art_def",   "Definite article",    "Определённый артикль"),
        ("inf",       "Infinitive marker",   "Инфинитивная частица"),
        ("linkv",     "Linking verb",        "Глагол-связка"),
        ("phrasal_verb", "Phrasal verb",     "Фразовый глагол"),
        ("other",     "Other",               "Прочее"),
    };

    public static PartOfSpeech Create(string code, string nameEn, string nameRu) => new()
    {
        Code = code,
        NameEn = nameEn,
        NameRu = nameRu,
    };
}
