namespace BackendV2.Models;

public class RoleFunctionGroup
{
    public Guid RoleId { get; set; }
    public Guid FunctionGroupId { get; set; }

    public Role? Role { get; set; }
    public FunctionGroup? FunctionGroup { get; set; }
}
