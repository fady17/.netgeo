// src/AutomotiveServices.Api/Data/AppDbContext.cs
using AutomotiveServices.Api.Models;
using Microsoft.EntityFrameworkCore;
// NetTopologySuite.Geometries types are used in the models

namespace AutomotiveServices.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Shop> Shops { get; set; } = null!;
    public DbSet<City> Cities { get; set; } = null!; // Keep for now, might become legacy or used for a different purpose

    // --- NEW DBSETS FOR GEOGRAPHICAL HIERARCHY AND OPERATIONAL ZONES ---
    public DbSet<AdministrativeBoundary> AdministrativeBoundaries { get; set; } = null!;
    public DbSet<OperationalArea> OperationalAreas { get; set; } = null!;
    // --- END NEW DBSETS ---

     // --- NEW DBSET FOR AGGREGATED STATS ---
    public DbSet<AdminAreaShopStats> AdminAreaShopStats { get; set; } = null!;
    // --- END NEW DBSET ---

    
    public DbSet<CityWithCoordinatesView> CityWithCoordinates { get; set; } = null!;
    public DbSet<ShopDetailsView> ShopDetailsView { get; set; } = null!;
    
    public DbSet<GlobalServiceDefinition> GlobalServiceDefinitions { get; set; } = null!;
    public DbSet<ShopService> ShopServices { get; set; } = null!;
    
    public DbSet<AnonymousCartItem> AnonymousCartItems { get; set; } = null!;
    public DbSet<AnonymousUserPreference> AnonymousUserPreferences { get; set; } = null!;
    public DbSet<UserCartItem> UserCartItems { get; set; } = null!;
    public DbSet<Booking> Bookings { get; set; } = null!;
    public DbSet<BookingItem> BookingItems { get; set; } = null!;
    public DbSet<UserPreference> UserPreferences { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasPostgresExtension("postgis");

        // --- AdministrativeBoundary Configuration ---
        modelBuilder.Entity<AdministrativeBoundary>(entity =>
        {
            entity.HasKey(ab => ab.Id);
            entity.Property(ab => ab.Id).ValueGeneratedOnAdd();

            entity.Property(ab => ab.NameEn).IsRequired().HasMaxLength(150);
            entity.Property(ab => ab.NameAr).IsRequired().HasMaxLength(150);
            entity.Property(ab => ab.AdminLevel).IsRequired();
            entity.Property(ab => ab.CountryCode).HasMaxLength(10);
            entity.Property(ab => ab.OfficialCode).HasMaxLength(50);

            entity.Property(ab => ab.Boundary).HasColumnType("geography(MultiPolygon, 4326)").IsRequired(false);
            entity.Property(ab => ab.SimplifiedBoundary).HasColumnType("geography(MultiPolygon, 4326)").IsRequired(false);
            entity.Property(ab => ab.Centroid).HasColumnType("geography(Point, 4326)").IsRequired(false);

            // Self-referencing hierarchy
            entity.HasOne(ab => ab.Parent)
                  .WithMany(p => p.Children)
                  .HasForeignKey(ab => ab.ParentId)
                  .IsRequired(false) // ParentId is nullable for top-level (e.g., countries)
                  .OnDelete(DeleteBehavior.Restrict); // Or SetNull if preferred

            entity.HasIndex(ab => ab.AdminLevel);
            entity.HasIndex(ab => ab.CountryCode);
            entity.HasIndex(ab => ab.ParentId);
            entity.HasIndex(ab => ab.IsActive);
            entity.HasIndex(ab => ab.Boundary).HasMethod("GIST");
            entity.HasIndex(ab => ab.SimplifiedBoundary).HasMethod("GIST");
            entity.HasIndex(ab => ab.Centroid).HasMethod("GIST");
        });
        // --- END AdministrativeBoundary Configuration ---

        // --- OperationalArea Configuration ---
        modelBuilder.Entity<OperationalArea>(entity =>
        {
            entity.HasKey(oa => oa.Id);
            entity.Property(oa => oa.Id).ValueGeneratedOnAdd();

            entity.Property(oa => oa.NameEn).IsRequired().HasMaxLength(150);
            entity.Property(oa => oa.NameAr).IsRequired().HasMaxLength(150);
            entity.Property(oa => oa.Slug).IsRequired().HasMaxLength(150);
            entity.HasIndex(oa => oa.Slug).IsUnique();

            entity.Property(oa => oa.DisplayLevel).HasMaxLength(50);
            entity.Property(oa => oa.CentroidLatitude).IsRequired();
            entity.Property(oa => oa.CentroidLongitude).IsRequired();
            entity.Property(oa => oa.GeometrySource).IsRequired();

            entity.Property(oa => oa.CustomBoundary).HasColumnType("geography(MultiPolygon, 4326)").IsRequired(false);
            entity.Property(oa => oa.CustomSimplifiedBoundary).HasColumnType("geography(MultiPolygon, 4326)").IsRequired(false);

            // Relationship to AdministrativeBoundary (optional context link)
            entity.HasOne(oa => oa.PrimaryAdministrativeBoundary)
                  .WithMany() // An AdminBoundary can be primary for multiple OperationalAreas conceptually
                  .HasForeignKey(oa => oa.PrimaryAdministrativeBoundaryId)
                  .IsRequired(false)
                  .OnDelete(DeleteBehavior.SetNull); // If admin boundary is deleted, nullify the link

            entity.HasIndex(oa => oa.IsActive);
            entity.HasIndex(oa => oa.PrimaryAdministrativeBoundaryId);
            entity.HasIndex(oa => oa.CustomBoundary).HasMethod("GIST");
            entity.HasIndex(oa => oa.CustomSimplifiedBoundary).HasMethod("GIST");
            // Consider an index on (CentroidLatitude, CentroidLongitude) if you ever query by these,
            // though GIST index on a Point geometry constructed from them would be better.
        });
        // --- END OperationalArea Configuration ---

        // City Configuration (Original - Review if this table remains, is repurposed, or removed)
        // For now, assuming it might still exist or be phased out.
        // If `Shop` no longer links to `City`, the `City.Shops` collection needs to be removed or updated.
        modelBuilder.Entity<City>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Id).ValueGeneratedOnAdd();
            entity.Property(c => c.NameEn).IsRequired().HasMaxLength(100);
            entity.Property(c => c.NameAr).IsRequired().HasMaxLength(100);
            entity.Property(c => c.Slug).IsRequired().HasMaxLength(100);
            entity.Property(c => c.StateProvince).HasMaxLength(100);
            entity.Property(c => c.Country).IsRequired().HasMaxLength(100);
            entity.Property(c => c.Location).HasColumnType("geography(Point, 4326)").IsRequired();
            
            // IF City.Shops relationship is removed because Shop now links to OperationalArea:
            // entity.HasMany(c => c.Shops) 
            //       .WithOne(s => s.City) 
            //       .HasForeignKey(s => s.CityId) // This FK would be removed from Shop model
            //       .OnDelete(DeleteBehavior.Restrict);
            // INSTEAD, if City is kept for some other purpose, its relationship to Shop changes.
            // For now, I'll leave the original City config but highlight that Shop's FK changes.

            entity.HasIndex(c => c.Location).HasMethod("GIST").HasDatabaseName("IX_Cities_Location");
            entity.HasIndex(c => c.Slug).IsUnique().HasDatabaseName("IX_Cities_Slug");
            entity.HasIndex(c => c.IsActive).HasDatabaseName("IX_Cities_IsActive");
        });

        // Configure Shop entity
        modelBuilder.Entity<Shop>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Id).ValueGeneratedOnAdd();

            entity.Property(s => s.Location).HasColumnType("geography(Point, 4326)").IsRequired();
            entity.HasIndex(s => s.Location).HasMethod("GIST").HasDatabaseName("IX_Shops_Location");

            // --- UPDATED Foreign Key & Relationship to OperationalArea ---
            entity.HasOne(s => s.OperationalArea)          // Navigation property in Shop
                  .WithMany(oa => oa.Shops)              // Navigation property in OperationalArea
                  .HasForeignKey(s => s.OperationalAreaId) // Foreign key in Shop
                  .OnDelete(DeleteBehavior.Restrict)       // Or Cascade, depending on business rule
                  .IsRequired();                          // Shop must belong to an OperationalArea
            // --- END UPDATED ---
            
            // REMOVE OR UPDATE OLD City Relationship:
            // entity.HasOne(s => s.City)
            //       .WithMany(c => c.Shops) // This would now error if City.Shops still exists but Shop.CityId doesn't
            //       .HasForeignKey(s => s.CityId) // This FK is removed from Shop model
            //       .OnDelete(DeleteBehavior.Restrict)
            //       .IsRequired(); 

            entity.HasMany(s => s.ShopServices)
                  .WithOne(ss => ss.Shop)
                  .HasForeignKey(ss => ss.ShopId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(s => s.IsDeleted).HasDatabaseName("IX_Shops_IsDeleted");
            entity.HasIndex(s => s.Category).HasDatabaseName("IX_Shops_Category");
            
            // Index on OperationalAreaId (NEW, replaces CityId index)
            entity.HasIndex(s => s.OperationalAreaId).HasDatabaseName("IX_Shops_OperationalAreaId");

            // Compound index for main listing queries (UPDATED)
            entity.HasIndex(s => new { s.OperationalAreaId, s.Category, s.IsDeleted })
                  .HasDatabaseName("IX_Shops_OperationalAreaId_Category_IsDeleted");

            // Unique constraint for shop slug within an OperationalArea (UPDATED)
            entity.HasIndex(s => new { s.OperationalAreaId, s.Slug })
                  .IsUnique()
                  .HasFilter("\"Slug\" IS NOT NULL") 
                  .HasDatabaseName("IX_Shops_OperationalAreaId_Slug");

            entity.Property(s => s.NameEn).IsRequired().HasMaxLength(200);
            entity.Property(s => s.NameAr).IsRequired().HasMaxLength(200);
            entity.Property(s => s.DescriptionEn).HasMaxLength(1000);
            entity.Property(s => s.DescriptionAr).HasMaxLength(1000);
            entity.Property(s => s.Address).IsRequired().HasMaxLength(500);
            entity.Property(s => s.PhoneNumber).HasMaxLength(20);
            entity.Property(s => s.ServicesOffered).HasMaxLength(1000);
            entity.Property(s => s.OpeningHours).HasMaxLength(500);
            entity.Property(s => s.LogoUrl).HasMaxLength(500);
            entity.Property(s => s.IsDeleted).HasDefaultValue(false);
            entity.HasQueryFilter(s => !s.IsDeleted);
        });

        // --- NEW: GlobalServiceDefinition Configuration ---
        modelBuilder.Entity<GlobalServiceDefinition>(entity =>
        {
            entity.HasKey(gsd => gsd.GlobalServiceId);
            // Use this if GlobalServiceId is Guid:
            // entity.Property(gsd => gsd.GlobalServiceId).ValueGeneratedOnAdd(); 

            entity.HasIndex(gsd => gsd.ServiceCode).IsUnique();
            entity.Property(gsd => gsd.ServiceCode).IsRequired().HasMaxLength(100);
            entity.Property(gsd => gsd.DefaultNameEn).IsRequired().HasMaxLength(200);
            entity.Property(gsd => gsd.DefaultNameAr).IsRequired().HasMaxLength(200);
            entity.Property(gsd => gsd.DefaultDescriptionEn).HasMaxLength(1000);
            entity.Property(gsd => gsd.DefaultDescriptionAr).HasMaxLength(1000);
            entity.Property(gsd => gsd.DefaultIconUrl).HasMaxLength(500);
            entity.Property(gsd => gsd.Category).IsRequired(); // Link to ShopCategory enum
        });

        // --- NEW: ShopService Configuration ---
        modelBuilder.Entity<ShopService>(entity =>
        {
            entity.HasKey(ss => ss.ShopServiceId);
            entity.Property(ss => ss.ShopServiceId).ValueGeneratedOnAdd();

            entity.Property(ss => ss.Price).HasColumnType("decimal(18,2)").IsRequired();
            entity.Property(ss => ss.EffectiveNameEn).IsRequired().HasMaxLength(200);
            entity.Property(ss => ss.EffectiveNameAr).IsRequired().HasMaxLength(200);
            entity.Property(ss => ss.CustomServiceNameEn).HasMaxLength(200);
            entity.Property(ss => ss.CustomServiceNameAr).HasMaxLength(200);
            entity.Property(ss => ss.ShopSpecificDescriptionEn).HasMaxLength(1000);
            entity.Property(ss => ss.ShopSpecificDescriptionAr).HasMaxLength(1000);
            entity.Property(ss => ss.ShopSpecificIconUrl).HasMaxLength(500);


            // Relationship to GlobalServiceDefinition (optional link)
            entity.HasOne(ss => ss.GlobalServiceDefinition)
                  .WithMany(gsd => gsd.ShopServices) // Global definition can be used by many shop services
                  .HasForeignKey(ss => ss.GlobalServiceId)
                  .IsRequired(false) // GlobalServiceId is nullable
                  .OnDelete(DeleteBehavior.SetNull); // If global def is deleted, don't delete shop service, just nullify link

            entity.HasIndex(ss => ss.ShopId);
            entity.HasIndex(ss => new { ss.ShopId, ss.IsOfferedByShop });
            entity.HasIndex(ss => ss.GlobalServiceId).IsUnique(false); // Not unique, many shops can use same global
            entity.HasQueryFilter(ss => !ss.Shop.IsDeleted);
        });

        // --- NEW: AnonymousCartItem Configuration ---
        modelBuilder.Entity<AnonymousCartItem>(entity =>
        {
            entity.HasKey(aci => aci.AnonymousCartItemId);
            entity.Property(aci => aci.AnonymousUserId).IsRequired().HasMaxLength(100);
            entity.Property(aci => aci.PriceAtAddition).HasColumnType("decimal(18,2)");

            entity.HasIndex(aci => aci.AnonymousUserId).HasDatabaseName("IX_AnonymousCartItems_AnonymousUserId");
            // Optional: Compound index if you often query by anon_id and shop_service_id
            entity.HasIndex(aci => new { aci.AnonymousUserId, aci.ShopId, aci.ShopServiceId })
                  .IsUnique() // A user should only have one entry for a specific service from a specific shop; quantity handles multiples.
                  .HasDatabaseName("IX_AnonymousCartItems_AnonUser_Shop_Service");
        });

        modelBuilder.Entity<AnonymousUserPreference>(entity =>
        {
            entity.HasKey(ap => ap.AnonymousUserPreferenceId);
            entity.Property(ap => ap.AnonymousUserPreferenceId).ValueGeneratedOnAdd(); // If using Guid PK

            entity.Property(ap => ap.AnonymousUserId)
                .IsRequired()
                .HasMaxLength(100);

            // Unique index on AnonymousUserId to ensure one preference record per anonymous user
            entity.HasIndex(ap => ap.AnonymousUserId)
                .IsUnique()
                .HasDatabaseName("IX_AnonymousUserPreferences_AnonymousUserId");

            entity.Property(ap => ap.LastKnownLatitude).IsRequired(false);
            entity.Property(ap => ap.LastKnownLongitude).IsRequired(false);
            entity.Property(ap => ap.LastKnownLocationAccuracy).IsRequired(false);
            entity.Property(ap => ap.LocationSource).HasMaxLength(50).IsRequired(false);

            entity.Property(ap => ap.OtherPreferencesJson)
                .HasColumnType("jsonb") // Specific to PostgreSQL for efficient JSON storage & querying
                .IsRequired(false);
        });

        // --- NEW: UserCartItem Configuration ---
        modelBuilder.Entity<UserCartItem>(entity =>
        {
            entity.HasKey(uci => uci.UserCartItemId);
            entity.Property(uci => uci.UserId).IsRequired().HasMaxLength(100); // Matches sub claim length
            entity.Property(uci => uci.PriceAtAddition).HasColumnType("decimal(18,2)");
            entity.Property(uci => uci.ShopNameSnapshotEn).HasMaxLength(200);
            entity.Property(uci => uci.ShopNameSnapshotAr).HasMaxLength(200);
            entity.Property(uci => uci.ServiceImageUrlSnapshot).HasMaxLength(500);


            entity.HasIndex(uci => uci.UserId).HasDatabaseName("IX_UserCartItems_UserId");
            // A user should only have one entry for a specific service from a specific shop;
            // quantity handles multiples. This ensures no duplicate line items for the same service.
            entity.HasIndex(uci => new { uci.UserId, uci.ShopId, uci.ShopServiceId })
                  .IsUnique()
                  .HasDatabaseName("IX_UserCartItems_User_Shop_Service");
        });
        // --- END UserCartItem Configuration ---

        // --- NEW: Booking Configuration ---
        modelBuilder.Entity<Booking>(entity =>
        {
            entity.HasKey(b => b.BookingId);
            entity.Property(b => b.UserId).IsRequired().HasMaxLength(100);
            entity.Property(b => b.TotalAmountAtBooking).HasColumnType("decimal(18,2)");
            entity.Property(b => b.Status).IsRequired(); // Enum converted to int by default

            // Relationship: Booking to Shop (One Booking belongs to one Shop)
            entity.HasOne(b => b.Shop)
                  .WithMany() // Assuming Shop doesn't need a direct ICollection<Booking>
                              // If Shop *does* have ICollection<Booking> Bookings, then use .WithMany(s => s.Bookings)
                  .HasForeignKey(b => b.ShopId)
                  .OnDelete(DeleteBehavior.Restrict); // Don't delete bookings if shop is deleted; might archive shop instead

            // Relationship: Booking to BookingItems (One Booking has many BookingItems)
            entity.HasMany(b => b.BookingItems)
                  .WithOne(bi => bi.Booking)
                  .HasForeignKey(bi => bi.BookingId)
                  .OnDelete(DeleteBehavior.Cascade); // Deleting a booking deletes its items

            entity.HasIndex(b => b.UserId).HasDatabaseName("IX_Bookings_UserId");
            entity.HasIndex(b => b.ShopId).HasDatabaseName("IX_Bookings_ShopId");
            entity.HasIndex(b => b.Status).HasDatabaseName("IX_Bookings_Status");
            entity.HasIndex(b => new { b.UserId, b.Status }).HasDatabaseName("IX_Bookings_User_Status"); // For fetching user's bookings by status

            entity.HasQueryFilter(b => !b.Shop.IsDeleted);
        });
        // --- END Booking Configuration ---

        // --- NEW: BookingItem Configuration ---
        modelBuilder.Entity<BookingItem>(entity =>
        {
            entity.HasKey(bi => bi.BookingItemId);
            entity.Property(bi => bi.PriceAtBooking).HasColumnType("decimal(18,2)");
            entity.Property(bi => bi.ShopNameSnapshotEn).HasMaxLength(200);
            entity.Property(bi => bi.ShopNameSnapshotAr).HasMaxLength(200);


            // Relationship: BookingItem to Booking (configured by Booking entity's HasMany)

            // Optional: Relationship to ShopService if you want to ensure FK integrity
            // but be careful with soft deletes or if ShopServices can be deleted.
            // Usually, ShopServiceId is just a Guid reference, and details are snapshotted.
            // If you add FK:
            // entity.HasOne<ShopService>() // No navigation property in BookingItem back to ShopService for simplicity
            //       .WithMany()          // ShopService doesn't need ICollection<BookingItem>
            //       .HasForeignKey(bi => bi.ShopServiceId)
            //       .OnDelete(DeleteBehavior.Restrict); // Prevent deleting a ShopService if it's in a booking

            entity.HasIndex(bi => bi.BookingId).HasDatabaseName("IX_BookingItems_BookingId");
            entity.HasIndex(bi => bi.ShopServiceId).HasDatabaseName("IX_BookingItems_ShopServiceId");
            entity.HasQueryFilter(bi => !bi.Booking.Shop.IsDeleted);
        });
        // --- END BookingItem Configuration ---

         // --- NEW: UserPreference Configuration ---
        modelBuilder.Entity<UserPreference>(entity =>
        {
            entity.HasKey(up => up.UserPreferenceId);
            entity.Property(up => up.UserPreferenceId).ValueGeneratedOnAdd(); // If using Guid PK, this is fine.
                                                                           // If int PK: .UseIdentityByDefaultColumn();

            entity.Property(up => up.UserId)
                .IsRequired()
                .HasMaxLength(100); // To store the 'sub' claim from JWT

            // Unique index on UserId to ensure one preference record per authenticated user
            entity.HasIndex(up => up.UserId)
                .IsUnique()
                .HasDatabaseName("IX_UserPreferences_UserId");

            entity.Property(up => up.LastKnownLatitude).IsRequired(false); // Nullable
            entity.Property(up => up.LastKnownLongitude).IsRequired(false); // Nullable
            entity.Property(up => up.LastKnownLocationAccuracy).IsRequired(false); // Nullable
            entity.Property(up => up.LocationSource).HasMaxLength(50).IsRequired(false); // Nullable
            entity.Property(up => up.LastSetAtUtc).IsRequired(false); // Nullable

            entity.Property(up => up.OtherPreferencesJson)
                .HasColumnType("jsonb") // Specific to PostgreSQL
                .IsRequired(false); // Nullable
        });
        // --- END UserPreference Configuration ---

        // --- NEW: AdminAreaShopStats Configuration ---
        modelBuilder.Entity<AdminAreaShopStats>(entity =>
        {
            // The [Key] and [ForeignKey] attributes on AdministrativeBoundaryId handle the PK and FK.
            // EF Core conventions for one-to-one relationships:
            // The dependent side (AdminAreaShopStats) has a PK that is also the FK to the principal (AdministrativeBoundary).
            // The navigation property AdministrativeBoundary links back.
            // AdministrativeBoundary does not need a navigation property to AdminAreaShopStats for this to work,
            // but one could be added if desired (public AdminAreaShopStats? Stats { get; set; }).

            entity.HasOne(d => d.AdministrativeBoundary)
                    .WithOne() // Or .WithOne(p => p.Stats) if you add nav prop to AdministrativeBoundary
                    .HasForeignKey<AdminAreaShopStats>(d => d.AdministrativeBoundaryId)
                    .OnDelete(DeleteBehavior.Cascade); // If an AdminBoundary is deleted, its stats are also deleted.

            entity.Property(e => e.LastUpdatedAtUtc).IsRequired();
            entity.HasIndex(e => e.LastUpdatedAtUtc); // Index for potential queries on update time
        });
        // --- END NEW: AdminAreaShopStats Configuration ---

        // Configure the CityWithCoordinatesView
        modelBuilder.Entity<CityWithCoordinatesView>(entity =>
        {
            entity.HasNoKey(); // Views typically don't have primary keys for EF queries
            entity.ToView("CityWithCoordinates");

            // Optionally, you can specify column mappings if needed
            entity.Property(e => e.Id).HasColumnName("Id");
            entity.Property(e => e.NameEn).HasColumnName("NameEn");
            entity.Property(e => e.NameAr).HasColumnName("NameAr");
            entity.Property(e => e.Slug).HasColumnName("Slug");
            entity.Property(e => e.StateProvince).HasColumnName("StateProvince");
            entity.Property(e => e.Country).HasColumnName("Country");
            entity.Property(e => e.IsActive).HasColumnName("IsActive");
            entity.Property(e => e.Latitude).HasColumnName("Latitude");
            entity.Property(e => e.Longitude).HasColumnName("Longitude");
        });

        modelBuilder.Entity<ShopDetailsView>(entity =>
       {
           entity.HasNoKey(); // Views are not updatable via EF Core directly and usually don't have a PK defined in EF
           entity.ToView("ShopDetailsView"); // Name of the database view


           // Explicit column mapping (optional if C# property names match view column names)
           entity.Property(e => e.ShopLatitude).HasColumnName("ShopLatitude");
           entity.Property(e => e.ShopLongitude).HasColumnName("ShopLongitude");
           entity.Property(e => e.Location).HasColumnName("Location"); // Map the Point column
       });
    }
}
// // // src/AutomotiveServices.Api/Data/AppDbContext.cs
// // using AutomotiveServices.Api.Models;
// // using Microsoft.EntityFrameworkCore;
// // // NetTopologySuite.Geometries.Point is implicitly used via Shop.Location

// // namespace AutomotiveServices.Api.Data;

// // public class AppDbContext : DbContext
// // {
// //     public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
// //     {
// //     }

// //     public DbSet<Shop> Shops { get; set; } = null!;
// //     public DbSet<City> Cities { get; set; } = null!;


// //     protected override void OnModelCreating(ModelBuilder modelBuilder)
// //     {
// //         base.OnModelCreating(modelBuilder);

// //         // Ensure PostGIS extension is available/enabled in the database
// //         modelBuilder.HasPostgresExtension("postgis");

// //         // City Configuration
// //         modelBuilder.Entity<City>(entity =>
// //         {
// //             entity.HasKey(c => c.Id);
// //             entity.Property(c => c.Id).ValueGeneratedOnAdd(); // For auto-increment int

// //             entity.Property(c => c.NameEn).IsRequired().HasMaxLength(100);
// //             entity.Property(c => c.NameAr).IsRequired().HasMaxLength(100);
// //             entity.Property(c => c.Slug).IsRequired().HasMaxLength(100);
// //             entity.Property(c => c.StateProvince).HasMaxLength(100);
// //             entity.Property(c => c.Country).IsRequired().HasMaxLength(100);

// //             // Configure City Location
// //             entity.Property(c => c.Location)
// //                 .HasColumnType("geography(Point, 4326)") // Using geography for consistency
// //                 .IsRequired();

// //             // Optional: Spatial index on city locations if you plan to query cities by proximity
// //             entity.HasIndex(c => c.Location)
// //                 .HasMethod("GIST")
// //                 .HasDatabaseName("IX_Cities_Location");


// //             entity.HasIndex(c => c.Slug).IsUnique().HasDatabaseName("IX_Cities_Slug");
// //             entity.HasIndex(c => c.IsActive).HasDatabaseName("IX_Cities_IsActive");
// //         });


// //         // Configure Shop entity
// //         modelBuilder.Entity<Shop>(entity =>
// //         {
// //             entity.HasKey(s => s.Id);
// //             entity.Property(s => s.Id).ValueGeneratedOnAdd(); // For auto-generated Guid

// //              entity.Property(s => s.Location)
// //                   .HasColumnType("geography(Point, 4326)")
// //                   .IsRequired();
// //             entity.HasIndex(s => s.Location).HasMethod("GIST").HasDatabaseName("IX_Shops_Location");

            
// //              // Foreign Key & Relationship to City
// //             entity.HasOne(s => s.City)
// //                   .WithMany(c => c.Shops)
// //                   .HasForeignKey(s => s.CityId)
// //                   .OnDelete(DeleteBehavior.Restrict)
// //                   .IsRequired(); // Ensures the FK itself is required


// //             // Index for soft delete queries
// //             entity.HasIndex(s => s.IsDeleted)
// //                 .HasDatabaseName("IX_Shops_IsDeleted");

// //              // Index on Category (SubCategory)
// //             entity.HasIndex(s => s.Category).HasDatabaseName("IX_Shops_Category");

// //             // Index on CityId
// //             entity.HasIndex(s => s.CityId).HasDatabaseName("IX_Shops_CityId");
            

// //            // Compound index for main listing queries
// //             entity.HasIndex(s => new { s.CityId, s.Category, s.IsDeleted })
// //                   .HasDatabaseName("IX_Shops_CityId_Category_IsDeleted");

// //             // Unique constraint for shop slug within a city (if shop slugs are used)
// //             entity.HasIndex(s => new { s.CityId, s.Slug })
// //                   .IsUnique()
// //                   .HasFilter("\"Slug\" IS NOT NULL") // Only for non-null slugs
// //                   .HasDatabaseName("IX_Shops_CityId_Slug");


// //             // Configure string properties with updated lengths
// //             entity.Property(s => s.NameEn)
// //                 .IsRequired()
// //                 .HasMaxLength(200);

// //             entity.Property(s => s.NameAr)
// //                 .IsRequired()
// //                 .HasMaxLength(200);

// //             entity.Property(s => s.DescriptionEn)
// //                 .HasMaxLength(1000);

// //             entity.Property(s => s.DescriptionAr)
// //                 .HasMaxLength(1000);

// //             entity.Property(s => s.Address)
// //                 .IsRequired()
// //                 .HasMaxLength(500);

// //             entity.Property(s => s.PhoneNumber)
// //                 .HasMaxLength(20);

// //             entity.Property(s => s.ServicesOffered)
// //                 .HasMaxLength(1000);

// //             entity.Property(s => s.OpeningHours)
// //                 .HasMaxLength(500);

// //             entity.Property(s => s.LogoUrl).HasMaxLength(500);

// //             // Removed configurations for CreatedAt and UpdatedAt
// //             // entity.Property(s => s.CreatedAt)...
// //             // entity.Property(s => s.UpdatedAt)...

// //             // Configure soft delete
// //             entity.Property(s => s.IsDeleted)
// //                 .HasDefaultValue(false);

// //             // Global query filter to automatically exclude soft-deleted records
// //             entity.HasQueryFilter(s => !s.IsDeleted);
// //         });


// //     }

// //     // Removed SaveChangesAsync/SaveChanges overrides if they were only for timestamps
// // }
// // // // src/AutomotiveServices.Api/Data/AppDbContext.cs
// // // using AutomotiveServices.Api.Models;
// // // using Microsoft.EntityFrameworkCore;
// // // // NetTopologySuite.Geometries.Point is implicitly used via Shop.Location

// // // namespace AutomotiveServices.Api.Data;

// // // public class AppDbContext : DbContext
// // // {
// // //     public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
// // //     {
// // //     }

// // //     public DbSet<Shop> Shops { get; set; } = null!;

// // //     protected override void OnModelCreating(ModelBuilder modelBuilder)
// // //     {
// // //         base.OnModelCreating(modelBuilder);

// // //         // Ensure PostGIS extension is available/enabled in the database
// // //         modelBuilder.HasPostgresExtension("postgis");

// // //         // Configure Shop entity
// // //         modelBuilder.Entity<Shop>(entity =>
// // //         {
// // //             // *** THIS IS THE KEY CHANGE ***
// // //             // Configure Location as geometry instead of geography
// // //             entity.Property(e => e.Location)
// // //                 .HasColumnType("geometry(Point, 4326)") // Use geometry, SRID 4326 for WGS84
// // //                 .IsRequired();

// // //             // Index on Location for spatial queries (using GIST index for PostGIS geometry)
// // //             entity.HasIndex(s => s.Location)
// // //                 .HasMethod("GIST")
// // //                 .HasDatabaseName("IX_Shops_Location");

// // //             // Index for soft delete queries
// // //             entity.HasIndex(s => s.IsDeleted)
// // //                 .HasDatabaseName("IX_Shops_IsDeleted");

// // //             // Optional: Compound index for common queries like active shops by creation date
// // //             entity.HasIndex(s => new { s.IsDeleted, s.CreatedAt })
// // //                 .HasDatabaseName("IX_Shops_IsDeleted_CreatedAt");

// // //             // Configure string properties with updated lengths
// // //             entity.Property(s => s.NameEn)
// // //                 .IsRequired()
// // //                 .HasMaxLength(200);

// // //             entity.Property(s => s.NameAr)
// // //                 .IsRequired()
// // //                 .HasMaxLength(200);

// // //             entity.Property(s => s.DescriptionEn)
// // //                 .HasMaxLength(1000);

// // //             entity.Property(s => s.DescriptionAr)
// // //                 .HasMaxLength(1000);

// // //             entity.Property(s => s.Address)
// // //                 .IsRequired()
// // //                 .HasMaxLength(500);

// // //             entity.Property(s => s.PhoneNumber)
// // //                 .HasMaxLength(20);

// // //             entity.Property(s => s.ServicesOffered)
// // //                 .HasMaxLength(1000);

// // //             entity.Property(s => s.OpeningHours)
// // //                 .HasMaxLength(500);

// // //             // Configure timestamps
// // //             // DB will set CreatedAt on insert if not provided by C#.
// // //             // UpdatedAt is set on insert if not provided, but C# should manage it for updates.
// // //             entity.Property(s => s.CreatedAt)
// // //                 .HasDefaultValueSql("CURRENT_TIMESTAMP");

// // //             entity.Property(s => s.UpdatedAt)
// // //                 .HasDefaultValueSql("CURRENT_TIMESTAMP"); // On insert only by default

// // //             // Configure soft delete
// // //             entity.Property(s => s.IsDeleted)
// // //                 .HasDefaultValue(false);

// // //             // Global query filter to automatically exclude soft-deleted records
// // //             entity.HasQueryFilter(s => !s.IsDeleted);
// // //         });
// // //     }
// // // }