// Data/DataSeeder.cs
using AutomotiveServices.Api.Models;
using AutomotiveServices.Api.Models.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using NetTopologySuite.Operation.Union;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AutomotiveServices.Api.Data
{
    public static class DataSeeder
    {
        private static readonly GeometryFactory _geometryFactory = new GeometryFactory(new PrecisionModel(), 4326);
        private static readonly GeoJsonReader _geoJsonReader = new GeoJsonReader();
        private static readonly JsonSerializerOptions _jsonSerializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        private static Point CreatePoint(double latitude, double longitude) =>
           _geometryFactory.CreatePoint(new Coordinate(longitude, latitude));

        private static Geometry? EnsureMultiPolygon(Geometry? geometry, ILogger logger)
        {
            if (geometry == null) return null;
            if (geometry is Polygon polygon)
                return _geometryFactory.CreateMultiPolygon(new[] { polygon.Copy() as Polygon });
            if (geometry is MultiPolygon mp)
                return mp.Copy() as MultiPolygon;
            logger.LogWarning($"EnsureMultiPolygon: Unexpected geometry type {geometry.GeometryType}. Copying as is.");
            return geometry.Copy();
        }

        private static Geometry? CreateSimplifiedGeometry(Geometry? detailedGeometry, ILogger logger, double tolerance = 0.005)
        {
            if (detailedGeometry == null) return null;
            try
            {
                var geometryToSimplify = EnsureMultiPolygon(detailedGeometry, logger);
                if (geometryToSimplify == null) return null;
                var simplifier = new NetTopologySuite.Simplify.DouglasPeuckerSimplifier(geometryToSimplify) { DistanceTolerance = tolerance };
                return EnsureMultiPolygon(simplifier.GetResultGeometry(), logger);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "CreateSimplifiedGeometry: Error simplifying geometry.");
                return EnsureMultiPolygon(detailedGeometry.Copy() as Geometry, logger);
            }
        }
        
        private static string GenerateOperationalAreaSlug(string nameEn)
        {
            if (string.IsNullOrWhiteSpace(nameEn)) return $"oa-{Guid.NewGuid().ToString().Substring(0, 8)}";
            string str = nameEn.ToLowerInvariant().Trim();
            str = Regex.Replace(str, @"[^a-z0-9\s-]", ""); 
            str = Regex.Replace(str, @"\s+", "-");          
            str = Regex.Replace(str, @"-+", "-");           
            str = str.Trim('-');                            
            str = str.Length > 80 ? str.Substring(0, 80) : str; 
            return string.IsNullOrEmpty(str) ? $"oa-{Guid.NewGuid().ToString().Substring(0, 8)}" : str; 
        }

        private static string GenerateShopSlug(string shopNameEn, string operationalAreaSlug)
        {
            if (string.IsNullOrWhiteSpace(shopNameEn)) shopNameEn = "shop";
            operationalAreaSlug = string.IsNullOrWhiteSpace(operationalAreaSlug) ? "unknown-area" : operationalAreaSlug.Trim();
            
            string namePart = shopNameEn.ToLowerInvariant().Trim();
            namePart = Regex.Replace(namePart, @"[^a-z0-9\s-]", "");
            namePart = Regex.Replace(namePart, @"\s+", "-"); namePart = Regex.Replace(namePart, @"-+", "-");
            namePart = namePart.Trim('-');
            namePart = namePart.Length > 60 ? namePart.Substring(0, 60) : namePart;
            if (string.IsNullOrEmpty(namePart)) namePart = "shop";
            
            string areaPart = operationalAreaSlug.ToLowerInvariant().Trim(); 
            areaPart = areaPart.Length > 40 ? areaPart.Substring(0, 40) : areaPart;
            if (string.IsNullOrEmpty(areaPart)) areaPart = "area";

            return $"{namePart}-in-{areaPart}";
        }

        private static string GenerateLogoUrlFromName(string shopSpecificSlug) => $"/logos/{shopSpecificSlug}.png";

        private static FeatureCollection? LoadFeatureCollectionFromFile(string relativePath, ILogger logger)
        {
            try
            {
                string filePath = Path.Combine(AppContext.BaseDirectory, relativePath);
                if (!File.Exists(filePath)) { logger.LogError($"LoadFeatureCollectionFromFile: File not found: {filePath}"); return null; }
                return _geoJsonReader.Read<FeatureCollection>(File.ReadAllText(filePath));
            }
            catch (Exception ex) { logger.LogError(ex, $"LoadFeatureCollectionFromFile: Error loading {relativePath}"); return null; }
        }

        private static List<ShopSeedDto>? LoadShopsFromFile(string relativePath, ILogger logger)
        {
            try
            {
                string filePath = Path.Combine(AppContext.BaseDirectory, relativePath);
                if (!File.Exists(filePath)) { logger.LogError($"LoadShopsFromFile: File not found: {filePath}"); return null; }
                var shops = JsonSerializer.Deserialize<List<ShopSeedDto>>(File.ReadAllText(filePath), _jsonSerializerOptions);
                logger.LogInformation($"LoadShopsFromFile: Loaded {shops?.Count ?? 0} shop records from {relativePath}.");
                return shops;
            }
            catch (Exception ex) { logger.LogError(ex, $"LoadShopsFromFile: Error loading {relativePath}"); return null; }
        }

        public static async Task SeedAsync(AppDbContext context, ILogger logger, bool forceReseed)
        {
            logger.LogInformation("DataSeeder.SeedAsync: Starting. ForceReseed: {ForceReseed}", forceReseed);
            if (forceReseed)
            {
                logger.LogInformation("DataSeeder.SeedAsync: Clearing relevant tables due to ForceReseed...");
                #pragma warning disable EF1002 
                await context.Database.ExecuteSqlRawAsync("ALTER TABLE \"Shops\" DROP CONSTRAINT IF EXISTS \"FK_Shops_OperationalAreas_OperationalAreaId\";");
                string[] tablesToDelete = {
                    "\"ShopServices\"", "\"GlobalServiceDefinitions\"", "\"BookingItems\"", "\"Bookings\"",
                    "\"UserCartItems\"", "\"AnonymousCartItems\"", "\"UserPreferences\"", "\"AnonymousUserPreferences\"",
                    "\"Shops\"", "\"AdminAreaShopStats\"", "\"OperationalAreas\"", "\"AdministrativeBoundaries\"", "\"Cities\""
                };
                foreach (var table in tablesToDelete) {
                    await context.Database.ExecuteSqlRawAsync($"DELETE FROM {table};");
                }
                #pragma warning restore EF1002
                logger.LogInformation("DataSeeder.SeedAsync: Tables cleared.");
            }

            if (forceReseed || !await context.Cities.AnyAsync())
            {
                logger.LogInformation("DataSeeder.SeedAsync: Seeding 'Cities' (legacy reference)...");
                var cities = new List<City> {
                    new() { NameEn = "CairoRef", NameAr = "القاهرة مرجع", Slug = "cairo-ref", StateProvince = "Cairo Governorate", Country="Egypt", Location = CreatePoint(30.0444, 31.2357) },
                    new() { NameEn = "AlexandriaRef", NameAr = "الإسكندرية مرجع", Slug = "alexandria-ref", StateProvince = "Alexandria Governorate", Country="Egypt", Location = CreatePoint(31.2001, 29.9187) },
                    new() { NameEn = "GizaRef", NameAr = "الجيزة مرجع", Slug = "giza-ref", StateProvince = "Giza Governorate", Country="Egypt", Location = CreatePoint(29.9870, 31.1313) },
                    new() { NameEn = "6th October CityRef", NameAr = "مدينة 6 أكتوبر مرجع", Slug = "6th-october-city-ref", StateProvince = "Giza Governorate", Country="Egypt", Location = CreatePoint(29.9660, 30.9232) },
                    new() { NameEn = "New CairoRef", NameAr = "القاهرة الجديدة مرجع", Slug = "new-cairo-ref", StateProvince = "Cairo Governorate", Country="Egypt", Location = CreatePoint(30.0271, 31.4961) }
                };
                if(cities.Any()) await context.Cities.AddRangeAsync(cities);
                await context.SaveChangesAsync();
                logger.LogInformation($"DataSeeder.SeedAsync: Seeded {cities.Count} legacy cities.");
            }

            Dictionary<string, AdministrativeBoundary> seededGovernoratesByGid = new();
            Dictionary<string, AdministrativeBoundary> seededLevel2BoundariesByGid = new(); 

            const string cairoGovGidConst = "EGY.11_1"; //verified
            const string gizaGovGidConst  = "EGY.8_1";  //verified
            const string alexGovGidConst  = "EGY.6_1";  // Assuming this is needed for Alexandria later

            if (forceReseed || !await context.AdministrativeBoundaries.AnyAsync())
            {
                logger.LogInformation("DataSeeder.SeedAsync: Seeding AdministrativeBoundaries from GADM files...");
                var adminL1Features = LoadFeatureCollectionFromFile("SeedData/gadm41_EGY_1.json", logger);
                if (adminL1Features != null) {
                    var governoratesToSeed = adminL1Features.Select(f => {
                        var p = f.Attributes; string? nE=p.GetOptionalString("NAME_1"), nA=p.GetOptionalString("NL_NAME_1"), g1=p.GetOptionalString("GID_1");
                        if(string.IsNullOrEmpty(nE)||string.IsNullOrEmpty(g1)) return null; var geom=EnsureMultiPolygon(f.Geometry,logger);
                        return new AdministrativeBoundary { NameEn=nE, NameAr=!string.IsNullOrWhiteSpace(nA)&&nA!="NA"?nA:nE, AdminLevel=1, CountryCode="EG", OfficialCode=g1, Boundary=geom, SimplifiedBoundary=CreateSimplifiedGeometry(geom,logger,0.01), Centroid=geom?.Centroid as Point, IsActive=true, CreatedAtUtc=DateTime.UtcNow, UpdatedAtUtc=DateTime.UtcNow };
                    }).Where(ab => ab != null).ToList();
                    if(governoratesToSeed.Any()) await context.AdministrativeBoundaries.AddRangeAsync(governoratesToSeed!); await context.SaveChangesAsync();
                }
            }
            seededGovernoratesByGid = await context.AdministrativeBoundaries.Where(ab => ab.AdminLevel == 1 && ab.CountryCode == "EG").AsNoTracking().ToDictionaryAsync(ab => ab.OfficialCode!, ab => ab);
            logger.LogInformation($"DataSeeder.SeedAsync: Loaded {seededGovernoratesByGid.Count} L1 AdminBoundaries (Governorates).");

            if (forceReseed || !await context.AdministrativeBoundaries.AnyAsync(ab => ab.AdminLevel == 2))
            {
                var adminL2Features = LoadFeatureCollectionFromFile("SeedData/gadm41_EGY_2.json", logger);
                if (adminL2Features != null && seededGovernoratesByGid.Any()) { 
                    var citiesToSeed = adminL2Features.Select(f => {
                        var p=f.Attributes; string? nE=p.GetOptionalString("NAME_2"), nA=p.GetOptionalString("NL_NAME_2"), g2=p.GetOptionalString("GID_2"), pG1=p.GetOptionalString("GID_1");
                        if(string.IsNullOrEmpty(nE)||string.IsNullOrEmpty(g2)||string.IsNullOrEmpty(pG1)||!seededGovernoratesByGid.TryGetValue(pG1, out var parent)) return null; var geom=EnsureMultiPolygon(f.Geometry,logger);
                        return new AdministrativeBoundary { NameEn=nE, NameAr=!string.IsNullOrWhiteSpace(nA)&&nA!="NA"?nA:nE, AdminLevel=2, ParentId=parent.Id, CountryCode="EG", OfficialCode=g2, Boundary=geom, SimplifiedBoundary=CreateSimplifiedGeometry(geom,logger,0.001), Centroid=geom?.Centroid as Point, IsActive=true, CreatedAtUtc=DateTime.UtcNow, UpdatedAtUtc=DateTime.UtcNow };
                    }).Where(ab => ab != null).ToList();
                    if(citiesToSeed.Any()) await context.AdministrativeBoundaries.AddRangeAsync(citiesToSeed!); await context.SaveChangesAsync();
                } else if (adminL2Features != null) { logger.LogWarning("DataSeeder.SeedAsync: GADM L2 data found, but no L1 Governorates were loaded/available to link them.");}
            }
            seededLevel2BoundariesByGid = await context.AdministrativeBoundaries.Where(ab => ab.AdminLevel == 2 && ab.CountryCode == "EG").AsNoTracking().ToDictionaryAsync(ab => ab.OfficialCode!, ab => ab);
            logger.LogInformation($"DataSeeder.SeedAsync: Loaded {seededLevel2BoundariesByGid.Count} L2 AdminBoundaries (Cities/Markazes).");

            Dictionary<string, OperationalArea> operationalAreasMap = new();
            if (forceReseed || !await context.OperationalAreas.AnyAsync())
            {
                logger.LogInformation("DataSeeder.SeedAsync: Seeding OperationalAreas (Governorate-level AND Granular)...");
                var oasToSeed = new List<OperationalArea>();
                
                AdministrativeBoundary? cairoGovFromDb=seededGovernoratesByGid.GetValueOrDefault(cairoGovGidConst);
                AdministrativeBoundary? gizaGovFromDb=seededGovernoratesByGid.GetValueOrDefault(gizaGovGidConst);
                AdministrativeBoundary? alexGovFromDb=seededGovernoratesByGid.GetValueOrDefault(alexGovGidConst); // Added back for potential use

                // 1. Create Operational Areas for entire Governorates
                Action<AdministrativeBoundary?, string, double, double, double> addGovOA = (gov, displayLevel, radiusKm, fallbackLat, fallbackLon) => {
                    if (gov?.Boundary != null) {
                        string oaNameEn = gov.NameEn; // Use direct GADM name for the OA
                        string oaNameAr = gov.NameAr;
                        oasToSeed.Add(new OperationalArea { 
                            NameEn = oaNameEn, NameAr = oaNameAr, 
                            Slug = GenerateOperationalAreaSlug($"{oaNameEn}-governorate"), // Explicitly add -governorate to slug
                            DefaultSearchRadiusMeters = radiusKm * 1000, 
                            GeometrySource = GeometrySourceType.DerivedFromAdmin, 
                            PrimaryAdministrativeBoundaryId = gov.Id, 
                            DisplayLevel = displayLevel, // "Governorate"
                            CentroidLatitude = gov.Centroid?.Y ?? fallbackLat, 
                            CentroidLongitude = gov.Centroid?.X ?? fallbackLon, 
                            IsActive = true 
                        });
                    } else {
                        logger.LogWarning($"DataSeeder.SeedAsync: Could not create Governorate OA for GID '{gov?.OfficialCode}' as AdminBoundary was not found or has no boundary.");
                    }
                };
                addGovOA(cairoGovFromDb, "Governorate", 30, 30.0444, 31.2357);
                addGovOA(gizaGovFromDb, "Governorate", 25, 29.9870, 31.1313);
                addGovOA(alexGovFromDb, "Governorate", 20, 31.2001, 29.9187); // Assuming alexGovGidConst is defined and verified

                // 2. Operational Areas from UNION of GADM Level 2 parts
                var ncGids=new[]{"EGY.11.33_1","EGY.11.34_1","EGY.11.35_1"}; // VERIFY New Cairo GID_2s
                logger.LogInformation("DataSeeder.SeedAsync: Creating 'New Cairo District' OA using GID_2s: {Gids}", string.Join(", ", ncGids));
                var ncGeoms=ncGids.Select(gid=>{ var b=seededLevel2BoundariesByGid.GetValueOrDefault(gid); if(b==null)logger.LogWarning($"GADM L2 for NC GID {gid} NOT FOUND in seededLevel2BoundariesByGid."); else if(b.Boundary==null)logger.LogWarning($"GADM L2 for NC GID {gid} has NULL Boundary."); return b?.Boundary; }).Where(g=>g!=null).ToList();
                logger.LogInformation("DataSeeder.SeedAsync: Found {Count} valid geometries for New Cairo parts.", ncGeoms.Count);
                if(cairoGovFromDb == null) logger.LogWarning("DataSeeder.SeedAsync: Parent Cairo Governorate (cairoGovFromDb) is null for New Cairo OA.");
                if(ncGeoms.Any()&&cairoGovFromDb!=null){ Geometry? ug=ncGeoms.Count==1?ncGeoms.First()!.Copy():new GeometryCollection(ncGeoms.ToArray()!).Union(); if(ug!=null&&!ug.IsEmpty) { var slug=GenerateOperationalAreaSlug("new-cairo-district"); logger.LogInformation("DataSeeder.SeedAsync: Creating 'New Cairo District' OA with slug '{Slug}'.",slug); oasToSeed.Add(new OperationalArea{NameEn="New Cairo District",NameAr="القاهرة الجديدة",Slug=slug,CentroidLatitude=ug.Centroid?.Y??30.0271,CentroidLongitude=ug.Centroid?.X??31.4961,DefaultSearchRadiusMeters=15000,GeometrySource=GeometrySourceType.Custom,CustomBoundary=EnsureMultiPolygon(ug,logger),CustomSimplifiedBoundary=CreateSimplifiedGeometry(ug,logger,0.004),PrimaryAdministrativeBoundaryId=cairoGovFromDb.Id,DisplayLevel="AggregatedUrbanArea",IsActive=true});}} else {logger.LogWarning("DataSeeder.SeedAsync: Failed to create New Cairo District OA; GADM L2 parts or parent Cairo Gov missing. Geoms found: {Count}, Cairo Gov Exists: {CairoExists}", ncGeoms.Count, cairoGovFromDb != null);}

                var soGids=new[]{"EGY.8.17_1","EGY.8.18_1"}; // VERIFY 6th Oct GID_2s
                logger.LogInformation("DataSeeder.SeedAsync: Creating '6th of October City' OA using GID_2s: {Gids}", string.Join(", ", soGids));
                var soGeoms=soGids.Select(gid=>{ var b=seededLevel2BoundariesByGid.GetValueOrDefault(gid); if(b==null)logger.LogWarning($"GADM L2 for 6Oct GID {gid} NOT FOUND in seededLevel2BoundariesByGid."); else if(b.Boundary==null)logger.LogWarning($"GADM L2 for 6Oct GID {gid} has NULL Boundary."); return b?.Boundary; }).Where(g=>g!=null).ToList();
                logger.LogInformation("DataSeeder.SeedAsync: Found {Count} valid geometries for 6th of October parts.", soGeoms.Count);
                if (gizaGovFromDb == null) logger.LogWarning("DataSeeder.SeedAsync: Parent Giza Governorate (gizaGovFromDb) is null for 6th October OA.");
                if(soGeoms.Any()&&gizaGovFromDb!=null){ Geometry? ug=soGeoms.Count==1?soGeoms.First()!.Copy():new GeometryCollection(soGeoms.ToArray()!).Union(); if(ug!=null&&!ug.IsEmpty) { var slug = "6th-of-october-city"; logger.LogInformation("DataSeeder.SeedAsync: Creating '6th of October City' OA with PREDEFINED slug '{Slug}'.",slug); oasToSeed.Add(new OperationalArea{NameEn="6th of October City",NameAr="مدينة السادس من أكتوبر",Slug=slug,CentroidLatitude=ug.Centroid?.Y??29.9660,CentroidLongitude=ug.Centroid?.X??30.9232,DefaultSearchRadiusMeters=18000,GeometrySource=GeometrySourceType.Custom,CustomBoundary=EnsureMultiPolygon(ug,logger),CustomSimplifiedBoundary=CreateSimplifiedGeometry(ug,logger,0.004),PrimaryAdministrativeBoundaryId=gizaGovFromDb.Id,DisplayLevel="MajorNewCity",IsActive=true});}} else {logger.LogWarning("DataSeeder.SeedAsync: Failed to create 6th of October City OA; GADM L2 parts or parent Giza Gov missing. Geoms found: {Count}, Giza Gov Exists: {GizaExists}", soGeoms.Count, gizaGovFromDb != null);}

                // 3. Operational Areas from specific GADM Level 2 entities (Districts/Cities)
                var distGids=new Dictionary<string,(string NameEn,string NameAr,string ParentGovGidConstant,string DisplayLevel,double DefaultRadiusKm,double FallbackLat,double FallbackLon)>{
                    // --- VERIFY ALL THESE GID_2s and their Parent GIDs ---
                    {"EGY.11.26_1",("Maadi","المعادي",cairoGovGidConst,"District",5,29.9624,31.2597)}, // Maadi GID & fallback LatLon
                    {"EGY.11.48_1",("Zamalek","الزمالك",cairoGovGidConst,"District",3,30.0608,31.2200)}, // Zamalek GID & fallback LatLon
                    {"EGY.11.17_1",("Heliopolis","مصر الجديدة",cairoGovGidConst,"District",7,30.0907,31.3258)}, 
                    {"EGY.11.30_1",("Nasr City","مدينة نصر",cairoGovGidConst,"District",10,30.0550,31.3300)},
                    {"EGY.8.10_1", ("Mohandessin","المهندسين",gizaGovGidConst,"District",4,30.0550,31.1990)}, 
                    {"EGY.8.6_1",   ("Dokki","الدقي",gizaGovGidConst,"District",3,30.0410,31.2050)},        
                    {"EGY.8.16_1",  ("Sheikh Zayed City", "مدينة الشيخ زايد", gizaGovGidConst, "MajorNewCity", 12, 30.0086, 30.9061) }, // Sheikh Zayed GID & fallback LatLon
                    {"EGY.11.5_1", ("Bulaq","بولاق",cairoGovGidConst,"District",4,30.0600,31.2380)}, // Bulaq GID & fallback LatLon (assuming it's Bulaq Abu El Ela)
                    // Add other GID_2s for districts in Alexandria, etc.
                }; 
                foreach(var kvp in distGids){ 
                    AdministrativeBoundary? parentGovContext = seededGovernoratesByGid.GetValueOrDefault(kvp.Value.ParentGovGidConstant); 
                    if(parentGovContext != null && seededLevel2BoundariesByGid.TryGetValue(kvp.Key,out var abL2)&&abL2?.Boundary!=null) 
                        oasToSeed.Add(new OperationalArea{
                            NameEn=kvp.Value.NameEn,NameAr=kvp.Value.NameAr,Slug=GenerateOperationalAreaSlug(kvp.Value.NameEn),
                            CentroidLatitude=abL2.Centroid?.Y??kvp.Value.FallbackLat,CentroidLongitude=abL2.Centroid?.X??kvp.Value.FallbackLon,
                            DefaultSearchRadiusMeters=kvp.Value.DefaultRadiusKm*1000,GeometrySource=GeometrySourceType.DerivedFromAdmin,
                            PrimaryAdministrativeBoundaryId=abL2.Id, 
                            DisplayLevel=kvp.Value.DisplayLevel,IsActive=true
                        }); 
                    else logger.LogWarning($"DataSeeder.SeedAsync: Failed for District OA GID: {kvp.Key} ('{kvp.Value.NameEn}'). L2 entity, boundary, or its context parent Gov (Ref: {kvp.Value.ParentGovGidConstant}) missing.");
                }

                if(oasToSeed.Any()) await context.OperationalAreas.AddRangeAsync(oasToSeed);
                await context.SaveChangesAsync();
                operationalAreasMap = await context.OperationalAreas.AsNoTracking().ToDictionaryAsync(oa => oa.Slug, oa => oa);
                logger.LogInformation($"DataSeeder.SeedAsync: Seeded {operationalAreasMap.Count} OperationalAreas. Keys: [{string.Join(", ", operationalAreasMap.Keys)}]");
            } else { operationalAreasMap = await context.OperationalAreas.AsNoTracking().ToDictionaryAsync(oa => oa.Slug, oa => oa); }

            List<GlobalServiceDefinition> globalServices = new();
            if (forceReseed || !await context.GlobalServiceDefinitions.AnyAsync()) { 
                globalServices = new List<GlobalServiceDefinition> {
                    new() { ServiceCode = "OIL_CHANGE_STD", DefaultNameEn = "Standard Oil Change", DefaultNameAr = "تغيير زيت قياسي", Category = ShopCategory.OilChange, DefaultEstimatedDurationMinutes = 30, IsGloballyActive = true },
                    new() { ServiceCode = "OIL_CHANGE_SYN", DefaultNameEn = "Synthetic Oil Change", DefaultNameAr = "تغيير زيت تخليقي", Category = ShopCategory.OilChange, DefaultEstimatedDurationMinutes = 45, IsGloballyActive = true },
                    new() { ServiceCode = "BRAKE_PAD_FRNT", DefaultNameEn = "Front Brake Pad Replacement", DefaultNameAr = "تغيير تيل الفرامل الأمامي", Category = ShopCategory.Brakes, DefaultEstimatedDurationMinutes = 60, IsGloballyActive = true },
                    new() { ServiceCode = "AC_REGAS", DefaultNameEn = "A/C Re-gas", DefaultNameAr = "إعادة شحن فريون التكييف", Category = ShopCategory.ACRepair, DefaultEstimatedDurationMinutes = 45, IsGloballyActive = true },
                    new() { ServiceCode = "CAR_WASH_EXT", DefaultNameEn = "Exterior Car Wash", DefaultNameAr = "غسيل خارجي للسيارة", Category = ShopCategory.CarWash, DefaultEstimatedDurationMinutes = 20, IsGloballyActive = true },
                    new() { ServiceCode = "TIRE_ROTATE", DefaultNameEn = "Tire Rotation", DefaultNameAr = "تدوير الإطارات", Category = ShopCategory.TireServices, DefaultEstimatedDurationMinutes = 30, IsGloballyActive = true },
                    new() { ServiceCode = "ENGINE_DIAG", DefaultNameEn = "Engine Diagnostics", DefaultNameAr = "تشخيص أعطال المحرك", Category = ShopCategory.Diagnostics, DefaultEstimatedDurationMinutes = 60, IsGloballyActive = true },
                    new() { ServiceCode = "GEN_MAINT_INSP", DefaultNameEn = "General Maintenance Inspection", DefaultNameAr = "فحص صيانة عام", Category = ShopCategory.GeneralMaintenance, DefaultEstimatedDurationMinutes = 90, IsGloballyActive = true }
                };
                if(globalServices.Any()) await context.GlobalServiceDefinitions.AddRangeAsync(globalServices);
                await context.SaveChangesAsync();
            }
            globalServices = await context.GlobalServiceDefinitions.AsNoTracking().ToListAsync();
            logger.LogInformation($"DataSeeder.SeedAsync: Loaded/Seeded {globalServices.Count} GlobalServiceDefinitions.");

            if (forceReseed || !await context.Shops.AnyAsync())
            {
                logger.LogInformation("DataSeeder.SeedAsync: Seeding shops, mapping to OAs using TargetOperationalAreaSlug...");
                var shopSeedDtos = LoadShopsFromFile("SeedData/shops_data.json", logger);
                var shopsToSave = new List<Shop>();
                if (shopSeedDtos != null && shopSeedDtos.Any())
                {
                    foreach (var dto in shopSeedDtos)
                    {
                        OperationalArea? targetOA = null;
                        if (!string.IsNullOrWhiteSpace(dto.TargetOperationalAreaSlug))
                        {
                            targetOA = operationalAreasMap.GetValueOrDefault(dto.TargetOperationalAreaSlug);
                        }
                        
                        if (targetOA == null) 
                        {
                            logger.LogError($"Shop '{dto.NameEn}' (OriginalRef: '{dto.OriginalCityRefSlug}') could NOT be mapped. TargetOperationalAreaSlug ('{dto.TargetOperationalAreaSlug}') was missing or did not match any created OA. SKIPPING SHOP. Available OA slugs: [{string.Join(", ", operationalAreasMap.Keys)}]");
                            continue; 
                        }

                        if (!Enum.TryParse<ShopCategory>(dto.Category, true, out var category)) category = ShopCategory.Unknown;
                        var shop = new Shop {
                            NameEn=dto.NameEn, NameAr=dto.NameAr, Address=dto.Address, Location=CreatePoint(dto.Latitude,dto.Longitude),
                            PhoneNumber=dto.PhoneNumber, ServicesOffered=dto.ServicesOffered, OpeningHours=dto.OpeningHours, Category=category,
                            OperationalAreaId=targetOA.Id, Slug=dto.Slug, LogoUrl=dto.LogoUrl, IsDeleted = false 
                        };
                        string initialSlug = string.IsNullOrWhiteSpace(shop.Slug) ? GenerateShopSlug(shop.NameEn, targetOA.Slug) : GenerateShopSlug(shop.Slug, targetOA.Slug);
                        shop.Slug = initialSlug; int c = 1;
                        while(shopsToSave.Any(s=>s.OperationalAreaId==targetOA.Id && s.Slug==shop.Slug) || 
                              await context.Shops.AnyAsync(s=>s.OperationalAreaId==targetOA.Id && s.Slug==shop.Slug))
                        { shop.Slug = $"{initialSlug}-{c++}"; }
                        if(string.IsNullOrEmpty(shop.LogoUrl)) shop.LogoUrl = GenerateLogoUrlFromName(shop.Slug);
                        shopsToSave.Add(shop);
                    }
                    if(shopsToSave.Any()) await context.Shops.AddRangeAsync(shopsToSave);
                    await context.SaveChangesAsync();
                    logger.LogInformation($"DataSeeder.SeedAsync: Seeded {shopsToSave.Count} shops.");
                } else { logger.LogWarning("DataSeeder.SeedAsync: No shop data loaded from shops_data.json.");}
            }
            
            List<Shop> finalSeededShops = await context.Shops.Where(s => !s.IsDeleted).AsNoTracking().ToListAsync();
            if ((forceReseed || !await context.ShopServices.AnyAsync()) && finalSeededShops.Any() && globalServices.Any())
            {
                var shopServicesToSeed = new List<ShopService>();
                var random = new Random();
                ShopService CreateShopSvc(Guid sId, GlobalServiceDefinition gsd, decimal p, string? nE=null, string? nA=null, int? customDuration=null) => 
                    new ShopService{ShopId=sId,GlobalServiceId=gsd.GlobalServiceId,CustomServiceNameEn=nE,CustomServiceNameAr=nA,EffectiveNameEn=!string.IsNullOrEmpty(nE)?nE:gsd.DefaultNameEn,EffectiveNameAr=!string.IsNullOrEmpty(nA)?nA:gsd.DefaultNameAr,Price=p,DurationMinutes=customDuration??gsd.DefaultEstimatedDurationMinutes,IsOfferedByShop=true,SortOrder=random.Next(1,100)};
                
                foreach(var s in finalSeededShops){ try {
                    var os=globalServices.FirstOrDefault(g=>g.ServiceCode=="OIL_CHANGE_STD"); var osy=globalServices.FirstOrDefault(g=>g.ServiceCode=="OIL_CHANGE_SYN"); var bf=globalServices.FirstOrDefault(g=>g.ServiceCode=="BRAKE_PAD_FRNT"); var ar=globalServices.FirstOrDefault(g=>g.ServiceCode=="AC_REGAS"); var cw=globalServices.FirstOrDefault(g=>g.ServiceCode=="CAR_WASH_EXT"); var tr=globalServices.FirstOrDefault(g=>g.ServiceCode=="TIRE_ROTATE"); var ed=globalServices.FirstOrDefault(g=>g.ServiceCode=="ENGINE_DIAG"); var gi=globalServices.FirstOrDefault(g=>g.ServiceCode=="GEN_MAINT_INSP");
                    if((s.Category==ShopCategory.GeneralMaintenance||s.Category==ShopCategory.OilChange)&&os!=null&&osy!=null){ if(os!=null)shopServicesToSeed.Add(CreateShopSvc(s.Id,os,Math.Round((decimal)(random.NextDouble()*100+250),2))); if(osy!=null)shopServicesToSeed.Add(CreateShopSvc(s.Id,osy,Math.Round((decimal)(random.NextDouble()*150+450),2),customDuration:50));}
                    if((s.Category==ShopCategory.GeneralMaintenance||s.Category==ShopCategory.Brakes)&&bf!=null) shopServicesToSeed.Add(CreateShopSvc(s.Id,bf,Math.Round((decimal)(random.NextDouble()*200+600),2)));
                    if((s.Category==ShopCategory.GeneralMaintenance||s.Category==ShopCategory.ACRepair)&&ar!=null) shopServicesToSeed.Add(CreateShopSvc(s.Id,ar,Math.Round((decimal)(random.NextDouble()*100+300),2)));
                    if(s.Category==ShopCategory.CarWash&&cw!=null) shopServicesToSeed.Add(CreateShopSvc(s.Id,cw,Math.Round((decimal)(random.NextDouble()*50+100),2),customDuration:25));
                    if(s.Category==ShopCategory.TireServices&&tr!=null) shopServicesToSeed.Add(CreateShopSvc(s.Id,tr,Math.Round((decimal)(random.NextDouble()*80+150),2)));
                    if((s.Category==ShopCategory.Diagnostics||s.Category==ShopCategory.GeneralMaintenance)&&ed!=null) shopServicesToSeed.Add(CreateShopSvc(s.Id,ed,Math.Round((decimal)(random.NextDouble()*150+200),2)));
                    if(s.Category==ShopCategory.GeneralMaintenance&&gi!=null) shopServicesToSeed.Add(CreateShopSvc(s.Id,gi,Math.Round((decimal)(random.NextDouble()*200+300),2),customDuration:100));
                    if(s.NameEn.Contains("Bosch",StringComparison.OrdinalIgnoreCase)) shopServicesToSeed.Add(new ShopService{ShopId=s.Id,CustomServiceNameEn="Bosch Premium Diagnostic Package",EffectiveNameEn="Bosch Premium Diagnostic Package",CustomServiceNameAr="باقة بوش التشخيصية الممتازة",EffectiveNameAr="باقة بوش التشخيصية الممتازة",Price=750.00m,DurationMinutes=120,IsOfferedByShop=true,SortOrder=5,IsPopularAtShop=true});
                } catch(Exception ex){logger.LogError(ex, $"DataSeeder.SeedAsync: Error creating services for shop {s.Id} ({s.NameEn})");}}
                if(shopServicesToSeed.Any()) await context.ShopServices.AddRangeAsync(shopServicesToSeed);
                await context.SaveChangesAsync();
                logger.LogInformation($"DataSeeder.SeedAsync: Seeded {shopServicesToSeed.Count} ShopServices.");
            }
            logger.LogInformation("DataSeeder.SeedAsync: Process complete.");
        }
    }

    public static class AttributesTableExtensions {
        public static string? GetOptionalString(this IAttributesTable table, string name) => (table==null||!table.Exists(name))?null:table[name]?.ToString();
    }
}
// // Data/DataSeeder.cs
// using AutomotiveServices.Api.Models;
// using AutomotiveServices.Api.Models.Seed;
// using Microsoft.EntityFrameworkCore;
// using Microsoft.Extensions.Logging;
// using NetTopologySuite.Features;
// using NetTopologySuite.Geometries;
// using NetTopologySuite.IO;
// using NetTopologySuite.Operation.Union;
// using System;
// using System.Collections.Generic;
// using System.IO;
// using System.Linq;
// using System.Text.Json;
// using System.Text.RegularExpressions;
// using System.Threading.Tasks;

// namespace AutomotiveServices.Api.Data
// {
//     public static class DataSeeder
//     {
//         private static readonly GeometryFactory _geometryFactory = new GeometryFactory(new PrecisionModel(), 4326);
//         private static readonly GeoJsonReader _geoJsonReader = new GeoJsonReader();
//         private static readonly JsonSerializerOptions _jsonSerializerOptions = new JsonSerializerOptions
//         {
//             PropertyNameCaseInsensitive = true,
//         };

//         private static Point CreatePoint(double latitude, double longitude) =>
//            _geometryFactory.CreatePoint(new Coordinate(longitude, latitude));

//         private static Geometry? EnsureMultiPolygon(Geometry? geometry, ILogger logger)
//         {
//             if (geometry == null) return null;
//             if (geometry is Polygon polygon)
//                 return _geometryFactory.CreateMultiPolygon(new[] { polygon.Copy() as Polygon });
//             if (geometry is MultiPolygon mp)
//                 return mp.Copy() as MultiPolygon;
//             logger.LogWarning($"EnsureMultiPolygon: Unexpected geometry type {geometry.GeometryType}. Copying as is.");
//             return geometry.Copy();
//         }

//         private static Geometry? CreateSimplifiedGeometry(Geometry? detailedGeometry, ILogger logger, double tolerance = 0.005)
//         {
//             if (detailedGeometry == null) return null;
//             try
//             {
//                 var geometryToSimplify = EnsureMultiPolygon(detailedGeometry, logger);
//                 if (geometryToSimplify == null) return null;
//                 var simplifier = new NetTopologySuite.Simplify.DouglasPeuckerSimplifier(geometryToSimplify) { DistanceTolerance = tolerance };
//                 return EnsureMultiPolygon(simplifier.GetResultGeometry(), logger);
//             }
//             catch (Exception ex)
//             {
//                 logger.LogError(ex, "CreateSimplifiedGeometry: Error simplifying geometry.");
//                 return EnsureMultiPolygon(detailedGeometry.Copy() as Geometry, logger);
//             }
//         }
        
//         private static string GenerateOperationalAreaSlug(string nameEn)
//         {
//             if (string.IsNullOrWhiteSpace(nameEn)) return $"oa-{Guid.NewGuid().ToString().Substring(0, 8)}";
//             string str = nameEn.ToLowerInvariant().Trim();
//             str = Regex.Replace(str, @"[^a-z0-9\s-]", ""); 
//             str = Regex.Replace(str, @"\s+", "-");          
//             str = Regex.Replace(str, @"-+", "-");           
//             str = str.Trim('-');                            
//             str = str.Length > 80 ? str.Substring(0, 80) : str; 
//             return string.IsNullOrEmpty(str) ? $"oa-{Guid.NewGuid().ToString().Substring(0, 8)}" : str; 
//         }

//         private static string GenerateShopSlug(string shopNameEn, string operationalAreaSlug)
//         {
//             if (string.IsNullOrWhiteSpace(shopNameEn)) shopNameEn = "shop";
//             operationalAreaSlug = string.IsNullOrWhiteSpace(operationalAreaSlug) ? "unknown-area" : operationalAreaSlug.Trim();
            
//             string namePart = shopNameEn.ToLowerInvariant().Trim();
//             namePart = Regex.Replace(namePart, @"[^a-z0-9\s-]", "");
//             namePart = Regex.Replace(namePart, @"\s+", "-"); namePart = Regex.Replace(namePart, @"-+", "-");
//             namePart = namePart.Trim('-');
//             namePart = namePart.Length > 60 ? namePart.Substring(0, 60) : namePart;
//             if (string.IsNullOrEmpty(namePart)) namePart = "shop";
            
//             string areaPart = operationalAreaSlug.ToLowerInvariant().Trim(); 
//             areaPart = areaPart.Length > 40 ? areaPart.Substring(0, 40) : areaPart;
//             if (string.IsNullOrEmpty(areaPart)) areaPart = "area";

//             return $"{namePart}-in-{areaPart}";
//         }

//         private static string GenerateLogoUrlFromName(string shopSpecificSlug) => $"/logos/{shopSpecificSlug}.png";

//         private static FeatureCollection? LoadFeatureCollectionFromFile(string relativePath, ILogger logger)
//         {
//             try
//             {
//                 string filePath = Path.Combine(AppContext.BaseDirectory, relativePath);
//                 if (!File.Exists(filePath)) { logger.LogError($"LoadFeatureCollectionFromFile: File not found: {filePath}"); return null; }
//                 return _geoJsonReader.Read<FeatureCollection>(File.ReadAllText(filePath));
//             }
//             catch (Exception ex) { logger.LogError(ex, $"LoadFeatureCollectionFromFile: Error loading {relativePath}"); return null; }
//         }

//         private static List<ShopSeedDto>? LoadShopsFromFile(string relativePath, ILogger logger)
//         {
//             try
//             {
//                 string filePath = Path.Combine(AppContext.BaseDirectory, relativePath);
//                 if (!File.Exists(filePath)) { logger.LogError($"LoadShopsFromFile: File not found: {filePath}"); return null; }
//                 var shops = JsonSerializer.Deserialize<List<ShopSeedDto>>(File.ReadAllText(filePath), _jsonSerializerOptions);
//                 logger.LogInformation($"LoadShopsFromFile: Loaded {shops?.Count ?? 0} shop records from {relativePath}.");
//                 return shops;
//             }
//             catch (Exception ex) { logger.LogError(ex, $"LoadShopsFromFile: Error loading {relativePath}"); return null; }
//         }

//         public static async Task SeedAsync(AppDbContext context, ILogger logger, bool forceReseed)
//         {
//             logger.LogInformation("DataSeeder.SeedAsync: Starting. ForceReseed: {ForceReseed}", forceReseed);
//             if (forceReseed)
//             {
//                 logger.LogInformation("DataSeeder.SeedAsync: Clearing relevant tables due to ForceReseed...");
//                 #pragma warning disable EF1002 
//                 await context.Database.ExecuteSqlRawAsync("ALTER TABLE \"Shops\" DROP CONSTRAINT IF EXISTS \"FK_Shops_OperationalAreas_OperationalAreaId\";");
//                 string[] tablesToDelete = {
//                     "\"ShopServices\"", "\"GlobalServiceDefinitions\"", "\"BookingItems\"", "\"Bookings\"",
//                     "\"UserCartItems\"", "\"AnonymousCartItems\"", "\"UserPreferences\"", "\"AnonymousUserPreferences\"",
//                     "\"Shops\"", "\"AdminAreaShopStats\"", "\"OperationalAreas\"", "\"AdministrativeBoundaries\"", "\"Cities\""
//                 };
//                 foreach (var table in tablesToDelete) {
//                     await context.Database.ExecuteSqlRawAsync($"DELETE FROM {table};");
//                 }
//                 #pragma warning restore EF1002
//                 logger.LogInformation("DataSeeder.SeedAsync: Tables cleared.");
//             }

//             if (forceReseed || !await context.Cities.AnyAsync())
//             {
//                 logger.LogInformation("DataSeeder.SeedAsync: Seeding 'Cities' (legacy reference)...");
//                 var cities = new List<City> {
//                     new() { NameEn = "CairoRef", NameAr = "القاهرة مرجع", Slug = "cairo-ref", StateProvince = "Cairo Governorate", Country="Egypt", Location = CreatePoint(30.0444, 31.2357) },
//                     new() { NameEn = "AlexandriaRef", NameAr = "الإسكندرية مرجع", Slug = "alexandria-ref", StateProvince = "Alexandria Governorate", Country="Egypt", Location = CreatePoint(31.2001, 29.9187) },
//                     new() { NameEn = "GizaRef", NameAr = "الجيزة مرجع", Slug = "giza-ref", StateProvince = "Giza Governorate", Country="Egypt", Location = CreatePoint(29.9870, 31.1313) },
//                     new() { NameEn = "6th October CityRef", NameAr = "مدينة 6 أكتوبر مرجع", Slug = "6th-october-city-ref", StateProvince = "Giza Governorate", Country="Egypt", Location = CreatePoint(29.9660, 30.9232) },
//                     new() { NameEn = "New CairoRef", NameAr = "القاهرة الجديدة مرجع", Slug = "new-cairo-ref", StateProvince = "Cairo Governorate", Country="Egypt", Location = CreatePoint(30.0271, 31.4961) }
//                 };
//                 if(cities.Any()) await context.Cities.AddRangeAsync(cities);
//                 await context.SaveChangesAsync();
//                 logger.LogInformation($"DataSeeder.SeedAsync: Seeded {cities.Count} legacy cities.");
//             }

//             Dictionary<string, AdministrativeBoundary> seededGovernoratesByGid = new();
//             Dictionary<string, AdministrativeBoundary> seededLevel2BoundariesByGid = new(); 

//             const string cairoGovGidConst = "EGY.11_1"; //verified
//             const string gizaGovGidConst  = "EGY.8_1";  //verified
//             // const string alexGovGidConst  = "EGY.6_1";  

//             if (forceReseed || !await context.AdministrativeBoundaries.AnyAsync())
//             {
//                 logger.LogInformation("DataSeeder.SeedAsync: Seeding AdministrativeBoundaries from GADM files...");
//                 var adminL1Features = LoadFeatureCollectionFromFile("SeedData/gadm41_EGY_1.json", logger);
//                 if (adminL1Features != null) {
//                     var governoratesToSeed = adminL1Features.Select(f => {
//                         var p = f.Attributes; string? nE=p.GetOptionalString("NAME_1"), nA=p.GetOptionalString("NL_NAME_1"), g1=p.GetOptionalString("GID_1");
//                         if(string.IsNullOrEmpty(nE)||string.IsNullOrEmpty(g1)) return null; var geom=EnsureMultiPolygon(f.Geometry,logger);
//                         return new AdministrativeBoundary { NameEn=nE, NameAr=!string.IsNullOrWhiteSpace(nA)&&nA!="NA"?nA:nE, AdminLevel=1, CountryCode="EG", OfficialCode=g1, Boundary=geom, SimplifiedBoundary=CreateSimplifiedGeometry(geom,logger,0.01), Centroid=geom?.Centroid as Point, IsActive=true, CreatedAtUtc=DateTime.UtcNow, UpdatedAtUtc=DateTime.UtcNow };
//                     }).Where(ab => ab != null).ToList();
//                     if(governoratesToSeed.Any()) await context.AdministrativeBoundaries.AddRangeAsync(governoratesToSeed!); await context.SaveChangesAsync();
//                 }
//             }
//             seededGovernoratesByGid = await context.AdministrativeBoundaries.Where(ab => ab.AdminLevel == 1 && ab.CountryCode == "EG").AsNoTracking().ToDictionaryAsync(ab => ab.OfficialCode!, ab => ab);
//             logger.LogInformation($"DataSeeder.SeedAsync: Loaded {seededGovernoratesByGid.Count} L1 AdminBoundaries (Governorates).");

//             if (forceReseed || !await context.AdministrativeBoundaries.AnyAsync(ab => ab.AdminLevel == 2))
//             {
//                 var adminL2Features = LoadFeatureCollectionFromFile("SeedData/gadm41_EGY_2.json", logger);
//                 if (adminL2Features != null && seededGovernoratesByGid.Any()) { 
//                     var citiesToSeed = adminL2Features.Select(f => {
//                         var p=f.Attributes; string? nE=p.GetOptionalString("NAME_2"), nA=p.GetOptionalString("NL_NAME_2"), g2=p.GetOptionalString("GID_2"), pG1=p.GetOptionalString("GID_1");
//                         if(string.IsNullOrEmpty(nE)||string.IsNullOrEmpty(g2)||string.IsNullOrEmpty(pG1)||!seededGovernoratesByGid.TryGetValue(pG1, out var parent)) return null; var geom=EnsureMultiPolygon(f.Geometry,logger);
//                         return new AdministrativeBoundary { NameEn=nE, NameAr=!string.IsNullOrWhiteSpace(nA)&&nA!="NA"?nA:nE, AdminLevel=2, ParentId=parent.Id, CountryCode="EG", OfficialCode=g2, Boundary=geom, SimplifiedBoundary=CreateSimplifiedGeometry(geom,logger,0.001), Centroid=geom?.Centroid as Point, IsActive=true, CreatedAtUtc=DateTime.UtcNow, UpdatedAtUtc=DateTime.UtcNow };
//                     }).Where(ab => ab != null).ToList();
//                     if(citiesToSeed.Any()) await context.AdministrativeBoundaries.AddRangeAsync(citiesToSeed!); await context.SaveChangesAsync();
//                 } else if (adminL2Features != null) { logger.LogWarning("DataSeeder.SeedAsync: GADM L2 data found, but no L1 Governorates were loaded/available to link them.");}
//             }
//             seededLevel2BoundariesByGid = await context.AdministrativeBoundaries.Where(ab => ab.AdminLevel == 2 && ab.CountryCode == "EG").AsNoTracking().ToDictionaryAsync(ab => ab.OfficialCode!, ab => ab);
//             logger.LogInformation($"DataSeeder.SeedAsync: Loaded {seededLevel2BoundariesByGid.Count} L2 AdminBoundaries (Cities/Markazes).");

//             Dictionary<string, OperationalArea> operationalAreasMap = new();
//             if (forceReseed || !await context.OperationalAreas.AnyAsync())
//             {
//                 logger.LogInformation("DataSeeder.SeedAsync: Seeding GRANULAR OperationalAreas (No Governorate-wide OAs)...");
//                 var oasToSeed = new List<OperationalArea>();
                
//                 AdministrativeBoundary? cairoGovForContext = seededGovernoratesByGid.GetValueOrDefault(cairoGovGidConst);
//                 AdministrativeBoundary? gizaGovForContext  = seededGovernoratesByGid.GetValueOrDefault(gizaGovGidConst);
//                 // Alexandria Gov might be needed if you define specific Alexandria district OAs

//                 // 1. Operational Areas from UNION of GADM Level 2 parts
//                 var ncGids=new[]{"EGY.11.33_1","EGY.11.34_1","EGY.11.35_1"}; // VERIFIED
//                 logger.LogInformation("DataSeeder.SeedAsync: Creating 'New Cairo District' OA using GID_2s: {Gids}", string.Join(", ", ncGids));
//                 var ncGeoms=ncGids.Select(gid=>{ var b=seededLevel2BoundariesByGid.GetValueOrDefault(gid); if(b==null)logger.LogWarning($"GADM L2 for NC GID {gid} NOT FOUND."); else if(b.Boundary==null)logger.LogWarning($"GADM L2 for NC GID {gid} has NULL Boundary."); return b?.Boundary; }).Where(g=>g!=null).ToList();
//                 if(ncGeoms.Any()&&cairoGovForContext!=null){ Geometry? ug=ncGeoms.Count==1?ncGeoms.First()!.Copy():new GeometryCollection(ncGeoms.ToArray()!).Union(); if(ug!=null&&!ug.IsEmpty) { var slug=GenerateOperationalAreaSlug("new-cairo-district"); logger.LogInformation("DataSeeder.SeedAsync: Creating 'New Cairo District' OA with slug '{Slug}'.",slug); oasToSeed.Add(new OperationalArea{NameEn="New Cairo District",NameAr="القاهرة الجديدة",Slug=slug,CentroidLatitude=ug.Centroid?.Y??30.0271,CentroidLongitude=ug.Centroid?.X??31.4961,DefaultSearchRadiusMeters=15000,GeometrySource=GeometrySourceType.Custom,CustomBoundary=EnsureMultiPolygon(ug,logger),CustomSimplifiedBoundary=CreateSimplifiedGeometry(ug,logger,0.004),PrimaryAdministrativeBoundaryId=cairoGovForContext.Id,DisplayLevel="AggregatedUrbanArea",IsActive=true});}} else {logger.LogWarning("DataSeeder.SeedAsync: Failed to create New Cairo District OA; GADM L2 parts or parent Cairo Gov missing. Geoms found: {Count}, Cairo Gov Exists: {CairoExists}", ncGeoms.Count, cairoGovForContext != null);}

//                 var soGids=new[]{"EGY.8.17_1","EGY.8.18_1"}; // VERIFIED
//                 logger.LogInformation("DataSeeder.SeedAsync: Creating '6th of October City' OA using GID_2s: {Gids}", string.Join(", ", soGids));
//                 var soGeoms=soGids.Select(gid=>{ var b=seededLevel2BoundariesByGid.GetValueOrDefault(gid); if(b==null)logger.LogWarning($"GADM L2 for 6Oct GID {gid} NOT FOUND."); else if(b.Boundary==null)logger.LogWarning($"GADM L2 for 6Oct GID {gid} has NULL Boundary."); return b?.Boundary; }).Where(g=>g!=null).ToList();
//                 if(soGeoms.Any()&&gizaGovForContext!=null){ Geometry? ug=soGeoms.Count==1?soGeoms.First()!.Copy():new GeometryCollection(soGeoms.ToArray()!).Union(); if(ug!=null&&!ug.IsEmpty) { var slug = "6th-of-october-city"; logger.LogInformation("DataSeeder.SeedAsync: Creating '6th of October City' OA with PREDEFINED slug '{Slug}'.",slug); oasToSeed.Add(new OperationalArea{NameEn="6th of October City",NameAr="مدينة السادس من أكتوبر",Slug=slug,CentroidLatitude=ug.Centroid?.Y??29.9660,CentroidLongitude=ug.Centroid?.X??30.9232,DefaultSearchRadiusMeters=18000,GeometrySource=GeometrySourceType.Custom,CustomBoundary=EnsureMultiPolygon(ug,logger),CustomSimplifiedBoundary=CreateSimplifiedGeometry(ug,logger,0.004),PrimaryAdministrativeBoundaryId=gizaGovForContext.Id,DisplayLevel="MajorNewCity",IsActive=true});}} else {logger.LogWarning("DataSeeder.SeedAsync: Failed to create 6th of October City OA; GADM L2 parts or parent Giza Gov missing. Geoms found: {Count}, Giza Gov Exists: {GizaExists}", soGeoms.Count, gizaGovForContext != null);}

//                 // 2. Operational Areas from specific GADM Level 2 entities (Districts/Cities)
//                 var distGids=new Dictionary<string,(string NE,string NA,string PGID_Const_For_Context,string DL,double DR,double Flat,double Flon)>{
//                     // --- VERIFY ALL THESE GID_2s and their Parent GIDs ---
//                     {"EGY.11.26_1",("Bulaq","بلاق",cairoGovGidConst,"District",5,30.00,31.25)}, 
//                     // {"EGY.11.48_1",("Zamalek","الزمالك",cairoGovGidConst,"District",3,30.06,31.22)},
//                     {"EGY.11.17_1",("As-Salam","السلام",cairoGovGidConst,"District",7,30.09,31.32)}, 
//                     {"EGY.11.31_1",("Nasr City","مدينة نصر",cairoGovGidConst,"District",10,30.05,31.33)},
//                     {"EGY.8.10_1",("As-Saff","المهندسين",gizaGovGidConst,"District",4,30.055,31.20)}, 
//                     {"EGY.8.6_1",("Al-Badrashayn","الدقي",gizaGovGidConst,"District",3,30.04,31.205)},

//                     { "EGY.8.16_1", ("Sheikh Zayed City", "مدينة الشيخ زايد", gizaGovGidConst, "MajorNewCity", 12, 29.9950, 30.9900) }, 
//                 }; 
//                 foreach(var kvp in distGids){ 
//                     AdministrativeBoundary? parentGovContext = seededGovernoratesByGid.GetValueOrDefault(kvp.Value.PGID_Const_For_Context); 
//                     if(parentGovContext != null && seededLevel2BoundariesByGid.TryGetValue(kvp.Key,out var abL2)&&abL2?.Boundary!=null) 
//                         oasToSeed.Add(new OperationalArea{
//                             NameEn=kvp.Value.NE,NameAr=kvp.Value.NA,Slug=GenerateOperationalAreaSlug(kvp.Value.NE),
//                             CentroidLatitude=abL2.Centroid?.Y??kvp.Value.Flat,CentroidLongitude=abL2.Centroid?.X??kvp.Value.Flon,
//                             DefaultSearchRadiusMeters=kvp.Value.DR*1000,GeometrySource=GeometrySourceType.DerivedFromAdmin,
//                             PrimaryAdministrativeBoundaryId=abL2.Id, // Link to the GADM L2 entity itself
//                             DisplayLevel=kvp.Value.DL,IsActive=true
//                         }); 
//                     else logger.LogWarning($"DataSeeder.SeedAsync: Failed for District OA GID: {kvp.Key} ('{kvp.Value.NE}'). L2 entity, boundary, or its context parent Gov (Ref: {kvp.Value.PGID_Const_For_Context}) missing.");
//                 }

//                 if(oasToSeed.Any()) await context.OperationalAreas.AddRangeAsync(oasToSeed);
//                 await context.SaveChangesAsync();
//                 operationalAreasMap = await context.OperationalAreas.AsNoTracking().ToDictionaryAsync(oa => oa.Slug, oa => oa);
//                 logger.LogInformation($"DataSeeder.SeedAsync: Seeded {operationalAreasMap.Count} granular OperationalAreas. Keys: [{string.Join(", ", operationalAreasMap.Keys)}]");
//             } else { operationalAreasMap = await context.OperationalAreas.AsNoTracking().ToDictionaryAsync(oa => oa.Slug, oa => oa); }

//             List<GlobalServiceDefinition> globalServices = new();
//             if (forceReseed || !await context.GlobalServiceDefinitions.AnyAsync()) { 
//                 globalServices = new List<GlobalServiceDefinition> {
//                     new() { ServiceCode = "OIL_CHANGE_STD", DefaultNameEn = "Standard Oil Change", DefaultNameAr = "تغيير زيت قياسي", Category = ShopCategory.OilChange, DefaultEstimatedDurationMinutes = 30, IsGloballyActive = true },
//                     new() { ServiceCode = "OIL_CHANGE_SYN", DefaultNameEn = "Synthetic Oil Change", DefaultNameAr = "تغيير زيت تخليقي", Category = ShopCategory.OilChange, DefaultEstimatedDurationMinutes = 45, IsGloballyActive = true },
//                     new() { ServiceCode = "BRAKE_PAD_FRNT", DefaultNameEn = "Front Brake Pad Replacement", DefaultNameAr = "تغيير تيل الفرامل الأمامي", Category = ShopCategory.Brakes, DefaultEstimatedDurationMinutes = 60, IsGloballyActive = true },
//                     new() { ServiceCode = "AC_REGAS", DefaultNameEn = "A/C Re-gas", DefaultNameAr = "إعادة شحن فريون التكييف", Category = ShopCategory.ACRepair, DefaultEstimatedDurationMinutes = 45, IsGloballyActive = true },
//                     new() { ServiceCode = "CAR_WASH_EXT", DefaultNameEn = "Exterior Car Wash", DefaultNameAr = "غسيل خارجي للسيارة", Category = ShopCategory.CarWash, DefaultEstimatedDurationMinutes = 20, IsGloballyActive = true },
//                     new() { ServiceCode = "TIRE_ROTATE", DefaultNameEn = "Tire Rotation", DefaultNameAr = "تدوير الإطارات", Category = ShopCategory.TireServices, DefaultEstimatedDurationMinutes = 30, IsGloballyActive = true },
//                     new() { ServiceCode = "ENGINE_DIAG", DefaultNameEn = "Engine Diagnostics", DefaultNameAr = "تشخيص أعطال المحرك", Category = ShopCategory.Diagnostics, DefaultEstimatedDurationMinutes = 60, IsGloballyActive = true },
//                     new() { ServiceCode = "GEN_MAINT_INSP", DefaultNameEn = "General Maintenance Inspection", DefaultNameAr = "فحص صيانة عام", Category = ShopCategory.GeneralMaintenance, DefaultEstimatedDurationMinutes = 90, IsGloballyActive = true }
//                 };
//                 if(globalServices.Any()) await context.GlobalServiceDefinitions.AddRangeAsync(globalServices);
//                 await context.SaveChangesAsync();
//             }
//             globalServices = await context.GlobalServiceDefinitions.AsNoTracking().ToListAsync();
//             logger.LogInformation($"DataSeeder.SeedAsync: Loaded/Seeded {globalServices.Count} GlobalServiceDefinitions.");

//             if (forceReseed || !await context.Shops.AnyAsync())
//             {
//                 logger.LogInformation("DataSeeder.SeedAsync: Seeding shops, mapping to granular OAs using TargetOperationalAreaSlug...");
//                 var shopSeedDtos = LoadShopsFromFile("SeedData/shops_data.json", logger);
//                 var shopsToSave = new List<Shop>();
//                 if (shopSeedDtos != null && shopSeedDtos.Any())
//                 {
//                     foreach (var dto in shopSeedDtos)
//                     {
//                         OperationalArea? targetOA = null;
//                         if (!string.IsNullOrWhiteSpace(dto.TargetOperationalAreaSlug))
//                         {
//                             targetOA = operationalAreasMap.GetValueOrDefault(dto.TargetOperationalAreaSlug);
//                         }
                        
//                         if (targetOA == null) 
//                         {
//                             // If still null after direct TargetOperationalAreaSlug lookup, this shop cannot be mapped
//                             // as we no longer have broad governorate OAs to fall back to by default.
//                             logger.LogError($"Shop '{dto.NameEn}' (OriginalRef: '{dto.OriginalCityRefSlug}') could NOT be mapped. TargetOperationalAreaSlug ('{dto.TargetOperationalAreaSlug}') was missing or did not match any created granular OA. SKIPPING SHOP.");
//                             continue; 
//                         }

//                         if (!Enum.TryParse<ShopCategory>(dto.Category, true, out var category)) category = ShopCategory.Unknown;
//                         var shop = new Shop {
//                             NameEn=dto.NameEn, NameAr=dto.NameAr, Address=dto.Address, Location=CreatePoint(dto.Latitude,dto.Longitude),
//                             PhoneNumber=dto.PhoneNumber, ServicesOffered=dto.ServicesOffered, OpeningHours=dto.OpeningHours, Category=category,
//                             OperationalAreaId=targetOA.Id, Slug=dto.Slug, LogoUrl=dto.LogoUrl, IsDeleted = false 
//                         };
//                         string initialSlug = string.IsNullOrWhiteSpace(shop.Slug) ? GenerateShopSlug(shop.NameEn, targetOA.Slug) : GenerateShopSlug(shop.Slug, targetOA.Slug);
//                         shop.Slug = initialSlug; int c = 1;
//                         while(shopsToSave.Any(s=>s.OperationalAreaId==targetOA.Id && s.Slug==shop.Slug) || 
//                               await context.Shops.AnyAsync(s=>s.OperationalAreaId==targetOA.Id && s.Slug==shop.Slug))
//                         { shop.Slug = $"{initialSlug}-{c++}"; }
//                         if(string.IsNullOrEmpty(shop.LogoUrl)) shop.LogoUrl = GenerateLogoUrlFromName(shop.Slug);
//                         shopsToSave.Add(shop);
//                     }
//                     if(shopsToSave.Any()) await context.Shops.AddRangeAsync(shopsToSave);
//                     await context.SaveChangesAsync();
//                     logger.LogInformation($"DataSeeder.SeedAsync: Seeded {shopsToSave.Count} shops.");
//                 } else { logger.LogWarning("DataSeeder.SeedAsync: No shop data loaded from shops_data.json.");}
//             }
            
//             List<Shop> finalSeededShops = await context.Shops.Where(s => !s.IsDeleted).AsNoTracking().ToListAsync();
//             if ((forceReseed || !await context.ShopServices.AnyAsync()) && finalSeededShops.Any() && globalServices.Any())
//             {
//                 var shopServicesToSeed = new List<ShopService>();
//                 var random = new Random();
//                 ShopService CreateShopSvc(Guid sId, GlobalServiceDefinition gsd, decimal p, string? nE=null, string? nA=null, int? customDuration=null) => 
//                     new ShopService{ShopId=sId,GlobalServiceId=gsd.GlobalServiceId,CustomServiceNameEn=nE,CustomServiceNameAr=nA,EffectiveNameEn=!string.IsNullOrEmpty(nE)?nE:gsd.DefaultNameEn,EffectiveNameAr=!string.IsNullOrEmpty(nA)?nA:gsd.DefaultNameAr,Price=p,DurationMinutes=customDuration??gsd.DefaultEstimatedDurationMinutes,IsOfferedByShop=true,SortOrder=random.Next(1,100)};
                
//                 foreach(var s in finalSeededShops){ try {
//                     var os=globalServices.FirstOrDefault(g=>g.ServiceCode=="OIL_CHANGE_STD"); var osy=globalServices.FirstOrDefault(g=>g.ServiceCode=="OIL_CHANGE_SYN"); var bf=globalServices.FirstOrDefault(g=>g.ServiceCode=="BRAKE_PAD_FRNT"); var ar=globalServices.FirstOrDefault(g=>g.ServiceCode=="AC_REGAS"); var cw=globalServices.FirstOrDefault(g=>g.ServiceCode=="CAR_WASH_EXT"); var tr=globalServices.FirstOrDefault(g=>g.ServiceCode=="TIRE_ROTATE"); var ed=globalServices.FirstOrDefault(g=>g.ServiceCode=="ENGINE_DIAG"); var gi=globalServices.FirstOrDefault(g=>g.ServiceCode=="GEN_MAINT_INSP");
//                     if((s.Category==ShopCategory.GeneralMaintenance||s.Category==ShopCategory.OilChange)&&os!=null&&osy!=null){ if(os!=null)shopServicesToSeed.Add(CreateShopSvc(s.Id,os,Math.Round((decimal)(random.NextDouble()*100+250),2))); if(osy!=null)shopServicesToSeed.Add(CreateShopSvc(s.Id,osy,Math.Round((decimal)(random.NextDouble()*150+450),2),customDuration:50));}
//                     if((s.Category==ShopCategory.GeneralMaintenance||s.Category==ShopCategory.Brakes)&&bf!=null) shopServicesToSeed.Add(CreateShopSvc(s.Id,bf,Math.Round((decimal)(random.NextDouble()*200+600),2)));
//                     if((s.Category==ShopCategory.GeneralMaintenance||s.Category==ShopCategory.ACRepair)&&ar!=null) shopServicesToSeed.Add(CreateShopSvc(s.Id,ar,Math.Round((decimal)(random.NextDouble()*100+300),2)));
//                     if(s.Category==ShopCategory.CarWash&&cw!=null) shopServicesToSeed.Add(CreateShopSvc(s.Id,cw,Math.Round((decimal)(random.NextDouble()*50+100),2),customDuration:25));
//                     if(s.Category==ShopCategory.TireServices&&tr!=null) shopServicesToSeed.Add(CreateShopSvc(s.Id,tr,Math.Round((decimal)(random.NextDouble()*80+150),2)));
//                     if((s.Category==ShopCategory.Diagnostics||s.Category==ShopCategory.GeneralMaintenance)&&ed!=null) shopServicesToSeed.Add(CreateShopSvc(s.Id,ed,Math.Round((decimal)(random.NextDouble()*150+200),2)));
//                     if(s.Category==ShopCategory.GeneralMaintenance&&gi!=null) shopServicesToSeed.Add(CreateShopSvc(s.Id,gi,Math.Round((decimal)(random.NextDouble()*200+300),2),customDuration:100));
//                     if(s.NameEn.Contains("Bosch",StringComparison.OrdinalIgnoreCase)) shopServicesToSeed.Add(new ShopService{ShopId=s.Id,CustomServiceNameEn="Bosch Premium Diagnostic Package",EffectiveNameEn="Bosch Premium Diagnostic Package",CustomServiceNameAr="باقة بوش التشخيصية الممتازة",EffectiveNameAr="باقة بوش التشخيصية الممتازة",Price=750.00m,DurationMinutes=120,IsOfferedByShop=true,SortOrder=5,IsPopularAtShop=true});
//                 } catch(Exception ex){logger.LogError(ex, $"DataSeeder.SeedAsync: Error creating services for shop {s.Id} ({s.NameEn})");}}
//                 if(shopServicesToSeed.Any()) await context.ShopServices.AddRangeAsync(shopServicesToSeed);
//                 await context.SaveChangesAsync();
//                 logger.LogInformation($"DataSeeder.SeedAsync: Seeded {shopServicesToSeed.Count} ShopServices.");
//             }
//             logger.LogInformation("DataSeeder.SeedAsync: Process complete.");
//         }
//     }

//     public static class AttributesTableExtensions {
//         public static string? GetOptionalString(this IAttributesTable table, string name) => (table==null||!table.Exists(name))?null:table[name]?.ToString();
//     }
// }
// // // Data/DataSeeder.cs
// // using AutomotiveServices.Api.Models;
// // using AutomotiveServices.Api.Models.Seed;
// // using Microsoft.EntityFrameworkCore;
// // using Microsoft.Extensions.Logging;
// // using NetTopologySuite.Features;
// // using NetTopologySuite.Geometries;
// // using NetTopologySuite.IO;
// // using NetTopologySuite.Operation.Union;
// // using System;
// // using System.Collections.Generic;
// // using System.IO;
// // using System.Linq;
// // using System.Text.Json;
// // using System.Text.RegularExpressions;
// // using System.Threading.Tasks;

// // namespace AutomotiveServices.Api.Data
// // {
// //     public static class DataSeeder
// //     {
// //         private static readonly GeometryFactory _geometryFactory = new GeometryFactory(new PrecisionModel(), 4326);
// //         private static readonly GeoJsonReader _geoJsonReader = new GeoJsonReader();
// //         private static readonly JsonSerializerOptions _jsonSerializerOptions = new JsonSerializerOptions
// //         {
// //             PropertyNameCaseInsensitive = true,
// //         };

// //         private static Point CreatePoint(double latitude, double longitude) =>
// //            _geometryFactory.CreatePoint(new Coordinate(longitude, latitude));

// //         private static Geometry? EnsureMultiPolygon(Geometry? geometry, ILogger logger)
// //         {
// //             if (geometry == null) return null;
// //             if (geometry is Polygon polygon)
// //                 return _geometryFactory.CreateMultiPolygon(new[] { polygon.Copy() as Polygon });
// //             if (geometry is MultiPolygon mp)
// //                 return mp.Copy() as MultiPolygon;
// //             logger.LogWarning($"EnsureMultiPolygon: Unexpected geometry type {geometry.GeometryType}. Copying as is.");
// //             return geometry.Copy();
// //         }

// //         private static Geometry? CreateSimplifiedGeometry(Geometry? detailedGeometry, ILogger logger, double tolerance = 0.005)
// //         {
// //             if (detailedGeometry == null) return null;
// //             try
// //             {
// //                 var geometryToSimplify = EnsureMultiPolygon(detailedGeometry, logger);
// //                 if (geometryToSimplify == null) return null;
// //                 var simplifier = new NetTopologySuite.Simplify.DouglasPeuckerSimplifier(geometryToSimplify) { DistanceTolerance = tolerance };
// //                 return EnsureMultiPolygon(simplifier.GetResultGeometry(), logger);
// //             }
// //             catch (Exception ex)
// //             {
// //                 logger.LogError(ex, "CreateSimplifiedGeometry: Error simplifying geometry.");
// //                 return EnsureMultiPolygon(detailedGeometry.Copy() as Geometry, logger);
// //             }
// //         }
        
// //         private static string GenerateOperationalAreaSlug(string nameEn)
// //         {
// //             if (string.IsNullOrWhiteSpace(nameEn)) return $"oa-{Guid.NewGuid().ToString().Substring(0, 8)}";
// //             string str = nameEn.ToLowerInvariant().Trim();
// //             str = Regex.Replace(str, @"[^a-z0-9\s-]", ""); 
// //             str = Regex.Replace(str, @"\s+", "-");          
// //             str = Regex.Replace(str, @"-+", "-");           
// //             str = str.Trim('-');                            
// //             str = str.Length > 80 ? str.Substring(0, 80) : str; 
// //             return string.IsNullOrEmpty(str) ? $"oa-{Guid.NewGuid().ToString().Substring(0, 8)}" : str; 
// //         }

// //         private static string GenerateShopSlug(string shopNameEn, string operationalAreaSlug)
// //         {
// //             if (string.IsNullOrWhiteSpace(shopNameEn)) shopNameEn = "shop";
// //             operationalAreaSlug = string.IsNullOrWhiteSpace(operationalAreaSlug) ? "unknown-area" : operationalAreaSlug.Trim();
            
// //             string namePart = shopNameEn.ToLowerInvariant().Trim();
// //             namePart = Regex.Replace(namePart, @"[^a-z0-9\s-]", "");
// //             namePart = Regex.Replace(namePart, @"\s+", "-"); namePart = Regex.Replace(namePart, @"-+", "-");
// //             namePart = namePart.Trim('-');
// //             namePart = namePart.Length > 60 ? namePart.Substring(0, 60) : namePart;
// //             if (string.IsNullOrEmpty(namePart)) namePart = "shop";
            
// //             string areaPart = operationalAreaSlug.ToLowerInvariant().Trim(); 
// //             areaPart = areaPart.Length > 40 ? areaPart.Substring(0, 40) : areaPart;
// //             if (string.IsNullOrEmpty(areaPart)) areaPart = "area";

// //             return $"{namePart}-in-{areaPart}";
// //         }

// //         private static string GenerateLogoUrlFromName(string shopSpecificSlug) => $"/logos/{shopSpecificSlug}.png";

// //         private static FeatureCollection? LoadFeatureCollectionFromFile(string relativePath, ILogger logger)
// //         {
// //             try
// //             {
// //                 string filePath = Path.Combine(AppContext.BaseDirectory, relativePath);
// //                 if (!File.Exists(filePath)) { logger.LogError($"LoadFeatureCollectionFromFile: File not found: {filePath}"); return null; }
// //                 return _geoJsonReader.Read<FeatureCollection>(File.ReadAllText(filePath));
// //             }
// //             catch (Exception ex) { logger.LogError(ex, $"LoadFeatureCollectionFromFile: Error loading {relativePath}"); return null; }
// //         }

// //         private static List<ShopSeedDto>? LoadShopsFromFile(string relativePath, ILogger logger)
// //         {
// //             try
// //             {
// //                 string filePath = Path.Combine(AppContext.BaseDirectory, relativePath);
// //                 if (!File.Exists(filePath)) { logger.LogError($"LoadShopsFromFile: File not found: {filePath}"); return null; }
// //                 var shops = JsonSerializer.Deserialize<List<ShopSeedDto>>(File.ReadAllText(filePath), _jsonSerializerOptions);
// //                 logger.LogInformation($"LoadShopsFromFile: Loaded {shops?.Count ?? 0} shop records from {relativePath}.");
// //                 return shops;
// //             }
// //             catch (Exception ex) { logger.LogError(ex, $"LoadShopsFromFile: Error loading {relativePath}"); return null; }
// //         }

// //         public static async Task SeedAsync(AppDbContext context, ILogger logger, bool forceReseed)
// //         {
// //             logger.LogInformation("DataSeeder.SeedAsync: Starting. ForceReseed: {ForceReseed}", forceReseed);
// //             if (forceReseed)
// //             {
// //                 logger.LogInformation("DataSeeder.SeedAsync: Clearing relevant tables due to ForceReseed...");
// //                 #pragma warning disable EF1002 
// //                 await context.Database.ExecuteSqlRawAsync("ALTER TABLE \"Shops\" DROP CONSTRAINT IF EXISTS \"FK_Shops_OperationalAreas_OperationalAreaId\";");
// //                 string[] tablesToDelete = {
// //                     "\"ShopServices\"", "\"GlobalServiceDefinitions\"", "\"BookingItems\"", "\"Bookings\"",
// //                     "\"UserCartItems\"", "\"AnonymousCartItems\"", "\"UserPreferences\"", "\"AnonymousUserPreferences\"",
// //                     "\"Shops\"", "\"AdminAreaShopStats\"", "\"OperationalAreas\"", "\"AdministrativeBoundaries\"", "\"Cities\""
// //                 };
// //                 foreach (var table in tablesToDelete) {
// //                     await context.Database.ExecuteSqlRawAsync($"DELETE FROM {table};");
// //                 }
// //                 #pragma warning restore EF1002
// //                 logger.LogInformation("DataSeeder.SeedAsync: Tables cleared.");
// //             }

// //             if (forceReseed || !await context.Cities.AnyAsync())
// //             {
// //                 logger.LogInformation("DataSeeder.SeedAsync: Seeding 'Cities' (legacy reference)...");
// //                 var cities = new List<City> {
// //                     new() { NameEn = "CairoRef", NameAr = "القاهرة مرجع", Slug = "cairo-ref", StateProvince = "Cairo Governorate", Country="Egypt", Location = CreatePoint(30.0444, 31.2357) },
// //                     new() { NameEn = "AlexandriaRef", NameAr = "الإسكندرية مرجع", Slug = "alexandria-ref", StateProvince = "Alexandria Governorate", Country="Egypt", Location = CreatePoint(31.2001, 29.9187) },
// //                     new() { NameEn = "GizaRef", NameAr = "الجيزة مرجع", Slug = "giza-ref", StateProvince = "Giza Governorate", Country="Egypt", Location = CreatePoint(29.9870, 31.1313) },
// //                     new() { NameEn = "6th October CityRef", NameAr = "مدينة 6 أكتوبر مرجع", Slug = "6th-october-city-ref", StateProvince = "Giza Governorate", Country="Egypt", Location = CreatePoint(29.9660, 30.9232) },
// //                     new() { NameEn = "New CairoRef", NameAr = "القاهرة الجديدة مرجع", Slug = "new-cairo-ref", StateProvince = "Cairo Governorate", Country="Egypt", Location = CreatePoint(30.0271, 31.4961) }
// //                 };
// //                 if(cities.Any()) await context.Cities.AddRangeAsync(cities);
// //                 await context.SaveChangesAsync();
// //                 logger.LogInformation($"DataSeeder.SeedAsync: Seeded {cities.Count} legacy cities.");
// //             }

// //             Dictionary<string, AdministrativeBoundary> seededGovernoratesByGid = new();
// //             Dictionary<string, AdministrativeBoundary> seededLevel2BoundariesByGid = new(); 

// //             const string cairoGovGidConst = "EGY.11_1"; 
// //             const string gizaGovGidConst  = "EGY.8_1";  
// //             const string alexGovGidConst  = "EGY.6_1";  

// //             if (forceReseed || !await context.AdministrativeBoundaries.AnyAsync())
// //             {
// //                 logger.LogInformation("DataSeeder.SeedAsync: Seeding AdministrativeBoundaries from GADM files...");
// //                 var adminL1Features = LoadFeatureCollectionFromFile("SeedData/gadm41_EGY_1.json", logger);
// //                 if (adminL1Features != null) {
// //                     var governoratesToSeed = adminL1Features.Select(f => {
// //                         var p = f.Attributes; string? nE=p.GetOptionalString("NAME_1"), nA=p.GetOptionalString("NL_NAME_1"), g1=p.GetOptionalString("GID_1");
// //                         if(string.IsNullOrEmpty(nE)||string.IsNullOrEmpty(g1)) return null; var geom=EnsureMultiPolygon(f.Geometry,logger);
// //                         return new AdministrativeBoundary { NameEn=nE, NameAr=!string.IsNullOrWhiteSpace(nA)&&nA!="NA"?nA:nE, AdminLevel=1, CountryCode="EG", OfficialCode=g1, Boundary=geom, SimplifiedBoundary=CreateSimplifiedGeometry(geom,logger,0.01), Centroid=geom?.Centroid as Point, IsActive=true, CreatedAtUtc=DateTime.UtcNow, UpdatedAtUtc=DateTime.UtcNow };
// //                     }).Where(ab => ab != null).ToList();
// //                     if(governoratesToSeed.Any()) await context.AdministrativeBoundaries.AddRangeAsync(governoratesToSeed!); await context.SaveChangesAsync();
// //                 }
// //             }
// //             seededGovernoratesByGid = await context.AdministrativeBoundaries.Where(ab => ab.AdminLevel == 1 && ab.CountryCode == "EG").AsNoTracking().ToDictionaryAsync(ab => ab.OfficialCode!, ab => ab);
// //             logger.LogInformation($"DataSeeder.SeedAsync: Loaded {seededGovernoratesByGid.Count} L1 AdminBoundaries (Governorates).");

// //             if (forceReseed || !await context.AdministrativeBoundaries.AnyAsync(ab => ab.AdminLevel == 2))
// //             {
// //                 var adminL2Features = LoadFeatureCollectionFromFile("SeedData/gadm41_EGY_2.json", logger);
// //                 if (adminL2Features != null && seededGovernoratesByGid.Any()) { 
// //                     var citiesToSeed = adminL2Features.Select(f => {
// //                         var p=f.Attributes; string? nE=p.GetOptionalString("NAME_2"), nA=p.GetOptionalString("NL_NAME_2"), g2=p.GetOptionalString("GID_2"), pG1=p.GetOptionalString("GID_1");
// //                         if(string.IsNullOrEmpty(nE)||string.IsNullOrEmpty(g2)||string.IsNullOrEmpty(pG1)||!seededGovernoratesByGid.TryGetValue(pG1, out var parent)) return null; var geom=EnsureMultiPolygon(f.Geometry,logger);
// //                         return new AdministrativeBoundary { NameEn=nE, NameAr=!string.IsNullOrWhiteSpace(nA)&&nA!="NA"?nA:nE, AdminLevel=2, ParentId=parent.Id, CountryCode="EG", OfficialCode=g2, Boundary=geom, SimplifiedBoundary=CreateSimplifiedGeometry(geom,logger,0.001), Centroid=geom?.Centroid as Point, IsActive=true, CreatedAtUtc=DateTime.UtcNow, UpdatedAtUtc=DateTime.UtcNow };
// //                     }).Where(ab => ab != null).ToList();
// //                     if(citiesToSeed.Any()) await context.AdministrativeBoundaries.AddRangeAsync(citiesToSeed!); await context.SaveChangesAsync();
// //                 } else if (adminL2Features != null) { logger.LogWarning("DataSeeder.SeedAsync: GADM L2 data found, but no L1 Governorates were loaded/available to link them.");}
// //             }
// //             seededLevel2BoundariesByGid = await context.AdministrativeBoundaries.Where(ab => ab.AdminLevel == 2 && ab.CountryCode == "EG").AsNoTracking().ToDictionaryAsync(ab => ab.OfficialCode!, ab => ab);
// //             logger.LogInformation($"DataSeeder.SeedAsync: Loaded {seededLevel2BoundariesByGid.Count} L2 AdminBoundaries (Cities/Markazes).");

// //             Dictionary<string, OperationalArea> operationalAreasMap = new();
// //             if (forceReseed || !await context.OperationalAreas.AnyAsync())
// //             {
// //                 logger.LogInformation("DataSeeder.SeedAsync: Seeding OperationalAreas...");
// //                 var oasToSeed = new List<OperationalArea>();
                
// //                 AdministrativeBoundary? cairoGovFromDb=seededGovernoratesByGid.GetValueOrDefault(cairoGovGidConst);
// //                 AdministrativeBoundary? gizaGovFromDb=seededGovernoratesByGid.GetValueOrDefault(gizaGovGidConst);
// //                 AdministrativeBoundary? alexGovFromDb=seededGovernoratesByGid.GetValueOrDefault(alexGovGidConst);

// //                 Action<AdministrativeBoundary?, string, double, double, double> addGovOA = (gov, level, radius, lat, lon) => {
// //                     if (gov?.Boundary != null) oasToSeed.Add(new OperationalArea { NameEn=gov.NameEn, NameAr=gov.NameAr, Slug=GenerateOperationalAreaSlug($"{gov.NameEn}-governorate"), DefaultSearchRadiusMeters=radius*1000, GeometrySource=GeometrySourceType.DerivedFromAdmin, PrimaryAdministrativeBoundaryId=gov.Id, DisplayLevel=level, CentroidLatitude=gov.Centroid?.Y??lat, CentroidLongitude=gov.Centroid?.X??lon, IsActive=true });
// //                     else logger.LogWarning($"DataSeeder.SeedAsync: Could not create Governorate OA for GID '{gov?.OfficialCode}' as it was not found or has no boundary in `seededGovernoratesByGid`.");
// //                 };
// //                 addGovOA(cairoGovFromDb, "Governorate", 30, 30.0444, 31.2357);
// //                 addGovOA(gizaGovFromDb, "Governorate", 25, 29.9870, 31.1313);
// //                 addGovOA(alexGovFromDb, "Governorate", 20, 31.2001, 29.9187);

// //                 var ncGids=new[]{"EGY.11.33_1","EGY.11.34_1","EGY.11.35_1"}; // VERIFY New Cairo GID_2s
// //                 logger.LogInformation("DataSeeder.SeedAsync: Attempting to create 'New Cairo District' OA using GID_2s: {Gids}", string.Join(", ", ncGids));
// //                 var ncGeoms=ncGids.Select(gid=>{ var b=seededLevel2BoundariesByGid.GetValueOrDefault(gid); if(b==null)logger.LogWarning($"GADM L2 for NC GID {gid} NOT FOUND in seededLevel2BoundariesByGid."); else if(b.Boundary==null)logger.LogWarning($"GADM L2 for NC GID {gid} has NULL Boundary."); return b?.Boundary; }).Where(g=>g!=null).ToList();
// //                 logger.LogInformation("DataSeeder.SeedAsync: Found {Count} valid geometries for New Cairo parts.", ncGeoms.Count);
// //                 if (gizaGovFromDb == null) logger.LogWarning("DataSeeder.SeedAsync: Parent Giza Governorate (gizaGovFromDb) is null for New Cairo OA check (should be cairoGovFromDb)."); // Corrected to cairoGovFromDb
// //                 if(ncGeoms.Any()&&cairoGovFromDb!=null){ Geometry? ug=ncGeoms.Count==1?ncGeoms.First()!.Copy():new GeometryCollection(ncGeoms.ToArray()!).Union(); if(ug!=null&&!ug.IsEmpty) { var slug=GenerateOperationalAreaSlug("new-cairo-district"); logger.LogInformation("DataSeeder.SeedAsync: Creating 'New Cairo District' OA with slug '{Slug}'.",slug); oasToSeed.Add(new OperationalArea{NameEn="New Cairo District",NameAr="القاهرة الجديدة",Slug=slug,CentroidLatitude=ug.Centroid?.Y??30.0271,CentroidLongitude=ug.Centroid?.X??31.4961,DefaultSearchRadiusMeters=15000,GeometrySource=GeometrySourceType.Custom,CustomBoundary=EnsureMultiPolygon(ug,logger),CustomSimplifiedBoundary=CreateSimplifiedGeometry(ug,logger,0.004),PrimaryAdministrativeBoundaryId=cairoGovFromDb.Id,DisplayLevel="AggregatedUrbanArea",IsActive=true});}} else {logger.LogWarning("DataSeeder.SeedAsync: Failed to create New Cairo District OA; GADM L2 parts or parent Cairo Gov missing. Geoms found: {Count}, Cairo Gov Exists: {CairoExists}", ncGeoms.Count, cairoGovFromDb != null);}

// //                 var soGids=new[]{"EGY.8.17_1","EGY.8.18_1"}; // VERIFY 6th Oct GID_2s
// //                 logger.LogInformation("DataSeeder.SeedAsync: Attempting to create '6th of October City' OA using GID_2s: {Gids}", string.Join(", ", soGids));
// //                 var soGeoms=soGids.Select(gid=>{ var b=seededLevel2BoundariesByGid.GetValueOrDefault(gid); if(b==null)logger.LogWarning($"GADM L2 for 6Oct GID {gid} NOT FOUND in seededLevel2BoundariesByGid."); else if(b.Boundary==null)logger.LogWarning($"GADM L2 for 6Oct GID {gid} has NULL Boundary."); return b?.Boundary; }).Where(g=>g!=null).ToList();
// //                 logger.LogInformation("DataSeeder.SeedAsync: Found {Count} valid geometries for 6th of October parts.", soGeoms.Count);
// //                 if (gizaGovFromDb == null) logger.LogWarning("DataSeeder.SeedAsync: Parent Giza Governorate (gizaGovFromDb) is null for 6th October OA.");
// //                 if(soGeoms.Any()&&gizaGovFromDb!=null){ Geometry? ug=soGeoms.Count==1?soGeoms.First()!.Copy():new GeometryCollection(soGeoms.ToArray()!).Union(); if(ug!=null&&!ug.IsEmpty) { var slug = "6th-of-october-city"; logger.LogInformation("DataSeeder.SeedAsync: Creating '6th of October City' OA with PREDEFINED slug '{Slug}'.",slug); oasToSeed.Add(new OperationalArea{NameEn="6th of October City",NameAr="مدينة السادس من أكتوبر",Slug=slug,CentroidLatitude=ug.Centroid?.Y??29.9660,CentroidLongitude=ug.Centroid?.X??30.9232,DefaultSearchRadiusMeters=18000,GeometrySource=GeometrySourceType.Custom,CustomBoundary=EnsureMultiPolygon(ug,logger),CustomSimplifiedBoundary=CreateSimplifiedGeometry(ug,logger,0.004),PrimaryAdministrativeBoundaryId=gizaGovFromDb.Id,DisplayLevel="MajorNewCity",IsActive=true});}} else {logger.LogWarning("DataSeeder.SeedAsync: Failed to create 6th of October City OA; GADM L2 parts or parent Giza Gov missing. Geoms found: {Count}, Giza Gov Exists: {GizaExists}", soGeoms.Count, gizaGovFromDb != null);}

// //                 var distGids=new Dictionary<string,(string NE,string NA,string PGID_Const,string DL,double DR,double Flat,double Flon)>{
// //                     {"EGY.11.26_1",("Bulaq","المعادي",cairoGovGidConst,"District",5,30.00,31.25)}, 
// //                     {"EGY.11.48_1",("Zamalek","الزمالك",cairoGovGidConst,"District",3,30.06,31.22)},
// //                     {"EGY.11.17_1",("Heliopolis","مصر الجديدة",cairoGovGidConst,"District",7,30.09,31.32)}, 
// //                     {"EGY.11.30_1",("Nasr City","مدينة نصر",cairoGovGidConst,"District",10,30.05,31.33)},
// //                     {"EGY.8.10_1",("Mohandessin","المهندسين",gizaGovGidConst,"District",4,30.055,31.20)}, 
// //                     {"EGY.8.6_1",("Dokki","الدقي",gizaGovGidConst,"District",3,30.04,31.205)}
// //                 }; 
// //                 foreach(var kvp in distGids){ 
// //                     AdministrativeBoundary? parentGovForDistrict = seededGovernoratesByGid.GetValueOrDefault(kvp.Value.PGID_Const); 
// //                     if(parentGovForDistrict != null && seededLevel2BoundariesByGid.TryGetValue(kvp.Key,out var abL2)&&abL2?.Boundary!=null) 
// //                         oasToSeed.Add(new OperationalArea{NameEn=kvp.Value.NE,NameAr=kvp.Value.NA,Slug=GenerateOperationalAreaSlug(kvp.Value.NE),CentroidLatitude=abL2.Centroid?.Y??kvp.Value.Flat,CentroidLongitude=abL2.Centroid?.X??kvp.Value.Flon,DefaultSearchRadiusMeters=kvp.Value.DR*1000,GeometrySource=GeometrySourceType.DerivedFromAdmin,PrimaryAdministrativeBoundaryId=abL2.Id,DisplayLevel=kvp.Value.DL,IsActive=true}); 
// //                     else logger.LogWarning($"DataSeeder.SeedAsync: Failed for District GID: {kvp.Key} ('{kvp.Value.NE}'). L2 entity, boundary, or parent Gov (Ref: {kvp.Value.PGID_Const}) missing or has no boundary.");
// //                 }

// //                 if(oasToSeed.Any()) await context.OperationalAreas.AddRangeAsync(oasToSeed);
// //                 await context.SaveChangesAsync();
// //                 operationalAreasMap = await context.OperationalAreas.AsNoTracking().ToDictionaryAsync(oa => oa.Slug, oa => oa);
// //                 logger.LogInformation($"DataSeeder.SeedAsync: Seeded {operationalAreasMap.Count} OperationalAreas. Keys: [{string.Join(", ", operationalAreasMap.Keys)}]");
// //             } else { operationalAreasMap = await context.OperationalAreas.AsNoTracking().ToDictionaryAsync(oa => oa.Slug, oa => oa); }

// //             List<GlobalServiceDefinition> globalServices = new();
// //             if (forceReseed || !await context.GlobalServiceDefinitions.AnyAsync()) { 
// //                 globalServices = new List<GlobalServiceDefinition> {
// //                     new() { ServiceCode = "OIL_CHANGE_STD", DefaultNameEn = "Standard Oil Change", DefaultNameAr = "تغيير زيت قياسي", Category = ShopCategory.OilChange, DefaultEstimatedDurationMinutes = 30, IsGloballyActive = true },
// //                     new() { ServiceCode = "OIL_CHANGE_SYN", DefaultNameEn = "Synthetic Oil Change", DefaultNameAr = "تغيير زيت تخليقي", Category = ShopCategory.OilChange, DefaultEstimatedDurationMinutes = 45, IsGloballyActive = true },
// //                     new() { ServiceCode = "BRAKE_PAD_FRNT", DefaultNameEn = "Front Brake Pad Replacement", DefaultNameAr = "تغيير تيل الفرامل الأمامي", Category = ShopCategory.Brakes, DefaultEstimatedDurationMinutes = 60, IsGloballyActive = true },
// //                     new() { ServiceCode = "AC_REGAS", DefaultNameEn = "A/C Re-gas", DefaultNameAr = "إعادة شحن فريون التكييف", Category = ShopCategory.ACRepair, DefaultEstimatedDurationMinutes = 45, IsGloballyActive = true },
// //                     new() { ServiceCode = "CAR_WASH_EXT", DefaultNameEn = "Exterior Car Wash", DefaultNameAr = "غسيل خارجي للسيارة", Category = ShopCategory.CarWash, DefaultEstimatedDurationMinutes = 20, IsGloballyActive = true },
// //                     new() { ServiceCode = "TIRE_ROTATE", DefaultNameEn = "Tire Rotation", DefaultNameAr = "تدوير الإطارات", Category = ShopCategory.TireServices, DefaultEstimatedDurationMinutes = 30, IsGloballyActive = true },
// //                     new() { ServiceCode = "ENGINE_DIAG", DefaultNameEn = "Engine Diagnostics", DefaultNameAr = "تشخيص أعطال المحرك", Category = ShopCategory.Diagnostics, DefaultEstimatedDurationMinutes = 60, IsGloballyActive = true },
// //                     new() { ServiceCode = "GEN_MAINT_INSP", DefaultNameEn = "General Maintenance Inspection", DefaultNameAr = "فحص صيانة عام", Category = ShopCategory.GeneralMaintenance, DefaultEstimatedDurationMinutes = 90, IsGloballyActive = true }
// //                 };
// //                 if(globalServices.Any()) await context.GlobalServiceDefinitions.AddRangeAsync(globalServices);
// //                 await context.SaveChangesAsync();
// //             }
// //             globalServices = await context.GlobalServiceDefinitions.AsNoTracking().ToListAsync();
// //             logger.LogInformation($"DataSeeder.SeedAsync: Loaded/Seeded {globalServices.Count} GlobalServiceDefinitions.");

// //             if (forceReseed || !await context.Shops.AnyAsync())
// //             {
// //                 logger.LogInformation("DataSeeder.SeedAsync: Seeding shops using TargetOperationalAreaSlug from shops_data.json...");
// //                 var shopSeedDtos = LoadShopsFromFile("SeedData/shops_data.json", logger);
// //                 var shopsToSave = new List<Shop>();
// //                 if (shopSeedDtos != null && shopSeedDtos.Any())
// //                 {
// //                     AdministrativeBoundary? cairoGovForShopFallback = seededGovernoratesByGid.GetValueOrDefault(cairoGovGidConst);
// //                     AdministrativeBoundary? gizaGovForShopFallback = seededGovernoratesByGid.GetValueOrDefault(gizaGovGidConst);
// //                     AdministrativeBoundary? alexGovForShopFallback = seededGovernoratesByGid.GetValueOrDefault(alexGovGidConst);

// //                     foreach (var dto in shopSeedDtos)
// //                     {
// //                         OperationalArea? targetOA = null;
// //                         if (!string.IsNullOrWhiteSpace(dto.TargetOperationalAreaSlug))
// //                         {
// //                             targetOA = operationalAreasMap.GetValueOrDefault(dto.TargetOperationalAreaSlug);
// //                             if(targetOA == null) logger.LogWarning($"Shop '{dto.NameEn}': Provided TargetOperationalAreaSlug '{dto.TargetOperationalAreaSlug}' not found in operationalAreasMap. Keys: [{string.Join(", ", operationalAreasMap.Keys)}]");
// //                         }
                        
// //                         if (targetOA == null) 
// //                         {
// //                             logger.LogWarning($"Shop '{dto.NameEn}' TargetOperationalAreaSlug ('{dto.TargetOperationalAreaSlug}') not found or missing. Attempting fallback using OriginalCityRefSlug '{dto.OriginalCityRefSlug}'.");
// //                             if (dto.OriginalCityRefSlug == "6th-october-city-ref")
// //                                 targetOA = operationalAreasMap.GetValueOrDefault("6th-of-october-city"); // Explicit slug
// //                             else if (dto.OriginalCityRefSlug == "new-cairo-ref")
// //                                 targetOA = operationalAreasMap.GetValueOrDefault(GenerateOperationalAreaSlug("new-cairo-district"));
// //                             else if (dto.OriginalCityRefSlug == "cairo-ref" && cairoGovForShopFallback != null) 
// //                                 targetOA = operationalAreasMap.GetValueOrDefault(GenerateOperationalAreaSlug($"{cairoGovForShopFallback.NameEn}-governorate"));
// //                             else if (dto.OriginalCityRefSlug == "giza-ref" && gizaGovForShopFallback != null) 
// //                                 targetOA = operationalAreasMap.GetValueOrDefault(GenerateOperationalAreaSlug($"{gizaGovForShopFallback.NameEn}-governorate"));
// //                             else if (dto.OriginalCityRefSlug == "alexandria-ref" && alexGovForShopFallback != null) 
// //                                 targetOA = operationalAreasMap.GetValueOrDefault(GenerateOperationalAreaSlug($"{alexGovForShopFallback.NameEn}-governorate"));
// //                         }

// //                         if (targetOA == null)
// //                         {
// //                             logger.LogError($"Shop '{dto.NameEn}' could not be mapped to any OperationalArea after direct lookup and fallback. OriginalRef: '{dto.OriginalCityRefSlug}', TargetSlug: '{dto.TargetOperationalAreaSlug}'. Skipping.");
// //                             continue;
// //                         }

// //                         if (!Enum.TryParse<ShopCategory>(dto.Category, true, out var category)) category = ShopCategory.Unknown;
// //                         var shop = new Shop {
// //                             NameEn=dto.NameEn, NameAr=dto.NameAr, Address=dto.Address, Location=CreatePoint(dto.Latitude,dto.Longitude),
// //                             PhoneNumber=dto.PhoneNumber, ServicesOffered=dto.ServicesOffered, OpeningHours=dto.OpeningHours, Category=category,
// //                             OperationalAreaId=targetOA.Id, Slug=dto.Slug, LogoUrl=dto.LogoUrl, IsDeleted = false 
// //                         };
// //                         string initialSlug = string.IsNullOrWhiteSpace(shop.Slug) ? GenerateShopSlug(shop.NameEn, targetOA.Slug) : GenerateShopSlug(shop.Slug, targetOA.Slug); // Ensure consistent slug format
// //                         shop.Slug = initialSlug; int c = 1;
// //                         while(shopsToSave.Any(s=>s.OperationalAreaId==targetOA.Id && s.Slug==shop.Slug) || 
// //                               await context.Shops.AnyAsync(s=>s.OperationalAreaId==targetOA.Id && s.Slug==shop.Slug))
// //                         { shop.Slug = $"{initialSlug}-{c++}"; }
// //                         if(string.IsNullOrEmpty(shop.LogoUrl)) shop.LogoUrl = GenerateLogoUrlFromName(shop.Slug);
// //                         shopsToSave.Add(shop);
// //                     }
// //                     if(shopsToSave.Any()) await context.Shops.AddRangeAsync(shopsToSave);
// //                     await context.SaveChangesAsync();
// //                     logger.LogInformation($"DataSeeder.SeedAsync: Seeded {shopsToSave.Count} shops.");
// //                 } else { logger.LogWarning("DataSeeder.SeedAsync: No shop data loaded from shops_data.json.");}
// //             }
            
// //             List<Shop> finalSeededShops = await context.Shops.Where(s => !s.IsDeleted).AsNoTracking().ToListAsync();
// //             if ((forceReseed || !await context.ShopServices.AnyAsync()) && finalSeededShops.Any() && globalServices.Any())
// //             {
// //                 var shopServicesToSeed = new List<ShopService>();
// //                 var random = new Random();
// //                 ShopService CreateShopSvc(Guid sId, GlobalServiceDefinition gsd, decimal p, string? nE=null, string? nA=null, int? customDuration=null) => 
// //                     new ShopService{ShopId=sId,GlobalServiceId=gsd.GlobalServiceId,CustomServiceNameEn=nE,CustomServiceNameAr=nA,EffectiveNameEn=!string.IsNullOrEmpty(nE)?nE:gsd.DefaultNameEn,EffectiveNameAr=!string.IsNullOrEmpty(nA)?nA:gsd.DefaultNameAr,Price=p,DurationMinutes=customDuration??gsd.DefaultEstimatedDurationMinutes,IsOfferedByShop=true,SortOrder=random.Next(1,100)};
                
// //                 foreach(var s in finalSeededShops){ try {
// //                     var os=globalServices.FirstOrDefault(g=>g.ServiceCode=="OIL_CHANGE_STD"); var osy=globalServices.FirstOrDefault(g=>g.ServiceCode=="OIL_CHANGE_SYN"); var bf=globalServices.FirstOrDefault(g=>g.ServiceCode=="BRAKE_PAD_FRNT"); var ar=globalServices.FirstOrDefault(g=>g.ServiceCode=="AC_REGAS"); var cw=globalServices.FirstOrDefault(g=>g.ServiceCode=="CAR_WASH_EXT"); var tr=globalServices.FirstOrDefault(g=>g.ServiceCode=="TIRE_ROTATE"); var ed=globalServices.FirstOrDefault(g=>g.ServiceCode=="ENGINE_DIAG"); var gi=globalServices.FirstOrDefault(g=>g.ServiceCode=="GEN_MAINT_INSP");
// //                     if((s.Category==ShopCategory.GeneralMaintenance||s.Category==ShopCategory.OilChange)&&os!=null&&osy!=null){ if(os!=null)shopServicesToSeed.Add(CreateShopSvc(s.Id,os,Math.Round((decimal)(random.NextDouble()*100+250),2))); if(osy!=null)shopServicesToSeed.Add(CreateShopSvc(s.Id,osy,Math.Round((decimal)(random.NextDouble()*150+450),2),customDuration:50));}
// //                     if((s.Category==ShopCategory.GeneralMaintenance||s.Category==ShopCategory.Brakes)&&bf!=null) shopServicesToSeed.Add(CreateShopSvc(s.Id,bf,Math.Round((decimal)(random.NextDouble()*200+600),2)));
// //                     if((s.Category==ShopCategory.GeneralMaintenance||s.Category==ShopCategory.ACRepair)&&ar!=null) shopServicesToSeed.Add(CreateShopSvc(s.Id,ar,Math.Round((decimal)(random.NextDouble()*100+300),2)));
// //                     if(s.Category==ShopCategory.CarWash&&cw!=null) shopServicesToSeed.Add(CreateShopSvc(s.Id,cw,Math.Round((decimal)(random.NextDouble()*50+100),2),customDuration:25));
// //                     if(s.Category==ShopCategory.TireServices&&tr!=null) shopServicesToSeed.Add(CreateShopSvc(s.Id,tr,Math.Round((decimal)(random.NextDouble()*80+150),2)));
// //                     if((s.Category==ShopCategory.Diagnostics||s.Category==ShopCategory.GeneralMaintenance)&&ed!=null) shopServicesToSeed.Add(CreateShopSvc(s.Id,ed,Math.Round((decimal)(random.NextDouble()*150+200),2)));
// //                     if(s.Category==ShopCategory.GeneralMaintenance&&gi!=null) shopServicesToSeed.Add(CreateShopSvc(s.Id,gi,Math.Round((decimal)(random.NextDouble()*200+300),2),customDuration:100));
// //                     if(s.NameEn.Contains("Bosch",StringComparison.OrdinalIgnoreCase)) shopServicesToSeed.Add(new ShopService{ShopId=s.Id,CustomServiceNameEn="Bosch Premium Diagnostic Package",EffectiveNameEn="Bosch Premium Diagnostic Package",CustomServiceNameAr="باقة بوش التشخيصية الممتازة",EffectiveNameAr="باقة بوش التشخيصية الممتازة",Price=750.00m,DurationMinutes=120,IsOfferedByShop=true,SortOrder=5,IsPopularAtShop=true});
// //                 } catch(Exception ex){logger.LogError(ex, $"DataSeeder.SeedAsync: Error creating services for shop {s.Id} ({s.NameEn})");}}
// //                 if(shopServicesToSeed.Any()) await context.ShopServices.AddRangeAsync(shopServicesToSeed);
// //                 await context.SaveChangesAsync();
// //                 logger.LogInformation($"DataSeeder.SeedAsync: Seeded {shopServicesToSeed.Count} ShopServices.");
// //             }
// //             logger.LogInformation("DataSeeder.SeedAsync: Process complete.");
// //         }
// //     }

// //     public static class AttributesTableExtensions {
// //         public static string? GetOptionalString(this IAttributesTable table, string name) => (table==null||!table.Exists(name))?null:table[name]?.ToString();
// //     }
// // }
// // // // Data/DataSeeder.cs
// // // using AutomotiveServices.Api.Models;
// // // using AutomotiveServices.Api.Models.Seed; // For ShopSeedDto
// // // using Microsoft.EntityFrameworkCore;
// // // using Microsoft.Extensions.Logging;
// // // using NetTopologySuite.Features;
// // // using NetTopologySuite.Geometries;
// // // using NetTopologySuite.IO;
// // // using NetTopologySuite.Operation.Union; // For UnaryUnionOp
// // // using System;
// // // using System.Collections.Generic;
// // // using System.IO;
// // // using System.Linq;
// // // using System.Text.Json;
// // // using System.Text.RegularExpressions;
// // // using System.Threading.Tasks;

// // // namespace AutomotiveServices.Api.Data
// // // {
// // //     public static class DataSeeder
// // //     {
// // //         private static readonly GeometryFactory _geometryFactory = new GeometryFactory(new PrecisionModel(), 4326);
// // //         private static readonly GeoJsonReader _geoJsonReader = new GeoJsonReader();
// // //         private static readonly JsonSerializerOptions _jsonSerializerOptions = new JsonSerializerOptions
// // //         {
// // //             PropertyNameCaseInsensitive = true,
// // //         };

// // //         private static Point CreatePoint(double latitude, double longitude) =>
// // //            _geometryFactory.CreatePoint(new Coordinate(longitude, latitude));

// // //         private static Geometry? EnsureMultiPolygon(Geometry? geometry, ILogger logger)
// // //         {
// // //             if (geometry == null) return null;
// // //             if (geometry is Polygon polygon)
// // //             {
// // //                 return _geometryFactory.CreateMultiPolygon(new[] { polygon.Copy() as Polygon });
// // //             }
// // //             if (geometry is MultiPolygon mp)
// // //             {
// // //                 return mp.Copy() as MultiPolygon;
// // //             }
// // //             logger.LogWarning($"Geometry type {geometry.GeometryType} was not Polygon or MultiPolygon. Returning as is, but schema expects MultiPolygon.");
// // //             return geometry.Copy();
// // //         }

// // //         private static Geometry? CreateSimplifiedGeometry(Geometry? detailedGeometry, ILogger logger, double tolerance = 0.005)
// // //         {
// // //             if (detailedGeometry == null) return null;
// // //             try
// // //             {
// // //                 var geometryToSimplify = EnsureMultiPolygon(detailedGeometry, logger);
// // //                 if (geometryToSimplify == null) return null;
// // //                 var simplifier = new NetTopologySuite.Simplify.DouglasPeuckerSimplifier(geometryToSimplify)
// // //                 {
// // //                     DistanceTolerance = tolerance
// // //                 };
// // //                 return EnsureMultiPolygon(simplifier.GetResultGeometry(), logger);
// // //             }
// // //             catch (Exception ex)
// // //             {
// // //                 logger.LogError(ex, "Error simplifying geometry.");
// // //                 return EnsureMultiPolygon(detailedGeometry.Copy() as Geometry, logger);
// // //             }
// // //         }
        
// // //         private static string GenerateOperationalAreaSlug(string nameEn)
// // //         {
// // //              if (string.IsNullOrWhiteSpace(nameEn)) return $"oa-{Guid.NewGuid().ToString().Substring(0, 8)}";
// // //             string str = nameEn.ToLowerInvariant().Trim();
// // //             str = Regex.Replace(str, @"[^a-z0-9\s-]", ""); // Allow only alphanumeric, space, hyphen
// // //             str = Regex.Replace(str, @"\s+", "-");           // Replace spaces with hyphens
// // //             str = Regex.Replace(str, @"-+", "-");            // Replace multiple hyphens with single
// // //             str = str.Trim('-');                             // Trim leading/trailing hyphens
// // //             str = str.Length > 80 ? str.Substring(0, 80) : str; // Max length
// // //             return string.IsNullOrEmpty(str) ? $"oa-{Guid.NewGuid().ToString().Substring(0, 8)}" : str; 
// // //         }

// // //         private static string GenerateShopSlug(string shopNameEn, string operationalAreaSlug)
// // //         {
// // //             if (string.IsNullOrWhiteSpace(shopNameEn)) shopNameEn = "shop";
// // //             if (string.IsNullOrWhiteSpace(operationalAreaSlug)) operationalAreaSlug = "unknown-area";
            
// // //             string namePart = shopNameEn.ToLowerInvariant().Trim();
// // //             namePart = Regex.Replace(namePart, @"[^a-z0-9\s-]", "");
// // //             namePart = Regex.Replace(namePart, @"\s+", "-"); namePart = Regex.Replace(namePart, @"-+", "-");
// // //             namePart = namePart.Trim('-');
// // //             namePart = namePart.Length > 60 ? namePart.Substring(0, 60) : namePart;
// // //             if (string.IsNullOrEmpty(namePart)) namePart = "shop";
            
// // //             string areaPart = operationalAreaSlug.ToLowerInvariant().Trim(); // Area slug should already be clean from GenerateOperationalAreaSlug
// // //             areaPart = areaPart.Length > 40 ? areaPart.Substring(0, 40) : areaPart;
// // //             if (string.IsNullOrEmpty(areaPart)) areaPart = "area";

// // //             return $"{namePart}-in-{areaPart}";
// // //         }

// // //         private static string GenerateLogoUrlFromName(string shopSpecificSlug) => $"/logos/{shopSpecificSlug}.png";

// // //         private static FeatureCollection? LoadFeatureCollectionFromFile(string relativePath, ILogger logger)
// // //         {
// // //             try
// // //             {
// // //                 string baseDirectory = AppContext.BaseDirectory;
// // //                 string filePath = Path.Combine(baseDirectory, relativePath);
// // //                 if (!File.Exists(filePath))
// // //                 {
// // //                     logger.LogError($"GeoJSON file not found at {filePath}. Ensure it's in the output directory (e.g., set 'Copy to Output Directory' in VS Properties). Path Searched: {Path.GetFullPath(filePath)}");
// // //                     return null;
// // //                 }
// // //                 string geoJsonString = File.ReadAllText(filePath);
// // //                 return _geoJsonReader.Read<FeatureCollection>(geoJsonString);
// // //             }
// // //             catch (Exception ex) { logger.LogError(ex, $"Error loading GeoJSON: {relativePath}"); return null; }
// // //         }

// // //         private static List<ShopSeedDto>? LoadShopsFromFile(string relativePath, ILogger logger)
// // //         {
// // //             try
// // //             {
// // //                 string baseDirectory = AppContext.BaseDirectory;
// // //                 string filePath = Path.Combine(baseDirectory, relativePath);
// // //                 if (!File.Exists(filePath))
// // //                 {
// // //                     logger.LogError($"Shop data file not found: {filePath}. Path Searched: {Path.GetFullPath(filePath)}");
// // //                     return null;
// // //                 }
// // //                 string jsonString = File.ReadAllText(filePath);
// // //                 var shops = JsonSerializer.Deserialize<List<ShopSeedDto>>(jsonString, _jsonSerializerOptions);
// // //                 logger.LogInformation($"Successfully loaded {shops?.Count ?? 0} shop data records from {relativePath}.");
// // //                 return shops;
// // //             }
// // //             catch (JsonException jsonEx)
// // //             {
// // //                 logger.LogError(jsonEx, $"Error deserializing shop data from JSON file: {relativePath}. Check JSON structure, DTO mapping, and Category enum string names.");
// // //                 return null;
// // //             }
// // //             catch (Exception ex) { logger.LogError(ex, $"Error loading shop data file: {relativePath}"); return null; }
// // //         }

// // //         public static async Task SeedAsync(AppDbContext context, ILogger logger, bool forceReseed)
// // //         {
// // //             logger.LogInformation("DataSeeder starting. ForceReseed: {ForceReseed}", forceReseed);
// // //             if (forceReseed)
// // //             {
// // //                 logger.LogInformation("(DataSeeder) Force re-seed: Clearing relevant tables in order...");
// // //                 await context.Database.ExecuteSqlRawAsync("ALTER TABLE \"Shops\" DROP CONSTRAINT IF EXISTS \"FK_Shops_OperationalAreas_OperationalAreaId\";");
// // //                 await context.Database.ExecuteSqlRawAsync("DELETE FROM \"ShopServices\";");
// // //                 await context.Database.ExecuteSqlRawAsync("DELETE FROM \"GlobalServiceDefinitions\";");
// // //                 await context.Database.ExecuteSqlRawAsync("DELETE FROM \"BookingItems\";");
// // //                 await context.Database.ExecuteSqlRawAsync("DELETE FROM \"Bookings\";");
// // //                 await context.Database.ExecuteSqlRawAsync("DELETE FROM \"UserCartItems\";");
// // //                 await context.Database.ExecuteSqlRawAsync("DELETE FROM \"AnonymousCartItems\";");
// // //                 await context.Database.ExecuteSqlRawAsync("DELETE FROM \"UserPreferences\";");
// // //                 await context.Database.ExecuteSqlRawAsync("DELETE FROM \"AnonymousUserPreferences\";");
// // //                 await context.Database.ExecuteSqlRawAsync("DELETE FROM \"Shops\";");
// // //                 await context.Database.ExecuteSqlRawAsync("DELETE FROM \"OperationalAreas\";");
// // //                 await context.Database.ExecuteSqlRawAsync("DELETE FROM \"AdministrativeBoundaries\";");
// // //                 await context.Database.ExecuteSqlRawAsync("DELETE FROM \"Cities\";");
// // //                 logger.LogInformation("(DataSeeder) Relevant database tables cleared for re-seeding.");
// // //             }

// // //             Dictionary<string, City> oldCitiesLookup = new();
// // //             if (forceReseed || !await context.Cities.AnyAsync())
// // //             {
// // //                 logger.LogInformation("Seeding original 'Cities' table (for mapping reference)...");
// // //                 var citiesForMappingRef = new List<City> {
// // //                     new() { NameEn = "CairoRef", NameAr = "القاهرة مرجع", Slug = "cairo-ref", StateProvince = "Cairo Governorate", Country = "Egypt", IsActive = true, Location = CreatePoint(30.0444, 31.2357) },
// // //                     new() { NameEn = "AlexandriaRef", NameAr = "الإسكندرية مرجع", Slug = "alexandria-ref", StateProvince = "Alexandria Governorate", Country = "Egypt", IsActive = true, Location = CreatePoint(31.2001, 29.9187) },
// // //                     new() { NameEn = "GizaRef", NameAr = "الجيزة مرجع", Slug = "giza-ref", StateProvince = "Giza Governorate", Country = "Egypt", IsActive = true, Location = CreatePoint(29.9870, 31.1313) },
// // //                     new() { NameEn = "6th October CityRef", NameAr = "مدينة 6 أكتوبر مرجع", Slug = "6th-october-city-ref", StateProvince = "Giza Governorate", Country = "Egypt", IsActive = true, Location = CreatePoint(29.9660, 30.9232) },
// // //                     new() { NameEn = "New CairoRef", NameAr = "القاهرة الجديدة مرجع", Slug = "new-cairo-ref", StateProvince = "Cairo Governorate", Country = "Egypt", IsActive = true, Location = CreatePoint(30.0271, 31.4961) }
// // //                 };
// // //                 if(citiesForMappingRef.Any()) await context.Cities.AddRangeAsync(citiesForMappingRef);
// // //                 await context.SaveChangesAsync();
// // //                 oldCitiesLookup = await context.Cities.AsNoTracking().ToDictionaryAsync(c => c.Slug, c => c);
// // //                 logger.LogInformation("Seeded {Count} original cities for mapping.", oldCitiesLookup.Count);
// // //             } else {
// // //                 oldCitiesLookup = await context.Cities.AsNoTracking().ToDictionaryAsync(c => c.Slug, c => c);
// // //             }

// // //             Dictionary<string, AdministrativeBoundary> seededGovernoratesById = new();
// // //             Dictionary<string, AdministrativeBoundary> seededLevel2BoundariesByGid = new(); 

// // //             if (forceReseed || !await context.AdministrativeBoundaries.AnyAsync())
// // //             {
// // //                 logger.LogInformation("Seeding AdministrativeBoundaries from GADM GeoJSON files...");
// // //                 var adminLevel1Features = LoadFeatureCollectionFromFile("SeedData/gadm41_EGY_1.json", logger);
// // //                 if (adminLevel1Features != null)
// // //                 {
// // //                     var governoratesToSeed = new List<AdministrativeBoundary>();
// // //                     foreach (var feature in adminLevel1Features)
// // //                     {
// // //                         var properties = feature.Attributes;
// // //                         string? nameEn = properties?.GetOptionalString("NAME_1");
// // //                         string? nameAr = properties?.GetOptionalString("NL_NAME_1");
// // //                         string? gid1 = properties?.GetOptionalString("GID_1");
// // //                         if (string.IsNullOrEmpty(nameEn) || string.IsNullOrEmpty(gid1))
// // //                         {
// // //                             logger.LogWarning($"Skipping Level 1 feature due to missing NAME_1 or GID_1. Feature GID_1: {gid1}, Name: {nameEn}");
// // //                             continue;
// // //                         }
// // //                         var geometry = EnsureMultiPolygon(feature.Geometry, logger);
// // //                         governoratesToSeed.Add(new AdministrativeBoundary {
// // //                             NameEn = nameEn, NameAr = !string.IsNullOrWhiteSpace(nameAr) && nameAr != "NA" ? nameAr : nameEn,
// // //                             AdminLevel = 1, ParentId = null, CountryCode = "EG", OfficialCode = gid1,
// // //                             Boundary = geometry, SimplifiedBoundary = CreateSimplifiedGeometry(geometry, logger, 0.01),
// // //                             Centroid = geometry?.Centroid as Point, IsActive = true, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
// // //                         });
// // //                     }
// // //                     if(governoratesToSeed.Any()) await context.AdministrativeBoundaries.AddRangeAsync(governoratesToSeed);
// // //                     await context.SaveChangesAsync(); 
// // //                     seededGovernoratesById = await context.AdministrativeBoundaries.Where(ab => ab.AdminLevel == 1 && ab.CountryCode == "EG").AsNoTracking().ToDictionaryAsync(ab => ab.OfficialCode!, ab => ab);
// // //                     logger.LogInformation($"Seeded {governoratesToSeed.Count} Level 1 AdministrativeBoundaries (Governorates).");
// // //                 }

// // //                 var adminLevel2Features = LoadFeatureCollectionFromFile("SeedData/gadm41_EGY_2.json", logger);
// // //                 if (adminLevel2Features != null && seededGovernoratesById.Any())
// // //                 {
// // //                     var citiesAndMarkazesToSeed = new List<AdministrativeBoundary>();
// // //                     foreach (var feature in adminLevel2Features)
// // //                     {
// // //                         var properties = feature.Attributes;
// // //                         string? nameEn = properties?.GetOptionalString("NAME_2");
// // //                         string? nameAr = properties?.GetOptionalString("NL_NAME_2");
// // //                         string? gid2 = properties?.GetOptionalString("GID_2");
// // //                         string? parentGid1 = properties?.GetOptionalString("GID_1");
// // //                         if (string.IsNullOrEmpty(nameEn) || string.IsNullOrEmpty(gid2) || string.IsNullOrEmpty(parentGid1) || !seededGovernoratesById.TryGetValue(parentGid1, out var parentAdminBoundary))
// // //                         {
// // //                             logger.LogWarning($"Skipping Level 2 feature GID_2: {gid2}, Name: {nameEn}. Missing required fields or parent GID_1: {parentGid1} not found.");
// // //                             continue;
// // //                         }
// // //                         var geometry = EnsureMultiPolygon(feature.Geometry, logger);
// // //                         citiesAndMarkazesToSeed.Add(new AdministrativeBoundary {
// // //                             NameEn = nameEn, NameAr = !string.IsNullOrWhiteSpace(nameAr) && nameAr != "NA" ? nameAr : nameEn,
// // //                             AdminLevel = 2, ParentId = parentAdminBoundary.Id, CountryCode = "EG", OfficialCode = gid2,
// // //                             Boundary = geometry, SimplifiedBoundary = CreateSimplifiedGeometry(geometry, logger, 0.001),
// // //                             Centroid = geometry?.Centroid as Point, IsActive = true, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
// // //                         });
// // //                     }
// // //                     if(citiesAndMarkazesToSeed.Any()) await context.AdministrativeBoundaries.AddRangeAsync(citiesAndMarkazesToSeed);
// // //                     await context.SaveChangesAsync(); 
// // //                     seededLevel2BoundariesByGid = await context.AdministrativeBoundaries.Where(ab => ab.AdminLevel == 2 && ab.CountryCode == "EG").AsNoTracking().ToDictionaryAsync(ab => ab.OfficialCode!, ab => ab);
// // //                     logger.LogInformation($"Seeded {citiesAndMarkazesToSeed.Count} Level 2 AdministrativeBoundaries (Cities/Markazes).");
// // //                 } else if (adminLevel2Features != null) logger.LogWarning("Admin Level 2 data found, but no Level 1 (Governorates) were seeded/loaded to link them.");
// // //             } else {
// // //                 logger.LogInformation("AdministrativeBoundaries already seeded or GADM files not found. Loading existing.");
// // //                 seededGovernoratesById = await context.AdministrativeBoundaries.Where(ab => ab.AdminLevel == 1 && ab.CountryCode == "EG").AsNoTracking().ToDictionaryAsync(ab => ab.OfficialCode!, ab => ab);
// // //                 seededLevel2BoundariesByGid = await context.AdministrativeBoundaries.Where(ab => ab.AdminLevel == 2 && ab.CountryCode == "EG").AsNoTracking().ToDictionaryAsync(ab => ab.OfficialCode!, ab => ab);
// // //             }

// // //             Dictionary<string, OperationalArea> operationalAreasMap = new();
// // //             if (forceReseed || !await context.OperationalAreas.AnyAsync())
// // //             {
// // //                 logger.LogInformation("Seeding OperationalAreas, deriving from GADM Level 2 where specified...");
// // //                 var operationalAreasToSeed = new List<OperationalArea>();
                
// // //                 // !!! VERIFY THESE GIDs against your actual GADM files !!!
// // //                 string cairoGovGid1 = "EGY.11_1"; 
// // //                 string gizaGovGid1  = "EGY.8_1";  
// // //                 string alexGovGid1  = "EGY.6_1";  

// // //                 AdministrativeBoundary? cairoGov = seededGovernoratesById.GetValueOrDefault(cairoGovGid1);
// // //                 AdministrativeBoundary? gizaGov  = seededGovernoratesById.GetValueOrDefault(gizaGovGid1);
// // //                 AdministrativeBoundary? alexGov  = seededGovernoratesById.GetValueOrDefault(alexGovGid1);

// // //                 // Example: Create OAs for the main Governorates themselves if needed for broader shop assignment or display
// // //                 if (cairoGov != null) {
// // //                     operationalAreasToSeed.Add(new OperationalArea { 
// // //                         NameEn = $"{cairoGov.NameEn} (Governorate Wide)", NameAr = $"{cairoGov.NameAr} (على مستوى المحافظة)", 
// // //                         Slug = GenerateOperationalAreaSlug($"{cairoGov.NameEn}-governorate"), IsActive = true, 
// // //                         CentroidLatitude = cairoGov.Centroid?.Y ?? 30.0444, CentroidLongitude = cairoGov.Centroid?.X ?? 31.2357, 
// // //                         DefaultSearchRadiusMeters = 30000, GeometrySource = GeometrySourceType.DerivedFromAdmin, 
// // //                         PrimaryAdministrativeBoundaryId = cairoGov.Id, DisplayLevel = "Governorate" 
// // //                     });
// // //                 }
// // //                 if (gizaGov != null) {
// // //                      operationalAreasToSeed.Add(new OperationalArea { 
// // //                         NameEn = $"{gizaGov.NameEn} (Governorate Wide)", NameAr = $"{gizaGov.NameAr} (على مستوى المحافظة)", 
// // //                         Slug = GenerateOperationalAreaSlug($"{gizaGov.NameEn}-governorate"), IsActive = true,
// // //                         CentroidLatitude = gizaGov.Centroid?.Y ?? 29.9870, CentroidLongitude = gizaGov.Centroid?.X ?? 31.1313,
// // //                         DefaultSearchRadiusMeters = 25000, GeometrySource = GeometrySourceType.DerivedFromAdmin,
// // //                         PrimaryAdministrativeBoundaryId = gizaGov.Id, DisplayLevel = "Governorate"
// // //                     });
// // //                 }
// // //                  if (alexGov != null) {
// // //                     operationalAreasToSeed.Add(new OperationalArea { 
// // //                         NameEn = $"{alexGov.NameEn} (Governorate Wide)", NameAr = $"{alexGov.NameAr} (على مستوى المحافظة)", 
// // //                         Slug = GenerateOperationalAreaSlug($"{alexGov.NameEn}-governorate"), IsActive = true, 
// // //                         CentroidLatitude = alexGov.Centroid?.Y ?? 31.2001, CentroidLongitude = alexGov.Centroid?.X ?? 29.9187, 
// // //                         DefaultSearchRadiusMeters = 20000, GeometrySource = GeometrySourceType.DerivedFromAdmin, 
// // //                         PrimaryAdministrativeBoundaryId = alexGov.Id, DisplayLevel = "Governorate" 
// // //                     });
// // //                 }

// // //                 // New Cairo District (Union of GADM Level 2 parts)
// // //                 var newCairoGid2s = new[] { "EGY.11.33_1", "EGY.11.34_1", "EGY.11.35_1" }; 
// // //                 List<Geometry> newCairoGeometries = new();
// // //                 foreach(var gid2 in newCairoGid2s)
// // //                 {
// // //                     if(seededLevel2BoundariesByGid.TryGetValue(gid2, out var adminBoundary) && adminBoundary.Boundary != null)
// // //                         newCairoGeometries.Add(adminBoundary.Boundary);
// // //                     else logger.LogWarning($"GADM Level 2 for New Cairo part ({gid2}) not found or has no boundary.");
// // //                 }
// // //                 if (newCairoGeometries.Any() && cairoGov != null)
// // //                 {
// // //                     Geometry? combinedNewCairoGeom = (newCairoGeometries.Count == 1) ? newCairoGeometries.First().Copy() : 
// // //                                                     (newCairoGeometries.Count > 1 ? new GeometryCollection(newCairoGeometries.ToArray()).Union() : null);
// // //                     if (combinedNewCairoGeom != null && !combinedNewCairoGeom.IsEmpty) {
// // //                         operationalAreasToSeed.Add(new OperationalArea {
// // //                             NameEn = "New Cairo District", NameAr = "منطقة القاهرة الجديدة", Slug = GenerateOperationalAreaSlug("new-cairo-district"), IsActive = true,
// // //                             CentroidLatitude = combinedNewCairoGeom.Centroid?.Y ?? 30.0271, CentroidLongitude = combinedNewCairoGeom.Centroid?.X ?? 31.4961,
// // //                             DefaultSearchRadiusMeters = 15000, GeometrySource = GeometrySourceType.Custom, 
// // //                             CustomBoundary = EnsureMultiPolygon(combinedNewCairoGeom, logger), CustomSimplifiedBoundary = CreateSimplifiedGeometry(combinedNewCairoGeom, logger, 0.005),
// // //                             PrimaryAdministrativeBoundaryId = cairoGov.Id, DisplayLevel = "MajorDistrict"
// // //                         });
// // //                     } else logger.LogWarning("Failed to create/union geometry for New Cairo District OA.");
// // //                 } else logger.LogWarning("Could not create 'New Cairo District' OA: Missing GADM L2 parts or parent Cairo Gov.");

// // //                 // 6th of October City Zone (Union of GADM Level 2 parts)
// // //                 var sixthOctGid2s = new[] { "EGY.8.17_1", "EGY.8.18_1" }; 
// // //                 List<Geometry> sixthOctGeometries = new();
// // //                 foreach(var gid2 in sixthOctGid2s)
// // //                 {
// // //                     if(seededLevel2BoundariesByGid.TryGetValue(gid2, out var adminBoundary) && adminBoundary.Boundary != null)
// // //                         sixthOctGeometries.Add(adminBoundary.Boundary);
// // //                     else logger.LogWarning($"GADM Level 2 for 6th October part ({gid2}) not found or has no boundary.");
// // //                 }
// // //                 if (sixthOctGeometries.Any() && gizaGov != null) 
// // //                 {
// // //                     Geometry? combinedSixthOctGeom = (sixthOctGeometries.Count == 1) ? sixthOctGeometries.First().Copy() :
// // //                                                      (sixthOctGeometries.Count > 1 ? new GeometryCollection(sixthOctGeometries.ToArray()).Union() : null);
// // //                     if(combinedSixthOctGeom != null && !combinedSixthOctGeom.IsEmpty) {
// // //                         operationalAreasToSeed.Add(new OperationalArea {
// // //                             NameEn = "6th of October City Zone", NameAr = "منطقة مدينة السادس من أكتوبر", Slug = GenerateOperationalAreaSlug("6th-of-october-city-zone"), IsActive = true,
// // //                             CentroidLatitude = combinedSixthOctGeom.Centroid?.Y ?? 29.9660, CentroidLongitude = combinedSixthOctGeom.Centroid?.X ?? 30.9232,
// // //                             DefaultSearchRadiusMeters = 15000, GeometrySource = GeometrySourceType.Custom, 
// // //                             CustomBoundary = EnsureMultiPolygon(combinedSixthOctGeom, logger), CustomSimplifiedBoundary = CreateSimplifiedGeometry(combinedSixthOctGeom, logger, 0.005),
// // //                             PrimaryAdministrativeBoundaryId = gizaGov.Id, DisplayLevel = "MajorCity"
// // //                         });
// // //                     } else logger.LogWarning("Failed to create/union geometry for 6th of October City Zone OA.");
// // //                 } else logger.LogWarning("Could not create '6th of October City Zone' OA: Missing GADM L2 parts or parent Giza Gov.");
                
// // //                 // Add OAs for individual GADM Level 2 entities (Cities/Districts)
// // //                 string[] specificGadmLevel2GidsToCreateOAsFor = {
// // //                     "EGY.11.26_1", // Example for Bulaq (VERIFY THIS GID)
// // //                     "EGY.11.48_1", // Example for Zamalek (VERIFY THIS GID)
// // //                     // Add other GID_2 values for cities/districts you want as distinct OperationalAreas
// // //                 };

// // //                 foreach (var gid2 in specificGadmLevel2GidsToCreateOAsFor)
// // //                 {
// // //                     if (seededLevel2BoundariesByGid.TryGetValue(gid2, out var adminL2Boundary) && adminL2Boundary.Boundary != null)
// // //                     {
// // //                         operationalAreasToSeed.Add(new OperationalArea
// // //                         {
// // //                             NameEn = adminL2Boundary.NameEn, NameAr = adminL2Boundary.NameAr,
// // //                             Slug = GenerateOperationalAreaSlug(adminL2Boundary.NameEn), IsActive = true,
// // //                             CentroidLatitude = adminL2Boundary.Centroid?.Y ?? 0, CentroidLongitude = adminL2Boundary.Centroid?.X ?? 0,
// // //                             DefaultSearchRadiusMeters = 5000, // Adjust as needed
// // //                             GeometrySource = GeometrySourceType.DerivedFromAdmin,
// // //                             PrimaryAdministrativeBoundaryId = adminL2Boundary.Id, // Link to the GADM L2 entity itself
// // //                             DisplayLevel = "District" // Or "CityProper", "Markaz"
// // //                         });
// // //                     } else { logger.LogWarning($"Could not create OA for GADM L2 GID: {gid2}. Entity not found or no boundary.");}
// // //                 }
                
// // //                 if (operationalAreasToSeed.Any()) await context.OperationalAreas.AddRangeAsync(operationalAreasToSeed);
// // //                 await context.SaveChangesAsync();
// // //                 operationalAreasMap = await context.OperationalAreas.AsNoTracking().ToDictionaryAsync(oa => oa.Slug, oa => oa);
// // //                 logger.LogInformation("Seeded {Count} OperationalAreas.", operationalAreasMap.Count);
// // //             } else {
// // //                  operationalAreasMap = await context.OperationalAreas.AsNoTracking().ToDictionaryAsync(oa => oa.Slug, oa => oa);
// // //             }


// // //             List<GlobalServiceDefinition> globalServices = new();
// // //             if (forceReseed || !await context.GlobalServiceDefinitions.AnyAsync()) { 
// // //                 logger.LogInformation("Seeding GlobalServiceDefinitions...");
// // //                 globalServices = new List<GlobalServiceDefinition> {
// // //                     new() { ServiceCode = "OIL_CHANGE_STD", DefaultNameEn = "Standard Oil Change", DefaultNameAr = "تغيير زيت قياسي", Category = ShopCategory.OilChange, DefaultEstimatedDurationMinutes = 30, IsGloballyActive = true },
// // //                     new() { ServiceCode = "OIL_CHANGE_SYN", DefaultNameEn = "Synthetic Oil Change", DefaultNameAr = "تغيير زيت تخليقي", Category = ShopCategory.OilChange, DefaultEstimatedDurationMinutes = 45, IsGloballyActive = true },
// // //                     new() { ServiceCode = "BRAKE_PAD_FRNT", DefaultNameEn = "Front Brake Pad Replacement", DefaultNameAr = "تغيير تيل الفرامل الأمامي", Category = ShopCategory.Brakes, DefaultEstimatedDurationMinutes = 60, IsGloballyActive = true },
// // //                     new() { ServiceCode = "AC_REGAS", DefaultNameEn = "A/C Re-gas", DefaultNameAr = "إعادة شحن فريون التكييف", Category = ShopCategory.ACRepair, DefaultEstimatedDurationMinutes = 45, IsGloballyActive = true },
// // //                     new() { ServiceCode = "CAR_WASH_EXT", DefaultNameEn = "Exterior Car Wash", DefaultNameAr = "غسيل خارجي للسيارة", Category = ShopCategory.CarWash, DefaultEstimatedDurationMinutes = 20, IsGloballyActive = true },
// // //                     new() { ServiceCode = "TIRE_ROTATE", DefaultNameEn = "Tire Rotation", DefaultNameAr = "تدوير الإطارات", Category = ShopCategory.TireServices, DefaultEstimatedDurationMinutes = 30, IsGloballyActive = true },
// // //                     new() { ServiceCode = "ENGINE_DIAG", DefaultNameEn = "Engine Diagnostics", DefaultNameAr = "تشخيص أعطال المحرك", Category = ShopCategory.Diagnostics, DefaultEstimatedDurationMinutes = 60, IsGloballyActive = true },
// // //                     new() { ServiceCode = "GEN_MAINT_INSP", DefaultNameEn = "General Maintenance Inspection", DefaultNameAr = "فحص صيانة عام", Category = ShopCategory.GeneralMaintenance, DefaultEstimatedDurationMinutes = 90, IsGloballyActive = true }
// // //                 };
// // //                 if(globalServices.Any()) await context.GlobalServiceDefinitions.AddRangeAsync(globalServices);
// // //                 await context.SaveChangesAsync(); // SaveChanges after AddRange
// // //                 logger.LogInformation("Seeded {Count} GlobalServiceDefinitions.", globalServices.Count);
// // //             } else {
// // //                 globalServices = await context.GlobalServiceDefinitions.AsNoTracking().ToListAsync();
// // //             }

// // //             List<Shop> shopsToSaveInDb = new List<Shop>();
// // //             List<Shop> seededShopsForServices; 
// // //             if (forceReseed || !await context.Shops.AnyAsync())
// // //             {
// // //                 logger.LogInformation("Preparing to seed shops from JSON file and map them to OperationalAreas...");
// // //                 var shopSeedDtos = LoadShopsFromFile("SeedData/shops_data.json", logger);
// // //                 if (shopSeedDtos != null && shopSeedDtos.Any())
// // //                 {
// // //                     foreach (var shopDto in shopSeedDtos)
// // //                     {
// // //                         if (!Enum.TryParse<ShopCategory>(shopDto.Category, true, out var shopCategoryEnum))
// // //                         {
// // //                             logger.LogWarning($"Could not parse shop category '{shopDto.Category}' for shop '{shopDto.NameEn}'. Defaulting to Unknown.");
// // //                             shopCategoryEnum = ShopCategory.Unknown;
// // //                         }
// // //                         var shopEntity = new Shop {
// // //                             NameEn = shopDto.NameEn, NameAr = shopDto.NameAr, Address = shopDto.Address,
// // //                             Location = CreatePoint(shopDto.Latitude, shopDto.Longitude), PhoneNumber = shopDto.PhoneNumber,
// // //                             ServicesOffered = shopDto.ServicesOffered, OpeningHours = shopDto.OpeningHours,
// // //                             Category = shopCategoryEnum, Slug = shopDto.Slug, LogoUrl = shopDto.LogoUrl
// // //                         };
                        
// // //                         OperationalArea? targetOA = null;
// // //                         // Refined Shop-to-OA Mapping Logic
// // //                         if (shopDto.OriginalCityRefSlug == "6th-october-city-ref") {
// // //                             targetOA = operationalAreasMap.GetValueOrDefault(GenerateOperationalAreaSlug("6th of October City Zone"));
// // //                         } else if (shopDto.OriginalCityRefSlug == "new-cairo-ref") {
// // //                             targetOA = operationalAreasMap.GetValueOrDefault(GenerateOperationalAreaSlug("New Cairo District"));
// // //                         } else if (shopDto.OriginalCityRefSlug == "cairo-ref") {
// // //                             // Attempt to map to more specific Cairo OAs based on address or fallback
// // //                             if (shopEntity.Address.Contains("Bulaq", StringComparison.OrdinalIgnoreCase))
// // //                                 targetOA = operationalAreasMap.GetValueOrDefault(GenerateOperationalAreaSlug("Bulaq")); // Ensure "Bulaq" OA is created
// // //                             else if (shopEntity.Address.Contains("Zamalek", StringComparison.OrdinalIgnoreCase))
// // //                                 targetOA = operationalAreasMap.GetValueOrDefault(GenerateOperationalAreaSlug("Zamalek")); // Ensure "Zamalek" OA is created
// // //                             else // Fallback for other Cairo shops if not fitting into specific districts
// // //                                 targetOA = operationalAreasMap.GetValueOrDefault(GenerateOperationalAreaSlug("AlQahirah-governorate")); // Fallback to governorate-wide OA
// // //                         } else if (shopDto.OriginalCityRefSlug == "giza-ref") {
// // //                             if (shopEntity.Address.Contains("6th of October", StringComparison.OrdinalIgnoreCase) || 
// // //                                 shopEntity.Address.Contains("October City", StringComparison.OrdinalIgnoreCase) ||
// // //                                 shopEntity.Address.Contains("السادس من أكتوبر", StringComparison.OrdinalIgnoreCase) ) {
// // //                                 targetOA = operationalAreasMap.GetValueOrDefault(GenerateOperationalAreaSlug("6th of October City Zone"));
// // //                             } else { 
// // //                                 targetOA = operationalAreasMap.GetValueOrDefault(GenerateOperationalAreaSlug("AlJizah-governorate")); // Fallback
// // //                             }
// // //                         } else if (shopDto.OriginalCityRefSlug == "alexandria-ref") {
// // //                             targetOA = operationalAreasMap.GetValueOrDefault(GenerateOperationalAreaSlug("AlIskandariyah-governorate")); // Fallback
// // //                         }

// // //                         if (targetOA != null) {
// // //                             shopEntity.OperationalAreaId = targetOA.Id;
// // //                             string initialSlug = string.IsNullOrWhiteSpace(shopEntity.Slug) ? 
// // //                                 GenerateShopSlug(shopEntity.NameEn, targetOA.Slug) : 
// // //                                 GenerateShopSlug(shopEntity.Slug, targetOA.Slug); 
                            
// // //                             string finalSlug = initialSlug;
// // //                             int counter = 1;
// // //                             while (shopsToSaveInDb.Any(s => s.OperationalAreaId == targetOA.Id && s.Slug == finalSlug) || 
// // //                                    await context.Shops.AnyAsync(s => s.OperationalAreaId == targetOA.Id && s.Slug == finalSlug && s.Id != shopEntity.Id ))
// // //                             {
// // //                                 finalSlug = $"{initialSlug}-{counter++}";
// // //                             }
// // //                             shopEntity.Slug = finalSlug;
// // //                             if (string.IsNullOrEmpty(shopEntity.LogoUrl)) shopEntity.LogoUrl = GenerateLogoUrlFromName(shopEntity.Slug);
// // //                             shopsToSaveInDb.Add(shopEntity);
// // //                         } else {
// // //                             logger.LogError($"Could not map shop '{shopEntity.NameEn}' (Original City Ref Slug: {shopDto.OriginalCityRefSlug}) to an OperationalArea. Please check OA slugs and shop mapping logic. Skipping shop.");
// // //                         }
// // //                     }
// // //                     if (shopsToSaveInDb.Any()) await context.Shops.AddRangeAsync(shopsToSaveInDb); 
// // //                     await context.SaveChangesAsync();
// // //                     logger.LogInformation("Successfully seeded {ShopCountActual} shops from JSON.", shopsToSaveInDb.Count);
// // //                 } else logger.LogWarning("No shop data loaded from shops_data.json or file was empty.");
// // //                 seededShopsForServices = await context.Shops.Where(s => !s.IsDeleted).AsNoTracking().ToListAsync(); 
// // //             } else {
// // //                 logger.LogInformation("Shops table already has data and forceReseed is false. Skipping shop seed.");
// // //                 seededShopsForServices = await context.Shops.Where(s => !s.IsDeleted).AsNoTracking().ToListAsync();
// // //             }


// // //             if ((forceReseed || !await context.ShopServices.AnyAsync()) && seededShopsForServices.Any() && globalServices.Any())
// // //             {
// // //                 logger.LogInformation("Seeding ShopServices...");
// // //                 var shopServicesToSeed = new List<ShopService>();
// // //                 var random = new Random();
// // //                 ShopService CreateShopServiceEntry(Guid shopId, GlobalServiceDefinition? globalDef, decimal price, string? customNameEn = null, string? customNameAr = null, int? duration = null) {
// // //                     if (globalDef == null) throw new InvalidOperationException($"GlobalServiceDefinition is unexpectedly null for shop {shopId}.");
// // //                     return new ShopService {
// // //                         ShopId = shopId, GlobalServiceId = globalDef.GlobalServiceId, CustomServiceNameEn = customNameEn, CustomServiceNameAr = customNameAr,
// // //                         EffectiveNameEn = !string.IsNullOrEmpty(customNameEn) ? customNameEn : globalDef.DefaultNameEn,
// // //                         EffectiveNameAr = !string.IsNullOrEmpty(customNameAr) ? customNameAr : globalDef.DefaultNameAr,
// // //                         Price = price, DurationMinutes = duration ?? globalDef.DefaultEstimatedDurationMinutes, IsOfferedByShop = true, SortOrder = random.Next(1,100)
// // //                     };
// // //                 }
// // //                 foreach (var shop in seededShopsForServices) {
// // //                     try {
// // //                         var oilChangeStd = globalServices.FirstOrDefault(g => g.ServiceCode == "OIL_CHANGE_STD");
// // //                         var oilChangeSyn = globalServices.FirstOrDefault(g => g.ServiceCode == "OIL_CHANGE_SYN");
// // //                         var brakeFront = globalServices.FirstOrDefault(g => g.ServiceCode == "BRAKE_PAD_FRNT");
// // //                         var acRegas = globalServices.FirstOrDefault(g => g.ServiceCode == "AC_REGAS");
// // //                         var carWashExt = globalServices.FirstOrDefault(g => g.ServiceCode == "CAR_WASH_EXT");
// // //                         var tireRotate = globalServices.FirstOrDefault(g => g.ServiceCode == "TIRE_ROTATE");
// // //                         var engineDiag = globalServices.FirstOrDefault(g => g.ServiceCode == "ENGINE_DIAG");
// // //                         var genMaintInsp = globalServices.FirstOrDefault(g => g.ServiceCode == "GEN_MAINT_INSP");

// // //                         if ((shop.Category == ShopCategory.GeneralMaintenance || shop.Category == ShopCategory.OilChange) && oilChangeStd != null && oilChangeSyn != null) {
// // //                             shopServicesToSeed.Add(CreateShopServiceEntry(shop.Id, oilChangeStd, Math.Round((decimal)(random.NextDouble() * 100 + 250), 2) ));
// // //                             shopServicesToSeed.Add(CreateShopServiceEntry(shop.Id, oilChangeSyn, Math.Round((decimal)(random.NextDouble() * 150 + 450), 2), duration: 50 ));
// // //                         }
// // //                         if ((shop.Category == ShopCategory.GeneralMaintenance || shop.Category == ShopCategory.Brakes) && brakeFront != null) 
// // //                             shopServicesToSeed.Add(CreateShopServiceEntry(shop.Id, brakeFront, Math.Round((decimal)(random.NextDouble() * 200 + 600), 2) ));
// // //                         if ((shop.Category == ShopCategory.GeneralMaintenance || shop.Category == ShopCategory.ACRepair) && acRegas != null)
// // //                             shopServicesToSeed.Add(CreateShopServiceEntry(shop.Id, acRegas, Math.Round((decimal)(random.NextDouble() * 100 + 300), 2) ));
// // //                         if (shop.Category == ShopCategory.CarWash && carWashExt != null)
// // //                             shopServicesToSeed.Add(CreateShopServiceEntry(shop.Id, carWashExt, Math.Round((decimal)(random.NextDouble() * 50 + 100), 2), duration: 25 ));
// // //                         if (shop.Category == ShopCategory.TireServices && tireRotate != null)
// // //                             shopServicesToSeed.Add(CreateShopServiceEntry(shop.Id, tireRotate, Math.Round((decimal)(random.NextDouble() * 80 + 150), 2) ));
// // //                         if ((shop.Category == ShopCategory.Diagnostics || shop.Category == ShopCategory.GeneralMaintenance) && engineDiag != null)
// // //                             shopServicesToSeed.Add(CreateShopServiceEntry(shop.Id, engineDiag, Math.Round((decimal)(random.NextDouble() * 150 + 200), 2) ));
// // //                         if (shop.Category == ShopCategory.GeneralMaintenance && genMaintInsp != null)
// // //                             shopServicesToSeed.Add(CreateShopServiceEntry(shop.Id, genMaintInsp, Math.Round((decimal)(random.NextDouble() * 200 + 300), 2), duration: 100 ));
// // //                         if (shop.NameEn.Contains("Bosch", StringComparison.OrdinalIgnoreCase)) { 
// // //                             shopServicesToSeed.Add(new ShopService { ShopId = shop.Id, GlobalServiceId = null, CustomServiceNameEn = "Bosch Premium Diagnostic Package", EffectiveNameEn = "Bosch Premium Diagnostic Package", CustomServiceNameAr = "باقة بوش التشخيصية الممتازة", EffectiveNameAr = "باقة بوش التشخيصية الممتازة", ShopSpecificDescriptionEn = "Full vehicle computer scan with Bosch certified equipment and detailed report.", Price = 750.00m, DurationMinutes = 120, IsOfferedByShop = true, SortOrder = 5, IsPopularAtShop = true });
// // //                         }
// // //                     } catch (InvalidOperationException ioEx) { logger.LogError(ioEx, $"Skipping service generation for shop {shop.Id} ({shop.NameEn}) due to missing global service definition."); }
// // //                 }
// // //                 if(shopServicesToSeed.Any()) await context.ShopServices.AddRangeAsync(shopServicesToSeed);
// // //                 await context.SaveChangesAsync(); // SaveChanges after AddRange
// // //                 logger.LogInformation("Successfully seeded {ShopServiceCount} shop services.", shopServicesToSeed.Count);
// // //             } else logger.LogInformation("ShopServices table already has data or prerequisites not met.");
// // //             logger.LogInformation("(DataSeeder) Seeding process complete.");
// // //         }
// // //     }

// // //     public static class AttributesTableExtensions
// // //     {
// // //         public static string? GetOptionalString(this IAttributesTable table, string name)
// // //         {
// // //             if (table == null || !table.Exists(name)) return null;
// // //             return table[name]?.ToString();
// // //         }
// // //     }
// // // }
// // // // // Data/DataSeeder.cs
// // // // using AutomotiveServices.Api.Models;
// // // // using Microsoft.EntityFrameworkCore;
// // // // using NetTopologySuite.Geometries;
// // // // using NetTopologySuite.IO; // For GeoJsonReader
// // // // using System;
// // // // using System.Collections.Generic;
// // // // using System.Linq;
// // // // // using System.Text.Json; // No longer needed for JsonDocument here if GeoJsonReader takes string
// // // // using System.Text.RegularExpressions;
// // // // using System.Threading.Tasks;
// // // // using Microsoft.Extensions.Logging;

// // // // namespace AutomotiveServices.Api.Data
// // // // {
// // // //     public static class DataSeeder
// // // //     {
// // // //         private static readonly GeometryFactory _geometryFactory = new GeometryFactory(new PrecisionModel(), 4326); // SRID 4326 for WGS84
// // // //         private static readonly GeoJsonReader _geoJsonReader = new GeoJsonReader(); // Keep instance for reuse

// // // //         private static Point CreatePoint(double latitude, double longitude) =>
// // // //            _geometryFactory.CreatePoint(new Coordinate(longitude, latitude));

// // // //         /// <summary>
// // // //         /// Parses a GeoJSON geometry string into an NTS Geometry object.
// // // //         /// </summary>
// // // //         /// <param name="geoJsonGeometryString">The JSON string representing the GeoJSON geometry object (e.g., {"type":"MultiPolygon", "coordinates":...}).</param>
// // // //         /// <param name="logger">Logger instance.</param>
// // // //         /// <returns>NTS Geometry object or null if parsing fails.</returns>
// // // //         // private static Geometry? CreateGeometryFromGeoJsonString(string? geoJsonGeometryString, ILogger logger)
// // // //         // {
// // // //         //     if (string.IsNullOrWhiteSpace(geoJsonGeometryString))
// // // //         //     {
// // // //         //         return null;
// // // //         //     }
// // // //         //     try
// // // //         //     {
// // // //         //         // GeoJsonReader directly parses the JSON string representing a geometry object
// // // //         //         var geometry = _geoJsonReader.Read<Geometry>(geoJsonGeometryString); // Use the string overload

// // // //         //         if (geometry is Polygon polygon && (geometry.UserData == null || !(bool)geometry.UserData))
// // // //         //         {
// // // //         //             var multiPolygon = _geometryFactory.CreateMultiPolygon(new[] { polygon });
// // // //         //             multiPolygon.UserData = true; 
// // // //         //             return multiPolygon;
// // // //         //         }
// // // //         //         if (geometry is MultiPolygon mp)
// // // //         //         {
// // // //         //             mp.UserData = true;
// // // //         //             return mp;
// // // //         //         }
// // // //         //         logger.LogWarning($"Parsed GeoJSON resulted in geometry type: {geometry?.GeometryType}, expected Polygon or MultiPolygon. String: {geoJsonGeometryString.Substring(0, Math.Min(geoJsonGeometryString.Length, 100))}");
// // // //         //         return geometry; 
// // // //         //     }
// // // //         //     catch (Exception ex)
// // // //         //     {
// // // //         //         logger.LogError(ex, "Error parsing GeoJSON geometry string. String (first 100 chars): {GeoJsonString}", geoJsonGeometryString.Substring(0, Math.Min(geoJsonGeometryString.Length, 100)));
// // // //         //         return null;
// // // //         //     }
// // // //         // }

// // // //         // private static Geometry? CreateSimplifiedGeometry(Geometry? detailedGeometry, ILogger logger, double tolerance = 0.005)
// // // //         // {
// // // //         //     if (detailedGeometry == null) return null;
// // // //         //     try
// // // //         //     {
// // // //         //         var simplifier = new NetTopologySuite.Simplify.DouglasPeuckerSimplifier(detailedGeometry)
// // // //         //         {
// // // //         //             DistanceTolerance = tolerance 
// // // //         //         };
// // // //         //         var simplified = simplifier.GetResultGeometry();
// // // //         //         if (simplified is Polygon polygon && (simplified.UserData == null || !(bool)simplified.UserData) )
// // // //         //         {
// // // //         //              var multiPolygon = _geometryFactory.CreateMultiPolygon(new[] { polygon });
// // // //         //              multiPolygon.UserData = true; 
// // // //         //              return multiPolygon;
// // // //         //         }
// // // //         //         if (simplified is MultiPolygon mp) {
// // // //         //             mp.UserData = true; 
// // // //         //             return mp;
// // // //         //         }
// // // //         //         return simplified;
// // // //         //     }
// // // //         //     catch (Exception ex)
// // // //         //     {
// // // //         //         logger.LogError(ex, "Error simplifying geometry.");
// // // //         //         return detailedGeometry.Copy() as Geometry;
// // // //         //     }
// // // //         // }
        
// // // //         private static Geometry? EnsureMultiPolygon(Geometry? geometry, ILogger logger)
// // // //         {
// // // //             if (geometry == null) return null;

// // // //             if (geometry is Polygon polygon)
// // // //             {
// // // //                 // Ensure UserData isn't misinterpreted if it was already set.
// // // //                 // A simple way is to create a new MultiPolygon.
// // // //                 var multiPolygon = _geometryFactory.CreateMultiPolygon(new[] { polygon.Copy() as Polygon });
// // // //                 // logger.LogTrace("Converted Polygon to MultiPolygon.");
// // // //                 return multiPolygon;
// // // //             }
// // // //             if (geometry is MultiPolygon mp)
// // // //             {
// // // //                 // logger.LogTrace("Geometry is already MultiPolygon.");
// // // //                 return mp.Copy() as MultiPolygon; // Return a copy to avoid side effects if original is cached
// // // //             }

// // // //             logger.LogWarning($"Geometry type {geometry.GeometryType} was not Polygon or MultiPolygon. Returning as is, but schema expects MultiPolygon.");
// // // //             return geometry.Copy(); // Return a copy
// // // //         }


// // // //         private static Geometry? CreateSimplifiedGeometry(Geometry? detailedGeometry, ILogger logger, double tolerance = 0.005)
// // // //         {
// // // //             if (detailedGeometry == null) return null;
// // // //             try
// // // //             {
// // // //                 // Ensure we simplify a MultiPolygon if the input was converted to it
// // // //                 var geometryToSimplify = EnsureMultiPolygon(detailedGeometry, logger);
// // // //                 if (geometryToSimplify == null) return null;

// // // //                 var simplifier = new NetTopologySuite.Simplify.DouglasPeuckerSimplifier(geometryToSimplify)
// // // //                 {
// // // //                     DistanceTolerance = tolerance
// // // //                 };
// // // //                 var simplified = simplifier.GetResultGeometry();
                
// // // //                 // The simplifier might return a Polygon even if input was MultiPolygon (if it simplifies to one shell)
// // // //                 // So, ensure it's MultiPolygon again for schema consistency.
// // // //                 return EnsureMultiPolygon(simplified, logger);
// // // //             }
// // // //             catch (Exception ex)
// // // //             {
// // // //                 logger.LogError(ex, "Error simplifying geometry.");
// // // //                 return EnsureMultiPolygon(detailedGeometry.Copy() as Geometry, logger); // Return a copy of original, ensured as MultiPolygon
// // // //             }
// // // //         }
        
// // // //         private static string GenerateSlugFromName(string name, bool isShop = false)
// // // //         {
// // // //             string prefix = isShop ? "shop-" : "area-";
// // // //             if (string.IsNullOrWhiteSpace(name)) return $"{prefix}{Guid.NewGuid().ToString().Substring(0, 8)}";

// // // //             string str = name.ToLowerInvariant().Trim();
// // // //             str = Regex.Replace(str, @"[^a-z0-9\s-]", "");
// // // //             str = Regex.Replace(str, @"\s+", "-");
// // // //             str = Regex.Replace(str, @"-+", "-");
// // // //             str = str.Length > 150 ? str.Substring(0, 150) : str;
// // // //             if (string.IsNullOrEmpty(str))
// // // //             {
// // // //                 string fallbackName = Regex.Replace(name.ToLowerInvariant().Trim(), @"[^a-z0-9]", "");
// // // //                 str = fallbackName.Length > 0 ? fallbackName : Guid.NewGuid().ToString().Substring(0, 8);
// // // //                 str = str.Length > 150 ? str.Substring(0, 150) : str;
// // // //             }
// // // //             return $"{prefix}{str}";
// // // //         }

// // // //         private static string GenerateLogoUrlFromName(string shopSpecificSlug)
// // // //         {
// // // //             return $"/logos/{shopSpecificSlug}.png";
// // // //         }
        
// // // //          // NEW HELPER: Load and parse a GeoJSON FeatureCollection file
// // // //         private static FeatureCollection? LoadFeatureCollectionFromFile(string relativePath, ILogger logger)
// // // //         {
// // // //             try
// // // //             {
// // // //                 string baseDirectory = AppContext.BaseDirectory;
// // // //                 string filePath = Path.Combine(baseDirectory, relativePath);

// // // //                 if (!File.Exists(filePath))
// // // //                 {
// // // //                     logger.LogError($"GeoJSON file not found at {filePath}. Ensure it's in the output directory (e.g., set 'Copy to Output Directory').");
// // // //                     return null;
// // // //                 }

// // // //                 string geoJsonString = File.ReadAllText(filePath);
// // // //                 var featureCollection = _geoJsonReader.Read<FeatureCollection>(geoJsonString);
// // // //                 logger.LogInformation($"Successfully loaded and parsed {featureCollection.Count} features from {relativePath}.");
// // // //                 return featureCollection;
// // // //             }
// // // //             catch (Exception ex)
// // // //             {
// // // //                 logger.LogError(ex, $"Error loading or parsing GeoJSON file: {relativePath}");
// // // //                 return null;
// // // //             }
// // // //         }


// // // //         public static async Task SeedAsync(AppDbContext context, ILogger logger, bool forceReseed)
// // // //         {
// // // //             if (forceReseed)
// // // //             {
// // // //                 logger.LogInformation("(DataSeeder) Force re-seed: Clearing relevant tables in order...");
// // // //                 await context.Database.ExecuteSqlRawAsync("ALTER TABLE \"Shops\" DROP CONSTRAINT IF EXISTS \"FK_Shops_OperationalAreas_OperationalAreaId\";");
// // // //                 await context.Database.ExecuteSqlRawAsync("DELETE FROM \"ShopServices\";");
// // // //                 await context.Database.ExecuteSqlRawAsync("DELETE FROM \"GlobalServiceDefinitions\";");
// // // //                 await context.Database.ExecuteSqlRawAsync("DELETE FROM \"Shops\";");
// // // //                 await context.Database.ExecuteSqlRawAsync("DELETE FROM \"OperationalAreas\";");
// // // //                 await context.Database.ExecuteSqlRawAsync("DELETE FROM \"AdministrativeBoundaries\";");
// // // //                 await context.Database.ExecuteSqlRawAsync("DELETE FROM \"Cities\";");
// // // //                 logger.LogInformation("(DataSeeder) Relevant database tables cleared for re-seeding.");
// // // //             }

// // // //             Dictionary<string, City> oldCitiesLookup = new();
// // // //             if (forceReseed || !await context.Cities.AnyAsync())
// // // //             {
// // // //                 logger.LogInformation("Seeding original 'Cities' table (for mapping reference)...");
// // // //                 var citiesForMappingRef = new List<City> {
// // // //                     new() { NameEn = "CairoRef", NameAr = "القاهرة مرجع", Slug = "cairo-ref", StateProvince = "Cairo Governorate", Country = "Egypt", IsActive = true, Location = CreatePoint(30.0444, 31.2357) },
// // // //                     new() { NameEn = "AlexandriaRef", NameAr = "الإسكندرية مرجع", Slug = "alexandria-ref", StateProvince = "Alexandria Governorate", Country = "Egypt", IsActive = true, Location = CreatePoint(31.2001, 29.9187) },
// // // //                     new() { NameEn = "GizaRef", NameAr = "الجيزة مرجع", Slug = "giza-ref", StateProvince = "Giza Governorate", Country = "Egypt", IsActive = true, Location = CreatePoint(29.9870, 31.1313) },
// // // //                     new() { NameEn = "6th October CityRef", NameAr = "مدينة 6 أكتوبر مرجع", Slug = "6th-october-city-ref", StateProvince = "Giza Governorate", Country = "Egypt", IsActive = true, Location = CreatePoint(29.9660, 30.9232) },
// // // //                     new() { NameEn = "New CairoRef", NameAr = "القاهرة الجديدة مرجع", Slug = "new-cairo-ref", StateProvince = "Cairo Governorate", Country = "Egypt", IsActive = true, Location = CreatePoint(30.0271, 31.4961) }
// // // //                 };
// // // //                 await context.Cities.AddRangeAsync(citiesForMappingRef);
// // // //                 await context.SaveChangesAsync();
// // // //                 oldCitiesLookup = citiesForMappingRef.ToDictionary(c => c.Slug, c => c);
// // // //                 logger.LogInformation("Seeded {Count} original cities for mapping.", citiesForMappingRef.Count);
// // // //             }
// // // //             else
// // // //             {
// // // //                 oldCitiesLookup = await context.Cities.ToDictionaryAsync(c => c.Slug, c => c);
// // // //             }

// // // //             // --- 1. SEED ADMINISTRATIVE BOUNDARIES (Governorates) ---
// // // //             //{"type":"Feature","properties":{"GID_1":"EGY.11_1","GID_0":"EGY","COUNTRY":"Egypt","NAME_1":"AlQahirah","VARNAME_1":"Cairo|ElCairo|ElQahira|LeCair","NL_NAME_1":"NA","TYPE_1":"Muhafazah","ENGTYPE_1":"Governorate","CC_1":"NA","HASC_1":"EG.QH","ISO_1":"EG-C"},"geometry":
// // // //             // {"type":"MultiPolygon","coordinates":[[[[31.2961,29.8041],[31.2958,29.8418],[31.2833,29.8833],[31.2805,29.9192],[31.2779,29.9329],[31.265,29.947],[31.2408,29.9619],[31.227,29.9965],[31.2233,30.0325],[31.2291,30.0361],[31.2201,30.048],[31.2168,30.0679],[31.2254,30.0756],[31.2419,30.1056],[31.2843,30.1135],[31.2906,30.1311],[31.3129,30.1423],[31.3166,30.1422],[31.3298,30.1568],[31.3217,30.1718],[31.3397,30.164],[31.3596,30.1664],[31.3663,30.1547],[31.3769,30.1622],[31.3775,30.1794],[31.4012,30.1752],[31.4052,30.1903],[31.4164,30.1882],[31.4148,30.1964],[31.433,30.191],[31.5917,30.1975],[31.6773,30.1838],[31.862,30.1877],[31.8996,30.0472],[31.907,29.8706],[31.8933,29.7337],[31.6837,29.7594],[31.5532,29.7627],[31.4368,29.7484],[31.321,29.7609],[31.308,29.7541],[31.2984,29.7717],[31.2898,29.771],[31.2961,29.8041]]]]}}
// // // //             // !!! REPLACE THESE PLACEHOLDER GEOJSON STRINGS WITH YOUR ACTUAL DATA !!!
// // // //             string geoJsonCairoGov_GeometryString = @"{""type"":""MultiPolygon"",
// // // //             ""coordinates"":[[[[31.2961,29.8041],[31.2958,29.8418],[31.2833,29.8833],[31.2805,29.9192],[31.2779,29.9329],[31.265,29.947],[31.2408,29.9619],[31.227,29.9965],[31.2233,30.0325],[31.2291,30.0361],[31.2201,30.048],[31.2168,30.0679],[31.2254,30.0756],[31.2419,30.1056],[31.2843,30.1135],[31.2906,30.1311],[31.3129,30.1423],[31.3166,30.1422],[31.3298,30.1568],[31.3217,30.1718],[31.3397,30.164],[31.3596,30.1664],[31.3663,30.1547],[31.3769,30.1622],[31.3775,30.1794],[31.4012,30.1752],[31.4052,30.1903],[31.4164,30.1882],[31.4148,30.1964],[31.433,30.191],[31.5917,30.1975],[31.6773,30.1838],[31.862,30.1877],[31.8996,30.0472],[31.907,29.8706],[31.8933,29.7337],[31.6837,29.7594],[31.5532,29.7627],[31.4368,29.7484],[31.321,29.7609],[31.308,29.7541],[31.2984,29.7717],[31.2898,29.771],[31.2961,29.8041]]]]}}";
// // // //             //{"type":"Feature","properties":{"GID_1":"EGY.8_1","GID_0":"EGY","COUNTRY":"Egypt","NAME_1":"AlJizah","VARNAME_1":"ElGiza|ElGīzah|Gizeh|Giza|Guiz","NL_NAME_1":"NA","TYPE_1":"Muhafazah","ENGTYPE_1":"Governorate","CC_1":"NA","HASC_1":"EG.JZ","ISO_1":"EG-GZ"},"geometry":
// // // //             // {"type":"MultiPolygon","coordinates":[[[[30.6409,30.1711],[30.8139,30.3314],[30.8095,30.3424],[30.8206,30.3461],[30.8293,30.3304],[30.8439,30.3245],[30.8766,30.3403],[30.9175,30.3346],[30.9227,30.3124],[30.9152,30.2863],[30.9569,30.2824],[30.9725,30.2284],[30.9993,30.2045],[31.0157,30.1974],[31.0653,30.2204],[31.0882,30.2103],[31.1302,30.1776],[31.17,30.1413],[31.2283,30.1284],[31.2419,30.1056],[31.2254,30.0756],[31.2168,30.0679],[31.2201,30.048],[31.2291,30.0361],[31.2233,30.0325],[31.227,29.9965],[31.2408,29.9619],[31.265,29.947],[31.2779,29.9329],[31.2805,29.9192],[31.2833,29.8833],[31.2958,29.8418],[31.2961,29.8041],[31.2898,29.771],[31.2984,29.7717],[31.308,29.7541],[31.321,29.7609],[31.4368,29.7484],[31.5532,29.7627],[31.6837,29.7594],[31.8933,29.7337],[31.8346,28.9937],[31.4674,29.0907],[31.2073,29.1965],[31.2022,29.2027],[31.2135,29.2201],[31.213,29.2461],[31.2213,29.2593],[31.2139,29.3449],[31.234,29.378],[31.2282,29.3997],[31.2017,29.4019],[31.1785,29.4321],[31.1262,29.4218],[31.1002,29.457],[31.0349,29.7219],[30.7597,29.7301],[30.2408,29.4156],[30.0105,29.228],[29.8565,29.0913],[29.7733,28.779],[29.7387,28.6039],[29.5929,28.3213],[29.2559,28.1026],[28.7884,27.9192],[28.4666,27.6746],[27.3609,27.6758],[27.8309,28.5787],[28.7615,28.7515],[29.6131,29.5091],[30.3378,29.8773],[30.3672,29.9154],[30.6409,30.1711]]]]}}
// // // //             string geoJsonGizaGov_GeometryString = @"{""type"":""MultiPolygon"",""coordinates"":[[[[30.6409,30.1711],[30.8139,30.3314],[30.8095,30.3424],[30.8206,30.3461],[30.8293,30.3304],[30.8439,30.3245],[30.8766,30.3403],[30.9175,30.3346],[30.9227,30.3124],[30.9152,30.2863],[30.9569,30.2824],[30.9725,30.2284],[30.9993,30.2045],[31.0157,30.1974],[31.0653,30.2204],[31.0882,30.2103],[31.1302,30.1776],[31.17,30.1413],[31.2283,30.1284],[31.2419,30.1056],[31.2254,30.0756],[31.2168,30.0679],[31.2201,30.048],[31.2291,30.0361],[31.2233,30.0325],[31.227,29.9965],[31.2408,29.9619],[31.265,29.947],[31.2779,29.9329],[31.2805,29.9192],[31.2833,29.8833],[31.2958,29.8418],[31.2961,29.8041],[31.2898,29.771],[31.2984,29.7717],[31.308,29.7541],[31.321,29.7609],[31.4368,29.7484],[31.5532,29.7627],[31.6837,29.7594],[31.8933,29.7337],[31.8346,28.9937],[31.4674,29.0907],[31.2073,29.1965],[31.2022,29.2027],[31.2135,29.2201],[31.213,29.2461],[31.2213,29.2593],[31.2139,29.3449],[31.234,29.378],[31.2282,29.3997],[31.2017,29.4019],[31.1785,29.4321],[31.1262,29.4218],[31.1002,29.457],[31.0349,29.7219],[30.7597,29.7301],[30.2408,29.4156],[30.0105,29.228],[29.8565,29.0913],[29.7733,28.779],[29.7387,28.6039],[29.5929,28.3213],[29.2559,28.1026],[28.7884,27.9192],[28.4666,27.6746],[27.3609,27.6758],[27.8309,28.5787],[28.7615,28.7515],[29.6131,29.5091],[30.3378,29.8773],[30.3672,29.9154],[30.6409,30.1711]]]]}}";

// // // //             //{"type":"Feature","properties":{"GID_1":"EGY.6_1","GID_0":"EGY","COUNTRY":"Egypt","NAME_1":"AlIskandariyah","VARNAME_1":"Alexandria|Alexandrie|ElIskanda","NL_NAME_1":"محافظةالإسكندرية","TYPE_1":"Muhafazah","ENGTYPE_1":"Governorate","CC_1":"NA","HASC_1":"EG.IK","ISO_1":"EG-ALX"},"geometry":
// // // //             // {"type":"MultiPolygon","coordinates":[[[[29.6328,30.2628],[29.6252,30.76],[29.5101,30.7485],[29.4719,30.7929],[29.396,30.8599],[29.3793,30.8921],[29.4049,30.9026],[29.4257,30.9146],[29.4435,30.9226],[29.4454,30.9215],[29.4643,30.929],[29.4801,30.9382],[29.4951,30.9446],[29.5035,30.9507],[29.5338,30.9646],[29.5465,30.9724],[29.5482,30.9718],[29.5485,30.974],[29.5513,30.976],[29.5537,30.9757],[29.5635,30.984],[29.5713,30.9876],[29.5774,30.9957],[29.5899,31.001],[29.6129,31.0185],[29.6215,31.0232],[29.6238,31.0265],[29.6304,31.0279],[29.6329,31.0312],[29.6379,31.0329],[29.6557,31.0457],[29.6676,31.0521],[29.6693,31.0568],[29.6707,31.0557],[29.6732,31.0568],[29.6907,31.0696],[29.6985,31.0776],[29.7157,31.0885],[29.7171,31.0907],[29.7249,31.0929],[29.7368,31.1013],[29.7429,31.1032],[29.7462,31.1076],[29.7557,31.1129],[29.7568,31.1154],[29.7621,31.1168],[29.7626,31.1193],[29.7668,31.1213],[29.7796,31.1343],[29.7829,31.1413],[29.7824,31.1463],[29.7896,31.1504],[29.7943,31.1507],[29.7918,31.1477],[29.7871,31.1465],[29.7874,31.1443],[29.7901,31.1443],[29.791,31.1424],[29.7935,31.1435],[29.7957,31.1476],[29.799,31.1465],[29.7985,31.1437],[29.7951,31.1404],[29.794,31.1354],[29.8012,31.1443],[29.8029,31.1424],[29.8021,31.1365],[29.8076,31.136],[29.809,31.1396],[29.811,31.1404],[29.811,31.1335],[29.8168,31.1365],[29.8207,31.136],[29.8235,31.1387],[29.8265,31.1393],[29.8312,31.1426],[29.8343,31.1482],[29.8363,31.1471],[29.8388,31.1482],[29.8435,31.1546],[29.8504,31.1579],[29.8543,31.1626],[29.856,31.1712],[29.8585,31.1712],[29.8585,31.1688],[29.8601,31.169],[29.8668,31.1743],[29.8665,31.176],[29.8696,31.1746],[29.8754,31.1821],[29.874,31.1849],[29.8713,31.1843],[29.8715,31.1826],[29.8699,31.1821],[29.8671,31.1854],[29.8718,31.1918],[29.8774,31.1951],[29.8765,31.1963],[29.8746,31.1949],[29.8726,31.1982],[29.8693,31.1982],[29.8646,31.1929],[29.8643,31.1907],[29.8599,31.191],[29.8587,31.1868],[29.856,31.1887],[29.8574,31.1849],[29.8524,31.1876],[29.8629,31.1971],[29.8635,31.1993],[29.8621,31.2004],[29.8679,31.2015],[29.869,31.2038],[29.8776,31.2051],[29.8776,31.2085],[29.8754,31.2113],[29.8779,31.2126],[29.8829,31.2124],[29.8857,31.2146],[29.8868,31.2132],[29.8838,31.2115],[29.8832,31.2079],[29.8871,31.2029],[29.8915,31.2006],[29.8999,31.2015],[29.9068,31.209],[29.9021,31.214],[29.9057,31.2138],[29.911,31.2096],[29.9188,31.2132],[29.9243,31.2182],[29.9335,31.2229],[29.9368,31.2271],[29.9418,31.229],[29.946,31.2343],[29.9493,31.2343],[29.9504,31.2374],[29.9535,31.2368],[29.9529,31.2396],[29.9546,31.2412],[29.9599,31.241],[29.9643,31.244],[29.9654,31.2471],[29.9682,31.2476],[29.9726,31.2543],[29.9774,31.2563],[29.9807,31.2607],[29.9865,31.2638],[29.9854,31.2679],[29.9868,31.2707],[29.9979,31.2713],[30.0021,31.2749],[30.0026,31.2787],[30.0085,31.2793],[30.0107,31.2835],[30.0079,31.2865],[30.0093,31.2882],[30.0121,31.2879],[30.0118,31.2904],[30.0157,31.291],[30.0204,31.2876],[30.0213,31.2901],[30.0185,31.2896],[30.0179,31.2907],[30.0201,31.2954],[30.0249,31.2904],[30.0293,31.2907],[30.0365,31.294],[30.0488,31.3079],[30.0485,31.3104],[30.0435,31.3135],[30.0476,31.3157],[30.047,31.3174],[30.0579,31.3199],[30.0621,31.3251],[30.0696,31.3299],[30.0799,31.3312],[30.0799,31.3296],[30.0821,31.3285],[30.0815,31.3271],[30.0776,31.3293],[30.0729,31.3262],[30.0735,31.3235],[30.0762,31.3229],[30.074,31.3196],[30.0765,31.3185],[30.0707,31.3174],[30.0735,31.3143],[30.0715,31.309],[30.0762,31.3035],[30.076,31.2993],[30.0787,31.2954],[30.0846,31.2924],[30.086,31.2826],[30.0893,31.2813],[30.0803,31.2804],[30.0543,31.2093],[30.0475,31.2105],[30.0369,31.1854],[29.9892,31.1843],[29.9863,31.1394],[29.9024,31.0002],[29.9167,30.9801],[29.9057,30.9642],[29.9119,30.9553],[29.8515,30.9312],[29.9162,30.8288],[29.9592,30.7889],[29.9244,30.7591],[29.9605,30.7309],[29.8668,30.646],[29.9395,30.5893],[29.7189,30.4983],[29.6328,30.2628]]],[[[29.8482,31.1685],[29.8463,31.1654],[29.8474,31.1726],[29.8485,31.1718],[29.8482,31.1685]]],[[[29.8513,31.1832],[29.8513,31.1763],[29.8479,31.1746],[29.8499,31.1849],[29.8513,31.1832]]],[[[30.0379,31.3085],[30.0388,31.3054],[30.0371,31.304],[30.0365,31.309],[30.0379,31.3085]]],[[[30.1085,31.359],[30.1068,31.3568],[30.1035,31.3557],[30.1049,31.3593],[30.1085,31.359]]]]}},
// // // //             string geoJsonAlexGov_GeometryString = @"{""type"":""MultiPolygon"",""coordinates"":[[[[29.6328,30.2628],[29.6252,30.76],[29.5101,30.7485],[29.4719,30.7929],[29.396,30.8599],[29.3793,30.8921],[29.4049,30.9026],[29.4257,30.9146],[29.4435,30.9226],[29.4454,30.9215],[29.4643,30.929],[29.4801,30.9382],[29.4951,30.9446],[29.5035,30.9507],[29.5338,30.9646],[29.5465,30.9724],[29.5482,30.9718],[29.5485,30.974],[29.5513,30.976],[29.5537,30.9757],[29.5635,30.984],[29.5713,30.9876],[29.5774,30.9957],[29.5899,31.001],[29.6129,31.0185],[29.6215,31.0232],[29.6238,31.0265],[29.6304,31.0279],[29.6329,31.0312],[29.6379,31.0329],[29.6557,31.0457],[29.6676,31.0521],[29.6693,31.0568],[29.6707,31.0557],[29.6732,31.0568],[29.6907,31.0696],[29.6985,31.0776],[29.7157,31.0885],[29.7171,31.0907],[29.7249,31.0929],[29.7368,31.1013],[29.7429,31.1032],[29.7462,31.1076],[29.7557,31.1129],[29.7568,31.1154],[29.7621,31.1168],[29.7626,31.1193],[29.7668,31.1213],[29.7796,31.1343],[29.7829,31.1413],[29.7824,31.1463],[29.7896,31.1504],[29.7943,31.1507],[29.7918,31.1477],[29.7871,31.1465],[29.7874,31.1443],[29.7901,31.1443],[29.791,31.1424],[29.7935,31.1435],[29.7957,31.1476],[29.799,31.1465],[29.7985,31.1437],[29.7951,31.1404],[29.794,31.1354],[29.8012,31.1443],[29.8029,31.1424],[29.8021,31.1365],[29.8076,31.136],[29.809,31.1396],[29.811,31.1404],[29.811,31.1335],[29.8168,31.1365],[29.8207,31.136],[29.8235,31.1387],[29.8265,31.1393],[29.8312,31.1426],[29.8343,31.1482],[29.8363,31.1471],[29.8388,31.1482],[29.8435,31.1546],[29.8504,31.1579],[29.8543,31.1626],[29.856,31.1712],[29.8585,31.1712],[29.8585,31.1688],[29.8601,31.169],[29.8668,31.1743],[29.8665,31.176],[29.8696,31.1746],[29.8754,31.1821],[29.874,31.1849],[29.8713,31.1843],[29.8715,31.1826],[29.8699,31.1821],[29.8671,31.1854],[29.8718,31.1918],[29.8774,31.1951],[29.8765,31.1963],[29.8746,31.1949],[29.8726,31.1982],[29.8693,31.1982],[29.8646,31.1929],[29.8643,31.1907],[29.8599,31.191],[29.8587,31.1868],[29.856,31.1887],[29.8574,31.1849],[29.8524,31.1876],[29.8629,31.1971],[29.8635,31.1993],[29.8621,31.2004],[29.8679,31.2015],[29.869,31.2038],[29.8776,31.2051],[29.8776,31.2085],[29.8754,31.2113],[29.8779,31.2126],[29.8829,31.2124],[29.8857,31.2146],[29.8868,31.2132],[29.8838,31.2115],[29.8832,31.2079],[29.8871,31.2029],[29.8915,31.2006],[29.8999,31.2015],[29.9068,31.209],[29.9021,31.214],[29.9057,31.2138],[29.911,31.2096],[29.9188,31.2132],[29.9243,31.2182],[29.9335,31.2229],[29.9368,31.2271],[29.9418,31.229],[29.946,31.2343],[29.9493,31.2343],[29.9504,31.2374],[29.9535,31.2368],[29.9529,31.2396],[29.9546,31.2412],[29.9599,31.241],[29.9643,31.244],[29.9654,31.2471],[29.9682,31.2476],[29.9726,31.2543],[29.9774,31.2563],[29.9807,31.2607],[29.9865,31.2638],[29.9854,31.2679],[29.9868,31.2707],[29.9979,31.2713],[30.0021,31.2749],[30.0026,31.2787],[30.0085,31.2793],[30.0107,31.2835],[30.0079,31.2865],[30.0093,31.2882],[30.0121,31.2879],[30.0118,31.2904],[30.0157,31.291],[30.0204,31.2876],[30.0213,31.2901],[30.0185,31.2896],[30.0179,31.2907],[30.0201,31.2954],[30.0249,31.2904],[30.0293,31.2907],[30.0365,31.294],[30.0488,31.3079],[30.0485,31.3104],[30.0435,31.3135],[30.0476,31.3157],[30.047,31.3174],[30.0579,31.3199],[30.0621,31.3251],[30.0696,31.3299],[30.0799,31.3312],[30.0799,31.3296],[30.0821,31.3285],[30.0815,31.3271],[30.0776,31.3293],[30.0729,31.3262],[30.0735,31.3235],[30.0762,31.3229],[30.074,31.3196],[30.0765,31.3185],[30.0707,31.3174],[30.0735,31.3143],[30.0715,31.309],[30.0762,31.3035],[30.076,31.2993],[30.0787,31.2954],[30.0846,31.2924],[30.086,31.2826],[30.0893,31.2813],[30.0803,31.2804],[30.0543,31.2093],[30.0475,31.2105],[30.0369,31.1854],[29.9892,31.1843],[29.9863,31.1394],[29.9024,31.0002],[29.9167,30.9801],[29.9057,30.9642],[29.9119,30.9553],[29.8515,30.9312],[29.9162,30.8288],[29.9592,30.7889],[29.9244,30.7591],[29.9605,30.7309],[29.8668,30.646],[29.9395,30.5893],[29.7189,30.4983],[29.6328,30.2628]]],[[[29.8482,31.1685],[29.8463,31.1654],[29.8474,31.1726],[29.8485,31.1718],[29.8482,31.1685]]],[[[29.8513,31.1832],[29.8513,31.1763],[29.8479,31.1746],[29.8499,31.1849],[29.8513,31.1832]]],[[[30.0379,31.3085],[30.0388,31.3054],[30.0371,31.304],[30.0365,31.309],[30.0379,31.3085]]],[[[30.1085,31.359],[30.1068,31.3568],[30.1035,31.3557],[30.1049,31.3593],[30.1085,31.359]]]]}}";

// // // //             Dictionary<string, AdministrativeBoundary> adminBoundariesMap = new();
// // // //             if (forceReseed || !await context.AdministrativeBoundaries.AnyAsync())
// // // //             {
// // // //                 logger.LogInformation("Seeding AdministrativeBoundaries...");
// // // //                 var adminBoundaries = new List<AdministrativeBoundary>
// // // //                 {
// // // //                     new() { NameEn = "Cairo Governorate", NameAr = "محافظة القاهرة", AdminLevel = 1, CountryCode = "EG", OfficialCode="EG-CAI", IsActive = true, Centroid = CreatePoint(30.0444, 31.2357), Boundary = CreateGeometryFromGeoJsonString(geoJsonCairoGov_GeometryString, logger), SimplifiedBoundary = CreateSimplifiedGeometry(CreateGeometryFromGeoJsonString(geoJsonCairoGov_GeometryString, logger), logger, 0.01) },
// // // //                     new() { NameEn = "Giza Governorate", NameAr = "محافظة الجيزة", AdminLevel = 1, CountryCode = "EG", OfficialCode="EG-GIZ", IsActive = true, Centroid = CreatePoint(29.9870, 31.1313), Boundary = CreateGeometryFromGeoJsonString(geoJsonGizaGov_GeometryString, logger), SimplifiedBoundary = CreateSimplifiedGeometry(CreateGeometryFromGeoJsonString(geoJsonGizaGov_GeometryString, logger), logger, 0.01) },
// // // //                     new() { NameEn = "Alexandria Governorate", NameAr = "محافظة الإسكندرية", AdminLevel = 1, CountryCode = "EG", OfficialCode="EG-ALX", IsActive = true, Centroid = CreatePoint(31.2001, 29.9187), Boundary = CreateGeometryFromGeoJsonString(geoJsonAlexGov_GeometryString, logger), SimplifiedBoundary = CreateSimplifiedGeometry(CreateGeometryFromGeoJsonString(geoJsonAlexGov_GeometryString, logger), logger, 0.01) },
// // // //                 };
// // // //                 await context.AdministrativeBoundaries.AddRangeAsync(adminBoundaries);
// // // //                 await context.SaveChangesAsync();
// // // //                 adminBoundariesMap = adminBoundaries.ToDictionary(ab => ab.NameEn, ab => ab);
// // // //                 logger.LogInformation("Seeded {Count} AdministrativeBoundaries.", adminBoundaries.Count);
// // // //             }
// // // //             else
// // // //             {
// // // //                 adminBoundariesMap = await context.AdministrativeBoundaries.ToDictionaryAsync(ab => ab.NameEn, ab => ab);
// // // //             }

// // // //             // --- 2. SEED OPERATIONAL AREAS ---
// // // //             // !!! REPLACE THESE PLACEHOLDER GEOJSON STRINGS !!!
// // // //             string geoJson6thOctCustom_GeometryString = @"{""type"":""MultiPolygon"",""coordinates"":[[[[30.90,29.95],[30.95,29.95],[30.95,30.00],[30.90,30.00],[30.90,29.95]]]]}";
// // // //             string geoJsonNewCairoCustom_GeometryString = @"{""type"":""MultiPolygon"",""coordinates"":[[[[31.40,30.00],[31.50,30.00],[31.50,30.05],[31.40,30.05],[31.40,30.00]]]]}";

// // // //             Dictionary<string, OperationalArea> operationalAreasMap = new();
// // // //             if (forceReseed || !await context.OperationalAreas.AnyAsync())
// // // //             {
// // // //                 logger.LogInformation("Seeding OperationalAreas...");
// // // //                 var operationalAreasToSeed = new List<OperationalArea>();
// // // //                 AdministrativeBoundary? cairoGov = adminBoundariesMap.GetValueOrDefault("Cairo Governorate");
// // // //                 AdministrativeBoundary? gizaGov = adminBoundariesMap.GetValueOrDefault("Giza Governorate");
// // // //                 AdministrativeBoundary? alexGov = adminBoundariesMap.GetValueOrDefault("Alexandria Governorate");

// // // //                 if (cairoGov != null)
// // // //                 {
// // // //                     operationalAreasToSeed.Add(new OperationalArea { NameEn = "Cairo", NameAr = "القاهرة", Slug = "cairo", IsActive = true, CentroidLatitude = cairoGov.Centroid?.Y ?? 30.0444, CentroidLongitude = cairoGov.Centroid?.X ?? 31.2357, DefaultSearchRadiusMeters = 25000, GeometrySource = GeometrySourceType.DerivedFromAdmin, PrimaryAdministrativeBoundaryId = cairoGov.Id, DisplayLevel = "GovernorateCity" });
// // // //                     operationalAreasToSeed.Add(new OperationalArea { NameEn = "New Cairo", NameAr = "القاهرة الجديدة", Slug = "new-cairo", IsActive = true, CentroidLatitude = 30.0271, CentroidLongitude = 31.4961, DefaultSearchRadiusMeters = 15000, GeometrySource = GeometrySourceType.Custom, PrimaryAdministrativeBoundaryId = cairoGov.Id, CustomBoundary = CreateGeometryFromGeoJsonString(geoJsonNewCairoCustom_GeometryString, logger), CustomSimplifiedBoundary = CreateSimplifiedGeometry(CreateGeometryFromGeoJsonString(geoJsonNewCairoCustom_GeometryString, logger), logger, 0.005), DisplayLevel = "MajorDistrict" });
// // // //                 }
// // // //                 if (alexGov != null)
// // // //                 {
// // // //                     operationalAreasToSeed.Add(new OperationalArea { NameEn = "Alexandria", NameAr = "الإسكندرية", Slug = "alexandria", IsActive = true, CentroidLatitude = alexGov.Centroid?.Y ?? 31.2001, CentroidLongitude = alexGov.Centroid?.X ?? 29.9187, DefaultSearchRadiusMeters = 20000, GeometrySource = GeometrySourceType.DerivedFromAdmin, PrimaryAdministrativeBoundaryId = alexGov.Id, DisplayLevel = "GovernorateCity" });
// // // //                 }
// // // //                 if (gizaGov != null)
// // // //                 {
// // // //                     operationalAreasToSeed.Add(new OperationalArea { NameEn = "Giza", NameAr = "الجيزة", Slug = "giza", IsActive = true, CentroidLatitude = gizaGov.Centroid?.Y ?? 29.9870, CentroidLongitude = gizaGov.Centroid?.X ?? 31.1313, DefaultSearchRadiusMeters = 10000, GeometrySource = GeometrySourceType.DerivedFromAdmin, PrimaryAdministrativeBoundaryId = gizaGov.Id, DisplayLevel = "MajorCityArea" });
// // // //                     operationalAreasToSeed.Add(new OperationalArea { NameEn = "6th of October City", NameAr = "مدينة السادس من أكتوبر", Slug = "6th-october-city", IsActive = true, CentroidLatitude = 29.9660, CentroidLongitude = 30.9232, DefaultSearchRadiusMeters = 15000, GeometrySource = GeometrySourceType.Custom, PrimaryAdministrativeBoundaryId = gizaGov.Id, CustomBoundary = CreateGeometryFromGeoJsonString(geoJson6thOctCustom_GeometryString, logger), CustomSimplifiedBoundary = CreateSimplifiedGeometry(CreateGeometryFromGeoJsonString(geoJson6thOctCustom_GeometryString, logger), logger, 0.005), DisplayLevel = "MajorCity" });
// // // //                 }

// // // //                 await context.OperationalAreas.AddRangeAsync(operationalAreasToSeed);
// // // //                 await context.SaveChangesAsync();
// // // //                 operationalAreasMap = operationalAreasToSeed.ToDictionary(oa => oa.Slug, oa => oa);
// // // //                 logger.LogInformation("Seeded {Count} OperationalAreas.", operationalAreasToSeed.Count);
// // // //             }
// // // //             else
// // // //             {
// // // //                 operationalAreasMap = await context.OperationalAreas.ToDictionaryAsync(oa => oa.Slug, oa => oa);
// // // //             }

// // // //             List<GlobalServiceDefinition> globalServices = new();
// // // //             if (forceReseed || !await context.GlobalServiceDefinitions.AnyAsync())
// // // //             {
// // // //                 logger.LogInformation("Seeding GlobalServiceDefinitions...");
// // // //                 globalServices = new List<GlobalServiceDefinition> {
// // // //                     new() { ServiceCode = "OIL_CHANGE_STD", DefaultNameEn = "Standard Oil Change", DefaultNameAr = "تغيير زيت قياسي", DefaultDescriptionEn = "Includes engine oil and filter replacement (standard oil).", Category = ShopCategory.OilChange, DefaultEstimatedDurationMinutes = 30, IsGloballyActive = true },
// // // //                     new() { ServiceCode = "OIL_CHANGE_SYN", DefaultNameEn = "Synthetic Oil Change", DefaultNameAr = "تغيير زيت تخليقي", DefaultDescriptionEn = "Includes synthetic engine oil and filter replacement.", Category = ShopCategory.OilChange, DefaultEstimatedDurationMinutes = 45, IsGloballyActive = true },
// // // //                     new() { ServiceCode = "BRAKE_PAD_FRNT", DefaultNameEn = "Front Brake Pad Replacement", DefaultNameAr = "تغيير تيل الفرامل الأمامي", Category = ShopCategory.Brakes, DefaultEstimatedDurationMinutes = 60, IsGloballyActive = true },
// // // //                     new() { ServiceCode = "BRAKE_PAD_REAR", DefaultNameEn = "Rear Brake Pad Replacement", DefaultNameAr = "تغيير تيل الفرامل الخلفي", Category = ShopCategory.Brakes, DefaultEstimatedDurationMinutes = 60, IsGloballyActive = true },
// // // //                     new() { ServiceCode = "AC_REGAS", DefaultNameEn = "A/C Re-gas", DefaultNameAr = "إعادة شحن فريون التكييف", Category = ShopCategory.ACRepair, DefaultEstimatedDurationMinutes = 45, IsGloballyActive = true },
// // // //                     new() { ServiceCode = "CAR_WASH_EXT", DefaultNameEn = "Exterior Car Wash", DefaultNameAr = "غسيل خارجي للسيارة", Category = ShopCategory.CarWash, DefaultEstimatedDurationMinutes = 20, IsGloballyActive = true },
// // // //                     new() { ServiceCode = "TIRE_ROTATE", DefaultNameEn = "Tire Rotation", DefaultNameAr = "تدوير الإطارات", Category = ShopCategory.TireServices, DefaultEstimatedDurationMinutes = 30, IsGloballyActive = true },
// // // //                     new() { ServiceCode = "ENGINE_DIAG", DefaultNameEn = "Engine Diagnostics", DefaultNameAr = "تشخيص أعطال المحرك", Category = ShopCategory.Diagnostics, DefaultEstimatedDurationMinutes = 60, IsGloballyActive = true },
// // // //                     new() { ServiceCode = "GEN_MAINT_INSP", DefaultNameEn = "General Maintenance Inspection", DefaultNameAr = "فحص صيانة عام", Category = ShopCategory.GeneralMaintenance, DefaultEstimatedDurationMinutes = 90, IsGloballyActive = true }
// // // //                 };
// // // //                 if (globalServices.Any())
// // // //                 {
// // // //                     await context.GlobalServiceDefinitions.AddRangeAsync(globalServices);
// // // //                     await context.SaveChangesAsync();
// // // //                     logger.LogInformation("Seeded {Count} GlobalServiceDefinitions.", globalServices.Count);
// // // //                 }
// // // //                 else
// // // //                 {
// // // //                     logger.LogWarning("GlobalServiceDefinitions list was empty. Nothing seeded.");
// // // //                 }
// // // //             }
// // // //             else
// // // //             {
// // // //                 globalServices = await context.GlobalServiceDefinitions.ToListAsync();
// // // //                 logger.LogInformation("GlobalServiceDefinitions already seeded. Loaded {Count}.", globalServices.Count);
// // // //             }
// // // //             List<Shop> shopsToSaveInDb = new List<Shop>(); // CORRECTED: Renamed from shopsToSave
// // // //             List<Shop> seededShopsForServices;

// // // //             if (forceReseed || !await context.Shops.AnyAsync())
// // // //             {
// // // //                 logger.LogInformation("Preparing to seed shops and map them to OperationalAreas...");
// // // //                 var shopsFromDataSource = new List<(Shop ShopPlaceholder, string OriginalCityRefSlug)>
// // // //                 {
// // // //                     // --- START OF YOUR FULL SHOP LIST (79 SHOPS) ---
// // // //                     // (Copied from your provided list - ENSURE ALL ARE PRESENT AND MAPPED CORRECTLY TO OriginalCityRefSlug)
// // // //                     (new Shop { NameEn = "Bosch Car Service - Auto Mech", NameAr = "بوش كار سيرفيس - أوتو ميك", Address = "13 Industrial Zone, 6th October City", Location = CreatePoint(29.9523, 30.9176), PhoneNumber = "01000021565", ServicesOffered = "Engine Diagnostics, Oil Change, Brake Service", OpeningHours = "Sat-Thu 9am-7pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "El Saba Automotive Service Center", NameAr = "مركز خدمة السباعى للسيارات", Address = "El Mehwar Al Markazi, 6th of October City", Location = CreatePoint(30.070042549123325, 31.35249452751594), PhoneNumber = "19112", ServicesOffered = "Maintenance, Diagnostics, Periodic Services", OpeningHours = "Sun-Fri 9am-6pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Auto Pro Center", NameAr = "مركز أوتو برو", Address = "Extension of 26th July Corridor, 6th October City", Location = CreatePoint(29.9701, 30.9054), PhoneNumber = "01099455663", ServicesOffered = "Tire Services, Oil Change, Engine Tune-up", OpeningHours = "Sat-Thu 10am-8pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Ghabbour Auto Service", NameAr = "غبور أوتو", Address = "Plot 2, Area 7, 6th October Industrial Zone", Location = CreatePoint(29.9609, 30.9134), PhoneNumber = "16661", ServicesOffered = "Hyundai Service, AC Repair, Electrical Work", OpeningHours = "Sat-Thu 8am-6pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Speed Car Service Center", NameAr = "سبيد كار مركز خدمة", Address = "Waslet Dahshur, 6th October City", Location = CreatePoint(29.9768, 30.9021), PhoneNumber = "01234567890", ServicesOffered = "Brake Service, Oil Change, Tune-Up", OpeningHours = "Sat-Fri 9am-8pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Auto Egypt", NameAr = "أوتو إيجيبت", Address = "Plot 10, 2nd Industrial Zone, 6th October City", Location = CreatePoint(29.9575, 30.9090), PhoneNumber = "01000276423", ServicesOffered = "Repair, Servicing Equipment, AC Repair", OpeningHours = "Sat-Thu 9am-6pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Tariq Auto Repair Center", NameAr = "مركز طارق لتصليح السيارات", Address = "Plot 54, Rd. 6, 2nd Service Spine, 1st Industrial Zone", Location = CreatePoint(29.9615, 30.9159), PhoneNumber = "01006865777", ServicesOffered = "General Repairs, Servicing Equipment", OpeningHours = "Sat-Thu 9am-6pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Hi Tech Commercial Vehicle Service", NameAr = "هاى تك لصيانة المركبات التجارية", Address = "Plot 73, Extension Industrial Zone III, 6th October City", Location = CreatePoint(29.9590, 30.9170), PhoneNumber = "01100900742", ServicesOffered = "Commercial Vehicles Maintenance, AC, Painting", OpeningHours = "Sat-Thu 8:30am-5:30pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Technical Workshop 11th District", NameAr = "ورشة فنية الحي 11", Address = "11th District, 6th October City", Location = CreatePoint(29.9640, 30.9200), PhoneNumber = "01004057239", ServicesOffered = "General Repairs, Electrical, AC", OpeningHours = "Sat-Thu 9am-6pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Car Repair Service Center October", NameAr = "مركز خدمة تصليح سيارات أكتوبر", Address = "6th of October City", Location = CreatePoint(29.9680, 30.9250), PhoneNumber = "01198765432", ServicesOffered = "Mechanical, Electrical, Brakes, Paint, AC", OpeningHours = "Sat-Thu 9am-7pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Repair Service Center October 2", NameAr = "مركز إصلاح السيارات أكتوبر 2", Address = "6th of October City", Location = CreatePoint(29.9650, 30.9220), PhoneNumber = "01011112222", ServicesOffered = "Automotive Service", OpeningHours = "Sat-Thu 9am-6pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Auto Service Nissan October", NameAr = "خدمة نيسان للسيارات أكتوبر", Address = "6th of October City", Location = CreatePoint(29.9670, 30.9260), PhoneNumber = "01022223333", ServicesOffered = "Mechanical, Electricity, Suspension, Brakes, Lubricants", OpeningHours = "Mon-Sat 10am-6pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Kiro Car Service Center", NameAr = "مركز كيرو لصيانة السيارات", Address = "Al‑Hayy 11, Gamal Abdel Nasser St, 6th October City", Location = CreatePoint(29.9580, 30.9100), PhoneNumber = "01222728260", ServicesOffered = "Engine Diagnostics (GT1/ISIS/ICOM), Electrical, AC, Mechanical, Transmission", OpeningHours = "Sat‑Thu 10am‑11pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Sand Group Auto Service", NameAr = "مركز ساند جروب لصيانة السيارات", Address = "Piece 23, 2nd Industrial Zone Service Spine, 6th October City", Location = CreatePoint(29.9550, 30.9050), PhoneNumber = "01098693222", ServicesOffered = "Mechanics, Electrical, AC, Body & Paint, Diesel engine, Genuine parts", OpeningHours = "Sat‑Thu 9am‑6pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Rally Motors", NameAr = "رالى موتورز", Address = "6th October City", Location = CreatePoint(29.91841448970566, 30.911635457698893), PhoneNumber = "", ServicesOffered = "Mechanics, Electrical, Suspension, Bodywork, Brakes, Oil", OpeningHours = "Mon‑Sat 9am‑5pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Jarrag Auto", NameAr = "جراج اوتو", Address = "6th October City", Location = CreatePoint(29.9620, 30.9150), PhoneNumber = "", ServicesOffered = "Mechanics, Electrical, Suspension, Bodywork, Brakes, Oil", OpeningHours = "Mon‑Sat 1pm‑11pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Commercial Motors AC Service", NameAr = "كومرشال موتورز لصيانة التكييف", Address = "1st Industrial Zone, 6th October City", Location = CreatePoint(29.9520, 30.9000), PhoneNumber = "0238200279", ServicesOffered = "Car AC recharge & repair", OpeningHours = "Sat‑Thu 9am‑6pm", Category = ShopCategory.ACRepair }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Magic Auto Service", NameAr = "ماجيك أوتو سيرفيس", Address = "Piece 117, 6th of October Corridor service road, 3rd Industrial Zone", Location = CreatePoint(29.9650, 30.9070), PhoneNumber = "01004149834", ServicesOffered = "AC, Mechanics, Electrical", OpeningHours = "Sat‑Thu 9am‑6pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Al-Khitab Service Center October", NameAr = "مركز الخطاب أكتوبر", Address = "Qaryat Al‑Mukhtar, 6th of October City", Location = CreatePoint(29.9700, 30.9200), PhoneNumber = "01000510078", ServicesOffered = "Electrical services", OpeningHours = "Sat‑Thu 9am‑6pm", Category = ShopCategory.Diagnostics }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Sahma Car Service", NameAr = "سمحا لصيانة السيارات", Address = "Kafraway Corridor, 6th of October City", Location = CreatePoint(29.9685, 30.9225), PhoneNumber = "01006994907", ServicesOffered = "General Mechanics, Suspension, Electrical", OpeningHours = "Sat‑Thu 9am‑6pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Al‑Alamiah Auto Repair October", NameAr = "العالمية لاصلاح السيارات أكتوبر", Address = "Garden of Firouze Center, Hayy 3, 6th October City", Location = CreatePoint(29.9635, 30.9180), PhoneNumber = "", ServicesOffered = "Mechanics & diagnostics", OpeningHours = "Sat‑Thu 9am‑6pm", Category = ShopCategory.Diagnostics }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Star Tools Auto", NameAr = "ستار تولز لصيانة السيارات", Address = "Villa 164, Sector 5, 6th October City", Location = CreatePoint(29.9570, 30.9120), PhoneNumber = "", ServicesOffered = "Spare parts, Mechanics", OpeningHours = "Sat‑Thu 9am‑6pm", Category = ShopCategory.NewAutoParts }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Technical Workshop For Cars Maintenance (3rd District)", NameAr = "ورشة فنية لصيانة السيارات – الحي 3", Address = "5th Neighbourhood, 3rd District, 6th Oct City", Location = CreatePoint(29.9620, 30.9180), PhoneNumber = "01099485311", ServicesOffered = "General Repairs, Servicing Equipment", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Technical Workshop For Cars Maintenance (11th District B)", NameAr = "ورشة فنية لصيانة السيارات – الحي 11 ب", Address = "11th District, 6th Oct City", Location = CreatePoint(29.9645, 30.9205), PhoneNumber = "01222976007", ServicesOffered = "General Repairs, Servicing Equipment", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Engineering Workshop (October Grand Mall)", NameAr = "الورشة الهندسية – أكتوبر جراند مول", Address = "3rd District, next to October Grand Mall, 6th Oct City", Location = CreatePoint(29.9618, 30.9177), PhoneNumber = "01001511267", ServicesOffered = "Mechanical, Electrical, Servicing Equipment", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "El Sabbah Auto Service", NameAr = "مركز الصباح لخدمة السيارات", Address = "Ajyad View Mall, 4th District, 6th Oct City", Location = CreatePoint(29.9585, 30.9150), PhoneNumber = "01099004561", ServicesOffered = "Electrical, Mechanical, Body & Paint, AC, Radiators", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Auto Repair (Hayy 7)", NameAr = "أوتو ريبير – الحي 7", Address = "Hayy 7, next to Kilawee Market & Sudanese Negotiation Bldg", Location = CreatePoint(29.9580, 30.9105), PhoneNumber = "01060980088", ServicesOffered = "Nissan, Renault, Korean & Japanese cars: Mechanics, Electrical, Chassis, AC, Paint & Body, Computer Diagnostics", OpeningHours = "Sat–Thu 11am–10pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Top Service October", NameAr = "توب سيرفيس أكتوبر", Address = "University Library St (parallel to Central Axis), Omrania, 6th Oct City", Location = CreatePoint(29.9600, 30.9200), PhoneNumber = "01226005753", ServicesOffered = "VW, Audi, Skoda, Seat: Mechanics, Electrical, Chassis, AC, Diagnostics, Body & Paint", OpeningHours = "Mon–Wed, Sat–Sun 11am–8pm; Thu 11am–4pm; Fri closed", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Auto Group Maintenance (Autel Outlet)", NameAr = "أوتو جروب لصيانة السيارات", Address = "38–39 First Service Corridor, 1st Industrial Zone, next to Outlet Mall", Location = CreatePoint(29.9535, 30.9040), PhoneNumber = "01029666622", ServicesOffered = "Opel, Chevrolet, MG: Inspection, Servicing, Body & Paint, Genuine Parts", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Auto Option", NameAr = "كار أوبشن", Address = "Al-Mogawara 5, Hayy 12, 6th Oct City", Location = CreatePoint(29.9570, 30.9230), PhoneNumber = "01229365458", ServicesOffered = "General Car Service", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Car Tech Workshop", NameAr = "كار تك", Address = "Hayy 11, 6th Oct City", Location = CreatePoint(29.9630, 30.9240), PhoneNumber = "01222548796", ServicesOffered = "General Car Service", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Car Spa October", NameAr = "كار سبا أكتوبر", Address = "Hayy Al-Mutamayez, 6th Oct City", Location = CreatePoint(29.9605, 30.9190), PhoneNumber = "01092042934", ServicesOffered = "General Car Service", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.CarWash }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Car Carry", NameAr = "كار كارى", Address = "Hayy 11, 6th Oct City", Location = CreatePoint(29.9635, 30.9195), PhoneNumber = "01117245884", ServicesOffered = "General Car Service", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Car Clinic October", NameAr = "كار كلينيك أكتوبر", Address = "Hayy 10, 6th Oct City", Location = CreatePoint(29.9628, 30.9188), PhoneNumber = "01120430308", ServicesOffered = "General Car Service", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.Diagnostics }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Crash Workshop", NameAr = "كراش ورشة", Address = "Hayy 7, 6th Oct City", Location = CreatePoint(29.9582, 30.9123), PhoneNumber = "01003010870", ServicesOffered = "General Car Service", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.BodyRepairAndPaint }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Clean Car October", NameAr = "كلين كار أكتوبر", Address = "6th October City", Location = CreatePoint(29.9600, 30.9200), PhoneNumber = "01001031717", ServicesOffered = "General Car Service", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.CarWash }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Mohamed Shensh Auto Electric", NameAr = "محمد شنش لكهرباء السيارات", Address = "6 October St (October Tower), 6th Oct City", Location = CreatePoint(29.9610, 30.9210), PhoneNumber = "01204955650", ServicesOffered = "Car Electrical Repair", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.Diagnostics }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Mohamed Farhat Auto Electric October", NameAr = "محمد فرحات لكهرباء السيارات أكتوبر", Address = "Industrial Area, 6th Oct City", Location = CreatePoint(29.9615, 30.9182), PhoneNumber = "01117726645", ServicesOffered = "Car Electrical Repair", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.Diagnostics }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Mahmoud Auto Electric", NameAr = "محمود لكهرباء السيارات", Address = "Hayy 6, 6th Oct City", Location = CreatePoint(29.9618, 30.9185), PhoneNumber = "01001936448", ServicesOffered = "Car Electrical Repair", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.Diagnostics }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Al Khitab Electrical Center", NameAr = "مركز الخطاب للكهرباء", Address = "Qaryat Al-Mukhtar, Area B, 6th Oct City", Location = CreatePoint(29.9702, 30.9203), PhoneNumber = "01000510079", ServicesOffered = "Electrical Services & Diagnostics", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.Diagnostics }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Al Ekhlaas Service Center", NameAr = "مركز الإخلاص", Address = "Al-Kefrawy Axis, Hayy 1, 6th Oct City", Location = CreatePoint(29.9600, 30.9150), PhoneNumber = "01006994907", ServicesOffered = "General Car Service", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Al Ameen Service Center", NameAr = "مركز الأمين", Address = "Al-Kefrawy Axis, Hayy 3, 6th Oct City", Location = CreatePoint(29.9620, 30.9160), PhoneNumber = "01119637015", ServicesOffered = "General Car Service", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Al Rahma Center", NameAr = "مركز الرحمة", Address = "Central Axis, Ne’ma Complex Bldg, Hayy 8, 6th Oct City", Location = CreatePoint(29.9630, 30.9200), PhoneNumber = "01112847808", ServicesOffered = "General Car Service", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Al Sultan Center", NameAr = "مركز السلطان", Address = "Hayy 6, 6th Oct City", Location = CreatePoint(29.9622, 30.9188), PhoneNumber = "01126262828", ServicesOffered = "General Car Service", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Al Samah Center", NameAr = "مركز السماح", Address = "Hayy 4, 6th Oct City", Location = CreatePoint(29.9590, 30.9170), PhoneNumber = "01003952427", ServicesOffered = "General Car Service", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Al Shurooq Center October", NameAr = "مركز الشروق أكتوبر", Address = "Makka Al‑Mukarramah St, Hayy 7, inside Al‑Ordonia Mall, 6th Oct City", Location = CreatePoint(29.9630, 30.9185), PhoneNumber = "01275459661", ServicesOffered = "General Car Service", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Al Worsha Al Handasiya October", NameAr = "الورشة الهندسية أكتوبر", Address = "Makka Al‑Mukarramah St, Hayy 7, inside Al‑Ordonia Mall, 6th Oct City", Location = CreatePoint(29.9632, 30.9187), PhoneNumber = "01126374432", ServicesOffered = "General Car Service", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Proton Point Service Center", NameAr = "بروتون بوينت", Address = "Al‑Kefrawy Axis, Hayy 2, inside New Jordanian Mall, 6th Oct City", Location = CreatePoint(29.9610, 30.9165), PhoneNumber = "01009627238", ServicesOffered = "General Car Service", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Al Marwa Repair Workshop", NameAr = "ورشة المروة لإصلاح السيارات", Address = "Geel 2000 St, Hayy 11, inside Nakheel 2 Center, 6th Oct City", Location = CreatePoint(29.9655, 30.9208), PhoneNumber = "01225214298", ServicesOffered = "General Car Service", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Ezz El-Arab Auto Group October", NameAr = "مجموعة عز العرب للسيارات فرع أكتوبر", Address = "Waslet Dahshur St, West Yard, (Not Sheikh Zayed for this entry, keeping in October)", Location = CreatePoint(29.9760, 30.9020), PhoneNumber = "01032220855", ServicesOffered = "Manufacture & Repair, Body & Paint, Spare Parts", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Engineering Workshop For Cars Maintenance Grand Mall", NameAr = "الورشة الهندسية لصيانة السيارات جراند مول", Address = "3rd District (inside October Grand Mall), 6th October City", Location = CreatePoint(29.9619, 30.9178), PhoneNumber = "01001511268", ServicesOffered = "Mechanical, Electrical, Servicing Equipment", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Technical Workshop For Cars Maintenance (3rd District B)", NameAr = "الورشة الفنية لصيانة السيارات – الحي 3 ب", Address = "5th Neighbourhood, 3rd District, Near Square, 6th October City", Location = CreatePoint(29.9621, 30.9181), PhoneNumber = "01099485312", ServicesOffered = "General Repairs, Servicing Equipment", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Technical Workshop For Cars Maintenance (11th District C)", NameAr = "الورشة الفنية لصيانة السيارات – الحي 11 ج", Address = "11th District, Next to El Hedaya 2 Center, 6th October City", Location = CreatePoint(29.9647, 30.9207), PhoneNumber = "01222976008", ServicesOffered = "General Repairs, Servicing Equipment", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Global Center For Maintenance Cars", NameAr = "المركز العالمي لصيانة السيارات", Address = "7th District, beside El Radwa Language Schools", Location = CreatePoint(29.9700, 30.9250), PhoneNumber = "01223657778", ServicesOffered = "General Car Servicing, Mechanical, Electrical", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Auto Repair - Hayy 7 Annex", NameAr = "أوتو ريبير – الحي 7 ملحق", Address = "Hayy 7, Behind Klaway Market, 6th October City", Location = CreatePoint(29.9583, 30.9124), PhoneNumber = "01110620023", ServicesOffered = "Mechanics, Electrical, Suspension, AC, Painting, Bodywork, Diagnostics", OpeningHours = "Sat–Thu 11am–11pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Ahmed Agha Workshop", NameAr = "ورشة أحمد آغا", Address = "Iskan El-Shabab 100m, 11th District, inside Center Al‑Wijih", Location = CreatePoint(29.9640, 30.9200), PhoneNumber = "01001408720", ServicesOffered = "General Car Repair & Equipment Service", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Mekka Workshop October", NameAr = "ورشة مكة أكتوبر", Address = "11th District, inside Center Al‑Halfawy, 6th October City", Location = CreatePoint(29.9642, 30.9202), PhoneNumber = "01003318094", ServicesOffered = "General Car Repair & Equipment Service", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Technical Workshop – Center El Hedaya 2 Main", NameAr = "الورشة الفنية لصيانة السيارات – مركز الهداية 2 الرئيسي", Address = "11th District, inside Center El Hedaya 2", Location = CreatePoint(29.9648, 30.9208), PhoneNumber = "01224145999", ServicesOffered = "General Car Repair & Equipment Service", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Alameya Cars Services October", NameAr = "العالمية لخدمات السيارات فرع أكتوبر", Address = "6th District, inside Center Wadi Al‑Malika, 6th October City", Location = CreatePoint(29.9595, 30.9175), PhoneNumber = "01282218444", ServicesOffered = "General Car Repair & Equipment Service", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Engineering Center For Mufflers", NameAr = "المركز الهندسي لخدمة الشكمانات", Address = "Piece 145, Street 6, 3rd Industrial Zone, 6th October City", Location = CreatePoint(29.9550, 30.9050), PhoneNumber = "02338341377", ServicesOffered = "Muffler & Exhaust System Service", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Abnaa Al-Daqahliya Mechanics", NameAr = "أبناء الدقهلية لميكانيكا السيارات", Address = "Inside Center Wadi Al‑Moluk, 6th District, 6th October City", Location = CreatePoint(29.9590, 30.9170), PhoneNumber = "01229452233", ServicesOffered = "Mechanical Repair & Servicing", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Wahdan Auto Group", NameAr = "وهدان أوتو جروب", Address = "Street 47 off Street 14, 3rd Industrial Zone, 6th October City", Location = CreatePoint(29.9555, 30.9045), PhoneNumber = "02338342412", ServicesOffered = "General Car Service & Spare Parts", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.NewAutoParts }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Abu Ziyad Al‑Sarougy", NameAr = "أبو زياد السروجي", Address = "Piece 80, Project 103, 6th District, 6th October City", Location = CreatePoint(29.9592, 30.9172), PhoneNumber = "01117946177", ServicesOffered = "General Car Repair & Servicing", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Ahmad Al-Afreet Auto Electric", NameAr = "أحمد العفريت لكهرباء السيارات", Address = "Inside Center Wadi Al‑Moluk, 6th District, 6th October City", Location = CreatePoint(29.9588, 30.9168), PhoneNumber = "01026537353", ServicesOffered = "Car Electrical Repairs", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.Diagnostics }, "6th-october-city-ref"),
// // // //                     (new Shop { NameEn = "Group United Toyota Service Center", NameAr = "المجموعة المتحدة - مركز خدمة تويوتا", Address = "Piece 339, Extension 3rd Industrial Zone, 6th October City", Location = CreatePoint(29.9545, 30.9049), PhoneNumber = "01008552358", ServicesOffered = "Toyota Authorized Maintenance & Repairs", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance }, "6th-october-city-ref"),
// // // //                     // --- Cairo Shops ---
// // // //                     (new Shop { NameEn = "Quick Oil Change Center Cairo", NameAr = "مركز تغيير الزيت السريع القاهرة", Category = ShopCategory.OilChange, Address = "Nasr City, Cairo", Location = CreatePoint(30.0590, 31.3280), PhoneNumber="01012345679"}, "cairo-ref"),
// // // //                     (new Shop { NameEn = "Parts Central Cairo", NameAr = "بارتس سنترال القاهرة", Category = ShopCategory.NewAutoParts, Address = "Abbas El Akkad, Cairo", Location = CreatePoint(30.0620, 31.3320), ServicesOffered="OEM Parts, Aftermarket Parts"}, "cairo-ref"),
// // // //                     (new Shop { NameEn = "Downtown Car Wash", NameAr = "غسيل سيارات وسط البلد", Category = ShopCategory.CarWash, Address = "Tahrir Square Area, Cairo", Location = CreatePoint(30.0440, 31.2350)}, "cairo-ref"),
// // // //                     // --- Alexandria Shops ---
// // // //                     (new Shop { NameEn = "Alexandria Performance Parts", NameAr = "قطع غيار الأداء بالإسكندرية", Category = ShopCategory.PerformanceParts, Address = "Sporting, Alexandria", Location = CreatePoint(31.2170, 29.9400), ServicesOffered="Exhausts,Intakes,Suspension"}, "alexandria-ref"),
// // // //                     (new Shop { NameEn = "Alexandria Diagnostics Pro", NameAr = "تشخيص محترف الإسكندرية", Category = ShopCategory.Diagnostics, Address = "Roushdy, Alexandria", Location = CreatePoint(31.2280, 29.9600)}, "alexandria-ref"),
// // // //                     // --- Giza Shops ---
// // // //                     (new Shop { NameEn = "Giza Car Accessories World", NameAr = "عالم إكسسوارات السيارات بالجيزة", Category = ShopCategory.CarAccessories, Address = "Mohandessin, Giza", Location = CreatePoint(30.0580, 31.2080), PhoneNumber="01119876543"}, "giza-ref"),
// // // //                     (new Shop { NameEn = "Giza EV Charging Station", NameAr = "محطة شحن السيارات الكهربائية بالجيزة", Category = ShopCategory.EVCharging, Address = "Dokki, Giza", Location = CreatePoint(30.0450, 31.2100)}, "giza-ref"),
// // // //                     // --- New Cairo Shops ---
// // // //                     (new Shop { NameEn = "New Cairo Premium Tire Service", NameAr = "خدمة إطارات مميزة بالقاهرة الجديدة", Category = ShopCategory.TireServices, Address = "90th Street, New Cairo", Location = CreatePoint(30.0250, 31.4900)}, "new-cairo-ref"),
// // // //                     (new Shop { NameEn = "New Cairo Body & Paint Masters", NameAr = "ماسترز الدهان والسمكرة بالقاهرة الجديدة", Category = ShopCategory.BodyRepairAndPaint, Address = "1st Settlement, New Cairo", Location = CreatePoint(30.0070, 31.4300)}, "new-cairo-ref"),
// // // //                     // --- END OF YOUR FULL SHOP LIST ---
// // // //                 };

// // // //                 foreach (var (shopPlaceholder, originalCityRefSlug) in shopsFromDataSource)
// // // //                 {
// // // //                     OperationalArea? targetOA = null;
// // // //                     if (originalCityRefSlug == "6th-october-city-ref")
// // // //                     {
// // // //                         targetOA = operationalAreasMap.GetValueOrDefault("6th-october-city");
// // // //                     }
// // // //                     else if (originalCityRefSlug == "new-cairo-ref")
// // // //                     {
// // // //                         targetOA = operationalAreasMap.GetValueOrDefault("new-cairo");
// // // //                     }
// // // //                     else if (originalCityRefSlug == "giza-ref")
// // // //                     {
// // // //                         if (shopPlaceholder.Address.Contains("6th of October", StringComparison.OrdinalIgnoreCase) ||
// // // //                             shopPlaceholder.Address.Contains("October City", StringComparison.OrdinalIgnoreCase) ||
// // // //                             shopPlaceholder.Address.Contains("السادس من أكتوبر", StringComparison.OrdinalIgnoreCase)
// // // //                             )
// // // //                         {
// // // //                             targetOA = operationalAreasMap.GetValueOrDefault("6th-october-city");
// // // //                         }
// // // //                         else
// // // //                         {
// // // //                             targetOA = operationalAreasMap.GetValueOrDefault("giza");
// // // //                         }
// // // //                     }
// // // //                     else if (originalCityRefSlug == "cairo-ref")
// // // //                     {
// // // //                         if (shopPlaceholder.Address.Contains("New Cairo", StringComparison.OrdinalIgnoreCase) ||
// // // //                             shopPlaceholder.Address.Contains("القاهرة الجديدة", StringComparison.OrdinalIgnoreCase) ||
// // // //                             shopPlaceholder.Address.Contains("1st Settlement", StringComparison.OrdinalIgnoreCase) ||
// // // //                             shopPlaceholder.Address.Contains("التجمع الاول", StringComparison.OrdinalIgnoreCase) ||
// // // //                             shopPlaceholder.Address.Contains("5th Settlement", StringComparison.OrdinalIgnoreCase) ||
// // // //                             shopPlaceholder.Address.Contains("التجمع الخامس", StringComparison.OrdinalIgnoreCase)
// // // //                             )
// // // //                         {
// // // //                             targetOA = operationalAreasMap.GetValueOrDefault("new-cairo");
// // // //                         }
// // // //                         else
// // // //                         {
// // // //                             targetOA = operationalAreasMap.GetValueOrDefault("cairo");
// // // //                         }
// // // //                     }
// // // //                     else if (operationalAreasMap.TryGetValue(originalCityRefSlug.Replace("-ref", ""), out var directOAFromMap))
// // // //                     {
// // // //                         targetOA = directOAFromMap;
// // // //                     }

// // // //                     if (targetOA != null)
// // // //                     {
// // // //                         shopPlaceholder.OperationalAreaId = targetOA.Id;
// // // //                         string baseSlugPart = GenerateSlugFromName(shopPlaceholder.NameEn, true);
// // // //                         string areaSlugPart = targetOA.Slug;
// // // //                         string initialSlug = $"{baseSlugPart}-in-{areaSlugPart}";

// // // //                         if (!string.IsNullOrWhiteSpace(shopPlaceholder.Slug))
// // // //                         {
// // // //                             initialSlug = GenerateSlugFromName(shopPlaceholder.Slug, true);
// // // //                         }
// // // //                         else
// // // //                         {
// // // //                             shopPlaceholder.Slug = initialSlug;
// // // //                         }

// // // //                         string finalSlug = initialSlug;
// // // //                         int counter = 1;
// // // //                         // CORRECTED: Use shopsToSaveInDb here
// // // //                         while (shopsToSaveInDb.Any(s => s.OperationalAreaId == targetOA.Id && s.Slug == finalSlug) ||
// // // //                                await context.Shops.AnyAsync(s => s.OperationalAreaId == targetOA.Id && s.Slug == finalSlug && s.Id != shopPlaceholder.Id))
// // // //                         {
// // // //                             finalSlug = $"{initialSlug}-{counter++}";
// // // //                         }
// // // //                         shopPlaceholder.Slug = finalSlug;
// // // //                         shopPlaceholder.LogoUrl = GenerateLogoUrlFromName(finalSlug);
// // // //                         shopsToSaveInDb.Add(shopPlaceholder); // CORRECTED: Add to shopsToSaveInDb
// // // //                     }
// // // //                     else
// // // //                     {
// // // //                         logger.LogError($"Could not map shop '{shopPlaceholder.NameEn}' (Original City Ref Slug: {originalCityRefSlug}) to an OperationalArea. Skipping shop.");
// // // //                     }
// // // //                 }

// // // //                 if (shopsToSaveInDb.Any())
// // // //                 { // CORRECTED: Check shopsToSaveInDb
// // // //                     await context.Shops.AddRangeAsync(shopsToSaveInDb); // CORRECTED: Add from shopsToSaveInDb
// // // //                     await context.SaveChangesAsync();
// // // //                     logger.LogInformation("Successfully seeded {ShopCountActual} shops with OperationalAreaId mapping.", shopsToSaveInDb.Count); // CORRECTED
// // // //                 }
// // // //                 seededShopsForServices = await context.Shops.Where(s => !s.IsDeleted).ToListAsync();
// // // //             }
// // // //             else
// // // //             {
// // // //                 logger.LogInformation("Shops table already has data and forceReseed is false. Skipping shop seed.");
// // // //                 seededShopsForServices = await context.Shops.Where(s => !s.IsDeleted).ToListAsync();
// // // //             }

// // // //             // --- Seed ShopServices ---
// // // //             // CORRECTED: Use seededShopsForServices
// // // //             if ((forceReseed || !await context.ShopServices.AnyAsync()) && seededShopsForServices.Any() && globalServices.Any())
// // // //             {
// // // //                 logger.LogInformation("Seeding ShopServices...");
// // // //                 var shopServicesToSeed = new List<ShopService>();
// // // //                 var random = new Random();

// // // //                 var oilChangeStd = globalServices.FirstOrDefault(g => g.ServiceCode == "OIL_CHANGE_STD");
// // // //                 var oilChangeSyn = globalServices.FirstOrDefault(g => g.ServiceCode == "OIL_CHANGE_SYN");
// // // //                 var brakeFront = globalServices.FirstOrDefault(g => g.ServiceCode == "BRAKE_PAD_FRNT");
// // // //                 var acRegas = globalServices.FirstOrDefault(g => g.ServiceCode == "AC_REGAS");
// // // //                 var carWashExt = globalServices.FirstOrDefault(g => g.ServiceCode == "CAR_WASH_EXT");
// // // //                 var tireRotate = globalServices.FirstOrDefault(g => g.ServiceCode == "TIRE_ROTATE");
// // // //                 var engineDiag = globalServices.FirstOrDefault(g => g.ServiceCode == "ENGINE_DIAG");
// // // //                 var genMaintInsp = globalServices.FirstOrDefault(g => g.ServiceCode == "GEN_MAINT_INSP");

// // // //                 ShopService CreateShopServiceEntry(Guid currentShopId, GlobalServiceDefinition? globalDef, decimal price, string? customNameEn = null, string? customNameAr = null, int? duration = null)
// // // //                 {
// // // //                     if (globalDef == null)
// // // //                     {
// // // //                         logger.LogError($"Attempted to create ShopService for shop {currentShopId} but GlobalServiceDefinition was null.");
// // // //                         throw new InvalidOperationException("GlobalServiceDefinition is null in CreateShopServiceEntry.");
// // // //                     }
// // // //                     return new ShopService
// // // //                     {
// // // //                         ShopId = currentShopId,
// // // //                         GlobalServiceId = globalDef.GlobalServiceId,
// // // //                         CustomServiceNameEn = customNameEn,
// // // //                         CustomServiceNameAr = customNameAr,
// // // //                         EffectiveNameEn = !string.IsNullOrEmpty(customNameEn) ? customNameEn : globalDef.DefaultNameEn,
// // // //                         EffectiveNameAr = !string.IsNullOrEmpty(customNameAr) ? customNameAr : globalDef.DefaultNameAr,
// // // //                         Price = price,
// // // //                         DurationMinutes = duration ?? globalDef.DefaultEstimatedDurationMinutes,
// // // //                         IsOfferedByShop = true,
// // // //                         SortOrder = random.Next(1, 100)
// // // //                     };
// // // //                 }

// // // //                 foreach (var shop in seededShopsForServices)
// // // //                 {
// // // //                     if ((shop.Category == ShopCategory.GeneralMaintenance || shop.Category == ShopCategory.OilChange) && oilChangeStd != null && oilChangeSyn != null)
// // // //                     {
// // // //                         shopServicesToSeed.Add(CreateShopServiceEntry(shop.Id, oilChangeStd, Math.Round((decimal)(random.NextDouble() * 100 + 250), 2)));
// // // //                         shopServicesToSeed.Add(CreateShopServiceEntry(shop.Id, oilChangeSyn, Math.Round((decimal)(random.NextDouble() * 150 + 450), 2), duration: 50));
// // // //                     }
// // // //                     if ((shop.Category == ShopCategory.GeneralMaintenance || shop.Category == ShopCategory.Brakes) && brakeFront != null)
// // // //                     {
// // // //                         shopServicesToSeed.Add(CreateShopServiceEntry(shop.Id, brakeFront, Math.Round((decimal)(random.NextDouble() * 200 + 600), 2)));
// // // //                     }
// // // //                     if ((shop.Category == ShopCategory.GeneralMaintenance || shop.Category == ShopCategory.ACRepair) && acRegas != null)
// // // //                     {
// // // //                         shopServicesToSeed.Add(CreateShopServiceEntry(shop.Id, acRegas, Math.Round((decimal)(random.NextDouble() * 100 + 300), 2)));
// // // //                     }
// // // //                     if (shop.Category == ShopCategory.CarWash && carWashExt != null)
// // // //                     {
// // // //                         shopServicesToSeed.Add(CreateShopServiceEntry(shop.Id, carWashExt, Math.Round((decimal)(random.NextDouble() * 50 + 100), 2), duration: 25));
// // // //                     }
// // // //                     if (shop.Category == ShopCategory.TireServices && tireRotate != null)
// // // //                     {
// // // //                         shopServicesToSeed.Add(CreateShopServiceEntry(shop.Id, tireRotate, Math.Round((decimal)(random.NextDouble() * 80 + 150), 2)));
// // // //                     }
// // // //                     if ((shop.Category == ShopCategory.Diagnostics || shop.Category == ShopCategory.GeneralMaintenance) && engineDiag != null)
// // // //                     {
// // // //                         shopServicesToSeed.Add(CreateShopServiceEntry(shop.Id, engineDiag, Math.Round((decimal)(random.NextDouble() * 150 + 200), 2)));
// // // //                     }
// // // //                     if (shop.Category == ShopCategory.GeneralMaintenance && genMaintInsp != null)
// // // //                     {
// // // //                         shopServicesToSeed.Add(CreateShopServiceEntry(shop.Id, genMaintInsp, Math.Round((decimal)(random.NextDouble() * 200 + 300), 2), duration: 100));
// // // //                     }

// // // //                     if (shop.NameEn.Contains("Bosch"))
// // // //                     {
// // // //                         shopServicesToSeed.Add(new ShopService
// // // //                         {
// // // //                             ShopId = shop.Id,
// // // //                             GlobalServiceId = null,
// // // //                             CustomServiceNameEn = "Bosch Premium Diagnostic Package",
// // // //                             EffectiveNameEn = "Bosch Premium Diagnostic Package",
// // // //                             CustomServiceNameAr = "باقة بوش التشخيصية الممتازة",
// // // //                             EffectiveNameAr = "باقة بوش التشخيصية الممتازة",
// // // //                             ShopSpecificDescriptionEn = "Full vehicle computer scan with Bosch certified equipment and detailed report.",
// // // //                             Price = 750.00m,
// // // //                             DurationMinutes = 120,
// // // //                             IsOfferedByShop = true,
// // // //                             SortOrder = 5,
// // // //                             IsPopularAtShop = true
// // // //                         });
// // // //                     }
// // // //                 }
// // // //                 if (shopServicesToSeed.Any())
// // // //                 {
// // // //                     await context.ShopServices.AddRangeAsync(shopServicesToSeed);
// // // //                     await context.SaveChangesAsync();
// // // //                     logger.LogInformation("Successfully seeded {ShopServiceCount} shop services.", shopServicesToSeed.Count);
// // // //                 }
// // // //                 else
// // // //                 {
// // // //                     logger.LogInformation("No shop services generated to seed for the current set of shops (or all global services were null).");
// // // //                 }
// // // //             }
// // // //             else
// // // //             {
// // // //                 logger.LogInformation("ShopServices table already has data or prerequisites not met. Skipping shop service seed.");
// // // //             }

// // // //             logger.LogInformation("(DataSeeder) Seeding process complete.");
// // // //         }
// // // //     }
// // // // }
// // // // // // Data/DataSeeder.cs
// // // // // using AutomotiveServices.Api.Models;
// // // // // using Microsoft.EntityFrameworkCore;
// // // // // using NetTopologySuite.Geometries;
// // // // // using System;
// // // // // using System.Collections.Generic;
// // // // // using System.Globalization; // For TextInfo
// // // // // using System.Linq;
// // // // // using System.Text.RegularExpressions; // For slug generation
// // // // // using System.Threading.Tasks;
// // // // // using Microsoft.Extensions.Logging; // Added for ILogger


// // // // // namespace AutomotiveServices.Api.Data;

// // // // // public static class DataSeeder
// // // // // {
// // // // //     private static readonly GeometryFactory _geometryFactory = new(new PrecisionModel(), 4326);
// // // // //     private static Point CreatePoint(double latitude, double longitude) =>
// // // // //        _geometryFactory.CreatePoint(new Coordinate(longitude, latitude));

// // // // //     // Helper to generate a basic slug from a name
// // // // //     private static string GenerateSlugFromName(string name)
// // // // //     {
// // // // //         if (string.IsNullOrWhiteSpace(name)) return $"shop-{Guid.NewGuid().ToString().Substring(0, 6)}"; // Fallback for empty names
        
// // // // //         string str = name.ToLowerInvariant();
// // // // //         // Remove accents
// // // // //         // str = RemoveDiacritics(str); // You might need a more robust diacritic remover
// // // // //         // Replace invalid chars with a space
// // // // //         str = Regex.Replace(str, @"[^a-z0-9\s-]", " ");
// // // // //         // Convert multiple spaces/hyphens into one space
// // // // //         str = Regex.Replace(str, @"[\s-]+", " ").Trim();
// // // // //         // Replace space with a hyphen
// // // // //         str = Regex.Replace(str, @"\s", "-");
// // // // //         // Ensure it's not too long
// // // // //         return str.Length > 200 ? str.Substring(0, 200) : str; 
// // // // //     }
    
// // // // //     // Helper to generate a logo URL from a name
// // // // //     private static string GenerateLogoUrlFromName(string nameSlug)
// // // // //     {
// // // // //         return $"/logos/{nameSlug}.png";
// // // // //     }


// // // // //     public static async Task SeedAsync(AppDbContext context, ILogger logger, bool forceReseed)
// // // // //     {
// // // // //         await context.Database.EnsureCreatedAsync();

// // // // //         if (forceReseed)
// // // // //         {
// // // // //             logger.LogInformation("(DataSeeder) Force re-seed: Clearing relevant tables...");
// // // // //             // Clear in reverse order of dependency for FK constraints
// // // // //             await context.Database.ExecuteSqlRawAsync("DELETE FROM \"ShopServices\";"); // Clear dependent first
// // // // //             await context.Database.ExecuteSqlRawAsync("DELETE FROM \"GlobalServiceDefinitions\";");
// // // // //             await context.Database.ExecuteSqlRawAsync("DELETE FROM \"Shops\";");
// // // // //             await context.Database.ExecuteSqlRawAsync("DELETE FROM \"Cities\";");
// // // // //             logger.LogInformation("(DataSeeder) Database tables (ShopServices, GlobalServiceDefinitions, Shops, Cities) cleared.");
// // // // //         }

// // // // //         if (forceReseed || !await context.Cities.AnyAsync())
// // // // //         {
// // // // //             logger.LogInformation("Seeding cities...");
// // // // //             var cities = new List<City>
// // // // //             {
// // // // //                 new() { NameEn = "Cairo", NameAr = "القاهرة", Slug = "cairo", StateProvince = "Cairo Governorate", Country = "Egypt", IsActive = true, Location = CreatePoint(30.0444, 31.2357) },
// // // // //                 new() { NameEn = "Alexandria", NameAr = "الإسكندرية", Slug = "alexandria", StateProvince = "Alexandria Governorate", Country = "Egypt", IsActive = true, Location = CreatePoint(31.2001, 29.9187) },
// // // // //                 new() { NameEn = "Giza", NameAr = "الجيزة", Slug = "giza", StateProvince = "Giza Governorate", Country = "Egypt", IsActive = true, Location = CreatePoint(29.9870, 31.1313) }, // Approx. Giza center
// // // // //                 new() { NameEn = "6th October City", NameAr = "مدينة 6 أكتوبر", Slug = "6th-october-city", StateProvince = "Giza Governorate", Country = "Egypt", IsActive = true, Location = CreatePoint(29.9660, 30.9232) }, // Approx. 6th Oct center
// // // // //                 new() { NameEn = "New Cairo", NameAr = "القاهرة الجديدة", Slug = "new-cairo", StateProvince = "Cairo Governorate", Country = "Egypt", IsActive = true, Location = CreatePoint(30.0271, 31.4961) } // Approx. New Cairo center
// // // // //             };
// // // // //             await context.Cities.AddRangeAsync(cities);
// // // // //             await context.SaveChangesAsync();
// // // // //             logger.LogInformation("Successfully seeded {CityCount} cities.", cities.Count);
// // // // //         }
// // // // //         else { logger.LogInformation("Cities table already has data. Skipping city seed."); }
        
// // // // //         // --- SEED GLOBAL SERVICE DEFINITIONS ---
// // // // //         List<GlobalServiceDefinition> globalServices = new(); // To hold them for ShopService linking

// // // // //         if (forceReseed || !await context.GlobalServiceDefinitions.AnyAsync())
// // // // //         {
// // // // //             logger.LogInformation("Seeding GlobalServiceDefinitions...");
// // // // //             globalServices = new List<GlobalServiceDefinition>
// // // // //             {
// // // // //                 new() { ServiceCode = "OIL_CHANGE_STD", DefaultNameEn = "Standard Oil Change", DefaultNameAr = "تغيير زيت قياسي", DefaultDescriptionEn = "Includes engine oil and filter replacement (standard oil).", Category = ShopCategory.OilChange, DefaultEstimatedDurationMinutes = 30, IsGloballyActive = true },
// // // // //                 new() { ServiceCode = "OIL_CHANGE_SYN", DefaultNameEn = "Synthetic Oil Change", DefaultNameAr = "تغيير زيت تخليقي", DefaultDescriptionEn = "Includes synthetic engine oil and filter replacement.", Category = ShopCategory.OilChange, DefaultEstimatedDurationMinutes = 45, IsGloballyActive = true },
// // // // //                 new() { ServiceCode = "BRAKE_PAD_FRNT", DefaultNameEn = "Front Brake Pad Replacement", DefaultNameAr = "تغيير تيل الفرامل الأمامي", Category = ShopCategory.Brakes, DefaultEstimatedDurationMinutes = 60, IsGloballyActive = true },
// // // // //                 new() { ServiceCode = "BRAKE_PAD_REAR", DefaultNameEn = "Rear Brake Pad Replacement", DefaultNameAr = "تغيير تيل الفرامل الخلفي", Category = ShopCategory.Brakes, DefaultEstimatedDurationMinutes = 60, IsGloballyActive = true },
// // // // //                 new() { ServiceCode = "AC_REGAS", DefaultNameEn = "A/C Re-gas", DefaultNameAr = "إعادة شحن فريون التكييف", Category = ShopCategory.ACRepair, DefaultEstimatedDurationMinutes = 45, IsGloballyActive = true },
// // // // //                 new() { ServiceCode = "CAR_WASH_EXT", DefaultNameEn = "Exterior Car Wash", DefaultNameAr = "غسيل خارجي للسيارة", Category = ShopCategory.CarWash, DefaultEstimatedDurationMinutes = 20, IsGloballyActive = true },
// // // // //                 new() { ServiceCode = "TIRE_ROTATE", DefaultNameEn = "Tire Rotation", DefaultNameAr = "تدوير الإطارات", Category = ShopCategory.TireServices, DefaultEstimatedDurationMinutes = 30, IsGloballyActive = true },
// // // // //                 new() { ServiceCode = "ENGINE_DIAG", DefaultNameEn = "Engine Diagnostics", DefaultNameAr = "تشخيص أعطال المحرك", Category = ShopCategory.Diagnostics, DefaultEstimatedDurationMinutes = 60, IsGloballyActive = true },
// // // // //                 new() { ServiceCode = "GEN_MAINT_INSP", DefaultNameEn = "General Maintenance Inspection", DefaultNameAr = "فحص صيانة عام", Category = ShopCategory.GeneralMaintenance, DefaultEstimatedDurationMinutes = 90, IsGloballyActive = true }
// // // // //             };
// // // // //             await context.GlobalServiceDefinitions.AddRangeAsync(globalServices);
// // // // //             await context.SaveChangesAsync(); // Save global services before ShopServices need their IDs
// // // // //             logger.LogInformation("Successfully seeded {GlobalServiceCount} global service definitions.", globalServices.Count);
// // // // //         }
// // // // //         else
// // // // //         {
// // // // //             logger.LogInformation("GlobalServiceDefinitions table already has data. Skipping global service seed.");
// // // // //             globalServices = await context.GlobalServiceDefinitions.ToListAsync(); // Load existing if not re-seeding
// // // // //         }

// // // // //         List<Shop> seededShops = new();

// // // // //         if (forceReseed || !await context.Shops.AnyAsync())
// // // // //         {
// // // // //             logger.LogInformation("Seeding shops...");
// // // // //             var cairo = await context.Cities.FirstAsync(c => c.Slug == "cairo");
// // // // //             var alex = await context.Cities.FirstAsync(c => c.Slug == "alexandria");
// // // // //             var giza = await context.Cities.FirstAsync(c => c.Slug == "giza");
// // // // //             var octoberCity = await context.Cities.FirstAsync(c => c.Slug == "6th-october-city");
// // // // //             var newCairo = await context.Cities.FirstAsync(c => c.Slug == "new-cairo");

// // // // //             var shopsToSeed = new List<Shop>
// // // // //             {
// // // // //                 // --- 6th October City Shops ---
// // // // //                 // To ensure slug uniqueness within a city, I'll generate slugs from names or add suffixes.
// // // // //                 new() {
// // // // //                     NameEn = "Bosch Car Service - Auto Mech", NameAr = "بوش كار سيرفيس - أوتو ميك",
// // // // //                     Slug = "bosch-auto-mech-october", LogoUrl = GenerateLogoUrlFromName("bosch-auto-mech-october"),
// // // // //                     Address = "13 Industrial Zone, 6th October City", Location = CreatePoint(29.9523, 30.9176), PhoneNumber = "01000021565",
// // // // //                     ServicesOffered = "Engine Diagnostics, Oil Change, Brake Service", OpeningHours = "Sat-Thu 9am-7pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "El Saba Automotive Service Center", NameAr = "مركز خدمة السباعى للسيارات", //30.070042549123325, 31.35249452751594
// // // // //                     Slug = "el-saba-automotive-october", LogoUrl = GenerateLogoUrlFromName("el-saba-automotive-october"),
// // // // //                     Address = "El Mehwar Al Markazi, 6th of October City", Location = CreatePoint(30.070042549123325, 31.35249452751594), PhoneNumber = "19112",
// // // // //                     ServicesOffered = "Maintenance, Diagnostics, Periodic Services", OpeningHours = "Sun-Fri 9am-6pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Auto Pro Center", NameAr = "مركز أوتو برو",
// // // // //                     Slug = "auto-pro-center-october", LogoUrl = GenerateLogoUrlFromName("auto-pro-center-october"),
// // // // //                     Address = "Extension of 26th July Corridor, 6th October City", Location = CreatePoint(29.9701, 30.9054), PhoneNumber = "01099455663",
// // // // //                     ServicesOffered = "Tire Services, Oil Change, Engine Tune-up", OpeningHours = "Sat-Thu 10am-8pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Ghabbour Auto Service", NameAr = "غبور أوتو",
// // // // //                     Slug = "ghabbour-auto-october", LogoUrl = GenerateLogoUrlFromName("ghabbour-auto-october"),
// // // // //                     Address = "Plot 2, Area 7, 6th October Industrial Zone", Location = CreatePoint(29.9609, 30.9134), PhoneNumber = "16661",
// // // // //                     ServicesOffered = "Hyundai Service, AC Repair, Electrical Work", OpeningHours = "Sat-Thu 8am-6pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Speed Car Service Center", NameAr = "سبيد كار مركز خدمة",
// // // // //                     Slug = "speed-car-service-october", LogoUrl = GenerateLogoUrlFromName("speed-car-service-october"),
// // // // //                     Address = "Waslet Dahshur, 6th October City", Location = CreatePoint(29.9768, 30.9021), PhoneNumber = "01234567890",
// // // // //                     ServicesOffered = "Brake Service, Oil Change, Tune-Up", OpeningHours = "Sat-Fri 9am-8pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Auto Egypt", NameAr = "أوتو إيجيبت",
// // // // //                     Slug = "auto-egypt-october", LogoUrl = GenerateLogoUrlFromName("auto-egypt-october"),
// // // // //                     Address = "Plot 10, 2nd Industrial Zone, 6th October City", Location = CreatePoint(29.9575, 30.9090), PhoneNumber = "01000276423",
// // // // //                     ServicesOffered = "Repair, Servicing Equipment, AC Repair", OpeningHours = "Sat-Thu 9am-6pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Tariq Auto Repair Center", NameAr = "مركز طارق لتصليح السيارات",
// // // // //                     Slug = "tariq-auto-repair-october", LogoUrl = GenerateLogoUrlFromName("tariq-auto-repair-october"),
// // // // //                     Address = "Plot 54, Rd. 6, 2nd Service Spine, 1st Industrial Zone", Location = CreatePoint(29.9615, 30.9159), PhoneNumber = "01006865777",
// // // // //                     ServicesOffered = "General Repairs, Servicing Equipment", OpeningHours = "Sat-Thu 9am-6pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Hi Tech Commercial Vehicle Service", NameAr = "هاى تك لصيانة المركبات التجارية",
// // // // //                     Slug = "hi-tech-commercial-october", LogoUrl = GenerateLogoUrlFromName("hi-tech-commercial-october"),
// // // // //                     Address = "Plot 73, Extension Industrial Zone III, 6th October City", Location = CreatePoint(29.9590, 30.9170), PhoneNumber = "01100900742",
// // // // //                     ServicesOffered = "Commercial Vehicles Maintenance, AC, Painting", OpeningHours = "Sat-Thu 8:30am-5:30pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Technical Workshop 11th District", NameAr = "ورشة فنية الحي 11", // Clarified name
// // // // //                     Slug = "technical-workshop-11-october", LogoUrl = GenerateLogoUrlFromName("technical-workshop-11-october"),
// // // // //                     Address = "11th District, 6th October City", Location = CreatePoint(29.9640, 30.9200), PhoneNumber = "01004057239",
// // // // //                     ServicesOffered = "General Repairs, Electrical, AC", OpeningHours = "Sat-Thu 9am-6pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Car Repair Service Center October", NameAr = "مركز خدمة تصليح سيارات أكتوبر", // Clarified name
// // // // //                     Slug = "car-repair-service-october", LogoUrl = GenerateLogoUrlFromName("car-repair-service-october"),
// // // // //                     Address = "6th of October City", Location = CreatePoint(29.9680, 30.9250), PhoneNumber = "01198765432",
// // // // //                     ServicesOffered = "Mechanical, Electrical, Brakes, Paint, AC", OpeningHours = "Sat-Thu 9am-7pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Repair Service Center October 2", NameAr = "مركز إصلاح السيارات أكتوبر 2", // Clarified name
// // // // //                     Slug = "repair-service-october-2", LogoUrl = GenerateLogoUrlFromName("repair-service-october-2"),
// // // // //                     Address = "6th of October City", Location = CreatePoint(29.9650, 30.9220), PhoneNumber = "01011112222",
// // // // //                     ServicesOffered = "Automotive Service", OpeningHours = "Sat-Thu 9am-6pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Auto Service Nissan October", NameAr = "خدمة نيسان للسيارات أكتوبر", // Clarified name
// // // // //                     Slug = "auto-service-nissan-october", LogoUrl = GenerateLogoUrlFromName("auto-service-nissan-october"),
// // // // //                     Address = "6th of October City", Location = CreatePoint(29.9670, 30.9260), PhoneNumber = "01022223333",
// // // // //                     ServicesOffered = "Mechanical, Electricity, Suspension, Brakes, Lubricants", OpeningHours = "Mon-Sat 10am-6pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Kiro Car Service Center", NameAr = "مركز كيرو لصيانة السيارات",
// // // // //                     Slug = "kiro-car-service-october", LogoUrl = GenerateLogoUrlFromName("kiro-car-service-october"),
// // // // //                     Address = "Al‑Hayy 11, Gamal Abdel Nasser St, 6th October City", Location = CreatePoint(29.9580, 30.9100), PhoneNumber = "01222728260",
// // // // //                     ServicesOffered = "Engine Diagnostics (GT1/ISIS/ICOM), Electrical, AC, Mechanical, Transmission", OpeningHours = "Sat‑Thu 10am‑11pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Sand Group Auto Service", NameAr = "مركز ساند جروب لصيانة السيارات",
// // // // //                     Slug = "sand-group-auto-october", LogoUrl = GenerateLogoUrlFromName("sand-group-auto-october"),
// // // // //                     Address = "Piece 23, 2nd Industrial Zone Service Spine, 6th October City", Location = CreatePoint(29.9550, 30.9050), PhoneNumber = "01098693222",
// // // // //                     ServicesOffered = "Mechanics, Electrical, AC, Body & Paint, Diesel engine, Genuine parts", OpeningHours = "Sat‑Thu 9am‑6pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Rally Motors", NameAr = "رالى موتورز",
// // // // //                     Slug = "rally-motors-october", LogoUrl = GenerateLogoUrlFromName("rally-motors-october"),
// // // // //                     Address = "6th October City", Location = CreatePoint(29.91841448970566, 30.911635457698893), PhoneNumber = "", //29.91841448970566, 30.911635457698893
// // // // //                     ServicesOffered = "Mechanics, Electrical, Suspension, Bodywork, Brakes, Oil", OpeningHours = "Mon‑Sat 9am‑5pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Jarrag Auto", NameAr = "جراج اوتو",
// // // // //                     Slug = "jarrag-auto-october", LogoUrl = GenerateLogoUrlFromName("jarrag-auto-october"),
// // // // //                     Address = "6th October City", Location = CreatePoint(29.9620, 30.9150), PhoneNumber = "",
// // // // //                     ServicesOffered = "Mechanics, Electrical, Suspension, Bodywork, Brakes, Oil", OpeningHours = "Mon‑Sat 1pm‑11pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Commercial Motors AC Service", NameAr = "كومرشال موتورز لصيانة التكييف",
// // // // //                     Slug = "commercial-motors-ac-october", LogoUrl = GenerateLogoUrlFromName("commercial-motors-ac-october"),
// // // // //                     Address = "1st Industrial Zone, 6th October City", Location = CreatePoint(29.9520, 30.9000), PhoneNumber = "0238200279",
// // // // //                     ServicesOffered = "Car AC recharge & repair", OpeningHours = "Sat‑Thu 9am‑6pm", Category = ShopCategory.ACRepair, CityId = octoberCity.Id // Changed category
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Magic Auto Service", NameAr = "ماجيك أوتو سيرفيس",
// // // // //                     Slug = "magic-auto-service-october", LogoUrl = GenerateLogoUrlFromName("magic-auto-service-october"),
// // // // //                     Address = "Piece 117, 6th of October Corridor service road, 3rd Industrial Zone", Location = CreatePoint(29.9650, 30.9070), PhoneNumber = "01004149834",
// // // // //                     ServicesOffered = "AC, Mechanics, Electrical", OpeningHours = "Sat‑Thu 9am‑6pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Al-Khitab Service Center October", NameAr = "مركز الخطاب أكتوبر", // Differentiated
// // // // //                     Slug = "al-khitab-service-october", LogoUrl = GenerateLogoUrlFromName("al-khitab-service-october"),
// // // // //                     Address = "Qaryat Al‑Mukhtar, 6th of October City", Location = CreatePoint(29.9700, 30.9200), PhoneNumber = "01000510078",
// // // // //                     ServicesOffered = "Electrical services", OpeningHours = "Sat‑Thu 9am‑6pm", Category = ShopCategory.Diagnostics, CityId = octoberCity.Id // Changed category
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Sahma Car Service", NameAr = "سمحا لصيانة السيارات",
// // // // //                     Slug = "sahma-car-service-october", LogoUrl = GenerateLogoUrlFromName("sahma-car-service-october"),
// // // // //                     Address = "Kafraway Corridor, 6th of October City", Location = CreatePoint(29.9685, 30.9225), PhoneNumber = "01006994907",
// // // // //                     ServicesOffered = "General Mechanics, Suspension, Electrical", OpeningHours = "Sat‑Thu 9am‑6pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Al‑Alamiah Auto Repair October", NameAr = "العالمية لاصلاح السيارات أكتوبر", // Differentiated
// // // // //                     Slug = "al-alamiah-auto-repair-october", LogoUrl = GenerateLogoUrlFromName("al-alamiah-auto-repair-october"),
// // // // //                     Address = "Garden of Firouze Center, Hayy 3, 6th October City", Location = CreatePoint(29.9635, 30.9180), PhoneNumber = "",
// // // // //                     ServicesOffered = "Mechanics & diagnostics", OpeningHours = "Sat‑Thu 9am‑6pm", Category = ShopCategory.Diagnostics, CityId = octoberCity.Id // Changed category
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Star Tools Auto", NameAr = "ستار تولز لصيانة السيارات",
// // // // //                     Slug = "star-tools-auto-october", LogoUrl = GenerateLogoUrlFromName("star-tools-auto-october"),
// // // // //                     Address = "Villa 164, Sector 5, 6th October City", Location = CreatePoint(29.9570, 30.9120), PhoneNumber = "",
// // // // //                     ServicesOffered = "Spare parts, Mechanics", OpeningHours = "Sat‑Thu 9am‑6pm", Category = ShopCategory.NewAutoParts, CityId = octoberCity.Id // Changed category
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Technical Workshop For Cars Maintenance (3rd District)", NameAr = "ورشة فنية لصيانة السيارات – الحي 3",
// // // // //                     Slug = "tech-workshop-3rd-district-october", LogoUrl = GenerateLogoUrlFromName("tech-workshop-3rd-district-october"),
// // // // //                     Address = "5th Neighbourhood, 3rd District, 6th Oct City", Location = CreatePoint(29.9620, 30.9180), PhoneNumber = "01099485311",
// // // // //                     ServicesOffered = "General Repairs, Servicing Equipment", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Technical Workshop For Cars Maintenance (11th District B)", NameAr = "ورشة فنية لصيانة السيارات – الحي 11 ب", // Differentiated
// // // // //                     Slug = "tech-workshop-11th-district-b-october", LogoUrl = GenerateLogoUrlFromName("tech-workshop-11th-district-b-october"),
// // // // //                     Address = "11th District, 6th Oct City", Location = CreatePoint(29.9645, 30.9205), PhoneNumber = "01222976007",
// // // // //                     ServicesOffered = "General Repairs, Servicing Equipment", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Engineering Workshop (October Grand Mall)", NameAr = "الورشة الهندسية – أكتوبر جراند مول",
// // // // //                     Slug = "eng-workshop-grand-mall-october", LogoUrl = GenerateLogoUrlFromName("eng-workshop-grand-mall-october"),
// // // // //                     Address = "3rd District, next to October Grand Mall, 6th Oct City", Location = CreatePoint(29.9618, 30.9177), PhoneNumber = "01001511267",
// // // // //                     ServicesOffered = "Mechanical, Electrical, Servicing Equipment", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "El Sabbah Auto Service", NameAr = "مركز الصباح لخدمة السيارات",
// // // // //                     Slug = "el-sabbah-auto-october", LogoUrl = GenerateLogoUrlFromName("el-sabbah-auto-october"),
// // // // //                     Address = "Ajyad View Mall, 4th District, 6th Oct City", Location = CreatePoint(29.9585, 30.9150), PhoneNumber = "01099004561",
// // // // //                     ServicesOffered = "Electrical, Mechanical, Body & Paint, AC, Radiators", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Auto Repair (Hayy 7)", NameAr = "أوتو ريبير – الحي 7",
// // // // //                     Slug = "auto-repair-hayy7-october", LogoUrl = GenerateLogoUrlFromName("auto-repair-hayy7-october"),
// // // // //                     Address = "Hayy 7, next to Kilawee Market & Sudanese Negotiation Bldg", Location = CreatePoint(29.9580, 30.9105), PhoneNumber = "01060980088",
// // // // //                     ServicesOffered = "Nissan, Renault, Korean & Japanese cars: Mechanics, Electrical, Chassis, AC, Paint & Body, Computer Diagnostics", OpeningHours = "Sat–Thu 11am–10pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Top Service October", NameAr = "توب سيرفيس أكتوبر", // Differentiated
// // // // //                     Slug = "top-service-october", LogoUrl = GenerateLogoUrlFromName("top-service-october"),
// // // // //                     Address = "University Library St (parallel to Central Axis), Omrania, 6th Oct City", Location = CreatePoint(29.9600, 30.9200), PhoneNumber = "01226005753",
// // // // //                     ServicesOffered = "VW, Audi, Skoda, Seat: Mechanics, Electrical, Chassis, AC, Diagnostics, Body & Paint", OpeningHours = "Mon–Wed, Sat–Sun 11am–8pm; Thu 11am–4pm; Fri closed", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Auto Group Maintenance (Autel Outlet)", NameAr = "أوتو جروب لصيانة السيارات",
// // // // //                     Slug = "auto-group-autel-october", LogoUrl = GenerateLogoUrlFromName("auto-group-autel-october"),
// // // // //                     Address = "38–39 First Service Corridor, 1st Industrial Zone, next to Outlet Mall", Location = CreatePoint(29.9535, 30.9040), PhoneNumber = "01029666622",
// // // // //                     ServicesOffered = "Opel, Chevrolet, MG: Inspection, Servicing, Body & Paint, Genuine Parts", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Auto Option", NameAr = "كار أوبشن",
// // // // //                     Slug = "auto-option-october", LogoUrl = GenerateLogoUrlFromName("auto-option-october"),
// // // // //                     Address = "Al-Mogawara 5, Hayy 12, 6th Oct City", Location = CreatePoint(29.9570, 30.9230), PhoneNumber = "01229365458",
// // // // //                     ServicesOffered = "General Car Service", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Car Tech Workshop", NameAr = "كار تك",
// // // // //                     Slug = "car-tech-workshop-october", LogoUrl = GenerateLogoUrlFromName("car-tech-workshop-october"),
// // // // //                     Address = "Hayy 11, 6th Oct City", Location = CreatePoint(29.9630, 30.9240), PhoneNumber = "01222548796",
// // // // //                     ServicesOffered = "General Car Service", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Car Spa October", NameAr = "كار سبا أكتوبر", // Differentiated
// // // // //                     Slug = "car-spa-october", LogoUrl = GenerateLogoUrlFromName("car-spa-october"),
// // // // //                     Address = "Hayy Al-Mutamayez, 6th Oct City", Location = CreatePoint(29.9605, 30.9190), PhoneNumber = "01092042934",
// // // // //                     ServicesOffered = "General Car Service", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.CarWash, CityId = octoberCity.Id // Changed Category
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Car Carry", NameAr = "كار كارى",
// // // // //                     Slug = "car-carry-october", LogoUrl = GenerateLogoUrlFromName("car-carry-october"),
// // // // //                     Address = "Hayy 11, 6th Oct City", Location = CreatePoint(29.9635, 30.9195), PhoneNumber = "01117245884",
// // // // //                     ServicesOffered = "General Car Service", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Car Clinic October", NameAr = "كار كلينيك أكتوبر", // Differentiated
// // // // //                     Slug = "car-clinic-october", LogoUrl = GenerateLogoUrlFromName("car-clinic-october"),
// // // // //                     Address = "Hayy 10, 6th Oct City", Location = CreatePoint(29.9628, 30.9188), PhoneNumber = "01120430308",
// // // // //                     ServicesOffered = "General Car Service", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.Diagnostics, CityId = octoberCity.Id // Changed Category
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Crash Workshop", NameAr = "كراش ورشة",
// // // // //                     Slug = "crash-workshop-october", LogoUrl = GenerateLogoUrlFromName("crash-workshop-october"),
// // // // //                     Address = "Hayy 7, 6th Oct City", Location = CreatePoint(29.9582, 30.9123), PhoneNumber = "01003010870",
// // // // //                     ServicesOffered = "General Car Service", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.BodyRepairAndPaint, CityId = octoberCity.Id // Changed Category
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Clean Car October", NameAr = "كلين كار أكتوبر", // Differentiated
// // // // //                     Slug = "clean-car-october", LogoUrl = GenerateLogoUrlFromName("clean-car-october"),
// // // // //                     Address = "6th October City", Location = CreatePoint(29.9600, 30.9200), PhoneNumber = "01001031717",
// // // // //                     ServicesOffered = "General Car Service", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.CarWash, CityId = octoberCity.Id // Changed Category
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Mohamed Shensh Auto Electric", NameAr = "محمد شنش لكهرباء السيارات",
// // // // //                     Slug = "mohamed-shensh-electric-october", LogoUrl = GenerateLogoUrlFromName("mohamed-shensh-electric-october"),
// // // // //                     Address = "6 October St (October Tower), 6th Oct City", Location = CreatePoint(29.9610, 30.9210), PhoneNumber = "01204955650",
// // // // //                     ServicesOffered = "Car Electrical Repair", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.Diagnostics, CityId = octoberCity.Id // Changed Category
// // // // //                 },
// // // // //                 new() { // This one had CityId = octoberCity.Id but address seemed to be Cairo. Assuming it's an October branch.
// // // // //                     NameEn = "Mohamed Farhat Auto Electric October", NameAr = "محمد فرحات لكهرباء السيارات أكتوبر",
// // // // //                     Slug = "mohamed-farhat-electric-october", LogoUrl = GenerateLogoUrlFromName("mohamed-farhat-electric-october"),
// // // // //                     Address = "Industrial Area, 6th Oct City", Location = CreatePoint(29.9615, 30.9182), PhoneNumber = "01117726645", // Adjusted location slightly for October
// // // // //                     ServicesOffered = "Car Electrical Repair", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.Diagnostics, CityId = octoberCity.Id // Changed Category
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Mahmoud Auto Electric", NameAr = "محمود لكهرباء السيارات",
// // // // //                     Slug = "mahmoud-auto-electric-october", LogoUrl = GenerateLogoUrlFromName("mahmoud-auto-electric-october"),
// // // // //                     Address = "Hayy 6, 6th Oct City", Location = CreatePoint(29.9618, 30.9185), PhoneNumber = "01001936448",
// // // // //                     ServicesOffered = "Car Electrical Repair", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.Diagnostics, CityId = octoberCity.Id // Changed Category
// // // // //                 },
// // // // //                  new() { // This one was identical to another Al-Khitab above, differentiated name/slug
// // // // //                     NameEn = "Al Khitab Electrical Center", NameAr = "مركز الخطاب للكهرباء",
// // // // //                     Slug = "al-khitab-electrical-october", LogoUrl = GenerateLogoUrlFromName("al-khitab-electrical-october"),
// // // // //                     Address = "Qaryat Al-Mukhtar, Area B, 6th Oct City", Location = CreatePoint(29.9702, 30.9203), PhoneNumber = "01000510079", // Slightly different phone for uniqueness
// // // // //                     ServicesOffered = "Electrical Services & Diagnostics", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.Diagnostics, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Al Ekhlaas Service Center", NameAr = "مركز الإخلاص",
// // // // //                     Slug = "al-ekhlaas-service-october", LogoUrl = GenerateLogoUrlFromName("al-ekhlaas-service-october"),
// // // // //                     Address = "Al-Kefrawy Axis, Hayy 1, 6th Oct City", Location = CreatePoint(29.9600, 30.9150), PhoneNumber = "01006994907",
// // // // //                     ServicesOffered = "General Car Service", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Al Ameen Service Center", NameAr = "مركز الأمين",
// // // // //                     Slug = "al-ameen-service-october", LogoUrl = GenerateLogoUrlFromName("al-ameen-service-october"),
// // // // //                     Address = "Al-Kefrawy Axis, Hayy 3, 6th Oct City", Location = CreatePoint(29.9620, 30.9160), PhoneNumber = "01119637015",
// // // // //                     ServicesOffered = "General Car Service", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Al Rahma Center", NameAr = "مركز الرحمة",
// // // // //                     Slug = "al-rahma-center-october", LogoUrl = GenerateLogoUrlFromName("al-rahma-center-october"),
// // // // //                     Address = "Central Axis, Ne’ma Complex Bldg, Hayy 8, 6th Oct City", Location = CreatePoint(29.9630, 30.9200), PhoneNumber = "01112847808",
// // // // //                     ServicesOffered = "General Car Service", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Al Sultan Center", NameAr = "مركز السلطان",
// // // // //                     Slug = "al-sultan-center-october", LogoUrl = GenerateLogoUrlFromName("al-sultan-center-october"),
// // // // //                     Address = "Hayy 6, 6th Oct City", Location = CreatePoint(29.9622, 30.9188), PhoneNumber = "01126262828",
// // // // //                     ServicesOffered = "General Car Service", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Al Samah Center", NameAr = "مركز السماح",
// // // // //                     Slug = "al-samah-center-october", LogoUrl = GenerateLogoUrlFromName("al-samah-center-october"),
// // // // //                     Address = "Hayy 4, 6th Oct City", Location = CreatePoint(29.9590, 30.9170), PhoneNumber = "01003952427",
// // // // //                     ServicesOffered = "General Car Service", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Al Shurooq Center October", NameAr = "مركز الشروق أكتوبر", // Differentiated
// // // // //                     Slug = "al-shurooq-center-october", LogoUrl = GenerateLogoUrlFromName("al-shurooq-center-october"),
// // // // //                     Address = "Makka Al‑Mukarramah St, Hayy 7, inside Al‑Ordonia Mall, 6th Oct City", Location = CreatePoint(29.9630, 30.9185), PhoneNumber = "01275459661",
// // // // //                     ServicesOffered = "General Car Service", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Al Worsha Al Handasiya October", NameAr = "الورشة الهندسية أكتوبر", // Differentiated
// // // // //                     Slug = "al-worsha-al-handasiya-october", LogoUrl = GenerateLogoUrlFromName("al-worsha-al-handasiya-october"),
// // // // //                     Address = "Makka Al‑Mukarramah St, Hayy 7, inside Al‑Ordonia Mall, 6th Oct City", Location = CreatePoint(29.9632, 30.9187), PhoneNumber = "01126374432",
// // // // //                     ServicesOffered = "General Car Service", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Proton Point Service Center", NameAr = "بروتون بوينت",
// // // // //                     Slug = "proton-point-october", LogoUrl = GenerateLogoUrlFromName("proton-point-october"),
// // // // //                     Address = "Al‑Kefrawy Axis, Hayy 2, inside New Jordanian Mall, 6th Oct City", Location = CreatePoint(29.9610, 30.9165), PhoneNumber = "01009627238",
// // // // //                     ServicesOffered = "General Car Service", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Al Marwa Repair Workshop", NameAr = "ورشة المروة لإصلاح السيارات",
// // // // //                     Slug = "al-marwa-repair-october", LogoUrl = GenerateLogoUrlFromName("al-marwa-repair-october"),
// // // // //                     Address = "Geel 2000 St, Hayy 11, inside Nakheel 2 Center, 6th Oct City", Location = CreatePoint(29.9655, 30.9208), PhoneNumber = "01225214298",
// // // // //                     ServicesOffered = "General Car Service", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() { // Ezz El Arab has multiple branches, this is distinct enough.
// // // // //                     NameEn = "Ezz El-Arab Auto Group October", NameAr = "مجموعة عز العرب للسيارات فرع أكتوبر",
// // // // //                     Slug = "ezz-el-arab-october", LogoUrl = GenerateLogoUrlFromName("ezz-el-arab-october"),
// // // // //                     Address = "Waslet Dahshur St, West Yard, (Not Sheikh Zayed for this entry, keeping in October)", Location = CreatePoint(29.9760, 30.9020), PhoneNumber = "01032220855",
// // // // //                     ServicesOffered = "Manufacture & Repair, Body & Paint, Spare Parts", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                  new() { //This one was identical to another "Engineering Workshop (October Grand Mall)" so differentiated slug
// // // // //                     NameEn = "Engineering Workshop For Cars Maintenance Grand Mall", NameAr = "الورشة الهندسية لصيانة السيارات جراند مول",
// // // // //                     Slug = "eng-workshop-cars-grand-mall-october", LogoUrl = GenerateLogoUrlFromName("eng-workshop-cars-grand-mall-october"),
// // // // //                     Address = "3rd District (inside October Grand Mall), 6th October City", Location = CreatePoint(29.9619, 30.9178), //Slightly different location for uniqueness
// // // // //                     PhoneNumber = "01001511268", //Slightly different phone for uniqueness
// // // // //                     ServicesOffered = "Mechanical, Electrical, Servicing Equipment", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() { // Identical to another "Technical Workshop For Cars Maintenance (3rd District)", differentiated slug
// // // // //                     NameEn = "Technical Workshop For Cars Maintenance (3rd District B)", NameAr = "الورشة الفنية لصيانة السيارات – الحي 3 ب",
// // // // //                     Slug = "tech-workshop-3rd-district-b-october", LogoUrl = GenerateLogoUrlFromName("tech-workshop-3rd-district-b-october"),
// // // // //                     Address = "5th Neighbourhood, 3rd District, Near Square, 6th October City", Location = CreatePoint(29.9621, 30.9181), //Slightly different location
// // // // //                     PhoneNumber = "01099485312", //Slightly different phone
// // // // //                     ServicesOffered = "General Repairs, Servicing Equipment", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() { // Identical to "Technical Workshop For Cars Maintenance (11th District B)", differentiated slug
// // // // //                     NameEn = "Technical Workshop For Cars Maintenance (11th District C)", NameAr = "الورشة الفنية لصيانة السيارات – الحي 11 ج",
// // // // //                     Slug = "tech-workshop-11th-district-c-october", LogoUrl = GenerateLogoUrlFromName("tech-workshop-11th-district-c-october"),
// // // // //                     Address = "11th District, Next to El Hedaya 2 Center, 6th October City", Location = CreatePoint(29.9647, 30.9207), //Slightly different location
// // // // //                     PhoneNumber = "01222976008", //Slightly different phone
// // // // //                     ServicesOffered = "General Repairs, Servicing Equipment", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Global Center For Maintenance Cars", NameAr = "المركز العالمي لصيانة السيارات",
// // // // //                     Slug = "global-center-maintenance-october", LogoUrl = GenerateLogoUrlFromName("global-center-maintenance-october"),
// // // // //                     Address = "7th District, beside El Radwa Language Schools", Location = CreatePoint(29.9700, 30.9250), PhoneNumber = "01223657778",
// // // // //                     ServicesOffered = "General Car Servicing, Mechanical, Electrical", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                  new() { // This was identical to another "Auto Repair - Hayy 7", differentiated slug
// // // // //                     NameEn = "Auto Repair - Hayy 7 Annex", NameAr = "أوتو ريبير – الحي 7 ملحق",
// // // // //                     Slug = "auto-repair-hayy7-annex-october", LogoUrl = GenerateLogoUrlFromName("auto-repair-hayy7-annex-october"),
// // // // //                     Address = "Hayy 7, Behind Klaway Market, 6th October City", Location = CreatePoint(29.9583, 30.9124), //Slightly different location
// // // // //                     PhoneNumber = "01110620023", //Slightly different phone
// // // // //                     ServicesOffered = "Mechanics, Electrical, Suspension, AC, Painting, Bodywork, Diagnostics", OpeningHours = "Sat–Thu 11am–11pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Ahmed Agha Workshop", NameAr = "ورشة أحمد آغا",
// // // // //                     Slug = "ahmed-agha-workshop-october", LogoUrl = GenerateLogoUrlFromName("ahmed-agha-workshop-october"),
// // // // //                     Address = "Iskan El-Shabab 100m, 11th District, inside Center Al‑Wijih", Location = CreatePoint(29.9640, 30.9200), PhoneNumber = "01001408720",
// // // // //                     ServicesOffered = "General Car Repair & Equipment Service", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Mekka Workshop October", NameAr = "ورشة مكة أكتوبر", // Differentiated
// // // // //                     Slug = "mekka-workshop-october", LogoUrl = GenerateLogoUrlFromName("mekka-workshop-october"),
// // // // //                     Address = "11th District, inside Center Al‑Halfawy, 6th October City", Location = CreatePoint(29.9642, 30.9202), PhoneNumber = "01003318094",
// // // // //                     ServicesOffered = "General Car Repair & Equipment Service", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() { // This was identical to "Technical Workshop For Cars Maintenance (11th District C)", differentiated slug
// // // // //                     NameEn = "Technical Workshop – Center El Hedaya 2 Main", NameAr = "الورشة الفنية لصيانة السيارات – مركز الهداية 2 الرئيسي",
// // // // //                     Slug = "tech-workshop-hedaya2-main-october", LogoUrl = GenerateLogoUrlFromName("tech-workshop-hedaya2-main-october"),
// // // // //                     Address = "11th District, inside Center El Hedaya 2", Location = CreatePoint(29.9648, 30.9208), //Slightly different location
// // // // //                     PhoneNumber = "01224145999", //Slightly different phone
// // // // //                     ServicesOffered = "General Car Repair & Equipment Service", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Alameya Cars Services October", NameAr = "العالمية لخدمات السيارات فرع أكتوبر", // Differentiated
// // // // //                     Slug = "alameya-cars-services-october", LogoUrl = GenerateLogoUrlFromName("alameya-cars-services-october"),
// // // // //                     Address = "6th District, inside Center Wadi Al‑Malika, 6th October City", Location = CreatePoint(29.9595, 30.9175), PhoneNumber = "01282218444",
// // // // //                     ServicesOffered = "General Car Repair & Equipment Service", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Engineering Center For Mufflers", NameAr = "المركز الهندسي لخدمة الشكمانات",
// // // // //                     Slug = "eng-center-mufflers-october", LogoUrl = GenerateLogoUrlFromName("eng-center-mufflers-october"),
// // // // //                     Address = "Piece 145, Street 6, 3rd Industrial Zone, 6th October City", Location = CreatePoint(29.9550, 30.9050), PhoneNumber = "02338341377",
// // // // //                     ServicesOffered = "Muffler & Exhaust System Service", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Abnaa Al-Daqahliya Mechanics", NameAr = "أبناء الدقهلية لميكانيكا السيارات",
// // // // //                     Slug = "abnaa-aldaqahliya-mech-october", LogoUrl = GenerateLogoUrlFromName("abnaa-aldaqahliya-mech-october"),
// // // // //                     Address = "Inside Center Wadi Al‑Moluk, 6th District, 6th October City", Location = CreatePoint(29.9590, 30.9170), PhoneNumber = "01229452233",
// // // // //                     ServicesOffered = "Mechanical Repair & Servicing", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Wahdan Auto Group", NameAr = "وهدان أوتو جروب",
// // // // //                     Slug = "wahdan-auto-group-october", LogoUrl = GenerateLogoUrlFromName("wahdan-auto-group-october"),
// // // // //                     Address = "Street 47 off Street 14, 3rd Industrial Zone, 6th October City", Location = CreatePoint(29.9555, 30.9045), PhoneNumber = "02338342412",
// // // // //                     ServicesOffered = "General Car Service & Spare Parts", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.NewAutoParts, CityId = octoberCity.Id // Changed category
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Abu Ziyad Al‑Sarougy", NameAr = "أبو زياد السروجي",
// // // // //                     Slug = "abu-ziyad-alsarougy-october", LogoUrl = GenerateLogoUrlFromName("abu-ziyad-alsarougy-october"),
// // // // //                     Address = "Piece 80, Project 103, 6th District, 6th October City", Location = CreatePoint(29.9592, 30.9172), PhoneNumber = "01117946177",
// // // // //                     ServicesOffered = "General Car Repair & Servicing", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Ahmad Al-Afreet Auto Electric", NameAr = "أحمد العفريت لكهرباء السيارات",
// // // // //                     Slug = "ahmad-alafreet-electric-october", LogoUrl = GenerateLogoUrlFromName("ahmad-alafreet-electric-october"),
// // // // //                     Address = "Inside Center Wadi Al‑Moluk, 6th District, 6th October City", Location = CreatePoint(29.9588, 30.9168), PhoneNumber = "01026537353",
// // // // //                     ServicesOffered = "Car Electrical Repairs", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.Diagnostics, CityId = octoberCity.Id // Changed category
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Group United Toyota Service Center", NameAr = "المجموعة المتحدة - مركز خدمة تويوتا",
// // // // //                     Slug = "group-united-toyota-october", LogoUrl = GenerateLogoUrlFromName("group-united-toyota-october"),
// // // // //                     Address = "Piece 339, Extension 3rd Industrial Zone, 6th October City", Location = CreatePoint(29.9545, 30.9049), PhoneNumber = "01008552358",
// // // // //                     ServicesOffered = "Toyota Authorized Maintenance & Repairs", OpeningHours = "Sat–Thu 9am–6pm", Category = ShopCategory.GeneralMaintenance, CityId = octoberCity.Id
// // // // //                 },

// // // // //                  // --- Cairo Shops ---
// // // // //                 new() {
// // // // //                     NameEn = "Quick Oil Change Center Cairo", NameAr = "مركز تغيير الزيت السريع القاهرة",
// // // // //                     Slug = "quick-oil-cairo", LogoUrl = GenerateLogoUrlFromName("quick-oil-cairo"),
// // // // //                     CityId = cairo.Id, Category = ShopCategory.OilChange, Address = "Nasr City, Cairo", Location = CreatePoint(30.0590, 31.3280), PhoneNumber="01012345679"
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Parts Central Cairo", NameAr = "بارتس سنترال القاهرة",
// // // // //                     Slug = "parts-central-cairo", LogoUrl = GenerateLogoUrlFromName("parts-central-cairo"),
// // // // //                     CityId = cairo.Id, Category = ShopCategory.NewAutoParts, Address = "Abbas El Akkad, Cairo", Location = CreatePoint(30.0620, 31.3320), ServicesOffered="OEM Parts, Aftermarket Parts"
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Downtown Car Wash", NameAr = "غسيل سيارات وسط البلد",
// // // // //                     Slug = "downtown-car-wash-cairo", LogoUrl = GenerateLogoUrlFromName("downtown-car-wash-cairo"),
// // // // //                     CityId = cairo.Id, Category = ShopCategory.CarWash, Address = "Tahrir Square Area, Cairo", Location = CreatePoint(30.0440, 31.2350),
// // // // //                 },


// // // // //                 // --- Alexandria Shops ---
// // // // //                 new() {
// // // // //                     NameEn = "Alexandria Performance Parts", NameAr = "قطع غيار الأداء بالإسكندرية",
// // // // //                     Slug = "alex-performance-parts", LogoUrl = GenerateLogoUrlFromName("alex-performance-parts"),
// // // // //                     CityId = alex.Id, Category = ShopCategory.PerformanceParts, Address = "Sporting, Alexandria", Location = CreatePoint(31.2170, 29.9400), ServicesOffered="Exhausts,Intakes,Suspension"
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "Alexandria Diagnostics Pro", NameAr = "تشخيص محترف الإسكندرية",
// // // // //                     Slug = "alex-diagnostics-pro", LogoUrl = GenerateLogoUrlFromName("alex-diagnostics-pro"),
// // // // //                     CityId = alex.Id, Category = ShopCategory.Diagnostics, Address = "Roushdy, Alexandria", Location = CreatePoint(31.2280, 29.9600)
// // // // //                 },


// // // // //                 // --- Giza Shops ---
// // // // //                 new() {
// // // // //                     NameEn = "Giza Car Accessories World", NameAr = "عالم إكسسوارات السيارات بالجيزة",
// // // // //                     Slug = "giza-car-accessories", LogoUrl = GenerateLogoUrlFromName("giza-car-accessories"),
// // // // //                     CityId = giza.Id, Category = ShopCategory.CarAccessories, Address = "Mohandessin, Giza", Location = CreatePoint(30.0580, 31.2080), PhoneNumber="01119876543"
// // // // //                 },
// // // // //                  new() {
// // // // //                     NameEn = "Giza EV Charging Station", NameAr = "محطة شحن السيارات الكهربائية بالجيزة",
// // // // //                     Slug = "giza-ev-charging", LogoUrl = GenerateLogoUrlFromName("giza-ev-charging"),
// // // // //                     CityId = giza.Id, Category = ShopCategory.EVCharging, Address = "Dokki, Giza", Location = CreatePoint(30.0450, 31.2100),
// // // // //                 },

// // // // //                  // --- New Cairo Shops ---
// // // // //                 new() {
// // // // //                     NameEn = "New Cairo Premium Tire Service", NameAr = "خدمة إطارات مميزة بالقاهرة الجديدة",
// // // // //                     Slug = "new-cairo-premium-tires", LogoUrl = GenerateLogoUrlFromName("new-cairo-premium-tires"),
// // // // //                     CityId = newCairo.Id, Category = ShopCategory.TireServices, Address = "90th Street, New Cairo", Location = CreatePoint(30.0250, 31.4900),
// // // // //                 },
// // // // //                 new() {
// // // // //                     NameEn = "New Cairo Body & Paint Masters", NameAr = "ماسترز الدهان والسمكرة بالقاهرة الجديدة",
// // // // //                     Slug = "new-cairo-body-paint", LogoUrl = GenerateLogoUrlFromName("new-cairo-body-paint"),
// // // // //                     CityId = newCairo.Id, Category = ShopCategory.BodyRepairAndPaint, Address = "1st Settlement, New Cairo", Location = CreatePoint(30.0070, 31.4300)
// // // // //                 }

// // // // //             };

// // // // //             logger.LogInformation("Attempting to seed {ShopCount} shops.", shopsToSeed.Count);

// // // // //             // Temporary check for duplicate (CityId, Slug) pairs
// // // // //             var duplicateSlugsInCities = shopsToSeed
// // // // //                 .Where(s => s.Slug != null)
// // // // //                 .GroupBy(s => new { s.CityId, s.Slug })
// // // // //                 .Where(g => g.Count() > 1)
// // // // //                 .Select(g => $"Duplicate shop slug '{g.Key.Slug}' in CityId '{g.Key.CityId}' for shops: {string.Join(", ", g.Select(s => s.NameEn))}")
// // // // //                 .ToList();

// // // // //             if (duplicateSlugsInCities.Any())
// // // // //             {
// // // // //                 logger.LogError("Found duplicate shop slugs within the same city BEFORE SAVING:");
// // // // //                 foreach (var errorMsg in duplicateSlugsInCities)
// // // // //                 {
// // // // //                     logger.LogError(errorMsg);
// // // // //                 }
// // // // //                 throw new InvalidOperationException("Correct duplicate (CityId, Slug) combinations in seeder data before proceeding.");
// // // // //             }
// // // // //             // End of temporary check


// // // // //             await context.Shops.AddRangeAsync(shopsToSeed);
// // // // //             await context.SaveChangesAsync();
// // // // //              seededShops = await context.Shops.Where(s => !s.IsDeleted).ToListAsync(); // Get all seeded, non-deleted shops
// // // // //             logger.LogInformation("Successfully seeded {ShopCountActual} shops.", seededShops.Count);
// // // // //         }
// // // // //         else
// // // // //         {
// // // // //             logger.LogInformation("Shops table already has data and forceReseed is false. Skipping shop seed.");
// // // // //             seededShops = await context.Shops.Where(s => !s.IsDeleted).ToListAsync(); // Load existing if not re-seeding
// // // // //         }
// // // // //     // --- SEED SHOP SERVICES ---
// // // // //         if ((forceReseed || !await context.ShopServices.AnyAsync()) && seededShops.Any() && globalServices.Any())
// // // // //         {
// // // // //             logger.LogInformation("Seeding ShopServices...");
// // // // //             var shopServicesToSeed = new List<ShopService>();
// // // // //             var random = new Random();

// // // // //             // Get global service definitions by code for easy lookup
// // // // //             var oilChangeStd = globalServices.First(g => g.ServiceCode == "OIL_CHANGE_STD");
// // // // //             var oilChangeSyn = globalServices.First(g => g.ServiceCode == "OIL_CHANGE_SYN");
// // // // //             var brakeFront = globalServices.First(g => g.ServiceCode == "BRAKE_PAD_FRNT");
// // // // //             var acRegas = globalServices.First(g => g.ServiceCode == "AC_REGAS");
// // // // //             var carWashExt = globalServices.First(g => g.ServiceCode == "CAR_WASH_EXT");
// // // // //             var tireRotate = globalServices.First(g => g.ServiceCode == "TIRE_ROTATE");
// // // // //             var engineDiag = globalServices.First(g => g.ServiceCode == "ENGINE_DIAG");
// // // // //             var genMaintInsp = globalServices.First(g => g.ServiceCode == "GEN_MAINT_INSP");


// // // // //             foreach (var shop in seededShops)
// // // // //             {
// // // // //                 // Helper to create ShopService, populating EffectiveName
// // // // //                 ShopService CreateShopServiceEntry(Guid currentShopId, GlobalServiceDefinition globalDef, decimal price, string? customNameEn = null, string? customNameAr = null, int? duration = null)
// // // // //                 {
// // // // //                     return new ShopService
// // // // //                     {
// // // // //                         ShopId = currentShopId,
// // // // //                         GlobalServiceId = globalDef.GlobalServiceId,
// // // // //                         CustomServiceNameEn = customNameEn,
// // // // //                         CustomServiceNameAr = customNameAr,
// // // // //                         EffectiveNameEn = !string.IsNullOrEmpty(customNameEn) ? customNameEn : globalDef.DefaultNameEn,
// // // // //                         EffectiveNameAr = !string.IsNullOrEmpty(customNameAr) ? customNameAr : globalDef.DefaultNameAr,
// // // // //                         Price = price,
// // // // //                         DurationMinutes = duration ?? globalDef.DefaultEstimatedDurationMinutes,
// // // // //                         IsOfferedByShop = true,
// // // // //                         SortOrder = random.Next(1, 100) // Random sort order for now
// // // // //                     };
// // // // //                 }

// // // // //                 // Each shop offers a few global services with their own prices
// // // // //                 if (shop.Category == ShopCategory.GeneralMaintenance || shop.Category == ShopCategory.OilChange)
// // // // //                 {
// // // // //                     shopServicesToSeed.Add(CreateShopServiceEntry(shop.Id, oilChangeStd, Math.Round((decimal)(random.NextDouble() * 100 + 250), 2) )); // Price between 250-350
// // // // //                     shopServicesToSeed.Add(CreateShopServiceEntry(shop.Id, oilChangeSyn, Math.Round((decimal)(random.NextDouble() * 150 + 450), 2), duration: 50 )); // Price 450-600
// // // // //                 }
// // // // //                 if (shop.Category == ShopCategory.GeneralMaintenance || shop.Category == ShopCategory.Brakes)
// // // // //                 {
// // // // //                     shopServicesToSeed.Add(CreateShopServiceEntry(shop.Id, brakeFront, Math.Round((decimal)(random.NextDouble() * 200 + 600), 2) )); // Price 600-800
// // // // //                 }
// // // // //                 if (shop.Category == ShopCategory.GeneralMaintenance || shop.Category == ShopCategory.ACRepair)
// // // // //                 {
// // // // //                     shopServicesToSeed.Add(CreateShopServiceEntry(shop.Id, acRegas, Math.Round((decimal)(random.NextDouble() * 100 + 300), 2) )); // Price 300-400
// // // // //                 }
// // // // //                  if (shop.Category == ShopCategory.CarWash)
// // // // //                 {
// // // // //                     shopServicesToSeed.Add(CreateShopServiceEntry(shop.Id, carWashExt, Math.Round((decimal)(random.NextDouble() * 50 + 100), 2), duration: 25 )); // Price 100-150
// // // // //                 }
// // // // //                 if (shop.Category == ShopCategory.TireServices)
// // // // //                 {
// // // // //                     shopServicesToSeed.Add(CreateShopServiceEntry(shop.Id, tireRotate, Math.Round((decimal)(random.NextDouble() * 80 + 150), 2) )); // Price 150-230
// // // // //                 }
// // // // //                  if (shop.Category == ShopCategory.Diagnostics || shop.Category == ShopCategory.GeneralMaintenance)
// // // // //                 {
// // // // //                     shopServicesToSeed.Add(CreateShopServiceEntry(shop.Id, engineDiag, Math.Round((decimal)(random.NextDouble() * 150 + 200), 2) )); // Price 200-350
// // // // //                 }
// // // // //                  if (shop.Category == ShopCategory.GeneralMaintenance)
// // // // //                 {
// // // // //                     shopServicesToSeed.Add(CreateShopServiceEntry(shop.Id, genMaintInsp, Math.Round((decimal)(random.NextDouble() * 200 + 300), 2), duration: 100 )); // Price 300-500
// // // // //                 }


// // // // //                 // Example of a shop-specific custom service (not linked to global)
// // // // //                 if (shop.NameEn.Contains("Bosch")) // Example condition
// // // // //                 {
// // // // //                     shopServicesToSeed.Add(new ShopService
// // // // //                     {
// // // // //                         ShopId = shop.Id,
// // // // //                         GlobalServiceId = null, // Custom service
// // // // //                         CustomServiceNameEn = "Bosch Premium Diagnostic Package",
// // // // //                         CustomServiceNameAr = "باقة بوش التشخيصية الممتازة",
// // // // //                         EffectiveNameEn = "Bosch Premium Diagnostic Package",
// // // // //                         EffectiveNameAr = "باقة بوش التشخيصية الممتازة",
// // // // //                         ShopSpecificDescriptionEn = "Full vehicle computer scan with Bosch certified equipment and detailed report.",
// // // // //                         Price = 750.00m,
// // // // //                         DurationMinutes = 120,
// // // // //                         IsOfferedByShop = true,
// // // // //                         SortOrder = 5,
// // // // //                         IsPopularAtShop = true
// // // // //                     });
// // // // //                 }
// // // // //             }
// // // // //             await context.ShopServices.AddRangeAsync(shopServicesToSeed);
// // // // //             await context.SaveChangesAsync();
// // // // //             logger.LogInformation("Successfully seeded {ShopServiceCount} shop services.", shopServicesToSeed.Count);
// // // // //         }
// // // // //         else
// // // // //         {
// // // // //             logger.LogInformation("ShopServices table already has data or prerequisites not met. Skipping shop service seed.");
// // // // //         }
// // // // //     }
// // // // // }
// // // // // // // Data/DataSeeder.cs
// // // // // // using AutomotiveServices.Api.Models;
// // // // // // using Microsoft.EntityFrameworkCore;
// // // // // // using NetTopologySuite.Geometries;
// // // // // // using System; // For Guid
// // // // // // using System.Collections.Generic; // For List
// // // // // // using System.Linq; // For Linq methods like FirstAsync
// // // // // // using System.Threading.Tasks; // For Task


// // // // // // namespace AutomotiveServices.Api.Data;

// // // // // // public static class DataSeeder
// // // // // // {
// // // // // //      private static readonly GeometryFactory _geometryFactory = new(new PrecisionModel(), 4326);
// // // // // //       private static Point CreatePoint(double latitude, double longitude) =>
// // // // // //         _geometryFactory.CreatePoint(new Coordinate(longitude, latitude));


// // // // // //     // private static Point CreatePoint(double latitude, double longitude, int srid = 4326) =>
// // // // // //     //     new Point(longitude, latitude) { SRID = srid };

// // // // // //     // Pass ILogger for better insights
// // // // // //     public static async Task SeedAsync(AppDbContext context, ILogger logger, bool forceReseed)
// // // // // //     {
// // // // // //         await context.Database.EnsureCreatedAsync();

// // // // // //         if (forceReseed)
// // // // // //         {
// // // // // //             logger.LogInformation("(DataSeeder) Force re-seed: Clearing Shops table...");
// // // // // //             // Order of deletion matters due to foreign keys
// // // // // //             await context.Database.ExecuteSqlRawAsync("DELETE FROM \"Shops\";");
// // // // // //             await context.Database.ExecuteSqlRawAsync("DELETE FROM \"Cities\";");
// // // // // //             logger.LogInformation("(DataSeeder) Database tables (Shops, Cities) cleared.");
// // // // // //         }
// // // // // //          // Seed Cities
// // // // // //         if (forceReseed || !await context.Cities.AnyAsync())
// // // // // //         {
// // // // // //             logger.LogInformation("Seeding cities...");
// // // // // //             var cities = new List<City>
// // // // // //             {
// // // // // //                 new() { NameEn = "Cairo", NameAr = "القاهرة", Slug = "cairo", StateProvince = "Cairo Governorate", Country = "Egypt", IsActive = true },
// // // // // //                 new() { NameEn = "Alexandria", NameAr = "الإسكندرية", Slug = "alexandria", StateProvince = "Alexandria Governorate", Country = "Egypt", IsActive = true },
// // // // // //                 new() { NameEn = "Giza", NameAr = "الجيزة", Slug = "giza", StateProvince = "Giza Governorate", Country = "Egypt", IsActive = true },
// // // // // //                 new() { NameEn = "6th October City", NameAr = "مدينة 6 أكتوبر", Slug = "6th-october-city", StateProvince = "Giza Governorate", Country = "Egypt", IsActive = true },
// // // // // //                 new() { NameEn = "New Cairo", NameAr = "القاهرة الجديدة", Slug = "new-cairo", StateProvince = "Cairo Governorate", Country = "Egypt", IsActive = true }
// // // // // //             };
// // // // // //             await context.Cities.AddRangeAsync(cities);
// // // // // //             await context.SaveChangesAsync();
// // // // // //             logger.LogInformation("Successfully seeded {CityCount} cities.", cities.Count);
// // // // // //         }
// // // // // //         else { logger.LogInformation("Cities table already has data. Skipping city seed."); }

// // // // // //         if (forceReseed || !await context.Shops.AnyAsync())
// // // // // //         {
// // // // // //             logger.LogInformation("Seeding shops...");
// // // // // //             var cairo = await context.Cities.FirstAsync(c => c.Slug == "cairo");
// // // // // //             var alex = await context.Cities.FirstAsync(c => c.Slug == "alexandria");
// // // // // //             var giza = await context.Cities.FirstAsync(c => c.Slug == "giza");
// // // // // //             var octoberCity = await context.Cities.FirstAsync(c => c.Slug == "6th-october-city");
// // // // // //             var newCairo = await context.Cities.FirstAsync(c => c.Slug == "new-cairo");

// // // // // //             var shops = new List<Shop>
// // // // // //             {
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Bosch Car Service - Auto Mech",
// // // // // //                     NameAr = "بوش كار سيرفيس - أوتو ميك",
// // // // // //                     Address = "13 Industrial Zone, 6th October City",
// // // // // //                     Location = CreatePoint(29.9523, 30.9176),
// // // // // //                     PhoneNumber = "01000021565",
// // // // // //                     ServicesOffered = "Engine Diagnostics, Oil Change, Brake Service",
// // // // // //                     OpeningHours = "Sat-Thu 9am-7pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="pro-auto-care-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"

// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "El Saba Automotive Service Center",
// // // // // //                     NameAr = "مركز خدمة السباعى للسيارات",
// // // // // //                     Address = "El Mehwar Al Markazi, 6th of October City",
// // // // // //                     Location = CreatePoint(29.9662, 30.9278),
// // // // // //                     PhoneNumber = "19112",
// // // // // //                     ServicesOffered = "Maintenance, Diagnostics, Periodic Services",
// // // // // //                     OpeningHours = "Sun-Fri 9am-6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="quick-oil-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"

// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Auto Pro Center",
// // // // // //                     NameAr = "مركز أوتو برو",
// // // // // //                     Address = "Extension of 26th July Corridor, 6th October City",
// // // // // //                     Location = CreatePoint(29.9701, 30.9054),
// // // // // //                     PhoneNumber = "01099455663",
// // // // // //                     ServicesOffered = "Tire Services, Oil Change, Engine Tune-up",
// // // // // //                     OpeningHours = "Sat-Thu 10am-8pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="parkle Care-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"

// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Ghabbour Auto Service",
// // // // // //                     NameAr = "غبور أوتو",
// // // // // //                     Address = "Plot 2, Area 7, 6th October Industrial Zone",
// // // // // //                     Location = CreatePoint(29.9609, 30.9134),
// // // // // //                     PhoneNumber = "16661",
// // // // // //                     ServicesOffered = "Hyundai Service, AC Repair, Electrical Work",
// // // // // //                     OpeningHours = "Sat-Thu 8am-6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="parkle Car-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"

// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Speed Car Service Center",
// // // // // //                     NameAr = "سبيد كار مركز خدمة",
// // // // // //                     Address = "Waslet Dahshur, 6th October City",
// // // // // //                     Location = CreatePoint(29.9768, 30.9021),
// // // // // //                     PhoneNumber = "01234567890",
// // // // // //                     ServicesOffered = "Brake Service, Oil Change, Tune-Up",
// // // // // //                     OpeningHours = "Sat-Fri 9am-8pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="parkle-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"

// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Auto Egypt",
// // // // // //                     NameAr = "أوتو إيجيبت",
// // // // // //                     Address = "Plot 10, 2nd Industrial Zone, 6th October City",
// // // // // //                     Location = CreatePoint(29.9575, 30.9090),
// // // // // //                     PhoneNumber = "01000276423",
// // // // // //                     ServicesOffered = "Repair, Servicing Equipment, AC Repair",
// // // // // //                     OpeningHours = "Sat-Thu 9am-6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="pr-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"

// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Tariq Auto Repair Center",
// // // // // //                     NameAr = "مركز طارق لتصليح السيارات",
// // // // // //                     Address = "Plot 54, Rd. 6, 2nd Service Spine, 1st Industrial Zone",
// // // // // //                     Location = CreatePoint(29.9615, 30.9159),
// // // // // //                     PhoneNumber = "01006865777",
// // // // // //                     ServicesOffered = "General Repairs, Servicing Equipment",
// // // // // //                     OpeningHours = "Sat-Thu 9am-6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="r-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"

// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Hi Tech Commercial Vehicle Service",
// // // // // //                     NameAr = "هاى تك لصيانة المركبات التجارية",
// // // // // //                     Address = "Plot 73, Extension Industrial Zone III, 6th October City",
// // // // // //                     Location = CreatePoint(29.9590, 30.9170),
// // // // // //                     PhoneNumber = "01100900742",
// // // // // //                     ServicesOffered = "Commercial Vehicles Maintenance, AC, Painting",
// // // // // //                     OpeningHours = "Sat-Thu 8:30am-5:30pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="pCar-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"

// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Technical Workshop",
// // // // // //                     NameAr = "ورشة فنية",
// // // // // //                     Address = "11th District, 6th October City",
// // // // // //                     Location = CreatePoint(29.9640, 30.9200),
// // // // // //                     PhoneNumber = "01004057239",
// // // // // //                     ServicesOffered = "General Repairs, Electrical, AC",
// // // // // //                     OpeningHours = "Sat-Thu 9am-6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="par-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"

// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Car Repair Service Center",
// // // // // //                     NameAr = "مركز خدمة تصليح سيارات",
// // // // // //                     Address = "6th of October City",
// // // // // //                     Location = CreatePoint(29.9680, 30.9250),
// // // // // //                     PhoneNumber = "01198765432",
// // // // // //                     ServicesOffered = "Mechanical, Electrical, Brakes, Paint, AC",
// // // // // //                     OpeningHours = "Sat-Thu 9am-7pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="paber",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"

// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Repair Service Center",
// // // // // //                     NameAr = "مركز إصلاح السيارات",
// // // // // //                     Address = "6th of October City",
// // // // // //                     Location = CreatePoint(29.9650, 30.9220),
// // // // // //                     PhoneNumber = "01011112222",
// // // // // //                     ServicesOffered = "Automotive Service",
// // // // // //                     OpeningHours = "Sat-Thu 9am-6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="lslCar-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"

// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Auto Service Nissan",
// // // // // //                     NameAr = "خدمة نيسان للسيارات",
// // // // // //                     Address = "6th of October City",
// // // // // //                     Location = CreatePoint(29.9670, 30.9260),
// // // // // //                     PhoneNumber = "01022223333",
// // // // // //                     ServicesOffered = "Mechanical, Electricity, Suspension, Brakes, Lubricants",
// // // // // //                     OpeningHours = "Mon-Sat 10am-6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Nissan-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"

// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Kiro Car Service Center",
// // // // // //                     NameAr = "مركز كيرو لصيانة السيارات",
// // // // // //                     Address = "Al‑Hayy 11, Gamal Abdel Nasser St, 6th October City",
// // // // // //                     Location = CreatePoint(29.9580, 30.9100),
// // // // // //                     PhoneNumber = "01222728260",
// // // // // //                     ServicesOffered = "Engine Diagnostics (GT1/ISIS/ICOM), Electrical, AC, Mechanical, Transmission",
// // // // // //                     OpeningHours = "Sat‑Thu 10am‑11pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Kiro-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
                    
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Sand Group Auto Service",
// // // // // //                     NameAr = "مركز ساند جروب لصيانة السيارات",
// // // // // //                     Address = "Piece 23, 2nd Industrial Zone Service Spine, 6th October City",
// // // // // //                     Location = CreatePoint(29.9550, 30.9050),
// // // // // //                     PhoneNumber = "01098693222",
// // // // // //                     ServicesOffered = "Mechanics, Electrical, AC, Body & Paint, Diesel engine, Genuine parts",
// // // // // //                     OpeningHours = "Sat‑Thu 9am‑6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Rally Motors",
// // // // // //                     NameAr = "رالى موتورز",
// // // // // //                     Address = "6th October City",
// // // // // //                     Location = CreatePoint(29.9600, 30.9120),
// // // // // //                     PhoneNumber = "", // no phone listed
// // // // // //                     ServicesOffered = "Mechanics, Electrical, Suspension, Bodywork, Brakes, Oil",
// // // // // //                     OpeningHours = "Mon‑Sat 9am‑5pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Jarrag Auto",
// // // // // //                     NameAr = "جراج اوتو",
// // // // // //                     Address = "6th October City",
// // // // // //                     Location = CreatePoint(29.9620, 30.9150),
// // // // // //                     PhoneNumber = "", // no phone listed
// // // // // //                     ServicesOffered = "Mechanics, Electrical, Suspension, Bodywork, Brakes, Oil",
// // // // // //                     OpeningHours = "Mon‑Sat 1pm‑11pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Commercial Motors AC Service",
// // // // // //                     NameAr = "كومرشال موتورز لصيانة التكييف",
// // // // // //                     Address = "1st Industrial Zone, 6th October City",
// // // // // //                     Location = CreatePoint(29.9520, 30.9000),
// // // // // //                     PhoneNumber = "0238200279",
// // // // // //                     ServicesOffered = "Car AC recharge & repair",
// // // // // //                     OpeningHours = "Sat‑Thu 9am‑6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Magic Auto Service",
// // // // // //                     NameAr = "ماجيك أوتو سيرفيس",
// // // // // //                     Address = "Piece 117, 6th of October Corridor service road, 3rd Industrial Zone",
// // // // // //                     Location = CreatePoint(29.9650, 30.9070),
// // // // // //                     PhoneNumber = "01004149834",
// // // // // //                     ServicesOffered = "AC, Mechanics, Electrical",
// // // // // //                     OpeningHours = "Sat‑Thu 9am‑6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Al-Khitab Service Center",
// // // // // //                     NameAr = "مركز الخطاب",
// // // // // //                     Address = "Qaryat Al‑Mukhtar, 6th of October City",
// // // // // //                     Location = CreatePoint(29.9700, 30.9200),
// // // // // //                     PhoneNumber = "01000510078",
// // // // // //                     ServicesOffered = "Electrical services",
// // // // // //                     OpeningHours = "Sat‑Thu 9am‑6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Sahma Car Service",
// // // // // //                     NameAr = "سمحا لصيانة السيارات",
// // // // // //                     Address = "Kafraway Corridor, 6th of October City",
// // // // // //                     Location = CreatePoint(29.9685, 30.9225),
// // // // // //                     PhoneNumber = "01006994907",
// // // // // //                     ServicesOffered = "General Mechanics, Suspension, Electrical",
// // // // // //                     OpeningHours = "Sat‑Thu 9am‑6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Al‑Alamiah Auto Repair",
// // // // // //                     NameAr = "العالمية لاصلاح السيارات",
// // // // // //                     Address = "Garden of Firouze Center, Hayy 3, 6th October City",
// // // // // //                     Location = CreatePoint(29.9635, 30.9180),
// // // // // //                     PhoneNumber = "", // no phone listed
// // // // // //                     ServicesOffered = "Mechanics & diagnostics",
// // // // // //                     OpeningHours = "Sat‑Thu 9am‑6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Star Tools Auto",
// // // // // //                     NameAr = "ستار تولز لصيانة السيارات",
// // // // // //                     Address = "Villa 164, Sector 5, 6th October City",
// // // // // //                     Location = CreatePoint(29.9570, 30.9120),
// // // // // //                     PhoneNumber = "", // no phone listed
// // // // // //                     ServicesOffered = "Spare parts, Mechanics",
// // // // // //                     OpeningHours = "Sat‑Thu 9am‑6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Technical Workshop For Cars Maintenance (3rd District)",
// // // // // //                     NameAr = "ورشة فنية لصيانة السيارات – الحي 3",
// // // // // //                     Address = "5th Neighbourhood, 3rd District, 6th Oct City",
// // // // // //                     Location = CreatePoint(29.9620, 30.9180),
// // // // // //                     PhoneNumber = "01099485311",
// // // // // //                     ServicesOffered = "General Repairs, Servicing Equipment",
// // // // // //                     OpeningHours = "Sat–Thu 9am–6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Technical Workshop For Cars Maintenance (11th District)",
// // // // // //                     NameAr = "ورشة فنية لصيانة السيارات – الحي 11",
// // // // // //                     Address = "11th District, 6th Oct City",
// // // // // //                     Location = CreatePoint(29.9645, 30.9205),
// // // // // //                     PhoneNumber = "01222976007",
// // // // // //                     ServicesOffered = "General Repairs, Servicing Equipment",
// // // // // //                     OpeningHours = "Sat–Thu 9am–6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Engineering Workshop (October Grand Mall)",
// // // // // //                     NameAr = "الورشة الهندسية – أكتوبر جراند مول",
// // // // // //                     Address = "3rd District, next to October Grand Mall, 6th Oct City",
// // // // // //                     Location = CreatePoint(29.9618, 30.9177),
// // // // // //                     PhoneNumber = "01001511267",
// // // // // //                     ServicesOffered = "Mechanical, Electrical, Servicing Equipment",
// // // // // //                     OpeningHours = "Sat–Thu 9am–6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "El Sabbah Auto Service",
// // // // // //                     NameAr = "مركز الصباح لخدمة السيارات",
// // // // // //                     Address = "Ajyad View Mall, 4th District, 6th Oct City",
// // // // // //                     Location = CreatePoint(29.9585, 30.9150),
// // // // // //                     PhoneNumber = "01099004561",
// // // // // //                     ServicesOffered = "Electrical, Mechanical, Body & Paint, AC, Radiators",
// // // // // //                     OpeningHours = "Sat–Thu 9am–6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Auto Repair (Hayy 7)",
// // // // // //                     NameAr = "أوتو ريبير – الحي 7",
// // // // // //                     Address = "Hayy 7, next to Kilawee Market & Sudanese Negotiation Bldg",
// // // // // //                     Location = CreatePoint(29.9580, 30.9105),
// // // // // //                     PhoneNumber = "01060980088",
// // // // // //                     ServicesOffered = "Nissan, Renault, Korean & Japanese cars: Mechanics, Electrical, Chassis, AC, Paint & Body, Computer Diagnostics",
// // // // // //                     OpeningHours = "Sat–Thu 11am–10pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Top Service",
// // // // // //                     NameAr = "توب سيرفيس",
// // // // // //                     Address = "University Library St (parallel to Central Axis), Omrania, 6th Oct City",
// // // // // //                     Location = CreatePoint(29.9600, 30.9200),
// // // // // //                     PhoneNumber = "01226005753",
// // // // // //                     ServicesOffered = "VW, Audi, Skoda, Seat: Mechanics, Electrical, Chassis, AC, Diagnostics, Body & Paint",
// // // // // //                     OpeningHours = "Mon–Wed, Sat–Sun 11am–8pm; Thu 11am–4pm; Fri closed",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Auto Group Maintenance (Autel Outlet)",
// // // // // //                     NameAr = "أوتو جروب لصيانة السيارات",
// // // // // //                     Address = "38–39 First Service Corridor, 1st Industrial Zone, next to Outlet Mall",
// // // // // //                     Location = CreatePoint(29.9535, 30.9040),
// // // // // //                     PhoneNumber = "01029666622",
// // // // // //                     ServicesOffered = "Opel, Chevrolet, MG: Inspection, Servicing, Body & Paint, Genuine Parts",
// // // // // //                     OpeningHours = "Sat–Thu 9am–6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Auto Option",
// // // // // //                     NameAr = "كار أوبشن",
// // // // // //                     Address = "Al-Mogawara 5, Hayy 12, 6th Oct City",
// // // // // //                     Location = CreatePoint(29.9570, 30.9230),
// // // // // //                     PhoneNumber = "01229365458",
// // // // // //                     ServicesOffered = "General Car Service",
// // // // // //                     OpeningHours = "Sat–Thu 9am–6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Car Tech Workshop",
// // // // // //                     NameAr = "كار تك",
// // // // // //                     Address = "Hayy 11, 6th Oct City",
// // // // // //                     Location = CreatePoint(29.9630, 30.9240),
// // // // // //                     PhoneNumber = "01222548796",
// // // // // //                     ServicesOffered = "General Car Service",
// // // // // //                     OpeningHours = "Sat–Thu 9am–6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Car Spa",
// // // // // //                     NameAr = "كار سبا",
// // // // // //                     Address = "Hayy Al-Mutamayez, 6th Oct City",
// // // // // //                     Location = CreatePoint(29.9605, 30.9190),
// // // // // //                     PhoneNumber = "01092042934",
// // // // // //                     ServicesOffered = "General Car Service",
// // // // // //                     OpeningHours = "Sat–Thu 9am–6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Car Carry",
// // // // // //                     NameAr = "كار كارى",
// // // // // //                     Address = "Hayy 11, 6th Oct City",
// // // // // //                     Location = CreatePoint(29.9635, 30.9195),
// // // // // //                     PhoneNumber = "01117245884",
// // // // // //                     ServicesOffered = "General Car Service",
// // // // // //                     OpeningHours = "Sat–Thu 9am–6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Car Clinic",
// // // // // //                     NameAr = "كار كلينيك",
// // // // // //                     Address = "Hayy 10, 6th Oct City",
// // // // // //                     Location = CreatePoint(29.9628, 30.9188),
// // // // // //                     PhoneNumber = "01120430308",
// // // // // //                     ServicesOffered = "General Car Service",
// // // // // //                     OpeningHours = "Sat–Thu 9am–6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Crash Workshop",
// // // // // //                     NameAr = "كراش ورشة",
// // // // // //                     Address = "Hayy 7, 6th Oct City",
// // // // // //                     Location = CreatePoint(29.9582, 30.9123),
// // // // // //                     PhoneNumber = "01003010870",
// // // // // //                     ServicesOffered = "General Car Service",
// // // // // //                     OpeningHours = "Sat–Thu 9am–6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Clean Car",
// // // // // //                     NameAr = "كلين كار",
// // // // // //                     Address = "6th October City",
// // // // // //                     Location = CreatePoint(29.9600, 30.9200),
// // // // // //                     PhoneNumber = "01001031717",
// // // // // //                     ServicesOffered = "General Car Service",
// // // // // //                     OpeningHours = "Sat–Thu 9am–6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Mohamed Shensh Auto Electric",
// // // // // //                     NameAr = "محمد شنش لكهرباء السيارات",
// // // // // //                     Address = "6 October St (October Tower), 6th Oct City",
// // // // // //                     Location = CreatePoint(29.9610, 30.9210),
// // // // // //                     PhoneNumber = "01204955650",
// // // // // //                     ServicesOffered = "Car Electrical Repair",
// // // // // //                     OpeningHours = "Sat–Thu 9am–6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Mohamed Farhat Auto Electric",
// // // // // //                     NameAr = "محمد فرحات لكهرباء السيارات",
// // // // // //                     Address = "15 6th October St, Ain Shams branch? (Cairo)",
// // // // // //                     Location = CreatePoint(30.0500, 31.3000),
// // // // // //                     PhoneNumber = "01117726645",
// // // // // //                     ServicesOffered = "Car Electrical Repair",
// // // // // //                     OpeningHours = "Sat–Thu 9am–6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Mahmoud Auto Electric",
// // // // // //                     NameAr = "محمود لكهرباء السيارات",
// // // // // //                     Address = "Hayy 6, 6th Oct City",
// // // // // //                     Location = CreatePoint(29.9618, 30.9185),
// // // // // //                     PhoneNumber = "01001936448",
// // // // // //                     ServicesOffered = "Car Electrical Repair",
// // // // // //                     OpeningHours = "Sat–Thu 9am–6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Al Khitab Service Center",
// // // // // //                     NameAr = "مركز الخطاب",
// // // // // //                     Address = "Qaryat Al-Mukhtar, 6th Oct City",
// // // // // //                     Location = CreatePoint(29.9700, 30.9200),
// // // // // //                     PhoneNumber = "01000510078",
// // // // // //                     ServicesOffered = "Electrical Services",
// // // // // //                     OpeningHours = "Sat–Thu 9am–6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Al Ekhlaas Service Center",
// // // // // //                     NameAr = "مركز الإخلاص",
// // // // // //                     Address = "Al-Kefrawy Axis, Hayy 1, 6th Oct City",
// // // // // //                     Location = CreatePoint(29.9600, 30.9150),
// // // // // //                     PhoneNumber = "01006994907",
// // // // // //                     ServicesOffered = "General Car Service",
// // // // // //                     OpeningHours = "Sat–Thu 9am–6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Al Ameen Service Center",
// // // // // //                     NameAr = "مركز الأمين",
// // // // // //                     Address = "Al-Kefrawy Axis, Hayy 3, 6th Oct City",
// // // // // //                     Location = CreatePoint(29.9620, 30.9160),
// // // // // //                     PhoneNumber = "01119637015",
// // // // // //                     ServicesOffered = "General Car Service",
// // // // // //                     OpeningHours = "Sat–Thu 9am–6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Al Rahma Center",
// // // // // //                     NameAr = "مركز الرحمة",
// // // // // //                     Address = "Central Axis, Ne’ma Complex Bldg, Hayy 8, 6th Oct City",
// // // // // //                     Location = CreatePoint(29.9630, 30.9200),
// // // // // //                     PhoneNumber = "01112847808",
// // // // // //                     ServicesOffered = "General Car Service",
// // // // // //                     OpeningHours = "Sat–Thu 9am–6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Al Sultan Center",
// // // // // //                     NameAr = "مركز السلطان",
// // // // // //                     Address = "Hayy 6, 6th Oct City",
// // // // // //                     Location = CreatePoint(29.9622, 30.9188),
// // // // // //                     PhoneNumber = "01126262828",
// // // // // //                     ServicesOffered = "General Car Service",
// // // // // //                     OpeningHours = "Sat–Thu 9am–6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Al Samah Center",
// // // // // //                     NameAr = "مركز السماح",
// // // // // //                     Address = "Hayy 4, 6th Oct City",
// // // // // //                     Location = CreatePoint(29.9590, 30.9170),
// // // // // //                     PhoneNumber = "01003952427",
// // // // // //                     ServicesOffered = "General Car Service",
// // // // // //                     OpeningHours = "Sat–Thu 9am–6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Al Shurooq Center",
// // // // // //                     NameAr = "مركز الشروق",
// // // // // //                     Address = "Makka Al‑Mukarramah St, Hayy 7, inside Al‑Ordonia Mall, 6th Oct City",
// // // // // //                     Location = CreatePoint(29.9630, 30.9185),
// // // // // //                     PhoneNumber = "01275459661",
// // // // // //                     ServicesOffered = "General Car Service",
// // // // // //                     OpeningHours = "Sat–Thu 9am–6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Al Worsha Al Handasiya",
// // // // // //                     NameAr = "الورشة الهندسية",
// // // // // //                     Address = "Makka Al‑Mukarramah St, Hayy 7, inside Al‑Ordonia Mall, 6th Oct City",
// // // // // //                     Location = CreatePoint(29.9632, 30.9187),
// // // // // //                     PhoneNumber = "01126374432",
// // // // // //                     ServicesOffered = "General Car Service",
// // // // // //                     OpeningHours = "Sat–Thu 9am–6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Proton Point Service Center",
// // // // // //                     NameAr = "بروتون بوينت",
// // // // // //                     Address = "Al‑Kefrawy Axis, Hayy 2, inside New Jordanian Mall, 6th Oct City",
// // // // // //                     Location = CreatePoint(29.9610, 30.9165),
// // // // // //                     PhoneNumber = "01009627238",
// // // // // //                     ServicesOffered = "General Car Service",
// // // // // //                     OpeningHours = "Sat–Thu 9am–6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Al Marwa Repair Workshop",
// // // // // //                     NameAr = "ورشة المروة لإصلاح السيارات",
// // // // // //                     Address = "Geel 2000 St, Hayy 11, inside Nakheel 2 Center, 6th Oct City",
// // // // // //                     Location = CreatePoint(29.9655, 30.9208),
// // // // // //                     PhoneNumber = "01225214298",
// // // // // //                     ServicesOffered = "General Car Service",
// // // // // //                     OpeningHours = "Sat–Thu 9am–6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Ezz El-Arab Auto Group",
// // // // // //                     NameAr = "مجموعة عز العرب للسيارات",
// // // // // //                     Address = "Waslet Dahshur St, West Yard, Sheikh Zayed (branch)",
// // // // // //                     Location = CreatePoint(29.9760, 30.9020),
// // // // // //                     PhoneNumber = "01032220855",
// // // // // //                     ServicesOffered = "Manufacture & Repair, Body & Paint, Spare Parts",
// // // // // //                     OpeningHours = "Sat–Thu 9am–6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Engineering Workshop For Cars Maintenance",
// // // // // //                     NameAr = "الورشة الهندسية لصيانة السيارات",
// // // // // //                     Address = "3rd District (inside October Grand Mall), 6th October City",
// // // // // //                     Location = CreatePoint(29.9618, 30.9177),
// // // // // //                     PhoneNumber = "01001511267",
// // // // // //                     ServicesOffered = "Mechanical, Electrical, Servicing Equipment",
// // // // // //                     OpeningHours = "Sat–Thu 9am–6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Technical Workshop For Cars Maintenance",
// // // // // //                     NameAr = "الورشة الفنية لصيانة السيارات – الحي 3",
// // // // // //                     Address = "5th Neighbourhood, 3rd District, 6th October City",
// // // // // //                     Location = CreatePoint(29.9620, 30.9180),
// // // // // //                     PhoneNumber = "01099485311",
// // // // // //                     ServicesOffered = "General Repairs, Servicing Equipment",
// // // // // //                     OpeningHours = "Sat–Thu 9am–6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Technical Workshop For Cars Maintenance",
// // // // // //                     NameAr = "الورشة الفنية لصيانة السيارات – الحي 11",
// // // // // //                     Address = "11th District, 6th October City (inside El Hedaya 2 Center)",
// // // // // //                     Location = CreatePoint(29.9645, 30.9205),
// // // // // //                     PhoneNumber = "01222976007",
// // // // // //                     ServicesOffered = "General Repairs, Servicing Equipment",
// // // // // //                     OpeningHours = "Sat–Thu 9am–6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Global Center For Maintenance Cars",
// // // // // //                     NameAr = "المركز العالمي لصيانة السيارات",
// // // // // //                     Address = "7th District, beside El Radwa Language Schools",
// // // // // //                     Location = CreatePoint(29.9700, 30.9250),
// // // // // //                     PhoneNumber = "01223657778",
// // // // // //                     ServicesOffered = "General Car Servicing, Mechanical, Electrical",
// // // // // //                     OpeningHours = "Sat–Thu 9am–6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Auto Repair - Hayy 7",
// // // // // //                     NameAr = "أوتو ريبير – الحي 7",
// // // // // //                     Address = "Hayy 7, next to Klaway Market & Sudanese Negotiation Building",
// // // // // //                     Location = CreatePoint(29.9582, 30.9123),
// // // // // //                     PhoneNumber = "01110620022",
// // // // // //                     ServicesOffered = "Mechanics, Electrical, Suspension, AC, Painting, Bodywork, Diagnostics",
// // // // // //                     OpeningHours = "Sat–Thu 11am–11pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Ahmed Agha Workshop",
// // // // // //                     NameAr = "ورشة أحمد آغا",
// // // // // //                     Address = "Iskan El-Shabab 100m, 11th District, inside Center Al‑Wijih",
// // // // // //                     Location = CreatePoint(29.9640, 30.9200),
// // // // // //                     PhoneNumber = "01001408720",
// // // // // //                     ServicesOffered = "General Car Repair & Equipment Service",
// // // // // //                     OpeningHours = "Sat–Thu 9am–6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Mekka Workshop",
// // // // // //                     NameAr = "ورشة مكة",
// // // // // //                     Address = "11th District, inside Center Al‑Halfawy, 6th October City",
// // // // // //                     Location = CreatePoint(29.9642, 30.9202),
// // // // // //                     PhoneNumber = "01003318094",
// // // // // //                     ServicesOffered = "General Car Repair & Equipment Service",
// // // // // //                     OpeningHours = "Sat–Thu 9am–6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Technical Workshop – Center El Hedaya 2",
// // // // // //                     NameAr = "الورشة الفنية لصيانة السيارات – الحي 11",
// // // // // //                     Address = "11th District, inside Center El Hedaya 2",
// // // // // //                     Location = CreatePoint(29.9646, 30.9206),
// // // // // //                     PhoneNumber = "01224145998",
// // // // // //                     ServicesOffered = "General Car Repair & Equipment Service",
// // // // // //                     OpeningHours = "Sat–Thu 9am–6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Alameya Cars Services",
// // // // // //                     NameAr = "العالمية لخدمات السيارات",
// // // // // //                     Address = "6th District, inside Center Wadi Al‑Malika, 6th October City",
// // // // // //                     Location = CreatePoint(29.9595, 30.9175),
// // // // // //                     PhoneNumber = "01282218444",
// // // // // //                     ServicesOffered = "General Car Repair & Equipment Service",
// // // // // //                     OpeningHours = "Sat–Thu 9am–6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Engineering Center For Mufflers",
// // // // // //                     NameAr = "المركز الهندسي لخدمة الشكمانات",
// // // // // //                     Address = "Piece 145, Street 6, 3rd Industrial Zone, 6th October City",
// // // // // //                     Location = CreatePoint(29.9550, 30.9050),
// // // // // //                     PhoneNumber = "02338341377",
// // // // // //                     ServicesOffered = "Muffler & Exhaust System Service",
// // // // // //                     OpeningHours = "Sat–Thu 9am–6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Abnaa Al-Daqahliya Mechanics",
// // // // // //                     NameAr = "أبناء الدقهلية لميكانيكا السيارات",
// // // // // //                     Address = "Inside Center Wadi Al‑Moluk, 6th District, 6th October City",
// // // // // //                     Location = CreatePoint(29.9590, 30.9170),
// // // // // //                     PhoneNumber = "01229452233",
// // // // // //                     ServicesOffered = "Mechanical Repair & Servicing",
// // // // // //                     OpeningHours = "Sat–Thu 9am–6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Wahdan Auto Group",
// // // // // //                     NameAr = "وهدان أوتو جروب",
// // // // // //                     Address = "Street 47 off Street 14, 3rd Industrial Zone, 6th October City",
// // // // // //                     Location = CreatePoint(29.9555, 30.9045),
// // // // // //                     PhoneNumber = "02338342412",
// // // // // //                     ServicesOffered = "General Car Service & Spare Parts",
// // // // // //                     OpeningHours = "Sat–Thu 9am–6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Abu Ziyad Al‑Sarougy",
// // // // // //                     NameAr = "أبو زياد السروجي",
// // // // // //                     Address = "Piece 80, Project 103, 6th District, 6th October City",
// // // // // //                     Location = CreatePoint(29.9592, 30.9172),
// // // // // //                     PhoneNumber = "01117946177",
// // // // // //                     ServicesOffered = "General Car Repair & Servicing",
// // // // // //                     OpeningHours = "Sat–Thu 9am–6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Ahmad Al-Afreet Auto Electric",
// // // // // //                     NameAr = "أحمد العفريت لكهرباء السيارات",
// // // // // //                     Address = "Inside Center Wadi Al‑Moluk, 6th District, 6th October City",
// // // // // //                     Location = CreatePoint(29.9588, 30.9168),
// // // // // //                     PhoneNumber = "01026537353",
// // // // // //                     ServicesOffered = "Car Electrical Repairs",
// // // // // //                     OpeningHours = "Sat–Thu 9am–6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 new() {
// // // // // //                     Id = Guid.NewGuid(),
// // // // // //                     NameEn = "Group United Toyota Service Center",
// // // // // //                     NameAr = "المجموعة المتحدة - مركز خدمة تويوتا",
// // // // // //                     Address = "Piece 339, Extension 3rd Industrial Zone, 6th October City",
// // // // // //                     Location = CreatePoint(29.9545, 30.9049),
// // // // // //                     PhoneNumber = "01008552358",
// // // // // //                     ServicesOffered = "Toyota Authorized Maintenance & Repairs",
// // // // // //                     OpeningHours = "Sat–Thu 9am–6pm",
// // // // // //                     Category = ShopCategory.GeneralMaintenance,
// // // // // //                     Slug="Sand-october",
// // // // // //                     CityId = octoberCity.Id,
// // // // // //                     LogoUrl="/logos/pro-auto.png"
// // // // // //                 },
// // // // // //                 // Add ~10-15 more curated shops specific to 6th of October City
// // // // // //                 // Use realistic (but fictional if needed) names, addresses, and actual lat/lon for 6th Oct.
// // // // // //             };
// // // // // //             logger.LogInformation("Attempting to seed {ShopCount} shops.", shops.Count);
// // // // // //             await context.Shops.AddRangeAsync(shops);
// // // // // //             await context.SaveChangesAsync();
// // // // // //             logger.LogInformation("Successfully seeded {ShopCountActual} shops.", await context.Shops.CountAsync());
// // // // // //         }
// // // // // //         else
// // // // // //         {
// // // // // //             logger.LogInformation("Shops table already contains data ({ShopCount} shops) and forceReseed is false. Skipping seed.", await context.Shops.CountAsync());
// // // // // //         }
// // // // // //     }
// // // // // // }