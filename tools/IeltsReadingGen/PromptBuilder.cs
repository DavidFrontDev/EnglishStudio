using System.Text;

namespace EnglishStudio.IeltsReadingGen;

internal static class PromptBuilder
{
    public static string Build(TestPlan plan)
    {
        var specs = Profiles.For(plan.Profile);

        var sb = new StringBuilder(capacity: 8000);

        sb.AppendLine("You are an experienced IELTS Academic test author. Generate one complete");
        sb.AppendLine("3-passage Academic Reading test. Output ONLY a single JSON object — no prose,");
        sb.AppendLine("no commentary, no markdown code fence.");
        sb.AppendLine();
        sb.AppendLine("===== STRICT JSON SCHEMA =====");
        sb.AppendLine(@"{
  ""code"": ""<test-id, must equal the one I give you>"",
  ""title"": ""<short title>"",
  ""mode"": ""Academic"",
  ""attribution"": ""EnglishStudio generated"",
  ""parts"": [
    {
      ""order"": 1 | 2 | 3,
      ""title"": ""<5-10 word passage title>"",
      ""body"": ""<academic passage, 700-900 words, multiple paragraphs separated by \\n\\n>"",
      ""introNoteRu"": ""Вопросы N–M относятся к данному отрывку."",
      ""questions"": [
        {
          ""order"": <1-based integer within the part>,
          ""type"": ""<one of the enum values, EXACT case>"",
          ""stem"": ""<question text>"",
          ""options"": [<string array; ONLY for MultipleChoiceSingle/Multi and Matching* types; omit otherwise>],
          ""answerKey"": <see rules below>,
          ""acceptableAnswers"": [<optional array of accepted spellings>],
          ""wordLimitMax"": <integer, ONLY for completion / short answer; usually 1-3>,
          ""points"": 1
        }
      ]
    }
  ]
}");
        sb.AppendLine();
        sb.AppendLine("===== ENUM VALUES (use these EXACT strings) =====");
        sb.AppendLine("TrueFalseNotGiven, YesNoNotGiven, MultipleChoiceSingle, MultipleChoiceMulti,");
        sb.AppendLine("MatchingHeadings, MatchingInformation, MatchingFeatures, MatchingSentenceEndings,");
        sb.AppendLine("SentenceCompletion, SummaryCompletion, NoteCompletion, TableCompletion,");
        sb.AppendLine("FlowChartCompletion, ShortAnswer");
        sb.AppendLine();
        sb.AppendLine("===== ANSWER KEY RULES =====");
        sb.AppendLine("• TrueFalseNotGiven: \"TRUE\" | \"FALSE\" | \"NOT GIVEN\"");
        sb.AppendLine("• YesNoNotGiven:     \"YES\" | \"NO\" | \"NOT GIVEN\"");
        sb.AppendLine("• MultipleChoiceSingle: single letter \"A\" | \"B\" | \"C\" | \"D\" (provide 4 options)");
        sb.AppendLine("• MultipleChoiceMulti:  string array of letters e.g. [\"A\",\"C\"] (provide 5 options, mark 2 correct)");
        sb.AppendLine("• Matching* (Headings/Information/Features/SentenceEndings):");
        sb.AppendLine("    one question per matching item; answerKey = single letter tag \"A\"-\"H\";");
        sb.AppendLine("    options = the SAME shared array of tagged choices repeated on every question of that group,");
        sb.AppendLine("    formatted as \"A — Choice text\". Include 1-2 distractors more than items.");
        sb.AppendLine("• SentenceCompletion / SummaryCompletion / NoteCompletion / TableCompletion /");
        sb.AppendLine("    FlowChartCompletion / ShortAnswer: answerKey = the exact word(s), lowercase preferred;");
        sb.AppendLine("    set wordLimitMax = 1, 2 or 3 (state the limit in the stem: \"No more than TWO words\").");
        sb.AppendLine();
        sb.AppendLine("===== PASSAGE GUIDELINES =====");
        sb.AppendLine("• 700-900 words each, academic register, neutral / informative tone.");
        sb.AppendLine("• 4-6 paragraphs, joined by \\n\\n in the JSON string.");
        sb.AppendLine("• Body must contain VERIFIABLE information that answers the questions —");
        sb.AppendLine("  do not invent statistics presented as fact unless they are reasonable and consistent.");
        sb.AppendLine("• Question stems must reference passage content unambiguously.");
        sb.AppendLine("• ALL distractors in MCQ must be plausible but clearly incorrect from the passage.");
        sb.AppendLine();
        sb.AppendLine($"===== TEST TO GENERATE =====");
        sb.AppendLine($"code: {plan.Code}");
        sb.AppendLine($"title: \"IELTS Academic Reading — {plan.Code.Replace("acad-r-", "Test #").TrimStart('0')}\"");
        sb.AppendLine();
        sb.AppendLine("• Part 1 (factual passage):");
        sb.AppendLine($"    topic: {plan.P1Topic}");
        sb.AppendLine($"    question distribution: {specs[0].Distribution}");
        sb.AppendLine("    question numbering: 1..13 within the part");
        sb.AppendLine();
        sb.AppendLine("• Part 2 (argument / opinion passage):");
        sb.AppendLine($"    topic: {plan.P2Topic}");
        sb.AppendLine($"    question distribution: {specs[1].Distribution}");
        sb.AppendLine("    question numbering: 1..14 within the part");
        sb.AppendLine();
        sb.AppendLine("• Part 3 (academic / scientific passage):");
        sb.AppendLine($"    topic: {plan.P3Topic}");
        sb.AppendLine($"    question distribution: {specs[2].Distribution}");
        sb.AppendLine("    question numbering: 1..13 within the part");
        sb.AppendLine();
        sb.AppendLine("Generate the complete test JSON now. Begin your response with { and end with }.");

        return sb.ToString();
    }
}
