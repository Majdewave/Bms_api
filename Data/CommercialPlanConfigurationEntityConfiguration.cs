using Clienta.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clienta.Api.Data;

public sealed class CommercialPlanConfigurationEntityConfiguration : IEntityTypeConfiguration<CommercialPlanConfiguration>
{
    public void Configure(EntityTypeBuilder<CommercialPlanConfiguration> builder)
    {
        builder.ToTable("CommercialPlanConfigurations", table =>
        {
            table.HasCheckConstraint(
                "CK_CommercialPlanConfigurations_MonthlyPrice_NonNegative",
                "\"MonthlyPrice\" IS NULL OR \"MonthlyPrice\" >= 0");
            table.HasCheckConstraint(
                "CK_CommercialPlanConfigurations_AnnualPrice_NonNegative",
                "\"AnnualPrice\" IS NULL OR \"AnnualPrice\" >= 0");
            table.HasCheckConstraint(
                "CK_CommercialPlanConfigurations_DisplayOrder_NonNegative",
                "\"DisplayOrder\" >= 0");
        });

        builder.HasKey(configuration => configuration.Id);
        builder.Property(configuration => configuration.Id).HasColumnType("uuid");
        builder.Property(configuration => configuration.ProductType).HasColumnType("integer");
        builder.Property(configuration => configuration.PlanType).HasColumnType("integer");
        builder.Property(configuration => configuration.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(configuration => configuration.Description).HasColumnType("text");
        builder.Property(configuration => configuration.MonthlyPrice).HasPrecision(18, 2);
        builder.Property(configuration => configuration.AnnualPrice).HasPrecision(18, 2);
        builder.Property(configuration => configuration.Enabled).IsRequired();
        builder.Property(configuration => configuration.AvailableForPurchase).IsRequired();
        builder.Property(configuration => configuration.DisplayOrder).IsRequired();
        builder.Property(configuration => configuration.CreatedAt).HasColumnType("timestamp with time zone");
        builder.Property(configuration => configuration.UpdatedAt).HasColumnType("timestamp with time zone");
        builder.HasIndex(configuration => new { configuration.ProductType, configuration.PlanType })
            .IsUnique()
            .HasDatabaseName("IX_CommercialPlanConfigurations_ProductType_PlanType");
    }
}