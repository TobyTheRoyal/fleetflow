using System.Linq.Expressions;
using FleetFlow.Domain.Shipments;

namespace FleetFlow.Application.Insights;

public enum FindingType { Overdue, InTransitOverdue, DueSoon, PickupWindowOpen, UnassignedDraft, AssignmentRequiresReview }
public enum FindingSeverity { Low = 1, Medium = 2, High = 3, Critical = 4 }
public enum SuggestedAction { VerifyProgress, ContactDispatcher, ReviewSchedule, ConfirmAssignment, ReviewResources }
public sealed record ShipmentFacts(Guid Id, string Number, long Version, ShipmentStatus Status,
    DateTimeOffset PickupUtc, DateTimeOffset DeadlineUtc, bool DriverAssigned, bool VehicleAssigned,
    bool DriverEligible, bool VehicleEligible)
{
    public ShipmentFacts() : this(default, "", 0, default, default, default, false, false, false, false) { }
}
public sealed record OperationalRule(FindingType Type, FindingSeverity Severity, Expression<Func<ShipmentFacts, bool>> Matches);
public sealed record FindingEvidence(ShipmentStatus Status, long Version, DateTimeOffset PickupUtc, DateTimeOffset DeadlineUtc,
    bool DriverAssigned, bool VehicleAssigned, bool DriverEligible, bool VehicleEligible);
public sealed record OperationalFinding(string FindingId, Guid ShipmentId, string ShipmentNumber, FindingType Type,
    FindingSeverity Severity, string Explanation, FindingEvidence Evidence, string RecommendedAction, DateTimeOffset TimestampUtc);

public static class OperationalRules
{
    // Expressions are shared by SQL filtering and pure unit tests. Deadline equality is due, not overdue.
    public static OperationalRule[] Definitions(DateTimeOffset now) =>
    [
        new(FindingType.Overdue, FindingSeverity.High, s => s.Status != ShipmentStatus.Delivered && s.Status != ShipmentStatus.Cancelled && s.Status != ShipmentStatus.InTransit && s.DeadlineUtc < now),
        new(FindingType.InTransitOverdue, FindingSeverity.Critical, s => s.Status == ShipmentStatus.InTransit && s.DeadlineUtc < now),
        new(FindingType.DueSoon, FindingSeverity.Medium, s => s.Status != ShipmentStatus.Delivered && s.Status != ShipmentStatus.Cancelled && s.DeadlineUtc >= now && s.DeadlineUtc <= now.AddHours(24)),
        new(FindingType.PickupWindowOpen, FindingSeverity.Low, s => s.Status == ShipmentStatus.Planned && s.PickupUtc <= now),
        new(FindingType.UnassignedDraft, FindingSeverity.Medium, s => s.Status == ShipmentStatus.Draft && s.PickupUtc <= now.AddHours(24) && (!s.DriverAssigned || !s.VehicleAssigned)),
        new(FindingType.AssignmentRequiresReview, FindingSeverity.High, s => (s.Status == ShipmentStatus.Draft || s.Status == ShipmentStatus.Planned) && s.DriverAssigned && s.VehicleAssigned && (!s.DriverEligible || !s.VehicleEligible))
    ];
    public static string Explain(FindingType type) => type switch
    {
        FindingType.Overdue => "The delivery deadline has passed and completion is not recorded.",
        FindingType.InTransitOverdue => "The shipment is recorded as in transit after its delivery deadline.",
        FindingType.DueSoon => "The delivery deadline is within the next 24 hours, including the current instant.",
        FindingType.PickupWindowOpen => "The pickup window has started; the shipment is still recorded as Planned. This alone does not prove a late pickup.",
        FindingType.UnassignedDraft => "Pickup is within 24 hours or has already started, and the Draft is missing a driver or vehicle.",
        _ => "The selected driver or vehicle no longer satisfies current eligibility or payload requirements."
    };
    public static SuggestedAction[] Actions(FindingType type) => type switch
    {
        FindingType.UnassignedDraft => [SuggestedAction.ConfirmAssignment, SuggestedAction.ReviewSchedule],
        FindingType.AssignmentRequiresReview => [SuggestedAction.ReviewResources, SuggestedAction.ContactDispatcher],
        FindingType.DueSoon => [SuggestedAction.ReviewSchedule, SuggestedAction.ContactDispatcher],
        _ => [SuggestedAction.VerifyProgress, SuggestedAction.ContactDispatcher]
    };
    public static string ActionText(SuggestedAction action) => action switch
    {
        SuggestedAction.VerifyProgress => "Verify actual progress with the dispatcher before recording any operational change.",
        SuggestedAction.ContactDispatcher => "Contact the responsible dispatcher to confirm the current situation; the cause is unknown.",
        SuggestedAction.ReviewSchedule => "Review the recorded pickup and delivery schedule with the operations team.",
        SuggestedAction.ConfirmAssignment => "Review eligible resources and confirm an assignment through the normal dispatch workflow.",
        _ => "Review driver eligibility, vehicle availability and payload through the existing dispatch workflow."
    };
    public static OperationalFinding Finding(ShipmentFacts s, FindingType type, FindingSeverity severity, DateTimeOffset now) =>
        new($"{s.Id:D}:{type}", s.Id, s.Number, type, severity, Explain(type),
            new(s.Status, s.Version, s.PickupUtc, s.DeadlineUtc, s.DriverAssigned, s.VehicleAssigned, s.DriverEligible, s.VehicleEligible),
            ActionText(Actions(type)[0]), now);
}
