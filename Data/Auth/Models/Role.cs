using System;
using System.Collections.Generic;

namespace TunnelSecurity.Data.Auth.Models
{
    public class Role
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;

        public ICollection<User> Users { get; set; } = new List<User>();
        public ICollection<RoleFunctionGroup> RoleFunctionGroups { get; set; } = new List<RoleFunctionGroup>();
    }
}
