using Microsoft.EntityFrameworkCore;

namespace FleetFlow.Infrastructure.Persistence;

// Business entities and migrations are introduced in M1.
public sealed class FleetFlowDbContext(DbContextOptions<FleetFlowDbContext> options) : DbContext(options);
