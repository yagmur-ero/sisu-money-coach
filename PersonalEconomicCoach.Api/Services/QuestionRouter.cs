namespace PersonalEconomicCoach.Api.Services;

public interface IQuestionRouter
{
    string Resolve(int? questionId, string text);
}

/// <summary>
/// MVP router: the LLM does not choose or run database functions in this version.
/// We deterministically map the question to one of the five supported insight types.
/// </summary>
public sealed class QuestionRouter : IQuestionRouter
{
    public string Resolve(int? questionId, string text)
    {
        if (questionId.HasValue)
        {
            return questionId.Value switch
            {
                1 => "baskets",
                2 => "comparison",
                3 => "leaks",
                4 => "safe_to_spend",
                5 => "what_if",
                _ => throw new ApiException(400, "question_id must be between 1 and 5, or null.")
            };
        }

        var value = text.ToLowerInvariant();
        if (ContainsAny(value, "what if", "reduce by", "cut by", "save if", "simulation", "simulate", "20%", "budget change"))
            return "what_if";
        if (ContainsAny(value, "safe to spend", "afford", "until payday", "until salary", "how much can i spend", "available to spend", "left until", "end of the month", "month-end", "month end", "how much will i have"))
            return "safe_to_spend";
        if (ContainsAny(value, "leak", "coffee", "small purchase", "frequent purchase", "money leaks"))
            return "leaks";
        if (ContainsAny(value, "compare", "comparison", "compared", "more than last month", "less than last month", "increased", "decreased", "versus", " vs "))
            return "comparison";
        if (ContainsAny(value, "where did my money go", "baskets", "breakdown", "spending summary", "category", "categories", "my spending", "how much did i spend", "spent on", "spend on"))
            return "baskets";

        throw new ApiException(422, "I could not identify which financial insight you are asking for. Try asking about your spending, monthly comparison, everyday leaks, safe-to-spend, or a what-if scenario.");
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.Ordinal));
}
