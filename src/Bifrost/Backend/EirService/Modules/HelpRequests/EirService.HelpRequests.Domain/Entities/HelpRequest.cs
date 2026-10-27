using EirService.HelpRequests.Domain.DomainEvents;
using EirService.HelpRequests.Domain.ValueObjects;
using Shared.Domain.Primitives;

namespace EirService.HelpRequests.Domain.Entities;

public sealed class HelpRequest : AggregateRoot<Guid>
{
    private readonly List<HelpRequestMessage> messages = [];
    private HelpRequest() { }

    public Guid SessionId { get; private set; }
    public int Generation { get; private set; }
    public int RequestNumber { get; private set; }
    public string DisplayNumber => $"{(IsFaq ? "FAQ" : "BF")}-{RequestNumber:D3}";
    public string? StudentId { get; private set; }
    public bool IsFaq => StudentId is null;
    public HelpType HelpType { get; private set; }
    public HelpRequestStatus Status { get; private set; }
    public string? AssignedTeacherId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset QueuedAt { get; private set; }
    public DateTimeOffset? ReturnedToQueueAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public Guid? FaqAnswerId { get; private set; }
    public IReadOnlyCollection<HelpRequestMessage> Messages => messages.AsReadOnly();
    public bool IsOpen => Status is HelpRequestStatus.Pending or HelpRequestStatus.Active or HelpRequestStatus.AwaitingFaq or HelpRequestStatus.AwaitingConfirmation;
    public bool IsCompleted => Status is HelpRequestStatus.CompletedByFaq or HelpRequestStatus.CompletedByDigital or HelpRequestStatus.CompletedByPhysical;

    public static HelpRequest Create(Guid sessionId, int generation, string studentId,
        HelpType helpType, MessageText question, DateTimeOffset now)
    {
        if (sessionId == Guid.Empty || generation < 1) throw new ArgumentException("A session and positive generation are required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(studentId);
        if (!Enum.IsDefined(helpType)) throw new ArgumentException("Unknown help type.", nameof(helpType));
        var request = new HelpRequest
        {
            Id = Guid.NewGuid(), SessionId = sessionId, Generation = generation, StudentId = studentId,
            HelpType = helpType, Status = HelpRequestStatus.Pending,
            CreatedAt = now.ToUniversalTime(), QueuedAt = now.ToUniversalTime()
        };
        request.AddMessage(studentId, AuthorRole.Student, HelpRequestMessageType.StudentQuestion, question, now);
        request.Changed("Created", now);
        return request;
    }

    public static HelpRequest CreateFaq(Guid faqId, Guid sessionId, int generation, MessageText question, DateTimeOffset now)
    {
        if (faqId == Guid.Empty || sessionId == Guid.Empty || generation < 1)
            throw new ArgumentException("A FAQ id, session and generation are required.");
        var request = new HelpRequest
        {
            Id = faqId, SessionId = sessionId, Generation = generation, FaqAnswerId = faqId,
            HelpType = HelpType.Digital, Status = HelpRequestStatus.Pending,
            CreatedAt = now.ToUniversalTime(), QueuedAt = now.ToUniversalTime()
        };
        request.AddMessage("mimir", AuthorRole.System, HelpRequestMessageType.FaqQuestion, question, now);
        request.Changed("FaqCreated", now);
        return request;
    }

    public bool CanLinkToFaq => !IsFaq &&
        Status == HelpRequestStatus.Pending && AssignedTeacherId is null && FaqAnswerId is null &&
        !messages.Any(message => message.MessageType == HelpRequestMessageType.TeacherAnswer) &&
        !HasFollowUpAfterFaqAnswer();

    // A delayed generalization answers the original question, not a reopened follow-up.
    // Rejecting a FAQ alone still leaves the original question awaiting help.
    private bool HasFollowUpAfterFaqAnswer() => messages.Any(answer =>
        answer.MessageType == HelpRequestMessageType.FaqAnswer && messages.Any(followUp =>
            followUp.MessageType == HelpRequestMessageType.FollowUpQuestion &&
            followUp.SequenceNumber > answer.SequenceNumber));

    public void LinkToFaq(Guid faqId, MessageText question, DateTimeOffset now)
    {
        Require(CanLinkToFaq && faqId != Guid.Empty, "Only unassigned waiting requests can join a FAQ.");
        AddMessage("mimir", AuthorRole.System, HelpRequestMessageType.FaqQuestion, question, now);
        FaqAnswerId = faqId;
        Status = HelpRequestStatus.AwaitingFaq;
        Changed("FaqLinked", now);
    }

    public void SuggestFaq(Guid faqId, string teacherId, MessageText answer, DateTimeOffset now)
    {
        Require(!IsFaq && Status == HelpRequestStatus.AwaitingFaq && FaqAnswerId == faqId,
            "The request is not awaiting this FAQ.");
        AddMessage(teacherId, AuthorRole.Teacher, HelpRequestMessageType.FaqAnswer, answer, now);
        Status = HelpRequestStatus.AwaitingConfirmation;
        Changed("FaqSuggested", now);
    }

    public void Reserve(string teacherId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teacherId);
        Require(Status == HelpRequestStatus.Pending && AssignedTeacherId is null, "The request is already reserved or no longer waiting.");
        AssignedTeacherId = teacherId;
        Changed("Reserved", now);
    }

    public void Start(string teacherId, DateTimeOffset now)
    {
        RequireTeacher(teacherId);
        Require(HelpType == HelpType.Physical && Status == HelpRequestStatus.Pending, "Only waiting physical help can be started.");
        Status = HelpRequestStatus.Active;
        StartedAt = now.ToUniversalTime();
        Changed("Started", now);
    }

    public void Complete(string teacherId, DateTimeOffset now)
    {
        RequireTeacher(teacherId);
        Require(HelpType == HelpType.Physical && Status == HelpRequestStatus.Active, "Only active physical help can be completed. Digital help requires an answer.");
        Close(HelpRequestStatus.CompletedByPhysical, now);
        Changed("Completed", now);
    }

    public void Answer(string teacherId, MessageText text, DateTimeOffset now)
    {
        RequireTeacher(teacherId);
        Require(HelpType == HelpType.Digital && Status == HelpRequestStatus.Pending, "Only waiting digital help can receive an answer.");
        AddMessage(teacherId, AuthorRole.Teacher,
            IsFaq ? HelpRequestMessageType.FaqAnswer : HelpRequestMessageType.TeacherAnswer, text, now);
        StartedAt = now.ToUniversalTime();
        Close(HelpRequestStatus.CompletedByDigital, now);
        Changed("Answered", now);
    }

    public void AddStudentMessage(string studentId, MessageText text, DateTimeOffset now)
    {
        Require(StudentId == studentId, "The request belongs to another student.");
        Require(Status == HelpRequestStatus.Pending || IsCompleted, "A message cannot be added while help is active, paused or cancelled.");
        Require(IsCompleted || AssignedTeacherId is null, "The request has already been reserved.");
        AddMessage(studentId, AuthorRole.Student, HelpRequestMessageType.FollowUpQuestion, text, now);
        ResetForQueue();
        QueuedAt = now.ToUniversalTime();
        ReturnedToQueueAt = null;
        Changed("StudentMessageAdded", now);
    }

    public void Cancel(string actorId, AuthorRole role, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        Require(IsOpen, "The request is already closed.");
        if (role == AuthorRole.Student)
        {
            Require(actorId == StudentId && Status != HelpRequestStatus.Active, "Students can only cancel before help starts.");
            Close(HelpRequestStatus.Cancelled, now);
            Changed("Cancelled", now);
        }
        else
        {
            Require(role == AuthorRole.Teacher && (AssignedTeacherId is null || AssignedTeacherId == actorId), "The request is assigned to another teacher.");
            ResetForQueue();
            ReturnedToQueueAt = now.ToUniversalTime();
            Changed("ReturnedToQueue", now);
        }
    }

    public void AcceptFaq(string studentId, DateTimeOffset now)
    {
        RequireFaqConfirmation(studentId);
        Close(HelpRequestStatus.CompletedByFaq, now);
        Changed("FaqAccepted", now);
    }

    public void RejectFaq(string studentId, DateTimeOffset now)
    {
        RequireFaqConfirmation(studentId);
        var question = messages.Where(message => message.MessageType == HelpRequestMessageType.FaqQuestion)
            .MaxBy(message => message.SequenceNumber);
        Require(question is not null, "The FAQ question is missing from the conversation.");
        foreach (var message in messages.Where(message => message.SequenceNumber >= question!.SequenceNumber &&
            message.MessageType is HelpRequestMessageType.FaqQuestion or HelpRequestMessageType.FaqAnswer))
            message.RejectFaq();
        Status = HelpRequestStatus.Pending;
        FaqAnswerId = null;
        AssignedTeacherId = null;
        Changed("FaqRejected", now); // QueuedAt is deliberately preserved.
    }

    private void RequireFaqConfirmation(string studentId) => Require(
        StudentId == studentId &&
        Status == HelpRequestStatus.AwaitingConfirmation && FaqAnswerId is not null,
        "There is no FAQ answer awaiting this student's confirmation.");

    private void RequireTeacher(string teacherId) => Require(
        !string.IsNullOrWhiteSpace(teacherId) && AssignedTeacherId == teacherId, "Take the request before helping the student.");

    private void AddMessage(string authorId, AuthorRole role, HelpRequestMessageType messageType, MessageText text, DateTimeOffset now) =>
        messages.Add(new HelpRequestMessage(Id, authorId, role, messageType, text, now, messages.Count + 1));

    private void Close(HelpRequestStatus status, DateTimeOffset now)
    {
        Status = status;
        ClosedAt = now.ToUniversalTime();
    }

    private void ResetForQueue()
    {
        Status = HelpRequestStatus.Pending;
        AssignedTeacherId = null;
        StartedAt = null;
        ClosedAt = null;
        if (!IsFaq) FaqAnswerId = null;
    }

    private void Changed(string change, DateTimeOffset now) =>
        RaiseDomainEvent(new HelpRequestChangedDomainEvent(this, change, now.ToUniversalTime()));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new HelpRequestRuleException(message);
    }
}
