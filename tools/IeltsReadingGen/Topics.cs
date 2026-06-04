namespace EnglishStudio.IeltsReadingGen;

/// <summary>
/// 30 curated test definitions. Each test = 3 passages on different topics across the
/// IELTS Academic Reading difficulty progression: P1 factual, P2 argument/opinion, P3 academic.
/// Topics are curated to avoid overlap and to give the generator enough variety.
/// </summary>
internal static class Topics
{
    public static readonly TestPlan[] All =
    [
        new("acad-r-001",  "P1 The history of bicycles",                   "P2 Should remote work replace office work?",    "P3 Dark matter research",                        Profile.A),
        new("acad-r-002",  "P1 Ancient water management systems",          "P2 The role of zoos in modern society",          "P3 Neural plasticity in adults",                 Profile.B),
        new("acad-r-003",  "P1 The domestication of dogs",                 "P2 Plastic pollution policy approaches",         "P3 The microbiome and human health",             Profile.C),
        new("acad-r-004",  "P1 Volcanic ash and agriculture",              "P2 Universal basic income debate",               "P3 Quantum computing fundamentals",              Profile.D),
        new("acad-r-005",  "P1 The invention of paper",                    "P2 Should bilingual education be mandatory?",    "P3 Linguistic relativity (Sapir-Whorf hypothesis)", Profile.A),
        new("acad-r-006",  "P1 Honey bees and pollination",                "P2 The future of urban farming",                 "P3 The economics of attention",                  Profile.B),
        new("acad-r-007",  "P1 The Silk Road",                             "P2 Digital privacy regulation",                  "P3 CRISPR gene editing",                         Profile.C),
        new("acad-r-008",  "P1 Coral reef ecosystems",                     "P2 Pros and cons of nuclear energy",             "P3 Materials science: aerogels",                 Profile.D),
        new("acad-r-009",  "P1 Traditional bread-making across cultures",  "P2 The ethics of genetic modification",          "P3 Cognitive load theory",                       Profile.A),
        new("acad-r-010",  "P1 The discovery of penicillin",               "P2 Libraries as community hubs",                 "P3 The science of behavioural economics",        Profile.B),

        new("acad-r-011",  "P1 Lighthouses and maritime navigation",       "P2 Cars vs public transit in cities",            "P3 The double-slit experiment",                  Profile.C),
        new("acad-r-012",  "P1 The history of cartography",                "P2 Should fast food advertising be restricted?", "P3 Plate tectonics and continental drift",       Profile.D),
        new("acad-r-013",  "P1 Pigment and dye in ancient art",            "P2 Is space exploration worth its cost?",        "P3 Sleep and memory consolidation",              Profile.A),
        new("acad-r-014",  "P1 Migration patterns of monarch butterflies", "P2 Mandatory voting — arguments for and against","P3 Game theory in economics",                    Profile.B),
        new("acad-r-015",  "P1 The story of tea cultivation",              "P2 The case for and against year-round schooling","P3 Pharmaceutical drug development",            Profile.C),
        new("acad-r-016",  "P1 Pre-Columbian agriculture in the Americas", "P2 Tourism and cultural heritage",               "P3 Black holes and event horizons",              Profile.D),
        new("acad-r-017",  "P1 The history of clocks",                     "P2 Should national service be reintroduced?",    "P3 The science of taste perception",             Profile.A),
        new("acad-r-018",  "P1 Salt and its role in human history",        "P2 Privatisation of public utilities",           "P3 Climate feedback loops",                      Profile.B),
        new("acad-r-019",  "P1 Origins of writing systems",                "P2 Should homework be banned?",                  "P3 Symbiosis and mutualism in nature",           Profile.C),
        new("acad-r-020",  "P1 The history of vaccination",                "P2 Workplace dress codes",                       "P3 Bioluminescence",                              Profile.D),

        new("acad-r-021",  "P1 Architecture of medieval cathedrals",       "P2 Should governments regulate sugar?",          "P3 The cosmic microwave background",             Profile.A),
        new("acad-r-022",  "P1 Indigenous knowledge of medicinal plants",  "P2 The four-day workweek",                       "P3 Animal migration and Earth's magnetic field", Profile.B),
        new("acad-r-023",  "P1 Glassmaking through the centuries",         "P2 Subsidies for the arts",                      "P3 The chemistry of perfume",                    Profile.C),
        new("acad-r-024",  "P1 The story of chocolate",                    "P2 Is a global language inevitable?",            "P3 Soft robotics",                               Profile.D),
        new("acad-r-025",  "P1 Roman engineering",                         "P2 Should cities ban private cars in centres?",  "P3 The biology of ageing",                       Profile.A),
        new("acad-r-026",  "P1 Forest fires as ecological renewal",        "P2 Are international sporting events worth it?", "P3 The biochemistry of photosynthesis",          Profile.B),
        new("acad-r-027",  "P1 Astronomy in early civilisations",          "P2 Mandatory recycling policies",                "P3 Plate-bearing structural design",             Profile.C),
        new("acad-r-028",  "P1 The history of printing",                   "P2 Should heritage buildings always be preserved?","P3 The science of forgetting",                  Profile.D),
        new("acad-r-029",  "P1 Pearl diving traditions",                   "P2 Should there be limits to immigration?",      "P3 Volcanism on other planets",                  Profile.A),
        new("acad-r-030",  "P1 The history of soap",                       "P2 Algorithm transparency in social media",      "P3 The science of ocean currents",               Profile.B),
    ];
}

internal sealed record TestPlan(string Code, string P1Topic, string P2Topic, string P3Topic, Profile Profile);

internal enum Profile { A, B, C, D }
