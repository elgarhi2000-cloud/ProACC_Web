using Microsoft.EntityFrameworkCore;

namespace BlazorApp1.Data.ProAcc;

public class ProAccDbContext(DbContextOptions<ProAccDbContext> options) : DbContext(options)
{
    public DbSet<AccEntity> Accounts => Set<AccEntity>();
    public DbSet<CardEntity> Cards => Set<CardEntity>();
    public DbSet<SourceEntity> Sources => Set<SourceEntity>();
    public DbSet<MethodEntity> Methods => Set<MethodEntity>();
    public DbSet<BankEntity> Banks => Set<BankEntity>();
    public DbSet<Center1Entity> Centers1 => Set<Center1Entity>();
    public DbSet<Center2Entity> Centers2 => Set<Center2Entity>();
    public DbSet<Center3Entity> Centers3 => Set<Center3Entity>();
    public DbSet<GlEntity> GLEntries => Set<GlEntity>();
    public DbSet<TransEntity> Transactions => Set<TransEntity>();
    public DbSet<CompanyEntity> Companies => Set<CompanyEntity>();
    public DbSet<PeriodEntity> Periods => Set<PeriodEntity>();
    public DbSet<VatEntity> VatRates => Set<VatEntity>();
    public DbSet<DirectionEntity> Directions => Set<DirectionEntity>();
    public DbSet<Level0Entity> Level0s => Set<Level0Entity>();
    public DbSet<Level1Entity> Level1s => Set<Level1Entity>();
    public DbSet<TypeLookupEntity> Types => Set<TypeLookupEntity>();
    public DbSet<AccTypeEntity> AccTypes => Set<AccTypeEntity>();
    public DbSet<CategEntity> Categories => Set<CategEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AccEntity>(entity =>
        {
            entity.ToTable("ACC");
            entity.HasKey(e => e.ACCID);
            entity.Property(e => e.ACCID).HasColumnName("ACCID");
            entity.Property(e => e.Name).HasColumnName("ACC").HasMaxLength(100);
            entity.Property(e => e.CategID).HasColumnName("CategID");
            entity.Property(e => e.Group1).HasColumnName("Group1");
            entity.Property(e => e.Group2).HasColumnName("Group2");
            entity.Property(e => e.Active).HasColumnName("Active");
            entity.Property(e => e.Chart).HasColumnName("Chart");
        });

        modelBuilder.Entity<CardEntity>(entity =>
        {
            entity.ToTable("CARD");
            entity.HasKey(e => e.CardID);
            entity.Property(e => e.CardID).HasColumnName("CardID");
            entity.Property(e => e.CardName).HasColumnName("CardName").HasMaxLength(50);
            entity.Property(e => e.Title).HasColumnName("Title").HasMaxLength(50);
            entity.Property(e => e.CardNum).HasColumnName("CardNum");
            entity.Property(e => e.Active).HasColumnName("Active");
        });

        modelBuilder.Entity<SourceEntity>(entity =>
        {
            entity.ToTable("SOURCE");
            entity.HasKey(e => e.SourceID);
            entity.Property(e => e.SourceID).HasColumnName("SourceID");
            entity.Property(e => e.SourceCode).HasColumnName("SourceCode").HasMaxLength(50);
            entity.Property(e => e.Source).HasColumnName("Source").HasMaxLength(50);
        });

        modelBuilder.Entity<MethodEntity>(entity =>
        {
            entity.ToTable("Method");
            entity.HasKey(e => e.MethodID);
            entity.Property(e => e.MethodID).HasColumnName("MethodID");
            entity.Property(e => e.MethodName).HasColumnName("Method").HasMaxLength(50);
        });

        modelBuilder.Entity<BankEntity>(entity =>
        {
            entity.ToTable("Bank");
            entity.HasKey(e => e.BankID);
            entity.Property(e => e.BankID).HasColumnName("BankID");
            entity.Property(e => e.BankName).HasColumnName("BankName").HasMaxLength(100);
        });

        modelBuilder.Entity<GlEntity>(entity =>
        {
            entity.ToTable("GL");
            entity.HasKey(e => e.GLID);
            entity.Property(e => e.GLID).HasColumnName("GLID");
            entity.Property(e => e.GLSN).HasColumnName("GLSN");
            entity.Property(e => e.GLDate).HasColumnName("GLDate");
            entity.Property(e => e.PeriodID).HasColumnName("PeriodID");
            entity.Property(e => e.SourceID).HasColumnName("SourceID");
            entity.Property(e => e.GLDesc).HasColumnName("GLDesc");
            entity.Property(e => e.GLRef).HasColumnName("GLRef").HasMaxLength(50);
            entity.Property(e => e.GLACC).HasColumnName("GLACC");
            entity.Property(e => e.CardID).HasColumnName("CardID");
            entity.Property(e => e.MethodID).HasColumnName("Method");
            entity.Property(e => e.BankName).HasColumnName("BankName").HasMaxLength(50);
            entity.Property(e => e.SanadID).HasColumnName("SanadID");
            entity.Property(e => e.Archive).HasColumnName("Archive");
            entity.Property(e => e.Info).HasColumnName("Info").HasMaxLength(50);
            entity.Property(e => e.Center1).HasColumnName("Center1");
            entity.Property(e => e.Center2).HasColumnName("Center2");

            entity.HasOne(e => e.Source)
                .WithMany(s => s.GlEntries)
                .HasForeignKey(e => e.SourceID)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(e => e.Card)
                .WithMany(c => c.GlEntries)
                .HasForeignKey(e => e.CardID)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(e => e.Method)
                .WithMany(m => m.GlEntries)
                .HasForeignKey(e => e.MethodID)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(e => e.Period)
                .WithMany(p => p.GlEntries)
                .HasForeignKey(e => e.PeriodID)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(e => e.Center1Lookup)
                .WithMany(c => c.GlEntries)
                .HasForeignKey(e => e.Center1)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(e => e.Center2Lookup)
                .WithMany(c => c.GlEntries)
                .HasForeignKey(e => e.Center2)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<TransEntity>(entity =>
        {
            entity.ToTable("TRANS");
            entity.HasKey(e => e.TransID);
            entity.Property(e => e.TransID).HasColumnName("TransID");
            entity.Property(e => e.GLID).HasColumnName("GLID");
            entity.Property(e => e.ACCID).HasColumnName("ACCID");
            entity.Property(e => e.DR).HasColumnName("DR");
            entity.Property(e => e.CR).HasColumnName("CR");
            entity.Property(e => e.TransDesc).HasColumnName("TransDesc");
            entity.Property(e => e.ItemCode).HasColumnName("ItemCode");
            entity.Property(e => e.TotalAmount).HasColumnName("TotalAmount");
            entity.Property(e => e.Amount).HasColumnName("Amount");
            entity.Property(e => e.TaxRate).HasColumnName("TaxRate");
            entity.Property(e => e.TaxAmount).HasColumnName("TaxAmount");
            entity.Property(e => e.Qty).HasColumnName("Qty");
            entity.Property(e => e.Unit).HasColumnName("Unit");
            entity.Property(e => e.UnitPrice).HasColumnName("UnitPrice");
            entity.Property(e => e.Discount).HasColumnName("Discount");
            entity.Property(e => e.DocRef).HasColumnName("DocRef").HasMaxLength(50);
            entity.Property(e => e.DocDate).HasColumnName("DocDate");
            entity.Property(e => e.Center3).HasColumnName("Center3");

            entity.HasOne(e => e.GL)
                .WithMany(g => g.Transactions)
                .HasForeignKey(e => e.GLID)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(e => e.Account)
                .WithMany(a => a.Transactions)
                .HasForeignKey(e => e.ACCID)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(e => e.Center3Lookup)
                .WithMany(c => c.Transactions)
                .HasForeignKey(e => e.Center3)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Center1Entity>(entity =>
        {
            entity.ToTable("CENTER1");
            entity.HasKey(e => e.CenterID);
            entity.Property(e => e.CenterID).HasColumnName("CenterID");
            entity.Property(e => e.CenterName).HasColumnName("Center1Name").HasMaxLength(100);
        });

        modelBuilder.Entity<Center2Entity>(entity =>
        {
            entity.ToTable("CENTER2");
            entity.HasKey(e => e.CenterID);
            entity.Property(e => e.CenterID).HasColumnName("CenterID");
            entity.Property(e => e.CenterName).HasColumnName("Center2Name").HasMaxLength(100);
        });

        modelBuilder.Entity<Center3Entity>(entity =>
        {
            entity.ToTable("CENTER3");
            entity.HasKey(e => e.CenterID);
            entity.Property(e => e.CenterID).HasColumnName("CenterID");
            entity.Property(e => e.CenterName).HasColumnName("Center3Name").HasMaxLength(100);
        });

        modelBuilder.Entity<CompanyEntity>(entity =>
        {
            entity.ToTable("COMPANY");
            entity.HasKey(e => e.CompID);
            entity.Property(e => e.CompID).HasColumnName("CompID");
            entity.Property(e => e.Comp).HasColumnName("Comp").HasMaxLength(50);
            entity.Property(e => e.CompTel).HasColumnName("CompTel").HasMaxLength(50);
            entity.Property(e => e.CompAddress).HasColumnName("CompAddress").HasMaxLength(150);
            entity.Property(e => e.VAT).HasColumnName("VAT").HasMaxLength(50);
            entity.Property(e => e.CR).HasColumnName("CR").HasMaxLength(50);
            entity.Property(e => e.Pic).HasColumnName("Pic").HasColumnType("image");
            entity.Property(e => e.DBPath).HasColumnName("DBPath").HasMaxLength(150);
            entity.Property(e => e.ReportsPath).HasColumnName("ReportsPath").HasMaxLength(150);
            entity.Property(e => e.AttachPath).HasColumnName("AttachPath").HasMaxLength(150);
            entity.Property(e => e.BackupPath).HasColumnName("BackupPath").HasMaxLength(150);
        });

        modelBuilder.Entity<PeriodEntity>(entity =>
        {
            entity.ToTable("Periods");
            entity.HasKey(e => e.PeriodID);
            entity.Property(e => e.PeriodID).HasColumnName("PeriodID");
            entity.Property(e => e.PeriodName).HasColumnName("PeriodName").HasMaxLength(50);
            entity.Property(e => e.StartDate).HasColumnName("StartDate");
            entity.Property(e => e.EndDate).HasColumnName("EndDate");
            entity.Property(e => e.PeriodClose).HasColumnName("PeriodClose");
            entity.Property(e => e.GLID).HasColumnName("GLID");
            entity.Property(e => e.RCID).HasColumnName("RCID");
            entity.Property(e => e.PMID).HasColumnName("PMID");
            entity.Property(e => e.SalesID).HasColumnName("SalesID");
            entity.Property(e => e.PurchID).HasColumnName("PurchID");
            entity.Property(e => e.PayrollID).HasColumnName("PayrollID");
        });

        modelBuilder.Entity<VatEntity>(entity =>
        {
            entity.ToTable("VAT");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("Id");
            entity.Property(e => e.TaxRate).HasColumnName("TaxRate");
            entity.Property(e => e.SalesTaxACC).HasColumnName("SalesTaxACC");
            entity.Property(e => e.PurchTaxACC).HasColumnName("PurchTaxACC");
        });

        modelBuilder.Entity<DirectionEntity>(entity =>
        {
            entity.ToTable("Direction");
            entity.HasKey(e => e.DirectionID);
            entity.Property(e => e.DirectionID).HasColumnName("DirectionID");
            entity.Property(e => e.DirectionName).HasColumnName("Direction").HasMaxLength(50);
        });

        modelBuilder.Entity<Level0Entity>(entity =>
        {
            entity.ToTable("Level0");
            entity.HasKey(e => e.Level0ID);
            entity.Property(e => e.Level0ID).HasColumnName("Level0ID");
            entity.Property(e => e.Level0Name).HasColumnName("Level0").HasMaxLength(100);
            entity.Property(e => e.DirectionID).HasColumnName("DirectionID");
        });

        modelBuilder.Entity<Level1Entity>(entity =>
        {
            entity.ToTable("Level1");
            entity.HasKey(e => e.Level1ID);
            entity.Property(e => e.Level1ID).HasColumnName("Level1ID");
            entity.Property(e => e.Level1Name).HasColumnName("Level1").HasMaxLength(100);
            entity.Property(e => e.Level0ID).HasColumnName("Level0ID");
        });

        modelBuilder.Entity<TypeLookupEntity>(entity =>
        {
            entity.ToTable("Type");
            entity.HasKey(e => e.TypeID);
            entity.Property(e => e.TypeID).HasColumnName("TypeID");
            entity.Property(e => e.TypeName).HasColumnName("Type").HasMaxLength(100);
            entity.Property(e => e.Level1ID).HasColumnName("Level1ID");
        });

        modelBuilder.Entity<AccTypeEntity>(entity =>
        {
            entity.ToTable("ACCType");
            entity.HasKey(e => e.ACCTypeID);
            entity.Property(e => e.ACCTypeID).HasColumnName("ACCTypeID");
            entity.Property(e => e.ACCTypeName).HasColumnName("ACCType").HasMaxLength(100);
            entity.Property(e => e.TypeID).HasColumnName("TypeID");
        });

        modelBuilder.Entity<CategEntity>(entity =>
        {
            entity.ToTable("CATEG");
            entity.HasKey(e => e.CategID);
            entity.Property(e => e.CategID).HasColumnName("CategID");
            entity.Property(e => e.CategName).HasColumnName("Categ").HasMaxLength(100);
            entity.Property(e => e.AccTypeID).HasColumnName("AccTypeID");
        });
    }
}

public class AccEntity
{
    public long ACCID { get; set; }
    public string? Name { get; set; }
    public int? CategID { get; set; }
    public int? Group1 { get; set; }
    public int? Group2 { get; set; }
    public bool? Active { get; set; }
    public bool? Chart { get; set; }
    public ICollection<TransEntity> Transactions { get; set; } = new List<TransEntity>();
}

public class CardEntity
{
    public string? Address1 { get; set; }
    public string? Address2 { get; set; }
    public string? VAT { get; set; }
    public int CardID { get; set; }
    public int? CardNum { get; set; }
    public string? Title { get; set; }
    public string? CardName { get; set; }
    public bool? Active { get; set; }
    public ICollection<GlEntity> GlEntries { get; set; } = new List<GlEntity>();
}

public class SourceEntity
{
    public int SourceID { get; set; }
    public string? SourceCode { get; set; }
    public string? Source { get; set; }
    public ICollection<GlEntity> GlEntries { get; set; } = new List<GlEntity>();
}

public class MethodEntity
{
    public int MethodID { get; set; }
    public string? MethodName { get; set; }
    public ICollection<GlEntity> GlEntries { get; set; } = new List<GlEntity>();
}

public class Center1Entity
{
    public int CenterID { get; set; }
    public string? CenterName { get; set; }
    public ICollection<GlEntity> GlEntries { get; set; } = new List<GlEntity>();
}

public class BankEntity
{
    public int BankID { get; set; }
    public string? BankName { get; set; }
}

public class Center2Entity
{
    public int CenterID { get; set; }
    public string? CenterName { get; set; }
    public ICollection<GlEntity> GlEntries { get; set; } = new List<GlEntity>();
}

public class Center3Entity
{
    public int CenterID { get; set; }
    public string? CenterName { get; set; }
    public ICollection<TransEntity> Transactions { get; set; } = new List<TransEntity>();
}

public class GlEntity
{
    public int GLID { get; set; }
    public int? GLSN { get; set; }
    public DateTime? GLDate { get; set; }
    public int? PeriodID { get; set; }
    public int? SourceID { get; set; }
    public string? GLDesc { get; set; }
    public string? GLRef { get; set; }
    public long? GLACC { get; set; }
    public int? CardID { get; set; }
    public int? MethodID { get; set; }
    public string? BankName { get; set; }
    public int? SanadID { get; set; }
    public bool? Archive { get; set; }
    public string? Info { get; set; }
    public int? Center1 { get; set; }
    public int? Center2 { get; set; }

    public SourceEntity? Source { get; set; }
    public CardEntity? Card { get; set; }
    public MethodEntity? Method { get; set; }
    public PeriodEntity? Period { get; set; }
    public ICollection<TransEntity> Transactions { get; set; } = new List<TransEntity>();
    public Center1Entity? Center1Lookup { get; set; }
    public Center2Entity? Center2Lookup { get; set; }
}

public class TransEntity
{
    public int TransID { get; set; }
    public int? GLID { get; set; }
    public long? ACCID { get; set; }
    public decimal? DR { get; set; }
    public decimal? CR { get; set; }
    public string? TransDesc { get; set; }
    public string? ItemCode { get; set; }
    public decimal? Amount { get; set; }
    public decimal? TaxRate { get; set; }
    public decimal? TaxAmount { get; set; }
    public decimal? TotalAmount { get; set; }
    public decimal? Qty { get; set; }
    public string? Unit { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? Discount { get; set; }
    public string? DocRef { get; set; }
    public DateTime? DocDate { get; set; }
    public int? Center3 { get; set; }

    public GlEntity? GL { get; set; }
    public AccEntity? Account { get; set; }
    public Center3Entity? Center3Lookup { get; set; }
}

public class CompanyEntity
{
    public int CompID { get; set; }
    public string? Comp { get; set; }
    public string? CompTel { get; set; }
    public string? CompAddress { get; set; }
    public string? VAT { get; set; }
    public string? CR { get; set; }
    public byte[]? Pic { get; set; }
    public string? DBPath { get; set; }
    public string? ReportsPath { get; set; }
    public string? AttachPath { get; set; }
    public string? BackupPath { get; set; }
}

public class PeriodEntity
{
    public int PeriodID { get; set; }
    public string? PeriodName { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool? PeriodClose { get; set; }
    public int? GLID { get; set; }
    public int? RCID { get; set; }
    public int? PMID { get; set; }
    public int? SalesID { get; set; }
    public int? PurchID { get; set; }
    public int? PayrollID { get; set; }
    public ICollection<GlEntity> GlEntries { get; set; } = new List<GlEntity>();
}

public class VatEntity
{
    public int Id { get; set; }
    public decimal? TaxRate { get; set; }
    public long? SalesTaxACC { get; set; }
    public long? PurchTaxACC { get; set; }
}

public class DirectionEntity
{
    public int DirectionID { get; set; }
    public string? DirectionName { get; set; }
}

public class Level0Entity
{
    public int Level0ID { get; set; }
    public string? Level0Name { get; set; }
    public int? DirectionID { get; set; }
}

public class Level1Entity
{
    public int Level1ID { get; set; }
    public string? Level1Name { get; set; }
    public int? Level0ID { get; set; }
}

public class TypeLookupEntity
{
    public int TypeID { get; set; }
    public string? TypeName { get; set; }
    public int? Level1ID { get; set; }
}

public class AccTypeEntity
{
    public int ACCTypeID { get; set; }
    public string? ACCTypeName { get; set; }
    public int? TypeID { get; set; }
}

public class CategEntity
{
    public int CategID { get; set; }
    public string? CategName { get; set; }
    public int? AccTypeID { get; set; }
}
