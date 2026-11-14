using Microsoft.EntityFrameworkCore;
using SukoonDesk.Api.Domain;

namespace SukoonDesk.Api.Infrastructure;

public static class SchemaUpgrade
{
    public static async Task ApplyAsync(AppDbContext db)
    {
        if (!db.Database.IsSqlite()) return;

        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        await EnsureColumnAsync(db, connection, "Tenants", "SubscriptionPlan", "INTEGER NOT NULL DEFAULT 2");
        await EnsureColumnAsync(db, connection, "Tenants", "SubscriptionStatus", "INTEGER NOT NULL DEFAULT 1");
        await EnsureColumnAsync(db, connection, "Tenants", "TrialEndsAt", "TEXT NULL");
        await EnsureColumnAsync(db, connection, "Tenants", "SubscriptionEndsAt", "TEXT NULL");
        await EnsureColumnAsync(db, connection, "Tenants", "ResidentLimit", "INTEGER NOT NULL DEFAULT 2000");
        await EnsureColumnAsync(db, connection, "Tenants", "StaffLimit", "INTEGER NOT NULL DEFAULT 50");
        await EnsureColumnAsync(db, connection, "Tenants", "MonthlyComplaintLimit", "INTEGER NOT NULL DEFAULT 5000");
        await EnsureColumnAsync(db, connection, "Tenants", "BillingNotes", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(db, connection, "TenantRoles", "CanReceiveAssignments", "INTEGER NOT NULL DEFAULT 0");
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info('TenantMemberships');";
            await using var reader = await command.ExecuteReaderAsync();
            var hasUnitLabel = false;
            while (await reader.ReadAsync())
                hasUnitLabel |= string.Equals(reader.GetString(1), "UnitLabel", StringComparison.OrdinalIgnoreCase);
            if (!hasUnitLabel)
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE TenantMemberships ADD COLUMN UnitLabel TEXT NOT NULL DEFAULT '';");
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info('AspNetUsers');";
            await using var reader = await command.ExecuteReaderAsync();
            var hasPlatformAdmin = false;
            while (await reader.ReadAsync())
                hasPlatformAdmin |= string.Equals(reader.GetString(1), "IsPlatformAdmin", StringComparison.OrdinalIgnoreCase);
            if (!hasPlatformAdmin)
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE AspNetUsers ADD COLUMN IsPlatformAdmin INTEGER NOT NULL DEFAULT 0;");
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info('AspNetUsers');";
            await using var reader = await command.ExecuteReaderAsync();
            var hasMustChangePassword = false;
            while (await reader.ReadAsync())
                hasMustChangePassword |= string.Equals(reader.GetString(1), "MustChangePassword", StringComparison.OrdinalIgnoreCase);
            if (!hasMustChangePassword)
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE AspNetUsers ADD COLUMN MustChangePassword INTEGER NOT NULL DEFAULT 0;");
        }

        var statements = new[]
        {
            "CREATE TABLE IF NOT EXISTS DataProtectionKeys (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, FriendlyName TEXT NULL, Xml TEXT NULL);",
            """
            CREATE TABLE IF NOT EXISTS ComplaintCategories (
                Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL, Name TEXT NOT NULL,
                Description TEXT NOT NULL, Color TEXT NOT NULL, DefaultSlaHours INTEGER NOT NULL,
                DefaultPriority INTEGER NOT NULL, IsActive INTEGER NOT NULL, CreatedAt TEXT NOT NULL,
                FOREIGN KEY (TenantId) REFERENCES Tenants(Id) ON DELETE CASCADE
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS Complaints (
                Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL, Number TEXT NOT NULL,
                Title TEXT NOT NULL, Description TEXT NOT NULL, Location TEXT NOT NULL,
                Priority INTEGER NOT NULL, Status INTEGER NOT NULL, CategoryId TEXT NOT NULL,
                CreatedByMembershipId TEXT NOT NULL, AssignedToMembershipId TEXT NULL,
                DueAt TEXT NOT NULL, ResolvedAt TEXT NULL, ClosedAt TEXT NULL,
                CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL,
                FOREIGN KEY (TenantId) REFERENCES Tenants(Id) ON DELETE CASCADE,
                FOREIGN KEY (CategoryId) REFERENCES ComplaintCategories(Id) ON DELETE CASCADE,
                FOREIGN KEY (CreatedByMembershipId) REFERENCES TenantMemberships(Id) ON DELETE RESTRICT,
                FOREIGN KEY (AssignedToMembershipId) REFERENCES TenantMemberships(Id) ON DELETE RESTRICT
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS ComplaintComments (
                Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL, ComplaintId TEXT NOT NULL,
                AuthorMembershipId TEXT NOT NULL, Body TEXT NOT NULL, IsInternal INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (ComplaintId) REFERENCES Complaints(Id) ON DELETE CASCADE,
                FOREIGN KEY (AuthorMembershipId) REFERENCES TenantMemberships(Id) ON DELETE RESTRICT
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS ComplaintStatusEvents (
                Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL, ComplaintId TEXT NOT NULL,
                FromStatus INTEGER NULL, ToStatus INTEGER NOT NULL, ChangedByMembershipId TEXT NOT NULL,
                Note TEXT NOT NULL, CreatedAt TEXT NOT NULL,
                FOREIGN KEY (ComplaintId) REFERENCES Complaints(Id) ON DELETE CASCADE,
                FOREIGN KEY (ChangedByMembershipId) REFERENCES TenantMemberships(Id) ON DELETE RESTRICT
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS ComplaintFeedback (
                Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL, ComplaintId TEXT NOT NULL,
                MembershipId TEXT NOT NULL, Rating INTEGER NOT NULL, Comment TEXT NOT NULL, CreatedAt TEXT NOT NULL,
                FOREIGN KEY (ComplaintId) REFERENCES Complaints(Id) ON DELETE CASCADE,
                FOREIGN KEY (MembershipId) REFERENCES TenantMemberships(Id) ON DELETE RESTRICT
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS AssignmentRules (
                Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL, Name TEXT NOT NULL,
                CategoryId TEXT NULL, LocationContains TEXT NOT NULL, SupervisorMembershipId TEXT NOT NULL,
                "Order" INTEGER NOT NULL, IsActive INTEGER NOT NULL,
                FOREIGN KEY (TenantId) REFERENCES Tenants(Id) ON DELETE CASCADE,
                FOREIGN KEY (CategoryId) REFERENCES ComplaintCategories(Id) ON DELETE CASCADE,
                FOREIGN KEY (SupervisorMembershipId) REFERENCES TenantMemberships(Id) ON DELETE RESTRICT
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS Notifications (
                Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL, MembershipId TEXT NOT NULL,
                Title TEXT NOT NULL, Message TEXT NOT NULL, Type TEXT NOT NULL, Link TEXT NOT NULL,
                IsRead INTEGER NOT NULL, CreatedAt TEXT NOT NULL,
                FOREIGN KEY (MembershipId) REFERENCES TenantMemberships(Id) ON DELETE CASCADE
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS PublicMembers (
                Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL, MembershipId TEXT NOT NULL,
                Cnic TEXT NOT NULL, FullName TEXT NOT NULL, Phone TEXT NOT NULL, Email TEXT NOT NULL,
                PlotNo TEXT NOT NULL, StreetNo TEXT NOT NULL, Block TEXT NOT NULL, Phase INTEGER NOT NULL,
                IsActive INTEGER NOT NULL, CreatedAt TEXT NOT NULL,
                FOREIGN KEY (TenantId) REFERENCES Tenants(Id) ON DELETE CASCADE,
                FOREIGN KEY (MembershipId) REFERENCES TenantMemberships(Id) ON DELETE CASCADE
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS TenantProjects (
                Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL, Name TEXT NOT NULL,
                Code TEXT NOT NULL, Description TEXT NOT NULL, SortOrder INTEGER NOT NULL,
                IsActive INTEGER NOT NULL, CreatedAt TEXT NOT NULL,
                FOREIGN KEY (TenantId) REFERENCES Tenants(Id) ON DELETE CASCADE
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS ComplaintAttachments (
                Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL, ComplaintId TEXT NOT NULL,
                CommentId TEXT NULL, UploaderMembershipId TEXT NOT NULL, FileName TEXT NOT NULL,
                StoredName TEXT NOT NULL, ContentType TEXT NOT NULL, Size INTEGER NOT NULL, CreatedAt TEXT NOT NULL,
                FOREIGN KEY (ComplaintId) REFERENCES Complaints(Id) ON DELETE CASCADE,
                FOREIGN KEY (CommentId) REFERENCES ComplaintComments(Id) ON DELETE CASCADE,
                FOREIGN KEY (UploaderMembershipId) REFERENCES TenantMemberships(Id) ON DELETE RESTRICT
            );
            """,
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_ComplaintCategories_TenantId_Name ON ComplaintCategories(TenantId, Name);",
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_Complaints_TenantId_Number ON Complaints(TenantId, Number);",
            "CREATE INDEX IF NOT EXISTS IX_Complaints_TenantId_Status_DueAt ON Complaints(TenantId, Status, DueAt);",
            "CREATE INDEX IF NOT EXISTS IX_ComplaintComments_TenantId_ComplaintId_CreatedAt ON ComplaintComments(TenantId, ComplaintId, CreatedAt);",
            "CREATE INDEX IF NOT EXISTS IX_ComplaintStatusEvents_TenantId_ComplaintId_CreatedAt ON ComplaintStatusEvents(TenantId, ComplaintId, CreatedAt);",
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_ComplaintFeedback_ComplaintId ON ComplaintFeedback(ComplaintId);",
            "CREATE INDEX IF NOT EXISTS IX_AssignmentRules_TenantId_Order ON AssignmentRules(TenantId, \"Order\");",
            "CREATE INDEX IF NOT EXISTS IX_Notifications_TenantId_MembershipId_IsRead_CreatedAt ON Notifications(TenantId, MembershipId, IsRead, CreatedAt);",
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_PublicMembers_MembershipId ON PublicMembers(MembershipId);",
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_TenantProjects_TenantId_Name ON TenantProjects(TenantId, Name);",
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_TenantProjects_TenantId_Code ON TenantProjects(TenantId, Code);",
            "CREATE INDEX IF NOT EXISTS IX_ComplaintAttachments_TenantId_ComplaintId ON ComplaintAttachments(TenantId, ComplaintId);"
        };

        foreach (var statement in statements) await db.Database.ExecuteSqlRawAsync(statement);
        await EnsureColumnAsync(db, connection, "PublicMembers", "WhatsAppNumber", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(db, connection, "PublicMembers", "ProjectId", "TEXT NULL");
        await EnsureColumnAsync(db, connection, "PublicMembers", "NotifyByEmail", "INTEGER NOT NULL DEFAULT 1");
        await EnsureColumnAsync(db, connection, "PublicMembers", "NotifyByWhatsApp", "INTEGER NOT NULL DEFAULT 0");
        await db.Database.ExecuteSqlRawAsync("UPDATE PublicMembers SET WhatsAppNumber = Phone WHERE WhatsAppNumber = ''; ");
        await db.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS IX_PublicMembers_TenantId_Cnic;");
        await db.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS IX_PublicMembers_Email;");
        await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_PublicMembers_Cnic ON PublicMembers(Cnic);");
        await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_PublicMembers_Phone ON PublicMembers(Phone);");
        await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_PublicMembers_WhatsAppNumber ON PublicMembers(WhatsAppNumber);");
        await db.Database.ExecuteSqlRawAsync("UPDATE TenantRoles SET CanReceiveAssignments = 1 WHERE Name IN ('Society Administrator','Manager','Supervisor');");
        await SeedDefaultProjectsAsync(db);
        await SeedDefaultCategoriesAsync(db);
    }

    private static async Task EnsureColumnAsync(AppDbContext db, System.Data.Common.DbConnection connection, string table, string column, string definition)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info('{table}');";
        await using var reader = await command.ExecuteReaderAsync();
        var exists = false;
        while (await reader.ReadAsync()) exists |= string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase);
        await reader.DisposeAsync();
        if (!exists)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
            await alter.ExecuteNonQueryAsync();
        }
    }

    private static async Task SeedDefaultProjectsAsync(AppDbContext db)
    {
        var tenantIds = await db.Tenants.Select(x => x.Id).ToListAsync();
        foreach (var tenantId in tenantIds)
        {
            if (await db.TenantProjects.AnyAsync(x => x.TenantId == tenantId)) continue;
            db.TenantProjects.AddRange(
                new TenantProject { TenantId = tenantId, Name = "Jinnah Gardens I", Code = "JGI", SortOrder = 1 },
                new TenantProject { TenantId = tenantId, Name = "Jinnah Gardens II", Code = "JGII", SortOrder = 2 },
                new TenantProject { TenantId = tenantId, Name = "Korang Town", Code = "KT", SortOrder = 3 },
                new TenantProject { TenantId = tenantId, Name = "Korang Town Extension", Code = "KTExt", SortOrder = 4 });
        }
        await db.SaveChangesAsync();
        var members = await db.PublicMembers.Where(x => x.ProjectId == null).ToListAsync();
        var projects = await db.TenantProjects.ToListAsync();
        foreach (var member in members)
        {
            var code = member.Phase switch { SocietyPhase.JGI => "JGI", SocietyPhase.JGII => "JGII", SocietyPhase.KTExt => "KTExt", _ => "KT" };
            member.ProjectId = projects.FirstOrDefault(x => x.TenantId == member.TenantId && x.Code == code)?.Id;
        }
        await db.SaveChangesAsync();
    }

    private static async Task SeedDefaultCategoriesAsync(AppDbContext db)
    {
        var tenantIds = await db.Tenants.Select(x => x.Id).ToListAsync();
        foreach (var tenantId in tenantIds)
        {
            if (await db.ComplaintCategories.AnyAsync(x => x.TenantId == tenantId)) continue;
            db.ComplaintCategories.AddRange(SeedData.CreateDefaultCategories(tenantId));
        }
        await db.SaveChangesAsync();
    }

}
