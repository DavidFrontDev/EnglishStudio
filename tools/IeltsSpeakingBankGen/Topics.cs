namespace EnglishStudio.IeltsSpeakingBankGen;

/// <summary>
/// Static lists driving the generator. Editing this file is how operators add or remove
/// topics — the IDs become permanent <c>TopicCode</c>s in the database, so don't rename
/// after a seed has shipped.
/// </summary>
public static class Topics
{
    /// <summary>40 Part 1 "familiar topic" banks. Code → human label.</summary>
    public static readonly (string Code, string Label)[] Part1 =
    {
        ("hometown", "Hometown"),
        ("work", "Work"),
        ("study", "Studies"),
        ("family", "Family"),
        ("friends", "Friends"),
        ("hobbies", "Hobbies"),
        ("music", "Music"),
        ("films", "Films and cinema"),
        ("books", "Books and reading"),
        ("sports", "Sports and exercise"),
        ("food", "Food and cooking"),
        ("weather", "Weather"),
        ("clothes", "Clothes and fashion"),
        ("shopping", "Shopping"),
        ("transport", "Transport"),
        ("holidays", "Holidays and travel"),
        ("free-time", "Free time"),
        ("weekends", "Weekends"),
        ("technology", "Technology"),
        ("social-media", "Social media"),
        ("internet", "The internet"),
        ("mobile-phones", "Mobile phones"),
        ("photography", "Photography"),
        ("art", "Art and museums"),
        ("animals", "Animals and pets"),
        ("plants", "Plants and gardening"),
        ("environment", "The environment"),
        ("school-memories", "School memories"),
        ("childhood", "Childhood"),
        ("dreams", "Dreams and ambitions"),
        ("success", "Success"),
        ("happiness", "Happiness"),
        ("health", "Health and fitness"),
        ("sleep", "Sleep"),
        ("languages", "Languages"),
        ("travel-abroad", "Travelling abroad"),
        ("neighbours", "Neighbours"),
        ("gifts", "Gifts and presents"),
        ("traditions", "Traditions"),
        ("festivals", "Festivals and celebrations")
    };

    /// <summary>
    /// 50 Part 2 descriptive cue-card topics. Each has a matching Part 3 follow-up bank
    /// sharing the suffix (e.g. <c>p2-influential-person</c> ↔ <c>p3-influential-person</c>).
    /// </summary>
    public static readonly (string Code, string Label, string Theme)[] Part2 =
    {
        ("influential-person", "A person who influenced you", "an influential person"),
        ("admired-friend", "A friend you admire", "a friend you admire"),
        ("inspiring-teacher", "An inspiring teacher you remember", "an inspiring teacher"),
        ("family-member", "A family member you are close to", "a close family member"),
        ("famous-person", "A famous person you would like to meet", "a famous person you'd like to meet"),
        ("place-visited", "A place you visited that you enjoyed", "a memorable place you visited"),
        ("childhood-place", "A place from your childhood", "a special childhood place"),
        ("city-to-visit", "A city you would like to visit", "a city you want to visit"),
        ("favourite-park", "A park or garden you enjoy", "a favourite park or garden"),
        ("interesting-building", "An interesting building in your area", "an interesting building"),
        ("important-event", "An important event in your life", "an important event in your life"),
        ("happy-memory", "A happy childhood memory", "a happy memory"),
        ("special-celebration", "A special celebration you attended", "a special celebration"),
        ("traditional-festival", "A traditional festival in your country", "a traditional festival"),
        ("recent-decision", "A difficult decision you had to make", "a difficult decision"),
        ("treasured-object", "An object you treasure", "an object you treasure"),
        ("useful-gadget", "A useful gadget in your home", "a useful gadget"),
        ("piece-of-clothing", "An item of clothing you like", "a favourite item of clothing"),
        ("photo-you-like", "A photograph you like", "a photograph you like"),
        ("recent-purchase", "Something you recently bought", "a recent purchase"),
        ("skill-to-learn", "A skill you want to learn", "a skill you'd like to learn"),
        ("language-learning", "An experience of learning a language", "learning a language"),
        ("hobby-you-enjoy", "A hobby you enjoy", "a hobby you enjoy"),
        ("sport-you-play", "A sport you play or watch", "a sport you enjoy"),
        ("book-impacted-you", "A book that influenced you", "an influential book"),
        ("film-you-liked", "A film you really enjoyed", "a memorable film"),
        ("song-you-love", "A song or piece of music you love", "a favourite song"),
        ("artist-you-admire", "An artist or musician you admire", "an admired artist"),
        ("tv-programme", "A TV programme you watch", "a TV programme"),
        ("interesting-website", "A website or app you find useful", "a useful website or app"),
        ("memorable-journey", "A memorable journey you took", "a memorable journey"),
        ("long-trip", "A long trip you went on", "a long trip"),
        ("public-transport", "An interesting train, bus or plane ride", "an interesting journey"),
        ("hotel-stay", "A hotel you stayed at", "a memorable hotel stay"),
        ("foreign-country", "A foreign country you would like to visit", "a foreign country to visit"),
        ("helped-someone", "A time you helped someone", "a time you helped someone"),
        ("good-deed", "A kind act you did or witnessed", "a kind act"),
        ("teamwork", "A successful team you were part of", "a successful team"),
        ("public-event", "A public event you attended", "a public event"),
        ("volunteering", "A time you did volunteer work", "volunteering"),
        ("healthy-habit", "A healthy habit you have", "a healthy habit"),
        ("piece-of-advice", "Some good advice you received", "good advice you received"),
        ("a-meal", "A meal you really enjoyed", "a memorable meal"),
        ("a-restaurant", "A restaurant you like", "a favourite restaurant"),
        ("cooking-experience", "A time you cooked for others", "cooking for others"),
        ("childhood-toy", "A toy you had as a child", "a childhood toy"),
        ("piece-of-art", "A work of art you find beautiful", "a work of art"),
        ("interesting-person", "An interesting person you met", "an interesting stranger"),
        ("noisy-place", "A noisy or crowded place you visited", "a noisy or crowded place"),
        ("future-plan", "A goal or plan you have for the next five years", "future plans")
    };
}
