using Saas.Identity.Authorization.Model.Data;
using Saas.Permissions.Service.Data.Configuration;

namespace Saas.Permissions.Service.Data.Context;

public class SaasPermissionsContext(DbContextOptions<SaasPermissionsContext> options) : DbContext(options)
{
    public DbSet<SaasPermission> SaasPermissions { get; set; }
    public DbSet<TenantPermission> TenantPermissions { get; set; }
    public DbSet<UserPermission> UserPermissions { get; set; }
    public DbSet<TenantInvitation> TenantInvitations { get; set; }
    public DbSet<TenantMember> TenantMembers { get; set; }


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        new UserPermissionEntityTypeConfiguration().Configure(modelBuilder.Entity<UserPermission>());
        new TenantPermissionEntityTypeConfiguration().Configure(modelBuilder.Entity<TenantPermission>());
        new SaasPermissionEntityTypeConfiguration().Configure(modelBuilder.Entity<SaasPermission>());

        modelBuilder.Entity<TenantInvitation>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.HasIndex(x => new { x.TenantId, x.Email });
        });

        modelBuilder.Entity<TenantMember>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
        });
    }
}
