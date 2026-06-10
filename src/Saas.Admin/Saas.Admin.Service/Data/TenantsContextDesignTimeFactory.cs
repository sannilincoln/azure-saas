using Microsoft.EntityFrameworkCore.Design;

namespace Saas.Admin.Service.Data;

/// <summary>
/// Design-time only. Lets <c>dotnet ef migrations</c> build the <see cref="TenantsContext"/>
/// model without running Program.cs (which connects to Azure App Configuration / Key Vault).
/// The connection string is a placeholder — <c>migrations add</c> scaffolds from the model,
/// not from a live database.
/// </summary>
public class TenantsContextDesignTimeFactory : IDesignTimeDbContextFactory<TenantsContext>
{
    public TenantsContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TenantsContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=asdk-tenant-design;")
            .Options;

        return new TenantsContext(options);
    }
}
