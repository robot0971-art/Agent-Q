using System.Collections.ObjectModel;

namespace AgentQ.Desktop.Services;

public sealed class DesktopPlanWorkflowService
{
    public IReadOnlyList<AgentPlanItem> ParsePlan(string planText)
    {
        return DesktopPlanParser.Parse(planText);
    }

    public AgentPlanItem? SelectNextOpen(IEnumerable<AgentPlanItem> planItems)
    {
        return planItems
            .OrderBy(item => item.Order)
            .FirstOrDefault(item => item.Status is AgentPlanItemStatus.Pending or AgentPlanItemStatus.InProgress);
    }

    public void ReplacePlanItems(
        ObservableCollection<AgentPlanItem> target,
        IEnumerable<AgentPlanItem> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    public void ApplyCheckpoint(
        ObservableCollection<AgentPlanItem> target,
        AgentCheckpoint checkpoint)
    {
        target.Clear();
        foreach (var item in checkpoint.PlanItems.OrderBy(item => item.Order))
        {
            target.Add(new AgentPlanItem
            {
                Order = item.Order,
                Title = item.Title,
                Detail = item.Detail,
                Status = item.Status
            });
        }
    }
}
