namespace BackendV2.Models;

public class FunctionGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public ICollection<RoleFunctionGroup> RoleFunctionGroups { get; set; } = new List<RoleFunctionGroup>();
}
