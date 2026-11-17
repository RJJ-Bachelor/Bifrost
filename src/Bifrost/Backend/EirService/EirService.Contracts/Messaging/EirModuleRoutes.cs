namespace EirService.Contracts.Messaging;

public static class EirModuleRoutes
{
    public const string SessionUpdated = "eir.session.updated";
    public const string FaqAnswerRecorded = "eir.faq.answer-recorded";

    // Retained to drain persisted commands created before versioned event contracts.
    public const string FaqAnswered = "eir.faq.answered";
    public const string SessionChanged = "eir.session.changed";
}
