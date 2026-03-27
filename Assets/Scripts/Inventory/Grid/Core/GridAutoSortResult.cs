using System.Collections.Generic;

public readonly struct GridAutoSortResult
{
    public readonly bool success;
    public readonly IReadOnlyList<string> failedInstanceIds;

    public GridAutoSortResult(bool success, IReadOnlyList<string> failedInstanceIds)
    {
        this.success = success;
        this.failedInstanceIds = failedInstanceIds;
    }
}
