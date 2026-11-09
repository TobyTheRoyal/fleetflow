using FleetFlow.Infrastructure.Identity;
using FleetFlow.Infrastructure.Persistence;
using FleetFlow.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace FleetFlow.Infrastructure.Portfolio;

// Explicit CLI only. Not registered as a startup job and never exposed through an HTTP reset route.
public sealed class PortfolioInitializer(IdentityPersistence identity, UserManager<ApplicationUser> users, IConfiguration configuration, PortfolioSettings settings, TimeProvider clock)
{
    public async Task ProvisionLoginsAsync(CancellationToken token)
    {
        await using var owner = new NpgsqlConnection(configuration.GetConnectionString("FleetFlow"));
        await owner.OpenAsync(token);
        foreach (var (key, group) in new[] { ("PortfolioRuntime", "fleetflow_portfolio_runtime"), ("PortfolioMaintenance", "fleetflow_portfolio_maintenance") })
        {
            var login = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString(key));
            if (login.Database != owner.Database || login.Username == owner.UserName || string.IsNullOrEmpty(login.Username) || (login.Password?.Length ?? 0) < 24)
                throw new InvalidOperationException("Provisioning requires distinct runtime/maintenance logins, the confirmed database and generated secrets of at least 24 characters.");
            await using var command = new NpgsqlCommand("""
                SELECT set_config('portfolio.login',@login,false),set_config('portfolio.password',@password,false),set_config('portfolio.group',@group,false);
                DO $$ DECLARE account text := current_setting('portfolio.login'); permission_group text := current_setting('portfolio.group'); BEGIN
                  IF NOT EXISTS(SELECT FROM pg_roles WHERE rolname=account) THEN
                    EXECUTE format('CREATE ROLE %I LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD %L',account,current_setting('portfolio.password'));
                  END IF;
                  IF EXISTS(SELECT FROM pg_roles WHERE rolname=account AND (rolsuper OR rolcreatedb OR rolcreaterole OR rolbypassrls)) THEN RAISE EXCEPTION 'Unsafe existing portfolio role'; END IF;
                  EXECUTE format('GRANT %I TO %I',permission_group,account);
                END $$;
                """, owner);
            command.Parameters.AddWithValue("login", login.Username); command.Parameters.AddWithValue("password", login.Password!); command.Parameters.AddWithValue("group", group);
            await command.ExecuteNonQueryAsync(token);
        }
    }
    public async Task InitializeAsync(string expectedDatabase, CancellationToken token)
    {
        var connectionString = configuration.GetConnectionString("FleetFlow") ?? throw new InvalidOperationException("Database connection required.");
        var connection = new NpgsqlConnection(connectionString);
        await using var owner = connection;
        await owner.OpenAsync(token);
        if (owner.Database != expectedDatabase || string.IsNullOrWhiteSpace(expectedDatabase)) throw new InvalidOperationException("Explicit target database confirmation does not match.");
        var options = new DbContextOptionsBuilder<FleetFlowDbContext>().UseNpgsql(owner)
            .ReplaceService<IModelCacheKeyFactory, PortfolioModelCacheKey>().Options;
        await using var normal = new FleetFlowDbContext(options);
        await normal.Database.MigrateAsync(token);
        await using var gate = new NpgsqlCommand("SELECT pg_advisory_lock(741070001)", owner);
        await gate.ExecuteNonQueryAsync(token);
        var rebound = false;
        try
        {
            var initialized = await identity.Database.SqlQueryRaw<bool>("SELECT EXISTS(SELECT 1 FROM public.\"PortfolioSlots\") AS \"Value\"").SingleAsync(token);
            if (initialized)
            {
                await using var catalogCheck = new NpgsqlCommand("SELECT \"MigrationId\"=(SELECT \"MigrationId\" FROM public.\"__EFMigrationsHistory\" ORDER BY \"MigrationId\" DESC LIMIT 1) FROM public.\"PortfolioCatalog\" WHERE \"Id\"=1", owner);
                if (await catalogCheck.ExecuteScalarAsync(token) is not true) throw new InvalidOperationException("Existing portfolio schemas require an explicit reviewed schema upgrade; initialization never silently replaces visitor data.");
                return; // Never reset active leases, passwords, certificates, budgets or live demo data on re-run.
            }
            await using var transaction = await owner.BeginTransactionAsync(token);
            identity.Database.SetDbConnection(owner, contextOwnsConnection: false);
            rebound = true;
            await identity.Database.UseTransactionAsync(transaction, token);
            await Execute(owner, """
                DO $$ BEGIN
                  IF NOT EXISTS(SELECT FROM pg_roles WHERE rolname='fleetflow_portfolio_runtime') THEN CREATE ROLE fleetflow_portfolio_runtime NOLOGIN; END IF;
                  IF NOT EXISTS(SELECT FROM pg_roles WHERE rolname='fleetflow_portfolio_maintenance') THEN CREATE ROLE fleetflow_portfolio_maintenance NOLOGIN; END IF;
                END $$;
                REVOKE CREATE ON SCHEMA public FROM PUBLIC;
                CREATE TABLE IF NOT EXISTS public."PortfolioCatalog" ("Id" integer PRIMARY KEY CHECK ("Id"=1), "SeededAtUtc" timestamptz NOT NULL, "MigrationId" text NOT NULL);
                """, token);
            foreach (var schema in new[] { "portfolio_template", "portfolio_viewer" }.Concat(Enumerable.Range(1, settings.DispatcherSlots).Select(i => $"portfolio_dispatch_{i}")))
                await CreateSchemaAsync(owner, schema, token);
            await using (var seed = new FleetFlowDbContext(options, PortfolioScope.ForSchema("portfolio_template")))
            {
                await seed.Database.UseTransactionAsync(transaction, token);
                await new DevelopmentSeeder(seed, clock).SeedPortfolioAsync(token);
            }
            await using (var catalog = new NpgsqlCommand("INSERT INTO public.\"PortfolioCatalog\" VALUES (1,@now,@migration) ON CONFLICT DO NOTHING", owner))
            {
                catalog.Parameters.AddWithValue("now", clock.GetUtcNow());
                catalog.Parameters.AddWithValue("migration", (await normal.Database.GetAppliedMigrationsAsync(token)).Last());
                await catalog.ExecuteNonQueryAsync(token);
            }
            await Execute(owner, MaintenanceSql, token);
            foreach (var schema in new[] { "portfolio_viewer" }.Concat(Enumerable.Range(1, settings.DispatcherSlots).Select(i => $"portfolio_dispatch_{i}")))
            {
                await using var copy = new NpgsqlCommand("SELECT public.portfolio_copy_seed(@schema,@now)", owner);
                copy.Parameters.AddWithValue("schema", schema); copy.Parameters.AddWithValue("now", clock.GetUtcNow()); await copy.ExecuteNonQueryAsync(token);
            }
            foreach (var (id, role, schema) in Enumerable.Range(1, settings.ViewerSlots).Select(i => (i, "Viewer", "portfolio_viewer"))
                .Concat(Enumerable.Range(1, settings.DispatcherSlots).Select(i => (10 + i, "Dispatcher", $"portfolio_dispatch_{i}"))))
            {
                var name = $"portfolio-{id}@fleetflow.invalid";
                var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = name, Email = name, DisplayName = $"Demo {role} {id}", IsDemo = true, CreatedAtUtc = clock.GetUtcNow() };
                if (!(await users.CreateAsync(user)).Succeeded || !(await users.AddToRoleAsync(user, role)).Succeeded) throw new InvalidOperationException("Demo identity provisioning failed.");
                identity.PortfolioSlots.Add(new() { Id = id, UserId = user.Id, Role = role, SchemaName = schema });
            }
            await identity.SaveChangesAsync(token);
            await Execute(owner, """
                GRANT USAGE ON SCHEMA public TO fleetflow_portfolio_runtime, fleetflow_portfolio_maintenance;
                GRANT SELECT, UPDATE ON public."AspNetUsers" TO fleetflow_portfolio_runtime;
                GRANT SELECT ON public."AspNetUserRoles", public."AspNetUserClaims", public."AspNetUserLogins", public."AspNetUserTokens", public."AspNetRoles", public."AspNetRoleClaims" TO fleetflow_portfolio_runtime;
                GRANT SELECT, INSERT, UPDATE, DELETE ON public."AiAttempts" TO fleetflow_portfolio_runtime;
                REVOKE ALL ON public."PortfolioCatalog" FROM fleetflow_portfolio_runtime;
                GRANT SELECT ON public."PortfolioCatalog", public."__EFMigrationsHistory" TO fleetflow_portfolio_runtime;
                REVOKE ALL ON public."PortfolioSlots" FROM fleetflow_portfolio_runtime;
                GRANT SELECT ON public."PortfolioSlots" TO fleetflow_portfolio_runtime;
                GRANT UPDATE ("LeaseId","ExpiresAtUtc","Ready","Writes") ON public."PortfolioSlots" TO fleetflow_portfolio_runtime;
                REVOKE ALL ON public."ShipmentEvents" FROM fleetflow_portfolio_runtime;
                REVOKE ALL ON public."DataProtectionKeys" FROM fleetflow_portfolio_runtime;
                GRANT SELECT, INSERT ON public."DataProtectionKeys" TO fleetflow_portfolio_runtime;
                GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO fleetflow_portfolio_runtime;
                REVOKE ALL ON FUNCTION public.portfolio_copy_seed(text,timestamptz) FROM PUBLIC, fleetflow_portfolio_runtime, fleetflow_portfolio_maintenance;
                REVOKE ALL ON FUNCTION public.portfolio_prepare_slot(integer,uuid,timestamptz) FROM PUBLIC, fleetflow_portfolio_runtime;
                GRANT EXECUTE ON FUNCTION public.portfolio_prepare_slot(integer,uuid,timestamptz) TO fleetflow_portfolio_maintenance;
                """, token);
            await transaction.CommitAsync(token);
        }
        finally
        {
            if (rebound)
            {
                await identity.Database.UseTransactionAsync(null, CancellationToken.None);
                identity.ChangeTracker.Clear();
                // The transaction's externally supplied connection is disposed below. Restore an
                // owned connection so another explicit invocation in this scope remains safe.
                identity.Database.SetDbConnection(new NpgsqlConnection(connectionString), contextOwnsConnection: true);
            }
            await using var release = new NpgsqlCommand("SELECT pg_advisory_unlock(741070001)", owner);
            await release.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
    private static async Task CreateSchemaAsync(NpgsqlConnection connection, string schema, CancellationToken token)
    {
        // All identifiers originate from this bounded initializer, not visitor input.
        await Execute(connection, $"CREATE SCHEMA \"{schema}\"", token);
        foreach (var table in new[] { "Customers", "Drivers", "Vehicles", "Shipments", "ShipmentEvents" })
            await Execute(connection, $"CREATE TABLE \"{schema}\".\"{table}\" (LIKE public.\"{table}\" INCLUDING ALL)", token);
        await Execute(connection, $"""
            ALTER TABLE "{schema}"."Shipments" ADD FOREIGN KEY ("CustomerId") REFERENCES "{schema}"."Customers"("Id"),
              ADD FOREIGN KEY ("AssignedDriverId") REFERENCES "{schema}"."Drivers"("Id"),
              ADD FOREIGN KEY ("AssignedVehicleId") REFERENCES "{schema}"."Vehicles"("Id");
            ALTER TABLE "{schema}"."ShipmentEvents" ADD FOREIGN KEY ("ShipmentId") REFERENCES "{schema}"."Shipments"("Id");
            CREATE TRIGGER shipment_events_immutable BEFORE UPDATE OR DELETE ON "{schema}"."ShipmentEvents"
              FOR EACH ROW EXECUTE FUNCTION public.prevent_shipment_event_mutation();
            REVOKE ALL ON SCHEMA "{schema}" FROM PUBLIC;
            GRANT USAGE ON SCHEMA "{schema}" TO fleetflow_portfolio_runtime;
            GRANT SELECT, INSERT, UPDATE ON ALL TABLES IN SCHEMA "{schema}" TO fleetflow_portfolio_runtime;
            REVOKE ALL ON "{schema}"."ShipmentEvents" FROM fleetflow_portfolio_runtime;
            GRANT SELECT, INSERT ON "{schema}"."ShipmentEvents" TO fleetflow_portfolio_runtime;
            """, token);
        if (schema == "portfolio_template") await Execute(connection, $"REVOKE ALL ON SCHEMA \"{schema}\" FROM fleetflow_portfolio_runtime; REVOKE ALL ON ALL TABLES IN SCHEMA \"{schema}\" FROM fleetflow_portfolio_runtime", token);
    }
    private static async Task Execute(NpgsqlConnection connection, string sql, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 60 }; await command.ExecuteNonQueryAsync(token);
    }
    private const string MaintenanceSql = """
        CREATE OR REPLACE FUNCTION public.portfolio_copy_seed(target text, observed timestamptz) RETURNS void
        LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
        DECLARE copy_table text; projection text; epoch timestamptz;
        BEGIN
          IF target NOT IN ('portfolio_viewer','portfolio_dispatch_1','portfolio_dispatch_2','portfolio_dispatch_3') THEN RAISE EXCEPTION 'Invalid demo target'; END IF;
          SELECT "SeededAtUtc" INTO STRICT epoch FROM public."PortfolioCatalog" WHERE "Id"=1;
          FOREACH copy_table IN ARRAY ARRAY['Customers','Drivers','Vehicles','Shipments','ShipmentEvents'] LOOP
            SELECT string_agg(CASE WHEN data_type='timestamp with time zone' THEN format('%I + ($1 - $2)',column_name) ELSE format('%I',column_name) END, ',' ORDER BY ordinal_position)
              INTO projection FROM information_schema.columns WHERE table_schema='portfolio_template' AND columns.table_name=copy_table;
            EXECUTE format('INSERT INTO %I.%I SELECT %s FROM portfolio_template.%I',target,copy_table,projection,copy_table) USING observed,epoch;
          END LOOP;
        END $$;
        REVOKE ALL ON FUNCTION public.portfolio_copy_seed(text,timestamptz) FROM PUBLIC;
        CREATE OR REPLACE FUNCTION public.portfolio_prepare_slot(slot_id integer, expected_lease uuid, observed timestamptz) RETURNS void
        LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
        DECLARE target text;
        BEGIN
          IF slot_id NOT BETWEEN 11 AND 13 THEN RAISE EXCEPTION 'Invalid demo slot'; END IF;
          PERFORM pg_advisory_xact_lock(741070000+slot_id);
          SELECT "SchemaName" INTO STRICT target FROM public."PortfolioSlots"
            WHERE "Id"=slot_id AND "LeaseId"=expected_lease AND NOT "Ready" AND "ExpiresAtUtc">observed FOR UPDATE;
          IF target <> 'portfolio_dispatch_'||(slot_id-10)::text THEN RAISE EXCEPTION 'Invalid demo mapping'; END IF;
          -- TRUNCATE is a maintenance-only privilege. The original UPDATE/DELETE trigger is unchanged.
          EXECUTE format('TRUNCATE TABLE %I."ShipmentEvents",%I."Shipments",%I."Customers",%I."Drivers",%I."Vehicles"',target,target,target,target,target);
          PERFORM public.portfolio_copy_seed(target,observed);
          UPDATE public."PortfolioSlots" SET "Ready"=true WHERE "Id"=slot_id AND "LeaseId"=expected_lease;
        END $$;
        REVOKE ALL ON FUNCTION public.portfolio_prepare_slot(integer,uuid,timestamptz) FROM PUBLIC;
        """;
}
